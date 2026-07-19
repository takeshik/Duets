# ADR-50: DuetsPad File Attachment State and Upload Protocol

## Status

Accepted

## Context

ADR-47 gives `ui.*` ordinary form controls whose string values live in a server-canonical field
store. The browser commits those values on focus-out and includes a string snapshot in an ADR-41
interaction invoke so a handler observes a just-edited value. A file picker looks like another form
control, but its state cannot use that model directly:

- A browser `FileList` contains opaque browser objects and binary bodies, not a string value.
- Browsers deliberately prohibit assigning a non-empty value to `<input type="file">`; a reconnect or
  server projection can restore an attached-file list, but cannot restore the native picker selection.
- Upload is asynchronous. A user can select files and immediately click a server-backed button while
  bytes are still in flight, or select a replacement while the previous upload is completing.
- Multiple selected files must become visible to a handler as one selection. Committing each file as
  it arrives exposes partial state and lets completion order decide the result.
- File bodies are large relative to DuetsPad's JSON control messages. Base64 in the interaction body
  would add allocation and encoding overhead, repeat data on every click, and collide with ADR-49's
  general request-body ceiling.
- An upload may finish after its picker has been replaced, its Timeline entry trimmed, its canvas
  cleared, or its session disposed. A late completion must not resurrect unreachable state.

The attachment API must also remain useful when the active runtime is Jint without `AllowClr`.
Jint permits member access on a host object explicitly injected with `SetValue`, including a
`Stream` returned by one of its methods, even when CLR namespace/type importing is disabled.
`AllowClr` controls the broader ability to import and construct CLR types; it is not required to
consume a deliberately supplied attachment capability.

## Decision Drivers

- A handler observes either the complete current selection or no invocation; never a partial upload.
- Selection, reselection, removal, upload completion, and handler invocation have a deterministic
  serialization point.
- Stale or unreachable uploads cannot revive attachment state.
- Network and storage I/O do not hold the session eval semaphore or state lock.
- Attachment lifetime follows rendered reachability, reusing ADR-47's Canvas/Timeline marker scan.
- File bodies are streamed and bounded by file, session byte total, and session file count.
- Hosts can replace temporary-file storage for constrained or platform-specific environments.
- Scripts receive a streaming read capability without exposing storage paths, plus explicit
  bounded convenience methods when whole-file byte or text materialization is appropriate.
- The native picker remains honest about browser constraints: it is an ephemeral selector, not a
  restorable server-controlled file input.

## Considered Alternatives

### A: Encode file bodies in the interaction invoke snapshot

- Pro: The handler invocation and bytes arrive in one request, making the click an obvious commit
  boundary.
- Pro: No separate upload lifecycle or server-side staging protocol is needed.
- Con: Base64 expands the body and requires full browser/server materialization rather than streaming.
- Con: Every click retransmits the files, even when the selection has not changed.
- Con: A large upload occupies the interaction request and eval path, coupling network duration to
  handler latency.
- Con: The existing snapshot is intentionally a small JSON map of string values; binary data would
  erase that bounded control-message role.

### B: Upload and commit each selected file immediately

- Pro: One raw-body endpoint per file is straightforward and supports streaming and progress.
- Pro: A completed file becomes usable without waiting for its siblings.
- Con: A multi-file selection becomes visible partially, and upload completion order becomes
  observable server state.
- Con: Reselection cannot atomically replace the old selection; late files from an invalidated
  selection can mix with the new one unless a generation protocol is added.
- Con: Client-side Promise waiting alone leaves a time-of-check/time-of-use race between the final
  wait and server-side handler invocation.

### C: Stage a revisioned selection and commit it atomically *(chosen)*

- Pro: Every native selection maps to one server transaction; all files commit together.
- Pro: A newer server-issued revision invalidates older uploads, and reachability pruning uses the
  same invalidation mechanism.
- Pro: Upload I/O stays outside the eval gate; only begin, final commit, cancellation, and invoke
  validation need short serialized state transitions.
