using System.Text;
using Duets.Pad;
using Duets.Pad.Attachments;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;

namespace Duets.Pad.Tests;

/// <summary>
/// Integration tests for transactional file-picker state, attachment lifetime, invocation
/// preconditions, quotas, and Jint host-capability marshaling (ADR-50).
/// </summary>
public sealed class DuetsPadAttachmentTests
{
    private sealed class TrackingAttachmentStorage : IAttachmentStorage
    {
        private readonly Dictionary<Guid, byte[]> _committed = [];

        public bool Disposed { get; private set; }

        public ValueTask<Stream> CreateWriteStreamAsync(
            Guid blobId,
            CancellationToken cancellationToken = default
        ) => new(new MemoryStream());

        public ValueTask CommitAsync(Guid blobId, CancellationToken cancellationToken = default)
        {
            this._committed[blobId] = [];
            return default;
        }

        public Stream OpenRead(Guid blobId) => new MemoryStream(this._committed[blobId]);

        public ValueTask DeleteAsync(Guid blobId, CancellationToken cancellationToken = default)
        {
            this._committed.Remove(blobId);
            return default;
        }

        public void Dispose() => this.Disposed = true;
    }

    private sealed class CancellationBlockingStream : Stream
    {
        private readonly TaskCompletionSource _readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task ReadStarted => this._readStarted.Task;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            this._readStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class FlakyDeleteAttachmentStorage(int failuresBeforeSuccess = 1)
        : IAttachmentStorage
    {
        private readonly int _failuresBeforeSuccess = failuresBeforeSuccess;
        private readonly TaskCompletionSource _attempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _deleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _deleteAttempts;

        public Task Attempted => this._attempted.Task;
        public Task Deleted => this._deleted.Task;
        public int DeleteAttempts => Volatile.Read(ref this._deleteAttempts);
        public bool Disposed { get; private set; }

        public ValueTask<Stream> CreateWriteStreamAsync(
            Guid blobId,
            CancellationToken cancellationToken = default
        ) => new(new MemoryStream());

        public ValueTask CommitAsync(Guid blobId, CancellationToken cancellationToken = default) =>
            default;

        public Stream OpenRead(Guid blobId) => new MemoryStream();

        public ValueTask DeleteAsync(Guid blobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = Interlocked.Increment(ref this._deleteAttempts);
            this._attempted.TrySetResult();
            if (attempt <= this._failuresBeforeSuccess)
            {
                throw new IOException("Transient delete failure.");
            }

            this._deleted.TrySetResult();
            return default;
        }

        public void Dispose() => this.Disposed = true;
    }

    private sealed class BlockingCreateAttachmentStorage : IAttachmentStorage
    {
        private readonly TaskCompletionSource<Stream> _releaseCreate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _createStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task CreateStarted => this._createStarted.Task;
        public Task Disposed => this._disposed.Task;

        public ValueTask<Stream> CreateWriteStreamAsync(
            Guid blobId,
            CancellationToken cancellationToken = default
        )
        {
            this._createStarted.TrySetResult();
            return new ValueTask<Stream>(this._releaseCreate.Task);
        }

        public ValueTask CommitAsync(Guid blobId, CancellationToken cancellationToken = default) =>
            default;

        public Stream OpenRead(Guid blobId) => new MemoryStream();

        public ValueTask DeleteAsync(Guid blobId, CancellationToken cancellationToken = default) =>
            default;

        public void ReleaseCreate() => this._releaseCreate.TrySetResult(new MemoryStream());

        public void Dispose() => this._disposed.TrySetResult();
    }

    private static async Task<DuetsPadSession> CreatePadSessionAsync(
        long maxBytesPerFile = 16 * 1024 * 1024,
        long maxBytesPerSession = 64 * 1024 * 1024,
        int maxFilesPerSession = 32
    )
    {
        var duetsSession = await JintTestRuntime.CreateSessionAsync();
        return new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            maxAttachmentBytesPerFile: maxBytesPerFile,
            maxAttachmentBytesPerSession: maxBytesPerSession,
            maxAttachmentsPerSession: maxFilesPerSession
        );
    }

