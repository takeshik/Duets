using Jint;

namespace Duets.Jint;

/// <summary>Jint-specific configuration extensions for <see cref="DuetsSession"/> creation.</summary>
public static class DuetsSessionConfigurationExtensions
{
    /// <summary>
    /// Configures the session to use the Jint JavaScript engine.
    /// Pass <paramref name="configure"/> to set Jint-specific options such as
    /// <see cref="Options.AllowClr"/> for CLR interop and <c>typings</c> built-in registration.
    /// When not called, the engine registered in <see cref="DuetsBackendRegistry"/> is used
    /// automatically — which defaults to Jint when <c>Duets.Jint</c> is referenced.
    /// </summary>
    public static DuetsSessionConfiguration UseJint(
        this DuetsSessionConfiguration configuration,
        Action<Options>? configure = null)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        return configuration.UseEngine(transpiler => new JintScriptEngine(configure, transpiler));
    }

    /// <summary>
    /// Configures the session to use <see cref="BabelTranspiler"/> as the transpiler.
    /// Pass <paramref name="options"/> to customize asset fetching or caching behavior.
    /// When not called, the transpiler registered in <see cref="DuetsBackendRegistry"/> is used
    /// automatically — which defaults to <see cref="BabelTranspiler"/> when <c>Duets.Jint</c> is
    /// referenced. Call this explicitly only when you need to pass custom <see cref="BabelTranspilerOptions"/>.
    /// </summary>
    public static DuetsSessionConfiguration UseBabel(
        this DuetsSessionConfiguration configuration,
        BabelTranspilerOptions? options = null)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        return configuration.UseTranspiler(async () => await BabelTranspiler.CreateAsync(options));
    }
}
