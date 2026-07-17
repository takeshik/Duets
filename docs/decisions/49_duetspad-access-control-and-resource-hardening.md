# ADR-49: DuetsPad Access Control and Resource Hardening for LAN Exposure

## Status

Accepted

## Context

DuetsPad exposes remote script evaluation over HTTP: `POST /sessions/{id}/eval`
executes arbitrary code in the embedded script engine, and hosts commonly enable
CLR access, at which point evaluation is equivalent to arbitrary code execution on
the host machine. The intended exposure is not limited to loopback: the pad exists
precisely so that devices on the local network — mobile hardware, game consoles —
can connect to a debug session running on a developer machine or on the device
under test.

As of ADR-48 the pad lives in its own `Duets.Pad` package, but it ships with no
access control and no resource ceilings:

- No authentication on any endpoint. Anyone who can reach the port can create
  sessions and evaluate code.
- No limit on session count. `POST /sessions` allocates a full engine per call.
- `IdleTimeout` defaults to `null`, so abandoned sessions are never reclaimed.
- Request bodies are read with unbounded `ReadToEndAsync` on `POST /sessions`,
  `/eval`, `/interactions/{id}/invoke`, and `/fields/{id}/commit`; only
  `/complete` enforces a size cap (`TaggedTemplateCompletionMaxRequestBytes`).

Four attack vectors follow from the LAN-exposure premise:

1. **Hostile LAN peer.** Any device on the same network segment reaches the
   endpoints directly.
2. **Cross-site request forgery.** A malicious web page loaded in any browser on
   the developer's machine (or on a LAN device) can fire cross-origin `POST`
   requests at the pad. `/eval` reads the raw body as code regardless of
   `Content-Type`, so a plain form post — which requires no CORS preflight —
   suffices. For a developer tool this drive-by vector is at least as realistic
   as a hostile device.

   Reaching `/eval` additionally requires a live session id, and the id is
   effectively an unguessable capability: an attacker cannot read the
   `POST /sessions` response cross-origin (it is opaque), and cannot choose an id
   either, because the registry mints a fresh GUID for any id it does not
   recognize. Blind CSRF therefore reaches session *creation*, not evaluation.
   This is a real barrier but a fragile one to rely on: it is one disclosure away
   from collapsing (see vector 3), and it would silently stop protecting any
   future endpoint that is not session-scoped.
3. **DNS rebinding.** A page at `evil.example` whose hostname is rebound to the
   pad's address becomes *same-origin* with the pad from the browser's point of
   view, which removes the barrier in vector 2: the attacker can now read the
   `POST /sessions` response, learn the session id, and evaluate. An exact-host
   listener prefix (`http://127.0.0.1:port/`) incidentally blocks this, because
   the prefix match rejects the rebound `Host` header — but LAN exposure needs a
   wildcard or specific-address prefix, and a wildcard prefix accepts any `Host`.
   An access token defeats rebinding regardless: the attacker's origin has no
   token, and the real pad origin's `sessionStorage` is unreadable from it.
   The exposed combination is therefore *wildcard prefix with no token*, which
   this ADR's rules already forbid — but the consequence is worse than "hostile
   LAN peers can connect": any web page the developer visits gets host RCE.
4. **Compromised frontend assets.** The pad's own scripts are embedded in the
   package, but the Monaco loader, the Monaco editor modules, and the Tabler
   assets are fetched from a CDN by default (`MonacoBaseUrl`, the `IAssetSource`
   options) and execute in the pad page's security context — `monaco-loader.js`
   runs *before* `duetspad.js`, i.e. before the token is taken out of the URL
   fragment. A compromised CDN can therefore read the fragment or
   `sessionStorage` and exfiltrate the token, which is equivalent to host RCE.
   Cross-origin *source* does not mean cross-origin *isolation*: an included
   script gets the embedding page's privileges. The default asset providers are
   consequently inside the trust boundary of an authenticated deployment.

## Decision Drivers

- Evaluation is remote code execution; network reachability must not imply the
  ability to evaluate.
- CSRF resistance should be structural, not dependent on headers
  (`Sec-Fetch-Site`, `Origin`) or cookie policies (`SameSite`) that constrained
  clients such as game-console browsers may not implement.