    private static Guid GetPickerId(DuetsPadSession session)
    {
        var picker = Assert.IsType<Element>(session.Canvas.State.Root.Children.Single());
        Assert.Equal("file", picker.Attributes["data-duetspad-field-kind"]);
        return Guid.Parse(picker.Attributes["data-duetspad-field"]!);
    }

    private static async Task<(Guid PickerId, BeginAttachmentSelectionResult Begin)> BeginAsync(
        DuetsPadSession session,
        params AttachmentFileManifest[] files
    )
    {
        var pickerId = GetPickerId(session);
        var begin = await session.BeginAttachmentSelectionAsync(pickerId, files);
        return (pickerId, begin);
    }

    [Fact]
    public async Task FilePicker_renders_native_input_and_option_attributes()
    {
        using var session = await CreatePadSessionAsync();

        var result = await session.EvaluateAsync(
            """
            var picker = ui.filePicker({
              accept: ".txt,text/plain",
              multiple: true,
              disabled: true,
              title: "Attach input"
            });
            canvas.add(picker);
            """
        );

        Assert.True(result.Ok, result.Error);
        var wrapper = Assert.IsType<Element>(session.Canvas.State.Root.Children.Single());
        Assert.Equal("file", wrapper.Attributes["data-duetspad-field-kind"]);
        Assert.Equal("0", wrapper.Attributes["data-duetspad-attachment-revision"]);
        Assert.Equal("duetspad-file-picker", wrapper.Attributes["class"]);
        var input = Assert.IsType<Element>(wrapper.Children[0]);
        Assert.Equal("file", input.Attributes["type"]);
        Assert.Equal(".txt,text/plain", input.Attributes["accept"]);
        Assert.True(input.Attributes.ContainsKey("multiple"));
        Assert.True(input.Attributes.ContainsKey("disabled"));
        Assert.Equal("Attach input", input.Attributes["title"]);
    }

    [Fact]
    public async Task Committed_selection_is_atomic_and_OpenRead_works_without_AllowClr()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync(
            "var picker = ui.filePicker({ multiple: true }); canvas.add(picker);"
        );

        var (pickerId, begin) = await BeginAsync(
            session,
            new AttachmentFileManifest("folder\\hello.txt", "text/plain", 5)
        );
        Assert.True(begin.Ok, begin.Error);
        var handle = Assert.IsType<DisplayFilePicker>(
            session.DuetsSession.Evaluate("picker").ToObject()
        );
        Assert.Empty(handle.Files);

