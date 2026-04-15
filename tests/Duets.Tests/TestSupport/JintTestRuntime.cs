using Duets.Jint;
using Jint;

namespace Duets.Tests.TestSupport;

internal static class JintTestRuntime
{
    public static JintScriptEngine CreateEngine(
        Action<Options>? configure = null,
        ITranspiler? transpiler = null)
    {
        return new JintScriptEngine(configure, transpiler ?? new IdentityTranspiler());
    }
}