- Pro: An invoke precondition closes the race left by client-side waiting.
- Con: Requires selection tokens, staging state, multiple endpoints, cleanup, and explicit failed
  selection behavior.
- Con: Atomic replacement temporarily retains both the old committed files and the staged new files,
  so replacement can fail the session quota when there is insufficient headroom.

### Storage: keep bodies in memory

- Pro: Small implementation and naturally fast access.
- Con: Large or concurrent attachments consume managed heap and create avoidable allocation/GC
  pressure in the same process as the debug target.
- Con: A finite request limit does not stop many retained requests from exhausting memory unless a
  separate aggregate store quota is implemented anyway.

### Storage: fixed temporary files

- Pro: Streams large bodies without retaining them on the managed heap.
- Con: Assumes a writable filesystem and fixes policy that constrained/mobile hosts may need to
  replace.

### Storage: pluggable storage with a temporary-file default *(chosen)*

- Pro: Gives the ordinary desktop host a bounded streaming implementation while letting embedders
  supply memory, platform storage, or another policy.
- Pro: Resource accounting remains in DuetsPad and therefore applies regardless of storage choice.
- Con: Adds a public storage extension point and lease lifecycle that must remain stable.

## Decision

Choose revisioned transactional selections (alternative C) and pluggable storage with a
temporary-file default.

### Script surface

`ui.filePicker(options?)` returns a dedicated file-picker handle rather than `DisplayInput`.
The TypeScript surface is:

```typescript
interface DuetsPadFile {
    readonly id: string;
    readonly name: string;
    readonly contentType: string;
    readonly size: number;
    openRead(): System.IO.Stream;
    readAllBytes(): Uint8Array;
    readAllText(): string;
}

interface DuetsPadFilePicker {
    readonly files: readonly DuetsPadFile[];
    remove(fileId: string): void;
    clear(): void;
}
```

The initial options include `accept`, `multiple`, `disabled`, `title`, and `className`. `accept` is
only a browser selection hint; it is not server-side validation. `files` is read-only because a
script cannot synthesize the browser-originated body represented by an attachment. `remove` and
`clear` are server-side mutations: they invalidate any pending selection, advance the picker
revision, release the removed blobs, and project the new list.

Each `DuetsPadFile` is immutable metadata plus an opaque reference to a stored blob. The client-
supplied name and content type are untrusted display metadata. DuetsPad strips directory components
and control characters from names, never uses the name as a storage path, and makes no claim that
the content matches the declared media type.

### Rendered shape and server-canonical state

A file picker renders a wrapper carrying the existing field identity marker
`data-duetspad-field="{id}"` and a new `data-duetspad-field-kind="file"`. The wrapper contains an
ephemeral native `<input type="file">` and a server-projected list of committed file metadata and
upload status. The native input is always empty after successful commit, fresh projection, or SSE
reconnect; only the committed list is restorable.

The attachment store is server-canonical. A successful browser commit updates the authoritative
Canvas/Timeline trees and broadcasts through the existing ADR-45 Canvas patch / ADR-36 Timeline
update paths. Unlike the silent ordinary-field commit in ADR-47, echo suppression is unnecessary:
the native input must be cleared after upload anyway, and broadcasting updates every placement of
the same handle. This does not make concurrent multi-browser editing first-class (ADR-38); it only
keeps the authoritative attachment list projectable.

Rendering a `DisplayFilePicker` establishes its attachment-store entry so the render can obtain the
authoritative revision and file projection. Rendering is therefore intentionally not a pure
operation. A speculative render whose output is never committed may temporarily create an empty
entry, but the authoritative Canvas/Timeline reachability scan removes it before invoke validation.
Future preview or speculative rendering paths must preserve that prune-before-validate coupling or
move registration to an explicit render-commit phase.

### Selection protocol and state machine

Each picker owns a monotonically increasing server revision and at most one unsettled selection.
Its states are:

