using System.Reflection;
using Duets.Okojo;
using Okojo.Reflection;
using Okojo.Runtime;

namespace Duets.Tests.TestSupport;

internal static class OkojoTestRuntime
{
    public static OkojoScriptEngine CreateEngine(
        Action<JsRuntimeBuilder>? configure = null,
        ITranspiler? transpiler = null)
    {
        return new OkojoScriptEngine(
            builder =>
            {
                builder.AllowClrAccess(
                    Assembly.GetExecutingAssembly(),
                    typeof(ScriptEngine).Assembly,
                    typeof(OkojoScriptEngine).Assembly,
                    typeof(object).Assembly
                );
                configure?.Invoke(builder);
            },
            transpiler ?? new IdentityTranspiler()
        );
    }
}