- The zero-configuration loopback experience must not change: security is opt-in.
- Server-side validation should be stateless and pluggable; hosts embed the pad
  in diverse environments and may already have their own credential story.
- Secrets must not travel in HTTP request targets: query strings reach servers
  and proxies and commonly appear in logs. A URL fragment is acceptable only as
  a bootstrap transport because the user agent does not send it in the request,
  and the client removes it from the current history entry immediately after
  capturing it. This follows ADR-42's narrower precedent of keeping `openText`
  content out of its navigated request URL.
- Resource ceilings must hold independently of authentication (defence in depth).
- TLS is out of scope: transport confidentiality on a LAN is delegated to a
  reverse proxy and documented as such.

## Considered Alternatives

### A: HTTP Basic authentication

- Pro: browser-native. The 401 challenge prompts once, then the browser attaches
  credentials to every same-origin request — including `EventSource` — with zero
  client-script changes and a native input UI on any device.
- Con: that automatic attachment is ambient authority — the browser adds the
  credential to cross-origin requests the user never intended, so authentication
  stops being an obstacle to CSRF at all. It does not by itself hand an attacker
  RCE: a forged request still needs a session id it cannot read (vector 2). What
  it does is remove the *independent* barrier, leaving session-id secrecy as the
  single thing between a drive-by page and code execution — and vector 3 is a
  concrete way to strip that away. It also leaves ambient-credentialed session
  creation (a DoS) reachable, and would silently endanger any future endpoint
  that is not session-scoped. Mitigation via `Origin`/`Sec-Fetch-Site` checks
  depends on client behaviour that old embedded browsers may lack. Basic also
  invites human-chosen (weak) passwords.

### B: Cookie-carried token

- Pro: `EventSource` keeps working; the cookie rides along automatically.
- Con: same ambient-authority problem as Basic unless `SameSite` is honoured by
  every client. Additionally a cookie needs an issuance flow — a login endpoint
  plus either server-side issued-cookie state or a signing key — which sits
  poorly on an otherwise stateless validation model.

### C: Bearer credential in the `Authorization` header, fetch-based SSE

- Pro: structurally CSRF-immune, and independently of session-id secrecy. The
  token lives in page JavaScript (sessionStorage), unreadable cross-origin; and a
  cross-origin request that sets `Authorization` triggers a CORS preflight the
  server never approves. No browser-managed credential exists to ride on, so an
  attacker who somehow learns a session id still has nothing. Validation is
  stateless — each request carries the credential — which matches a pluggable
  credential-to-boolean handler exactly.
- Con: `EventSource` cannot set request headers, so the SSE channel must be
  reimplemented over `fetch` with a streaming reader, including the manual
  reconnection logic `EventSource` provided for free.

A query-parameter variant of C (token in the SSE URL only) was rejected outright:
unlike the one-time bootstrap fragment, it puts the secret in an HTTP request
target and therefore in server and proxy logs. ADR-42 likewise keeps `openText`
content out of its navigated request URL, carrying only an opaque handoff id.

## Decision

Alternative C, implemented entirely in `Duets.Pad`.

**Authentication is a single pluggable handler.** `DuetsPadServiceOptions` gains

```csharp
public Func<DuetsPadAuthenticationContext, ValueTask<bool>>? Authenticate { get; set; }
```

evaluated on every session-API request; for the SSE endpoint it is evaluated
once, at connection establishment. Static UI assets (the pad page, scripts,
styles, fonts) stay unauthenticated: they carry no session state, and the page
must be able to load in order to present the token input in the first place.
Note this is not a claim that the assets are harmless — by vector 4 the default
CDN-backed ones are inside the trust boundary — only that gating them would
protect nothing while breaking the token prompt.

**The gate is a middleware, not a per-route wrapper.** It runs ahead of the
router and authenticates by path: everything under `/sessions` is gated, whether
or not the author of a future route remembers to opt in. Wrapping each route
individually would have made the ADR's "everything under `/sessions`" invariant a
convention rather than a property, i.e. fail-open. The path test is
case-insensitive so the gate can never be narrower than the router behind it.

