namespace Duets;

/// <summary>Loads scripts embedded by the core package for use by runtime backends.</summary>
public static class ScriptEngineResources
{
    /// <summary>Loads the JavaScript that initializes engine globals.</summary>
    /// <returns>The embedded JavaScript source.</returns>
    public static string LoadScriptEngineInitJs()
    {
        using var stream = typeof(ScriptEngineResources).Assembly.GetManifestResourceStream(
            "Duets.Resources.ScriptEngineInit.js"
        )!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Loads the TypeScript declarations for engine globals.</summary>
    /// <returns>The embedded declaration source.</returns>
    public static string LoadScriptEngineInitDts()
    {
        using var stream = typeof(ScriptEngineResources).Assembly.GetManifestResourceStream(
            "Duets.Resources.ScriptEngineInit.d.ts"
        )!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Loads the TypeScript language-service host script.</summary>
    /// <returns>The embedded JavaScript source.</returns>
    public static async Task<string> LoadLanguageServiceJsAsync()
    {
        await using var stream = typeof(ScriptEngineResources).Assembly.GetManifestResourceStream(
            "Duets.Resources.language-service.js"
        )!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