- `Stable(revision, files)`: the committed selection is available to scripts.
- `Uploading(nextRevision, previousFiles, token)`: new files are being staged; the previous committed
  selection remains stored but interaction invocation is blocked.
- `Failed(nextRevision, previousFiles, token)`: staging failed or validation rejected a file;
  interaction invocation remains blocked so a click cannot silently process the previous selection.
- `Removed`: the picker marker is unreachable and no new operation may revive it.

The browser protocol is:

1. `POST /sessions/{sessionId}/attachments/{pickerId}/selections` sends the complete metadata manifest
   together with a browser-client id and that client's monotonically increasing generation. Under the
   eval gate the server first rejects a generation no newer than the last one observed from the same
   client, then checks marker reachability, validates and reserves count/bytes, advances the revision,
   invalidates any prior unsettled selection, and returns an opaque selection token plus server-issued
   opaque file ids.
2. One raw-body `POST` per file streams to
   `/sessions/{sessionId}/attachments/{pickerId}/selections/{token}/files/{fileId}`. Files may upload
   concurrently. The endpoint does not hold the eval semaphore while reading or writing bytes.
   If one sibling upload fails, the browser aborts the generation's shared `AbortController` so the
   remaining requests do not continue consuming bandwidth; server token checks still provide the
   correctness boundary if an abort arrives too late.
3. `POST /sessions/{sessionId}/attachments/{pickerId}/selections/{token}/commit` enters the eval gate,
   rechecks the token, reachability, manifest completion, and quota reservation, then atomically swaps
   the staged selection into `Stable` and projects it. A stale token receives a stale response and
   its staging is released; it never changes committed state.
4. `DELETE /sessions/{sessionId}/attachments/{pickerId}/selections/{token}` cancels the unsettled
   selection and returns to the preceding stable selection. Starting a newer selection has the same
   invalidating effect on the older token.
5. `DELETE /sessions/{sessionId}/attachments/{pickerId}/selections/failed?revision={revision}`
   cancels only the failed selection at the expected revision. This recovery operation does not need
   the selection token, because a reload cannot retain that token; authentication still covers the
   session API, and the revision condition prevents a stale placement from cancelling newer state.

The manifest's sizes are untrusted but are useful for reservation. Each raw upload must finish at
exactly its declared size; exceeding the declaration or ending early fails the selection. Chunked
bodies are counted while streaming rather than trusted by header. Reservations are taken before
staging begins and include all concurrent selections, preventing parallel requests from each
observing spare capacity and collectively exceeding the session quota.

The HttpListener API used through HttpHarker does not expose a request-aborted cancellation token.
The upload endpoint therefore cannot directly pass client-disconnect cancellation to the store. It
passes a placeholder token which the attachment store links with the selection cancellation token;
disconnect is otherwise observed as an input-stream read failure. Reselection, explicit cancellation,
reachability loss, and session disposal still cancel the linked storage operation promptly.

On reselection the client aborts the previous generation's body/commit requests and begins a new
transaction. The browser client id and generation are persisted in `sessionStorage`, so reload does
not reset ordering for a reconnected session. The server compares generations before invalidating
pending state; therefore an older begin request that acquires the eval gate after a newer request is
rejected rather than retiring the user's latest selection. The new server revision is the
authoritative state version, while `AbortController` remains only an I/O optimization. Concurrent
multi-browser editing remains outside ADR-38's supported model; generations are ordered only within
one browser-client id. The last observed client generation lives on the picker handle rather than its
retained blob-store entry, so pruning and later re-rendering the same script-held handle cannot admit a
delayed request from its previous rendered lifetime. A failed selection remains visibly failed and
blocks invocation until the user reselects or cancels it, so a failed replacement never falls through
to processing the previous files by surprise. The failed projection includes an enabled cancellation
button even when the native picker is disabled. It uses the revision-conditioned recovery endpoint,
so a reload or an older Timeline placement can recover without exposing selection tokens in the DOM
or browser storage and cannot cancel a newer selection.

### Interaction ordering