`DuetsPadAuthenticationContext` is a small dedicated type (presented credential,
request path, remote endpoint) so that HttpHarker types do not leak into the
options surface. Its `RemoteEndPoint` is the *direct socket peer*: behind the
reverse proxy this ADR recommends for TLS, every request appears to come from the
proxy, so a handler must not treat it as the client address without a
trusted-proxy story of its own.

`null` — the default — means no authentication, preserving today's
zero-configuration behaviour; the README states that this mode assumes
loopback-only exposure, and specifically an exact-host listener prefix (vector 3).

There is no separate `AccessToken` property. Fixed-token validation is provided
as a factory, `DuetsPadAuthenticator.Token(string)`, which returns a handler that
compares credentials with `CryptographicOperations.FixedTimeEquals`. The factory
exists to keep the constant-time comparison in one place — the obvious hand-written
lambda would use `==`, which short-circuits — not to save keystrokes. The
comparison is constant-time in the token's *content*, not its length, since
`FixedTimeEquals` returns immediately on a length mismatch; a token's length is
not treated as a secret.

**The credential travels in `Authorization: Bearer` on every request.** The
browser client replaces `EventSource` with a fetch-based SSE reader (the change
is localized: all SSE connections already flow through the single `openSse()`
helper in `duetspad.js`) and gains manual reconnection with the same
retry-on-error semantics. Failed authentication yields `401` without a
`WWW-Authenticate` challenge, so browsers never pop a native credential prompt.

**The token reaches the browser via the URL fragment.** The pad accepts
`http://host:port/#token=...`; the fragment is never sent to the server and never
appears in access logs. On load the client stores it in `sessionStorage` and
strips it from the address bar. When a request is rejected with `401` and no
token is stored, the UI presents an in-page token input.

**Resource ceilings apply regardless of authentication.**

- `MaxSessions` (`int?`, default `16`): `POST /sessions` refuses to create a new
  session beyond the cap with `429`; `null` means unlimited. Each session owns a
  full engine, so a small cap suffices for a debug pad. The cap is enforced by an
  atomic reservation taken *before* the asynchronous session factory runs.
  Checking the session count and then awaiting the factory would not bound
  anything under load: every concurrent create would observe a below-cap count
  and build an engine anyway, so a cap of 16 would in fact admit as many sessions
  as the server has concurrent request slots (1024 by default in HttpHarker).
- `IdleTimeout` default changes from `null` to 30 minutes. The existing
  subscriber-presence guard already exempts any session with a live SSE stream,
  so an open pad tab is never reclaimed; only orphaned sessions are.
- `MaxRequestBodyBytes` (`int`, default 1 MiB): a bounded body reader replaces
  the unbounded `ReadToEndAsync` on all POST endpoints, rejecting larger bodies
  with `413`. It reads streamingly rather than trusting `Content-Length`, so a
  chunked body (which reports no length) is capped like any other. Before
  responding, the reader drains the oversized body under *two* bounds, bytes and
  time: closing with unread request data pending makes `HttpListener` reset the
  connection and the client would see a network error instead of the `413`, so a
  cooperative overshoot is drained and answered — but a byte cap alone bounds
  only volume, not duration, and a slow trickle would park a request slot for as
  long as the attacker likes. When either bound is hit the drain stops and the
  client gets the reset, which is the right outcome for an abusive request.
  `/complete` keeps its stricter `TaggedTemplateCompletionMaxRequestBytes` cap,
  but as a floor rather than an override: the effective limit there is the
  smaller of the two, so an endpoint option cannot raise a body past the global
  ceiling. It keeps its own error *shape* (the completion response with
  `ok: false`, which the Monaco client already handles) but now carries `413`
  rather than `200`, and drains like the other endpoints — an over-limit body is
  an over-limit body regardless of which route it arrived on.

New numeric options are range-checked in the existing `Validate()` fail-fast
hook.

**Out of scope.** TLS is a reverse-proxy responsibility, documented in the
README; without it a LAN sniffer can capture the bearer token, and deployments
that care must terminate TLS in front of the pad. Per-user identity and
authorization are not provided: the token is one shared secret, and revocation
means restarting with a new one.

