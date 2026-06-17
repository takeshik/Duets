using Duets.Jint;
using Jint;

namespace Duets.Tests.TestSupport;

internal static class JintTestRuntime
{
    /// <summary>
    /// Creates a real Jint-backed session for tests that exercise runtime interop but do not
    /// validate TypeScript transpilation. Tests that rely on TypeScript syntax must supply a real
    /// transpiler instead.
    /// </summary>
    public static Task<DuetsSession> CreateSessionAsync(Action<Options>? configure = null)
    {
        return DuetsSession.CreateAsync(config =>
            config
                .UseTranspiler(_ => Task.FromResult<ITranspiler>(new IdentityTranspiler()))
                .UseEngine(transpiler => CreateEngine(configure, transpiler))
        );
    }

    public static JintScriptEngine CreateEngine(
        Action<Options>? configure = null,
        ITranspiler? transpiler = null
    )
    {
        return new JintScriptEngine(configure, transpiler ?? new IdentityTranspiler());
    }
}
