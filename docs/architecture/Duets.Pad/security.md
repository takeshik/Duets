# DuetsPad Security

DuetsPad evaluates browser-supplied code in the host process. Remote code execution is its intended function, so its
network and asset boundaries must be selected deliberately. This page describes the architectural security contract;
the [package guide](../../../src/Duets.Pad/README.md#security) provides deployment examples and configuration names.

See the [DuetsPad architecture landing](README.md) for service ownership and the [protocol](protocol.md) for routes.

## Threat model

The intended LAN-exposure model includes hostile LAN peers, cross-site request forgery and drive-by pages, DNS
rebinding, credential disclosure on an unencrypted network, and compromised CDN-backed frontend assets. It does not
treat a session id as an authorization credential: a browser that can reach an unprotected service can create or
discover a session and submit code
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

No authentication is the default and means loopback-only deployment with an exact-host listener prefix such as
`127.0.0.1`. Combining that default with a wildcard listener exposes the RCE surface to reachable peers and lets DNS
rebinding defeat assumptions based on the browser's origin or session-id secrecy
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

## Authentication boundary

`DuetsPadServiceOptions.Authenticate` is the single pluggable authentication handler. A `null` handler means the
loopback-only default; `DuetsPadAuthenticator.Token` provides the built-in constant-time fixed-token policy. Custom
handlers can integrate another service without changing the router
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

Authentication middleware runs ahead of routing and gates the entire `/sessions` subtree by path. This fail-closed
placement covers session routes added later without requiring every handler to remember an authorization check. Static
UI assets deliberately remain public so the page can load and present the token prompt
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

The built-in credential is sent explicitly as `Authorization: Bearer` on every session request. Basic and cookie
schemes were rejected because browsers attach those ambient credentials automatically; a drive-by page or DNS
rebind would then need only reach the origin. The browser captures a bootstrap token from the URL fragment, which is
not sent in the HTTP request, removes it from the visible URL, and keeps it in `sessionStorage`
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

Native `EventSource` cannot attach the required authorization header, so DuetsPad uses a fetch-based SSE reader with
manual reconnection. Authentication applies equally to stream reconnection and bounded HTTP commands
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

## Frontend asset trust

The Monaco and Tabler assets execute or influence content in the page that holds the bearer token. Default CDN-backed
assets are therefore inside the trust boundary of an authenticated deployment. An embedder that cannot extend that
trust to the configured CDN must supply pinned, embedded, or self-hosted `IAssetSource` implementations
([ADR-18](../../decisions/18_pluggable-asset-source-abstraction.md),
[ADR-37](../../decisions/37_binary-first-iassetsource.md),
[ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

Rendered structured content is projected through validated nodes. Raw HTML remains an explicit caller-owned escape
hatch; full replacements and incremental patches apply the same node and interaction validation before browser
mutation
([ADR-35](../../decisions/35_duetspad-rendering-model.md),
[ADR-45](../../decisions/45_duetspad-canvas-incremental-patch-protocol.md)).

## Transport boundary

TLS termination is a reverse-proxy responsibility. A bearer token sent over plain HTTP can be observed by a network
peer. `RemoteEndPoint` identifies the direct socket peer, which is the proxy in a proxied deployment; an application
must not build a forwarded-client IP policy without defining and enforcing its own trusted-proxy boundary
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

Host-header validation is not part of the current service. The loopback default relies on an exact-host prefix, and
authenticated LAN deployment relies on the explicit bearer gate rather than hostname secrecy. Per-user identity and
authorization are also outside the current single-developer debug-pad scope
([ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md)).

Open-text content is handed to a new page through a one-shot opaque key rather than placed in the URL. The control
channel therefore does not leak script text through browser history, referrers, or ordinary URL logging
([ADR-42](../../decisions/42_duetspad-pad-control-surface-and-command-channel.md)).

## Resource ceilings

Resource bounds apply independently of authentication because a valid user or script can still exhaust the host:

- `MaxSessions` reserves capacity atomically before invoking the asynchronous session factory; requests beyond the
  cap receive `429`.
- `IdleTimeout` defaults to 30 minutes and reclaims abandoned sessions; live SSE activity counts as use.
- `MaxRequestBodyBytes` bounds control-message bodies by streaming rather than trusting `Content-Length`. Oversized
  requests receive `413` after a byte- and time-bounded drain intended to keep the response deliverable.
- Tagged-template completion has separate body, text-length, result-count, rate, and callback-time limits.
- `MaxActiveModals` bounds retained Modal surfaces per session.
- Attachment storage has per-file bytes, per-session bytes, and per-session file-count limits. Staging reserves quota
  before accepting file content, and quota remains charged until physical deletion succeeds.
- `TimelineEntryLimit` can bound retained history by entry count when a host opts into a limit.

The session cap, request-body reader, authentication boundary, idle policy, and Modal cap are established by
[ADR-49](../../decisions/49_duetspad-access-control-and-resource-hardening.md). Attachment bounds and cleanup behavior
are established by [ADR-50](../../decisions/50_duetspad-file-attachment-state-and-upload-protocol.md); Timeline trimming
is established by [ADR-39](../../decisions/39_duetspad-timeline-quota-policy.md); completion bounds are part of
[ADR-44](../../decisions/44_tagged-template-completion-rpc-boundary.md).
