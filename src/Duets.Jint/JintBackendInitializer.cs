using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Duets.Jint;

internal static class JintBackendInitializer
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255")]
    internal static void Initialize()
    {
        DuetsBackendRegistry.RegisterDefaultEngine(transpiler => new JintScriptEngine(null, transpiler));
        DuetsBackendRegistry.RegisterDefaultTranspiler(async _ => await BabelTranspiler.CreateAsync());
    }
}
