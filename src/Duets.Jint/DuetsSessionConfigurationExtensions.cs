using Jint;

namespace Duets.Jint;

/// <summary>Jint-specific configuration extensions for <see cref="DuetsSession"/> creation.</summary>
public static class DuetsSessionConfigurationExtensions
{
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
}