The browser maintains one generation record per picker (`AbortController`, completion Promise, and
committed revision). Before invoking any ADR-41 interaction it waits for every locally unsettled
attachment selection in the session. After each wait it rechecks the current map and repeats when a
new generation replaced one of the entries being awaited; only an empty unsettled map permits the
invoke. It then includes the observed stable revisions in an `attachments` member beside ADR-47's
`fields` snapshot.

The same picker handle may appear in multiple Canvas or Timeline placements, and their SSE updates can
be briefly staggered. When collecting a browser snapshot, DuetsPad selects the greatest projected
revision for each picker rather than allowing traversal order to replace a newer placement with an
older one.

Waiting is session-wide rather than limited to the clicked element's render subtree. A server-side
handler is an arbitrary closure and may read a picker rendered elsewhere; DuetsPad cannot infer that
dependency. The trade-off is that an unrelated attachment upload delays every interaction in the
same session.

Client waiting is not the correctness boundary. `InvokeInteractionAsync` enters the eval gate,
prunes field-backed state against the authoritative Canvas/Timeline trees, and then verifies that the
session has no unsettled attachment selection. The attachment snapshot must enumerate every retained
picker exactly once, and every supplied revision must match. Requiring a complete snapshot is
intentional: a server-side handler is an arbitrary closure and may read any retained picker, so
validating only client-selected entries would leave an omitted dependency unchecked. It also means
that a browser projection which intentionally retains only part of the authoritative output is not a
compatible interaction client; such a client would require a different dependency protocol.

A pending, incomplete, or mismatched state returns a distinct non-success response without running
the handler; the client waits for the latest projection and may retry. If a new begin and an invoke
race at the server, eval-gate acquisition linearizes them: the invoke either runs against the
preceding stable selection or observes the new pending selection and does not run. The handler is
never invoked twice for one request.

### Reachability and cleanup

File-picker lifetime follows ADR-47 field lifetime. The existing field-marker reachability walk is
refactored into one shared scan that runs when either the string field store or attachment store is
non-empty. It collects `data-duetspad-field` ids from every authoritative Canvas tree and Timeline
entry body once, then feeds the same retained-id set to both stores.

`AttachmentStore.Retain` removes committed ownership for unreachable picker ids, invalidates and
cancels their unsettled selections, and detaches staging. Actual storage disposal and filesystem I/O
run outside `_stateLock`. A full canvas rebuild, Timeline trim, output replacement, and session
disposal therefore release attachments by the same reachability rule as ordinary input values and
interactions. A late upload or commit checks its token after I/O and cannot recreate a removed entry.
Transient storage-deletion failures are retried with bounded exponential backoff while the session is
live, so a one-off custom-storage failure cannot permanently consume quota. Session disposal cancels
that retry loop and begins draining tracked storage operations on a background cleanup task. The
synchronous caller waits only for `AttachmentStorageDrainTimeout`; on timeout it receives a
`TimeoutException`, while the cleanup task retains the storage and synchronization state until every
operation finishes. Storage disposal remains the final boundary and is never forced concurrently
with an outstanding operation. A storage implementation that ignores cancellation can therefore
leak its background cleanup indefinitely, but cannot block session deletion or an idle sweep beyond
the configured timeout. `SessionRegistry` contains disposal exceptions so one failed session cannot
break deletion, registry teardown, or the idle-reclamation timer, and reports them with the session id
through the optional `SessionDisposalErrorHandler` callback. Exceptions thrown by the callback are
also contained.

A script may retain a `DuetsPadFile` object after its picker becomes unreachable. Its metadata
remains readable, but a new `OpenRead` fails as stale because the attachment store no longer owns the
blob.

### Streaming access and leases