        var serverFile = Assert.Single(begin.Files);
        await using var body = new MemoryStream("hello"u8.ToArray());
        var upload = await session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            serverFile.Id,
            body,
            TestContext.Current.CancellationToken
        );
        Assert.True(upload.Ok, upload.Error);

        var commit = await session.CommitAttachmentSelectionAsync(pickerId, begin.Token);
        Assert.True(commit.Ok, commit.Error);
        Assert.Equal(begin.Revision, commit.Revision);

        var count = await session.EvaluateAsync("picker.files.length");
        Assert.True(count.Ok, count.Error);
        Assert.Equal("1", count.Result);
        var name = await session.EvaluateAsync("picker.files[0].name");
        Assert.Equal("hello.txt", name.Result);
        var streamReadable = await session.EvaluateAsync(
            "var attachmentStream = picker.files[0].openRead(); var canRead = attachmentStream.CanRead; attachmentStream.Dispose(); canRead"
        );
        Assert.True(streamReadable.Ok, streamReadable.Error);
        Assert.Equal("true", streamReadable.Result);
        var text = await session.EvaluateAsync("picker.files[0].readAllText()");
        Assert.True(text.Ok, text.Error);
        Assert.Equal("hello", text.Result);
        var bytes = await session.EvaluateAsync(
            "var bytes = picker.files[0].readAllBytes(); `${bytes instanceof Uint8Array}:${bytes.length}:${Array.from(bytes).join(',')}`"
        );
        Assert.True(bytes.Ok, bytes.Error);
        Assert.Equal("true:5:104,101,108,108,111", bytes.Result);

        var declarations = session.DuetsSession.Declarations.GetDeclarations();
        Assert.Contains(
            declarations,
            declaration =>
                declaration.Content.Contains(
                    "class Stream extends System.MarshalByRefObject",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            declarations,
            declaration =>
                declaration.Content.Contains("readAllBytes(): Uint8Array", StringComparison.Ordinal)
        );
        Assert.Contains(
            declarations,
            declaration =>
                declaration.Content.Contains("readAllText(): string", StringComparison.Ordinal)
        );
        Assert.Contains(
            declarations,
            declaration =>
                declaration.Content.Contains(
                    "openRead(): System.IO.Stream",
                    StringComparison.Ordinal
                )
        );
        Assert.DoesNotContain(
            declarations,
            declaration =>
                declaration.Content.Contains("DuetsPadAttachmentStream", StringComparison.Ordinal)
        );

        session.DuetsSession.SetValue(
            "__readAttachment__",
            new Func<Stream, string>(stream =>
            {
                using (stream)
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            })
        );
        var streamContents = await session.EvaluateAsync(
            "__readAttachment__(picker.files[0].openRead())"
        );
        Assert.True(streamContents.Ok, streamContents.Error);
        Assert.Equal("hello", streamContents.Result);
    }

    [Fact]
    public async Task Invoke_rejects_unsettled_or_mismatched_attachment_state_without_running_handler()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync(
            """
            var picker = ui.filePicker();
            var invoked = 0;
            canvas.add(ui.stack([picker, ui.button("Run", () => { invoked++; })]));
            """
        );
        Assert.True(session.TryGetCanvasSnapshot("default", out var snapshot));
        var handlerId = Assert.Single(snapshot.Interactions).HandlerId;
        var pickerId = Guid.Parse(
            Assert
                .IsType<Element>(
                    Assert.IsType<Element>(snapshot.State.Root.Children.Single()).Children[0]
                )
                .Attributes["data-duetspad-field"]!
        );
        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("a.txt", "text/plain", 1)]
        );

        var pendingInvoke = await session.InvokeInteractionAsync(
            handlerId,
            attachmentRevisions: new Dictionary<Guid, long> { [pickerId] = begin.Revision }
        );
        Assert.False(pendingInvoke.Ok);
        Assert.True(pendingInvoke.AttachmentConflict);
        Assert.Equal("0", (await session.EvaluateAsync("invoked")).Result);

        var file = Assert.Single(begin.Files);
        await using var body = new MemoryStream("x"u8.ToArray());
        Assert.True(
            (
                await session.UploadAttachmentFileAsync(
                    pickerId,
                    begin.Token,
                    file.Id,
                    body,
                    TestContext.Current.CancellationToken
                )
            ).Ok
        );
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, begin.Token)).Ok);

        var wrongRevision = await session.InvokeInteractionAsync(
            handlerId,
            attachmentRevisions: new Dictionary<Guid, long> { [pickerId] = begin.Revision - 1 }
        );
        Assert.False(wrongRevision.Ok);
        Assert.True(wrongRevision.AttachmentConflict);
        Assert.Equal("0", (await session.EvaluateAsync("invoked")).Result);

        var invoke = await session.InvokeInteractionAsync(
            handlerId,
            attachmentRevisions: new Dictionary<Guid, long> { [pickerId] = begin.Revision }
        );
        Assert.True(invoke.Ok, invoke.Error);
        Assert.Equal("1", (await session.EvaluateAsync("invoked")).Result);
    }

    [Fact]
    public async Task Invoke_prunes_a_picker_rendered_without_a_committed_projection()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync(
            "var orphan = ui.filePicker(); var invoked = 0; canvas.add(ui.button('Run', () => { invoked++; }));"
        );
        var orphan = Assert.IsType<DisplayFilePicker>(
            session.DuetsSession.Evaluate("orphan").ToObject()
        );
        _ = orphan.Render();

        Assert.True(session.TryGetCanvasSnapshot("default", out var snapshot));
        var handlerId = Assert.Single(snapshot.Interactions).HandlerId;
        var invoke = await session.InvokeInteractionAsync(
            handlerId,
            attachmentRevisions: new Dictionary<Guid, long>()
        );

        Assert.True(invoke.Ok, invoke.Error);
        Assert.Equal("1", (await session.EvaluateAsync("invoked")).Result);
    }

    [Fact]
    public async Task Reselection_retires_the_old_token_and_only_the_new_selection_can_commit()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);

        var first = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("old.txt", "text/plain", 3)]
        );
        var second = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("new.txt", "text/plain", 3)]
        );
        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.True(second.Revision > first.Revision);

        await using var oldBody = new MemoryStream("old"u8.ToArray());
        var staleUpload = await session.UploadAttachmentFileAsync(
            pickerId,
            first.Token,
            Assert.Single(first.Files).Id,
            oldBody,
            TestContext.Current.CancellationToken
        );
        Assert.False(staleUpload.Ok);
        Assert.True(staleUpload.Stale);

        await using var newBody = new MemoryStream("new"u8.ToArray());
        var upload = await session.UploadAttachmentFileAsync(
            pickerId,
            second.Token,
            Assert.Single(second.Files).Id,
            newBody,
            TestContext.Current.CancellationToken
        );
        Assert.True(upload.Ok, upload.Error);
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, second.Token)).Ok);
        Assert.Equal("new.txt", (await session.EvaluateAsync("picker.files[0].name")).Result);
    }

    [Fact]
    public async Task Older_browser_generation_cannot_retire_a_newer_generation_processed_first()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var clientId = Guid.NewGuid();

        var newer = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("new.txt", "text/plain", 3)],
            new AttachmentSelectionOrder(clientId, 2)
        );
        var delayedOlder = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("old.txt", "text/plain", 3)],
            new AttachmentSelectionOrder(clientId, 1)
        );

        Assert.True(newer.Ok, newer.Error);
        Assert.False(delayedOlder.Ok);
        Assert.Contains("superseded", delayedOlder.Error);

        await using var body = new MemoryStream("new"u8.ToArray());
        Assert.True(
            (
                await session.UploadAttachmentFileAsync(
                    pickerId,
                    newer.Token,
                    Assert.Single(newer.Files).Id,
                    body,
                    TestContext.Current.CancellationToken
                )
            ).Ok
        );
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, newer.Token)).Ok);
        Assert.Equal("new.txt", (await session.EvaluateAsync("picker.files[0].name")).Result);
    }

    [Fact]
    public async Task Browser_generation_order_survives_picker_pruning_and_replacement()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var clientId = Guid.NewGuid();
        var first = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [],
            new AttachmentSelectionOrder(clientId, 2)
        );
        Assert.True(first.Ok, first.Error);
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, first.Token)).Ok);

        await session.EvaluateAsync("canvas.clear(); canvas.add(picker);");
        var delayedOlder = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [],
            new AttachmentSelectionOrder(clientId, 1)
        );

        Assert.False(delayedOlder.Ok);
        Assert.Contains("superseded", delayedOlder.Error);
    }

    [Fact]
    public async Task Reachability_pruning_revokes_new_reads_but_an_existing_lease_remains_readable()
    {
        using var session = await CreatePadSessionAsync();
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var (pickerId, begin) = await BeginAsync(
            session,
            new AttachmentFileManifest("leased.txt", "text/plain", 6)
        );
        var file = Assert.Single(begin.Files);
        await using var body = new MemoryStream("leased"u8.ToArray());
        Assert.True(
            (
                await session.UploadAttachmentFileAsync(
                    pickerId,
                    begin.Token,
                    file.Id,
                    body,
                    TestContext.Current.CancellationToken
                )
            ).Ok
        );
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, begin.Token)).Ok);

        var handle = Assert.IsType<DisplayFilePicker>(
            session.DuetsSession.Evaluate("picker").ToObject()
        );
        var committedFile = Assert.Single(handle.Files);
        using var lease = committedFile.OpenRead();

        await session.EvaluateAsync("canvas.clear();");

        Assert.Empty(handle.Files);
        Assert.Throws<InvalidOperationException>(committedFile.OpenRead);
        using var reader = new StreamReader(lease, Encoding.UTF8, leaveOpen: true);
        Assert.Equal("leased", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Declared_length_is_enforced_before_commit()
    {
        using var session = await CreatePadSessionAsync(
            maxBytesPerFile: 3,
            maxBytesPerSession: 4,
            maxFilesPerSession: 1
        );
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);

        var oversized = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("large.bin", "application/octet-stream", 4)]
        );
        Assert.False(oversized.Ok);
        Assert.True(oversized.TooLarge);

        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("short.bin", "application/octet-stream", 3)]
        );
        Assert.True(begin.Ok, begin.Error);
        await using var shortBody = new MemoryStream("ab"u8.ToArray());
        var upload = await session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            Assert.Single(begin.Files).Id,
            shortBody,
            TestContext.Current.CancellationToken
        );
        Assert.False(upload.Ok);
        Assert.Contains("ended before", upload.Error);
        Assert.False((await session.CommitAttachmentSelectionAsync(pickerId, begin.Token)).Ok);
    }

    [Fact]
    public async Task Sibling_upload_failure_releases_the_other_staged_file_and_keeps_the_original_error()
    {
        using var session = await CreatePadSessionAsync(
            maxBytesPerFile: 1,
            maxBytesPerSession: 2,
            maxFilesPerSession: 2
        );
        await session.EvaluateAsync(
            "var picker = ui.filePicker({ multiple: true }); canvas.add(picker);"
        );
        var (pickerId, begin) = await BeginAsync(
            session,
            new AttachmentFileManifest("a.bin", "", 1),
            new AttachmentFileManifest("b.bin", "", 1)
        );
        Assert.True(begin.Ok, begin.Error);

        // Park the sibling upload mid-read so the first failure cancels it through the shared
        // selection token — the same shape the browser's per-generation AbortController produces.
        var blocking = new CancellationBlockingStream();
        var siblingUpload = session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            begin.Files[1].Id,
            blocking,
            TestContext.Current.CancellationToken
        );
        await blocking.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);

        await using var truncated = new MemoryStream();
        var failedUpload = await session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            begin.Files[0].Id,
            truncated,
            TestContext.Current.CancellationToken
        );
        Assert.False(failedUpload.Ok);
        Assert.Contains("ended before", failedUpload.Error);
        Assert.False((await siblingUpload).Ok);

        // The stored selection error must remain the original failure, not the sibling's
        // cancellation observed while the selection was already failed.
        await session.EvaluateAsync("canvas.add(picker)");
        var rendered = Assert.IsType<Element>(session.Canvas.State.Root.Children[1]);
        Assert.Contains("ended before", rendered.Attributes["data-duetspad-attachment-error"]);

        // Both staged blobs must release their quota reservation once deletion completes; the
        // cancelled sibling's write ends only after the selection has already been failed.
        BeginAttachmentSelectionResult? retry = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            retry = await session.BeginAttachmentSelectionAsync(
                pickerId,
                [
                    new AttachmentFileManifest("c.bin", "", 1),
                    new AttachmentFileManifest("d.bin", "", 1),
                ]
            );
            if (retry.Ok)
            {
                break;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(retry);
        Assert.True(retry.Ok, retry.Error);
    }

    [Fact]
    public async Task Per_file_session_total_and_file_count_quotas_are_enforced()
    {
        using (var perFile = await CreatePadSessionAsync(3, 8, 4))
        {
            await perFile.EvaluateAsync(
                "var picker = ui.filePicker({ multiple: true }); canvas.add(picker);"
            );
            var result = await perFile.BeginAttachmentSelectionAsync(
                GetPickerId(perFile),
                [new AttachmentFileManifest("large.bin", "", 4)]
            );
            Assert.False(result.Ok);
            Assert.True(result.TooLarge);
        }

        using (var count = await CreatePadSessionAsync(3, 8, 1))
        {
            await count.EvaluateAsync(
                "var picker = ui.filePicker({ multiple: true }); canvas.add(picker);"
            );
            var result = await count.BeginAttachmentSelectionAsync(
                GetPickerId(count),
                [
                    new AttachmentFileManifest("a.bin", "", 0),
                    new AttachmentFileManifest("b.bin", "", 0),
                ]
            );
            Assert.False(result.Ok);
            Assert.True(result.TooLarge);
        }

        using var aggregate = await CreatePadSessionAsync(3, 4, 4);
        await aggregate.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(aggregate);
        var first = await aggregate.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("first.bin", "", 3)]
        );
        await using var body = new MemoryStream("one"u8.ToArray());
        Assert.True(
            (
                await aggregate.UploadAttachmentFileAsync(
                    pickerId,
                    first.Token,
                    Assert.Single(first.Files).Id,
                    body,
                    TestContext.Current.CancellationToken
                )
            ).Ok
        );
        Assert.True((await aggregate.CommitAttachmentSelectionAsync(pickerId, first.Token)).Ok);

        var replacement = await aggregate.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("replacement.bin", "", 2)]
        );
        Assert.False(replacement.Ok);
        Assert.True(replacement.TooLarge);
    }

    [Fact]
    public void Attachment_options_reject_invalid_limits_and_a_null_storage_factory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuetsPadServiceOptions { MaxAttachmentBytesPerFile = 0 }.Validate()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuetsPadServiceOptions
            {
                MaxAttachmentBytesPerFile = 4,
                MaxAttachmentBytesPerSession = 3,
            }.Validate()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuetsPadServiceOptions { MaxAttachmentsPerSession = 0 }.Validate()
        );
        Assert.Throws<ArgumentNullException>(() =>
            new DuetsPadServiceOptions { AttachmentStorageFactory = null! }.Validate()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuetsPadServiceOptions { AttachmentStorageDrainTimeout = TimeSpan.Zero }.Validate()
        );
    }

    [Fact]
    public async Task Session_disposal_cancels_and_drains_an_active_upload_before_disposing_storage()
    {
        var storage = new TrackingAttachmentStorage();
        var duetsSession = await JintTestRuntime.CreateSessionAsync();
        var session = new DuetsPadSession(Guid.NewGuid(), duetsSession, attachmentStorage: storage);
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("blocked.bin", "application/octet-stream", 1)]
        );
        var input = new CancellationBlockingStream();
        var upload = session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            Assert.Single(begin.Files).Id,
            input,
            TestContext.Current.CancellationToken
        );
        await input.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);

        await Task.Run(session.Dispose, TestContext.Current.CancellationToken);
        var result = await upload;

        Assert.False(result.Ok);
        Assert.True(storage.Disposed);
    }

    [Fact]
    public async Task Storage_drain_timeout_releases_the_caller_and_cleanup_retains_storage_ownership()
    {
        var storage = new BlockingCreateAttachmentStorage();
        var duetsSession = await JintTestRuntime.CreateSessionAsync();
        var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            attachmentStorage: storage,
            attachmentStorageDrainTimeout: TimeSpan.FromMilliseconds(50)
        );
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("blocked.bin", "application/octet-stream", 1)]
        );
        await using var input = new MemoryStream([1]);
        var upload = session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            Assert.Single(begin.Files).Id,
            input,
            TestContext.Current.CancellationToken
        );
        await storage.CreateStarted.WaitAsync(TestContext.Current.CancellationToken);

        var error = Assert.Throws<TimeoutException>(session.Dispose);
        Assert.Contains("continuing in the background", error.Message);
        Assert.False(storage.Disposed.IsCompleted);

        storage.ReleaseCreate();
        var uploadResult = await upload;
        Assert.False(uploadResult.Ok);
        await storage.Disposed.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Transient_storage_delete_failure_is_retried_and_releases_quota()
    {
        var storage = new FlakyDeleteAttachmentStorage();
        var duetsSession = await JintTestRuntime.CreateSessionAsync();
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            attachmentStorage: storage,
            maxAttachmentBytesPerFile: 1,
            maxAttachmentBytesPerSession: 1,
            maxAttachmentsPerSession: 1
        );
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var first = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("first.bin", "", 1)]
        );
        await using var firstBody = new MemoryStream([1]);
        Assert.True(
            (
                await session.UploadAttachmentFileAsync(
                    pickerId,
                    first.Token,
                    Assert.Single(first.Files).Id,
                    firstBody,
                    TestContext.Current.CancellationToken
                )
            ).Ok
        );
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, first.Token)).Ok);

        await session.EvaluateAsync("picker.clear()");
        await storage.Deleted.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(storage.DeleteAttempts >= 2);

        BeginAttachmentSelectionResult? second = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            second = await session.BeginAttachmentSelectionAsync(
                pickerId,
                [new AttachmentFileManifest("second.bin", "", 1)]
            );
            if (second.Ok)
            {
                break;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(second);
        Assert.True(second.Ok, second.Error);
    }

    [Fact]
    public async Task Upload_started_after_session_disposal_returns_stale_instead_of_throwing()
    {
        var session = await CreatePadSessionAsync();
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("late.bin", "", 1)]
        );
        session.Dispose();

        await using var body = new MemoryStream([1]);
        var result = await session.UploadAttachmentFileAsync(
            pickerId,
            begin.Token,
            Assert.Single(begin.Files).Id,
            body,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.Ok);
        Assert.True(result.Stale);
        Assert.Contains("disposed", result.Error);
    }

    [Fact]
    public async Task Quota_remains_reserved_during_delete_retry_and_disposal_cancels_it()
    {
        var storage = new FlakyDeleteAttachmentStorage(int.MaxValue);
        var duetsSession = await JintTestRuntime.CreateSessionAsync();
        var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            attachmentStorage: storage,
            maxAttachmentBytesPerFile: 1,
            maxAttachmentBytesPerSession: 1,
            maxAttachmentsPerSession: 1
        );
        await session.EvaluateAsync("var picker = ui.filePicker(); canvas.add(picker);");
        var pickerId = GetPickerId(session);
        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("retry.bin", "", 1)]
        );
        await using var body = new MemoryStream([1]);
        Assert.True(
            (
                await session.UploadAttachmentFileAsync(
                    pickerId,
                    begin.Token,
                    Assert.Single(begin.Files).Id,
                    body,
                    TestContext.Current.CancellationToken
                )
            ).Ok
        );
        Assert.True((await session.CommitAttachmentSelectionAsync(pickerId, begin.Token)).Ok);
        await session.EvaluateAsync("picker.clear()");
        await storage.Attempted.WaitAsync(TestContext.Current.CancellationToken);

        var beforePhysicalDeletion = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("replacement.bin", "", 1)]
        );
        Assert.False(beforePhysicalDeletion.Ok);
        Assert.True(beforePhysicalDeletion.TooLarge);

        await Task.Run(session.Dispose, TestContext.Current.CancellationToken);

        Assert.True(storage.Disposed);
    }
}