Host-header validation against an allow-list — a direct structural answer to
vector 3 — is **deferred, not dismissed**. The pad does not know the listener's
prefix (HttpHarker does not expose it), so this would mean new API on both
libraries, and it protects exactly one combination that this ADR's rules already
forbid: a wildcard prefix with no token. A token defeats rebinding on its own.
Revisit if the pad ever grows a reason to be reachable without a token.

## Rationale

The deciding line between the alternatives is ambient versus explicit authority.
Basic and cookies both make the browser attach credentials automatically, which
is exactly the mechanism CSRF exploits.

The honest version of that argument is narrower than "one forged request gives
RCE", and worth stating precisely because the imprecise version is tempting: as
things stand, a forged request also needs a session id, which cross-origin rules
already withhold. Choosing bearer tokens is therefore a defence-in-depth call,
not the closing of an open hole. It is still the right call, because the
alternative is to let session-id secrecy be the *only* barrier — a barrier that
DNS rebinding removes wholesale (vector 3), that any future non-session-scoped
endpoint would bypass by construction, and that fails the moment an id turns up
in a log or a screenshot. Layering a credential that no browser will ever attach
on the attacker's behalf costs one SSE rewrite, and that cost turned out modest
because the client already funnels every stream through one helper.

The handler-plus-factory shape keeps the options surface to a single member,
avoids a two-property mutual-exclusion rule, and still encodes the one
security-sensitive implementation detail (constant-time comparison) in library
code rather than in every host's lambda.

The resource ceilings are deliberately independent of authentication: they bound
damage when no token is configured, when the token leaks, and against buggy
rather than malicious clients alike.

## Consequences

- **Positive**: LAN exposure becomes an explicit, supportable configuration — one
  factory call gates every endpoint, and the gate is a property of the path
  rather than a habit of route authors. Drive-by CSRF stops being a question of
  whether session ids stay secret, and DNS rebinding stops being exploitable in
  any configuration this ADR endorses. Session-flood and body-size
  denial-of-service vectors are bounded by default — including under concurrency
  and for chunked bodies — and abandoned sessions are reclaimed without affecting
  open tabs. Loopback-only development keeps working with zero configuration.
- **Negative / trade-offs**: the pad now maintains its own SSE reconnection
  logic instead of relying on `EventSource`. The shared token has no per-user
  identity and no revocation short of a restart. Over plain HTTP the token is
  visible to LAN sniffers — accepted and documented, since transport security is
  delegated to a reverse proxy. The `IdleTimeout` default change reclaims
  sessions that previous versions kept alive indefinitely; hosts that relied on
  eternal sessions must set it back to `null` explicitly.
- **The frontend supply chain is now security-relevant.** Before this ADR the
  pad had no secret to steal, so a compromised CDN could only deface a debug
  tool; now it can exfiltrate a token that is equivalent to host RCE (vector 4).
  This is not introduced by the token — the CDN could already run code in the
  page — but the token raises what that code is worth. Authenticated deployments
  that care should point the asset options at self-hosted or pinned immutable
  copies. The default remains CDN-backed, because changing it would break the
  zero-configuration loopback experience that is the pad's common case.
- **The threat model rests on a rule the code cannot enforce**: "no token means
  loopback only, bound to an exact host". Nothing rejects a wildcard prefix with
  no `Authenticate` — see the deferred Host-validation note above.
- **The registry gained a lifecycle lock**, which the `MaxSessions` reservation
  brought to the surface rather than caused. Reserving a slot before an
  asynchronous factory means construction can now outlive events it used to
  precede: a session could be published after `Dispose` began, a factory-built
  engine could leak if `DuetsPadSession`'s constructor threw, and a request could
  resolve a session the idle sweep was concurrently evicting on a pre-request
  timestamp. Publication, acquisition (resolve plus activity touch), and eviction
  therefore now happen under one lock — held only across dictionary work, never
  across the factory or `Dispose`, both of which run arbitrary host code. These
  are ADR-38 lifecycle invariants; they are recorded here because this ADR's cap
  is what made them reachable.