The C# `DuetsPadFile.OpenRead()` contract returns a fresh readable `System.IO.Stream` positioned at
zero. It does not require `AllowClr`: the file object and returned stream are explicit host
capabilities. `SessionBootstrap` explicitly registers `System.IO.Stream` with `TypeDeclarations`, and
the manually registered `DuetsPadFile` declaration refers to that generated BCL declaration. This
dependency is deliberate: the runtime object is the ordinary .NET stream API, not a DuetsPad-specific
subset or wrapper interface. Without `AllowClr`, a script can call members on the returned host object
or pass it to another host-injected API; importing and constructing helpers such as
`System.IO.StreamReader` still requires the backend's normal CLR-import capability.

Direct `Stream.Read(byte[], ...)` is awkward from JavaScript because a JavaScript `Array` or
`Uint8Array` passed through Jint's CLR binder is not a reliable mutable `byte[]` output buffer.
`readAllText()` therefore provides the common text path using UTF-8 by default and honoring a Unicode
byte-order mark. `readAllBytes()` provides the common binary path as a JavaScript-owned `Uint8Array`.
It allocates one bounded array and transfers that array to the backend through the explicit
`ScriptByteBuffer` ownership envelope defined by ADR-51; Jint does not make a second copy. Both
methods are opt-in eager materialization conveniences layered above `openRead()`, not replacements
for streaming.

The returned runtime object is a read-only delegating stream, not the underlying `FileStream`, and
does not expose a temporary path. Each open stream holds a blob lease. Removing/pruning an attachment
releases the store's ownership but an already opened stream may finish until it is disposed; the
underlying blob is deleted after the last lease closes. Session disposal force-closes outstanding
leases. Scripts are responsible for disposing streams they open.

### Resource ceilings and storage

`DuetsPadServiceOptions` gains finite, fail-fast validated defaults:

```csharp
public long MaxAttachmentBytesPerFile { get; set; } = 16 * 1024 * 1024;
public long MaxAttachmentBytesPerSession { get; set; } = 64 * 1024 * 1024;
public int MaxAttachmentsPerSession { get; set; } = 32;
public Func<AttachmentStorageContext, IAttachmentStorage> AttachmentStorageFactory { get; set; }
public TimeSpan AttachmentStorageDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
public Action<Guid, Exception>? SessionDisposalErrorHandler { get; set; }
```

The session totals include committed blobs, staging blobs, and reservations. Atomic replacement
keeps the previous selection until commit, so both old and staged bytes/counts consume the limit;
callers do not receive an unaccounted transient-storage allowance. A replacement near the limit may
therefore require clearing the old selection first or raising the configured session ceiling.

Quota is released only after physical deletion succeeds, not when picker ownership is logically
removed. Consequently, `clear()` followed immediately by an equal-size selection may receive a
temporary quota rejection while deletion or a deletion retry is still running. This backpressure is
intentional: releasing quota at logical removal would let a slow or failing custom storage retain
unbounded physical data while the session repeatedly admits replacements.

The default factory creates per-session temporary-file storage. Custom storage cannot bypass DuetsPad
quota accounting. Storage identities and paths are never derived from client filenames, and session
disposal releases the complete per-session store.

`IAttachmentStorage` separates implementer obligations from DuetsPad guarantees. Implementations must
support overlapping operations for different ids, honor cancellation, and make deletion idempotent;
DuetsPad serializes one-id operations, prevents deletion during a read lease, retries deletion, and
orders final disposal after drain. No conformance helper is included in the runtime package: lease and
drain ordering are properties of `AttachmentStore`, while prompt cancellation cannot be verified
deterministically by a generic black-box helper. The default implementation and the orchestration
guarantees are covered by repository tests. If external storage implementations later justify a
reusable test kit, it belongs in a dedicated testing package rather than the runtime API.

This ADR narrowly amends ADR-49's statement that `MaxRequestBodyBytes` caps every POST body. JSON
selection manifests, commit requests, eval, and all existing
endpoints remain subject to that general limit. A raw attachment body is instead subject to
`MaxAttachmentBytesPerFile`, while the session byte/count ceilings provide aggregate bounds. Thus
every request remains bounded, but the default 1 MiB control-message ceiling does not accidentally
become the maximum useful attachment size.

## Rationale

