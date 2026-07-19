namespace Duets.Pad.Attachments;

/// <summary>
/// Owns file-picker metadata, transactional selection staging, quotas, blob leases, and storage
/// cleanup for one DuetsPad session. Its internal lock is independent of the session state lock;
/// storage I/O is never performed while either lock is held. Reachability pruning may enter this
/// store while holding the session state lock, so code under the attachment lock must never call
/// back into session state or otherwise acquire the session state lock.
/// </summary>
internal sealed class AttachmentStore(
    IAttachmentStorage storage,
    long maxBytesPerFile,
    long maxBytesPerSession,
    int maxFilesPerSession,
    TimeSpan storageDrainTimeout
) : IDisposable
{
    private sealed class BlobEntry
    {
        public required Guid Id { get; init; }
        public required Guid PickerId { get; init; }
        public required string Name { get; init; }
        public required string ContentType { get; init; }
        public required long Size { get; init; }
        public required DuetsPadFile PublicFile { get; init; }
        public bool Owner { get; set; } = true;
        public bool Uploaded { get; set; }
        public bool UploadStarted { get; set; }
        public int ActiveWrites { get; set; }
        public int Leases { get; set; }
        public bool DeleteScheduled { get; set; }
    }

    private sealed class PendingSelection
    {
        public required Guid Token { get; init; }
        public required long Revision { get; init; }
        public required List<BlobEntry> Files { get; set; }
        public required CancellationTokenSource Cancellation { get; init; }
        public AttachmentSelectionStatus Status { get; set; } = AttachmentSelectionStatus.Uploading;
        public string? Error { get; set; }
    }

    private sealed class PickerEntry
    {
        public required DisplayFilePicker Handle { get; set; }
        public long Revision { get; set; }
        public List<BlobEntry> Files { get; set; } = [];
        public PendingSelection? Pending { get; set; }
    }

    // DuetsPadSession may enter this lock while holding _stateLock during reachability pruning.
    // Nothing under _sync may call the picker host or otherwise acquire session state.
    private readonly object _sync = new();
    private readonly IAttachmentStorage _storage =
        storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly long _maxBytesPerFile =
        maxBytesPerFile > 0
            ? maxBytesPerFile
            : throw new ArgumentOutOfRangeException(nameof(maxBytesPerFile));
    private readonly long _maxBytesPerSession =
        maxBytesPerSession >= maxBytesPerFile
            ? maxBytesPerSession
            : throw new ArgumentOutOfRangeException(
                nameof(maxBytesPerSession),
                "Session attachment bytes must be at least the per-file limit."
            );
    private readonly int _maxFilesPerSession =
        maxFilesPerSession > 0
            ? maxFilesPerSession
            : throw new ArgumentOutOfRangeException(nameof(maxFilesPerSession));
    private readonly TimeSpan _storageDrainTimeout =
        storageDrainTimeout > TimeSpan.Zero && storageDrainTimeout.TotalMilliseconds <= int.MaxValue
            ? storageDrainTimeout
            : throw new ArgumentOutOfRangeException(nameof(storageDrainTimeout));
    private readonly Dictionary<Guid, PickerEntry> _pickers = [];

    // Enforces session-wide uniqueness for opaque storage identities until physical deletion.
    private readonly HashSet<Guid> _blobIds = [];
    private readonly HashSet<LeasedReadStream> _leases = [];
    private readonly HashSet<Task> _cleanupTasks = [];
    private readonly ManualResetEventSlim _storageOperationsDrained = new(initialState: true);
    private readonly CancellationTokenSource _cleanupCancellation = new();
    private long _usedBytes;
    private int _usedFiles;
    private int _activeStorageOperations;
    private Task? _disposeTask;
    private bool _disposed;

    public bool IsEmpty
    {
        get
        {
            lock (this._sync)
            {
                return this._pickers.Count == 0;
            }
        }
    }

    public AttachmentPickerSnapshot EnsurePicker(DisplayFilePicker picker)
    {
        if (picker is null)
        {
            throw new ArgumentNullException(nameof(picker));
        }

        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (!this._pickers.TryGetValue(picker.Id, out var entry))
            {
                entry = new PickerEntry { Handle = picker };
                this._pickers.Add(picker.Id, entry);
            }
            else
            {
                entry.Handle = picker;
            }

            return Snapshot(entry);
        }
    }

    public AttachmentPickerSnapshot? TryGetSnapshot(Guid pickerId)
    {
        lock (this._sync)
        {
            return this._pickers.TryGetValue(pickerId, out var entry) ? Snapshot(entry) : null;
        }
    }

    public DisplayFilePicker? TryGetHandle(Guid pickerId)
    {
        lock (this._sync)
        {
            return this._pickers.TryGetValue(pickerId, out var entry) ? entry.Handle : null;
        }
    }

    public IReadOnlyList<DuetsPadFile> GetFiles(Guid pickerId)
    {
        lock (this._sync)
        {
            return this._pickers.TryGetValue(pickerId, out var entry)
                ? [.. entry.Files.Select(file => file.PublicFile)]
                : [];
        }
    }

    public BeginAttachmentSelectionResult BeginSelection(
        Guid pickerId,
        AttachmentSelectionOrder order,
        IReadOnlyList<AttachmentFileManifest> manifest
    )
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (!this._pickers.TryGetValue(pickerId, out var picker))
            {
                return BeginFailure("The file picker is no longer available.", tooLarge: false);
            }

            if (order.ClientId == Guid.Empty || order.Sequence <= 0)
            {
                return BeginFailure("The attachment selection order is invalid.", tooLarge: false);
            }

            if (
                picker.Handle.LastClientId == order.ClientId
                && order.Sequence <= picker.Handle.LastClientSequence
            )
            {
                return BeginFailure(
                    "The attachment selection was superseded by a newer browser generation.",
                    tooLarge: false
                );
            }

            picker.Handle.LastClientId = order.ClientId;
            picker.Handle.LastClientSequence = order.Sequence;

            var nextRevision = NextRevision(picker);
            this.InvalidatePendingLocked(picker);
            var token = Guid.NewGuid();

            string? validationError = null;
            var tooLarge = false;
            if (!picker.Handle.Multiple && manifest.Count > 1)
            {
                validationError = "This file picker accepts only one file.";
            }
            else if (manifest.Count > this._maxFilesPerSession)
            {
                validationError = "The selection exceeds the session attachment count limit.";
                tooLarge = true;
            }

            long selectionBytes = 0;
            if (validationError is null)
            {
                foreach (var file in manifest)
                {
                    if (file.Size < 0)
                    {
                        validationError = "Attachment size cannot be negative.";
                        break;
                    }

                    if (file.Size > this._maxBytesPerFile)
                    {
                        validationError = "An attachment exceeds the per-file byte limit.";
                        tooLarge = true;
                        break;
                    }

                    try
                    {
                        selectionBytes = checked(selectionBytes + file.Size);
                    }
                    catch (OverflowException)
                    {
                        validationError = "The selection byte total is too large.";
                        tooLarge = true;
                        break;
                    }
                }
            }

            if (
                validationError is null
                && (
                    selectionBytes > this._maxBytesPerSession - this._usedBytes
                    || manifest.Count > this._maxFilesPerSession - this._usedFiles
                )
            )
            {
                validationError = "The selection exceeds the session attachment quota.";
                tooLarge = true;
            }

            if (validationError is not null)
            {
                picker.Pending = new PendingSelection
                {
                    Token = token,
                    Revision = nextRevision,
                    Files = [],
                    Cancellation = new CancellationTokenSource(),
                    Status = AttachmentSelectionStatus.Failed,
                    Error = validationError,
                };
                return new BeginAttachmentSelectionResult(
                    false,
                    token,
                    nextRevision,
                    [],
                    validationError,
                    tooLarge
                );
            }

            var pendingFiles = new List<BlobEntry>(manifest.Count);
            foreach (var item in manifest)
            {
                Guid fileId;
                do
                {
                    fileId = Guid.NewGuid();
                } while (!this._blobIds.Add(fileId));
                var name = SanitizeFileName(item.Name);
                var contentType = SanitizeContentType(item.ContentType);
                var publicFile = new DuetsPadFile(
                    picker.Handle.Host,
                    pickerId,
                    fileId,
                    name,
                    contentType,
                    item.Size
                );
                var blob = new BlobEntry
                {
                    Id = fileId,
                    PickerId = pickerId,
                    Name = name,
                    ContentType = contentType,
                    Size = item.Size,
                    PublicFile = publicFile,
                };
                pendingFiles.Add(blob);
                this._usedBytes += item.Size;
                this._usedFiles++;
            }

            picker.Pending = new PendingSelection
            {
                Token = token,
                Revision = nextRevision,
                Files = pendingFiles,
                Cancellation = new CancellationTokenSource(),
            };

            return new BeginAttachmentSelectionResult(
                true,
                token,
                nextRevision,
                [
                    .. pendingFiles.Select(file => new AttachmentSelectionFile(
                        file.Id,
                        file.Name,
                        file.ContentType,
                        file.Size
                    )),
                ],
                null,
                false
            );
        }

        static BeginAttachmentSelectionResult BeginFailure(string error, bool tooLarge) =>
            new(false, Guid.Empty, 0, [], error, tooLarge);
    }

    public async Task<AttachmentOperationResult> UploadFileAsync(
        Guid pickerId,
        Guid token,
        Guid fileId,
        Stream input,
        CancellationToken cancellationToken
    )
    {
        BlobEntry blob;
        CancellationToken selectionCancellation;
        long revision;
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (
                !this.TryGetUploadingSelectionLocked(pickerId, token, out var pending)
                || pending.Files.FirstOrDefault(file => file.Id == fileId) is not { } found
                || found.UploadStarted
            )
            {
                return Stale("The attachment upload is no longer current.");
            }

            blob = found;
            blob.UploadStarted = true;
            blob.ActiveWrites++;
            this.BeginStorageOperationLocked();
            selectionCancellation = pending.Cancellation.Token;
            revision = pending.Revision;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                selectionCancellation
            );
            await using (
                var output = await this._storage.CreateWriteStreamAsync(blob.Id, linked.Token)
            )
            {
                await CopyExactAsync(input, output, blob.Size, linked.Token).ConfigureAwait(false);
                await output.FlushAsync(linked.Token).ConfigureAwait(false);
            }

            await this._storage.CommitAsync(blob.Id, linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lock (this._sync)
            {
                this.EndWriteStorageOperationLocked(blob);
                if (
                    this.TryGetUploadingSelectionLocked(pickerId, token, out var pending)
                    && this._pickers.TryGetValue(pickerId, out var picker)
                )
                {
                    this.FailPendingLocked(picker, NormalizeUploadError(ex));
                    revision = pending.Revision;
                }
                else
                {
                    // A sibling failure already failed this selection and detached its file list,
                    // or the selection was retired. Only the first failure may set the selection
                    // error, and this blob's deletion must be scheduled here: the earlier
                    // ownership transition skipped it while this write was still active.
                    blob.Owner = false;
                    this.ScheduleDeleteIfReadyLocked(blob);
                }
            }

            return new AttachmentOperationResult(
                false,
                ex is OperationCanceledException,
                revision,
                NormalizeUploadError(ex)
            );
        }

        lock (this._sync)
        {
            this.EndWriteStorageOperationLocked(blob);
            if (
                this.TryGetUploadingSelectionLocked(pickerId, token, out var pending)
                && pending.Files.Contains(blob)
                && blob.Owner
            )
            {
                blob.Uploaded = true;
                return new AttachmentOperationResult(true, false, pending.Revision, null);
            }

            blob.Owner = false;
            this.ScheduleDeleteIfReadyLocked(blob);
            return Stale("The attachment upload completed after its selection was retired.");
        }

        static AttachmentOperationResult Stale(string error) => new(false, true, 0, error);
    }

    public AttachmentOperationResult CommitSelection(Guid pickerId, Guid token)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (
                !this.TryGetUploadingSelectionLocked(pickerId, token, out var pending)
                || !this._pickers.TryGetValue(pickerId, out var picker)
            )
            {
                return new AttachmentOperationResult(
                    false,
                    true,
                    0,
                    "The attachment selection is no longer current."
                );
            }

            if (pending.Files.Any(file => !file.Uploaded))
            {
                return new AttachmentOperationResult(
                    false,
                    false,
                    pending.Revision,
                    "The attachment selection is still uploading."
                );
            }

            foreach (var oldFile in picker.Files)
            {
                oldFile.Owner = false;
                this.ScheduleDeleteIfReadyLocked(oldFile);
            }

            picker.Files = pending.Files;
            picker.Revision = pending.Revision;
            picker.Pending = null;
            pending.Cancellation.Dispose();
            return new AttachmentOperationResult(true, false, picker.Revision, null);
        }
    }

    public AttachmentOperationResult CancelSelection(Guid pickerId, Guid token)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (
                !this._pickers.TryGetValue(pickerId, out var picker)
                || picker.Pending is not { } pending
                || pending.Token != token
            )
            {
                return new AttachmentOperationResult(
                    false,
                    true,
                    0,
                    "The attachment selection is no longer current."
                );
            }

            picker.Revision = pending.Revision;
            this.InvalidatePendingLocked(picker);
            return new AttachmentOperationResult(true, false, picker.Revision, null);
        }
    }

    public AttachmentOperationResult CancelFailedSelection(Guid pickerId, long expectedRevision)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (
                !this._pickers.TryGetValue(pickerId, out var picker)
                || picker.Pending is not { Status: AttachmentSelectionStatus.Failed } pending
                || pending.Revision != expectedRevision
            )
            {
                return new AttachmentOperationResult(
                    false,
                    true,
                    0,
                    "The failed attachment selection is no longer current."
                );
            }

            picker.Revision = pending.Revision;
            this.InvalidatePendingLocked(picker);
            return new AttachmentOperationResult(true, false, picker.Revision, null);
        }
    }

    public bool RemoveFile(Guid pickerId, Guid fileId)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (!this._pickers.TryGetValue(pickerId, out var picker))
            {
                return false;
            }

            var nextRevision = NextRevision(picker);
            var changed = this.InvalidatePendingLocked(picker);
            var file = picker.Files.FirstOrDefault(candidate => candidate.Id == fileId);
            if (file is not null)
            {
                picker.Files.Remove(file);
                file.Owner = false;
                this.ScheduleDeleteIfReadyLocked(file);
                changed = true;
            }

            if (changed)
            {
                picker.Revision = nextRevision;
            }

            return changed;
        }
    }

    public bool ClearFiles(Guid pickerId)
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (!this._pickers.TryGetValue(pickerId, out var picker))
            {
                return false;
            }

            var nextRevision = NextRevision(picker);
            var changed = this.InvalidatePendingLocked(picker);
            if (picker.Files.Count > 0)
            {
                foreach (var file in picker.Files)
                {
                    file.Owner = false;
                    this.ScheduleDeleteIfReadyLocked(file);
                }

                picker.Files = [];
                changed = true;
            }

            if (changed)
            {
                picker.Revision = nextRevision;
            }

            return changed;
        }
    }

    public Stream OpenRead(Guid pickerId, Guid fileId)
    {
        BlobEntry blob;
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (
                !this._pickers.TryGetValue(pickerId, out var picker)
                || picker.Files.FirstOrDefault(file => file.Id == fileId) is not { } found
                || !found.Owner
                || !found.Uploaded
            )
            {
                throw new InvalidOperationException("The attachment is no longer available.");
            }

            blob = found;
            blob.Leases++;
            this.BeginStorageOperationLocked();
        }

        Stream inner;
        try
        {
            inner = this._storage.OpenRead(blob.Id);
        }
        catch
        {
            lock (this._sync)
            {
                blob.Leases--;
                this.EndStorageOperationLocked();
                this.ScheduleDeleteIfReadyLocked(blob);
            }

            throw;
        }

        LeasedReadStream? lease = null;
        lease = new LeasedReadStream(inner, () => this.ReleaseLease(blob, lease!));
        var reject = false;
        lock (this._sync)
        {
            if (this._disposed)
            {
                reject = true;
            }
            else
            {
                this._leases.Add(lease);
                this.EndStorageOperationLocked();
            }
        }

        if (reject)
        {
            lease.Dispose();
            lock (this._sync)
            {
                this.EndStorageOperationLocked();
            }

            throw new ObjectDisposedException(nameof(AttachmentStore));
        }

        return lease;
    }

    public AttachmentInvokeValidationResult ValidateInvoke(
        IReadOnlyDictionary<Guid, long>? revisions
    )
    {
        lock (this._sync)
        {
            this.ThrowIfDisposed();
            if (this._pickers.Values.Any(picker => picker.Pending is not null))
            {
                return new AttachmentInvokeValidationResult(
                    false,
                    "An attachment selection is still unsettled."
                );
            }

            revisions ??= new Dictionary<Guid, long>();
            // Invoke pruning runs immediately before this check. Requiring an exact snapshot then
            // proves that the authoritative browser projection includes every retained picker an
            // arbitrary handler could read, rather than validating only client-selected entries.
            if (revisions.Count != this._pickers.Count)
            {
                return new AttachmentInvokeValidationResult(
                    false,
                    "The attachment revision snapshot is incomplete."
                );
            }

            foreach (var (pickerId, picker) in this._pickers)
            {
                if (
                    !revisions.TryGetValue(pickerId, out var revision)
                    || revision != picker.Revision
                )
                {
                    return new AttachmentInvokeValidationResult(
                        false,
                        "An attachment picker revision changed before invocation."
                    );
                }
            }

            return AttachmentInvokeValidationResult.Success;
        }
    }

    public void Retain(ISet<Guid> retainedIds)
    {
        if (retainedIds is null)
        {
            throw new ArgumentNullException(nameof(retainedIds));
        }

        lock (this._sync)
        {
            foreach (var pickerId in this._pickers.Keys.ToList())
            {
                if (retainedIds.Contains(pickerId))
                {
                    continue;
                }

                var picker = this._pickers[pickerId];
                this.InvalidatePendingLocked(picker);
                foreach (var file in picker.Files)
                {
                    file.Owner = false;
                    this.ScheduleDeleteIfReadyLocked(file);
                }

                this._pickers.Remove(pickerId);
            }
        }
    }

    public void Dispose()
    {
        Task disposeTask;
        lock (this._sync)
        {
            if (this._disposeTask is null)
            {
                // Mark the store unavailable synchronously, but run cancellation callbacks, lease
                // closure, and storage drain on the thread pool so a non-conforming custom storage
                // implementation cannot block this caller beyond the configured limit.
                this._disposed = true;
                this._disposeTask = Task.Run(this.DisposeCore);
            }

            disposeTask = this._disposeTask;
        }

        bool completed;
        try
        {
            completed = disposeTask.Wait(this._storageDrainTimeout);
        }
        catch (AggregateException)
        {
            // Task.Wait wraps a completed task's error. The single GetResult below preserves the
            // original storage/cleanup exception instead.
            completed = true;
        }

        if (!completed)
        {
            throw new TimeoutException(
                $"Attachment storage did not drain within {this._storageDrainTimeout}. Cleanup is continuing in the background."
            );
        }

        disposeTask.GetAwaiter().GetResult();
    }

    private void DisposeCore()
    {
        List<LeasedReadStream> leases;
        lock (this._sync)
        {
            foreach (var picker in this._pickers.Values)
            {
                this.InvalidatePendingLocked(picker);
                foreach (var file in picker.Files)
                {
                    file.Owner = false;
                    this.ScheduleDeleteIfReadyLocked(file);
                }
            }

            this._pickers.Clear();
            leases = [.. this._leases];
        }

        CancelIgnoringCallbackErrors(this._cleanupCancellation);

        foreach (var lease in leases)
        {
            lease.ForceClose();
        }

        this._storageOperationsDrained.Wait();

        while (true)
        {
            Task[] tasks;
            lock (this._sync)
            {
                tasks = [.. this._cleanupTasks];
            }

            if (tasks.Length == 0)
            {
                break;
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();
        }

        try
        {
            this._storage.Dispose();
        }
        finally
        {
            this._cleanupCancellation.Dispose();
            this._storageOperationsDrained.Dispose();
        }
    }

    private static AttachmentPickerSnapshot Snapshot(PickerEntry entry)
    {
        var pending = entry.Pending;
        return new AttachmentPickerSnapshot(
            pending?.Revision ?? entry.Revision,
            pending?.Status ?? AttachmentSelectionStatus.Stable,
            [.. entry.Files.Select(file => file.PublicFile)],
            pending?.Error
        );
    }

    private static long NextRevision(PickerEntry picker) =>
        Math.Max(picker.Revision, picker.Pending?.Revision ?? picker.Revision) + 1;

    private bool TryGetUploadingSelectionLocked(
        Guid pickerId,
        Guid token,
        out PendingSelection pending
    )
    {
        if (
            !this._disposed
            && this._pickers.TryGetValue(pickerId, out var picker)
            && picker.Pending is { Status: AttachmentSelectionStatus.Uploading } current
            && current.Token == token
        )
        {
            pending = current;
            return true;
        }

        pending = null!;
        return false;
    }

    private bool InvalidatePendingLocked(PickerEntry picker)
    {
        if (picker.Pending is not { } pending)
        {
            return false;
        }

        CancelIgnoringCallbackErrors(pending.Cancellation);
        pending.Cancellation.Dispose();
        foreach (var file in pending.Files)
        {
            file.Owner = false;
            this.ScheduleDeleteIfReadyLocked(file);
        }

        picker.Pending = null;
        return true;
    }

    private void FailPendingLocked(PickerEntry picker, string error)
    {
        if (picker.Pending is not { } pending)
        {
            return;
        }

        pending.Status = AttachmentSelectionStatus.Failed;
        pending.Error = error;
        CancelIgnoringCallbackErrors(pending.Cancellation);
        foreach (var file in pending.Files)
        {
            file.Owner = false;
            this.ScheduleDeleteIfReadyLocked(file);
        }

        pending.Files = [];
    }

    private void ReleaseLease(BlobEntry blob, LeasedReadStream lease)
    {
        lock (this._sync)
        {
            this._leases.Remove(lease);
            if (blob.Leases > 0)
            {
                blob.Leases--;
            }

            this.ScheduleDeleteIfReadyLocked(blob);
        }
    }

    private void ScheduleDeleteIfReadyLocked(BlobEntry blob)
    {
        if (blob.Owner || blob.ActiveWrites > 0 || blob.Leases > 0 || blob.DeleteScheduled)
        {
            return;
        }

        blob.DeleteScheduled = true;
        var task = Task.Run(async () =>
        {
            var deleted = false;
            var retryDelay = TimeSpan.FromMilliseconds(10);
            while (!this._cleanupCancellation.IsCancellationRequested)
            {
                try
                {
                    await this
                        ._storage.DeleteAsync(blob.Id, this._cleanupCancellation.Token)
                        .ConfigureAwait(false);
                    deleted = true;
                    break;
                }
                catch (OperationCanceledException)
                    when (this._cleanupCancellation.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // A transient custom-storage failure must neither surface on an eval/HTTP
                    // thread nor permanently consume quota. Retry until deletion succeeds or the
                    // session disposal boundary takes ownership of final cleanup.
                }

                try
                {
                    await Task.Delay(retryDelay, this._cleanupCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                retryDelay = TimeSpan.FromMilliseconds(
                    Math.Min(retryDelay.TotalMilliseconds * 2, 1000)
                );
            }

            lock (this._sync)
            {
                if (deleted)
                {
                    this._blobIds.Remove(blob.Id);
                    // Quota measures physical storage still owned by the session, not logical
                    // picker reachability. Releasing it earlier could admit unbounded retained
                    // data while a custom storage implementation retries or stalls deletion.
                    this._usedBytes -= blob.Size;
                    this._usedFiles--;
                }
                else
                {
                    blob.DeleteScheduled = false;
                }
            }
        });
        this._cleanupTasks.Add(task);
        _ = task.ContinueWith(
            _ =>
            {
                lock (this._sync)
                {
                    this._cleanupTasks.Remove(task);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private void BeginStorageOperationLocked()
    {
        if (this._activeStorageOperations++ == 0)
        {
            this._storageOperationsDrained.Reset();
        }
    }

    private void EndWriteStorageOperationLocked(BlobEntry blob)
    {
        if (blob.ActiveWrites <= 0)
        {
            throw new InvalidOperationException("The attachment write operation is not active.");
        }

        blob.ActiveWrites--;
        this.EndStorageOperationLocked();
    }

    private void EndStorageOperationLocked()
    {
        if (this._activeStorageOperations <= 0)
        {
            throw new InvalidOperationException("The attachment storage operation is not active.");
        }

        if (--this._activeStorageOperations == 0)
        {
            this._storageOperationsDrained.Set();
        }
    }

    private static async Task CopyExactAsync(
        Stream input,
        Stream output,
        long expectedBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[81920];
        long total = 0;
        while (total < expectedBytes)
        {
            var readSize = (int)Math.Min(buffer.Length, expectedBytes - total);
            var read = await input
                .ReadAsync(buffer.AsMemory(0, readSize), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException(
                    "The attachment body ended before its declared size."
                );
            }

            await output
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            total += read;
        }

        var probe = await input
            .ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
            .ConfigureAwait(false);
        if (probe != 0)
        {
            throw new InvalidDataException("The attachment body exceeds its declared size.");
        }
    }

    private static string NormalizeUploadError(Exception exception) =>
        exception switch
        {
            OperationCanceledException => "The attachment upload was cancelled.",
            InvalidDataException invalid => invalid.Message,
            _ => $"Attachment upload failed: {exception.Message}",
        };

    private static string SanitizeFileName(string? value)
    {
        var normalized =
            (value ?? "")
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()
            ?? "attachment";
        var chars = normalized.Where(character => !char.IsControl(character)).Take(255).ToArray();
        var result = new string(chars).Trim();
        return result.Length == 0 ? "attachment" : result;
    }

    private static string SanitizeContentType(string? value)
    {
        var chars = (value ?? "")
            .Where(character => !char.IsControl(character))
            .Take(255)
            .ToArray();
        return new string(chars).Trim();
    }

    private void ThrowIfDisposed()
    {
        if (this._disposed)
        {
            throw new ObjectDisposedException(nameof(AttachmentStore));
        }
    }

    private static void CancelIgnoringCallbackErrors(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // A custom stream may register a throwing callback. Cancellation is already signaled;
            // cleanup ownership transitions must still complete.
        }
    }
}
