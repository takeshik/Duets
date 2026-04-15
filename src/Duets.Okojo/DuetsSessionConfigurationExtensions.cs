using Okojo.Runtime;

namespace Duets.Okojo;

/// <summary>Okojo-specific configuration extensions for <see cref="DuetsSession"/> creation.</summary>
public static class DuetsSessionConfigurationExtensions
{
    public static DuetsSessionConfiguration UseOkojo(
        this DuetsSessionConfiguration configuration,
        Action<JsRuntimeBuilder>? configure = null)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        return configuration.UseEngine(transpiler => new OkojoScriptEngine(configure, transpiler));
    }
}
