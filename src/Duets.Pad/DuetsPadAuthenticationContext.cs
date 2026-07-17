using System.Net;

namespace Duets.Pad;

/// <summary>
/// The information available to a <see cref="DuetsPadServiceOptions.Authenticate"/> handler for a
/// single request: <see cref="Credential"/> is the bearer credential presented in the request's
/// <c>Authorization</c> header (<c>Bearer &lt;credential&gt;</c>), or <see langword="null"/> when
/// the header is absent or does not use the <c>Bearer</c> scheme; <see cref="Path"/> is the
/// request's absolute URL path; <see cref="RemoteEndPoint"/> is the client's remote endpoint. This
/// is a dedicated type — rather than exposing HttpHarker's <c>HttpActionContext</c> or
/// <see cref="System.Net.HttpListenerRequest"/> directly — so that HttpHarker types do not leak
/// into the options surface (ADR-49).
/// </summary>
public sealed record DuetsPadAuthenticationContext(
    string? Credential,
    string Path,
    IPEndPoint? RemoteEndPoint
);