Transactional selection is the smallest model that makes a native multi-file selection atomic.
Once reselection and removal are admitted, ordering generation, revision, and token checks are
required even for a one-file picker: client cancellation cannot prove that the server stopped
receiving an earlier body, concurrent begin handlers need not acquire the eval gate in browser issue
order, and upload completion can race a projection mutation. Validating the browser-issued ordering
generation before begin and the server-issued token again at final commit turns every late result into
a harmless stale operation.

The client must wait because it owns the in-flight fetches and can provide good progress/error
feedback, but client waiting cannot close the final network race. The eval-gated invoke precondition
is what makes the handler observation deterministic. Session-wide waiting is conservative, but it
matches the fact that handlers are arbitrary closures rather than declared form submissions.

A failed selection must remain explicit rather than silently revealing the previous committed files
to a handler. Recovery cannot rely exclusively on the opaque token because browser reload discards
the in-memory generation record. Allowing tokenless cancellation only for `Failed` state and only at
an expected revision preserves that explicit failure boundary without allowing stale UI to retire an
upload that began later.

Reusing field-marker reachability preserves the lifetime rule users already see for `ui.*` inputs:
state exists because rendered output reaches its handle. A second attachment-only tree walk or a
placement index would duplicate authoritative-state traversal and introduce another invalidation
mechanism. Blob leases then separate logical reachability from the narrower lifetime of a read that
has already begun.

Streaming to a pluggable store keeps large bodies off the managed heap without making a writable
filesystem a DuetsPad platform requirement. Dedicated attachment limits preserve ADR-49's core
security property — all input and retained state is bounded — while recognizing that binary payloads
and JSON control messages need different useful defaults. Whole-file convenience reads do allocate
managed memory, but only on explicit script request and within the already committed per-file bound;
the upload and retained-storage paths remain streaming and temporary-file-backed by default.

## Consequences

- **Positive**: A handler sees an atomic, complete attachment selection; it never races a partial or
  superseded upload.
- **Positive**: Reselection, removal, pruning, and session disposal all invalidate late completion
  through the same token/revision mechanism.
- **Positive**: File bodies stream to bounded storage; scripts can either retain streaming access or
  explicitly materialize the bounded file as text or a zero-extra-copy JavaScript byte array.
- **Positive**: `AllowClr` is not required to receive or pass an attachment stream; enabling it only
  broadens the processing APIs scripts can construct.
- **Positive**: A failed selection remains recoverable after reload without persisting or projecting
  its opaque selection token.
- **Positive**: The existing Canvas/Timeline marker scan, patch protocol, auth middleware, and eval
  gate remain the architectural boundaries; no parallel placement or command channel is introduced.
- **Negative / trade-offs**: File selection needs a multi-step protocol, staging cleanup, revisioned
  client state, and server-side invoke preconditions.
- **Negative / trade-offs**: Any unsettled attachment delays every interaction in the session, even
  if the handler is unrelated.
- **Negative / trade-offs**: Atomic replacement may temporarily require quota headroom for both old
  and new selections.
- **Negative / trade-offs**: Quota remains occupied until physical deletion succeeds, so clear and
  immediate reselection can experience temporary storage backpressure.
- **Negative / trade-offs**: Whole-file convenience methods deliberately allocate managed memory;
  callers processing larger files should use `openRead()` and a host streaming API.
- **Negative / trade-offs**: Native picker contents cannot survive a patch or reconnect; only the
  server-canonical committed list survives.
- **Negative / trade-offs**: Open stream leases can postpone physical deletion until disposal; session
  disposal remains the final cleanup boundary for leaked leases.
- **Negative / trade-offs**: A custom storage implementation that ignores cancellation is detached
  from the synchronous disposal caller after the drain timeout, but its background cleanup and
  resources remain rooted until the operation eventually finishes.
- **Negative / trade-offs**: ADR-49's single general POST limit gains one specialized exception, so
  future binary endpoints must not copy that exception without their own request and aggregate bounds.
