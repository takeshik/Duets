namespace Duets;

public static class ScriptEngineResources
{
    public static string LoadScriptEngineInitJs()
    {
        using var stream = typeof(ScriptEngineResources).Assembly
            .GetManifestResourceStream("Duets.Resources.ScriptEngineInit.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string LoadScriptEngineInitDts()
    {
        using var stream = typeof(ScriptEngineResources).Assembly
            .GetManifestResourceStream("Duets.Resources.ScriptEngineInit.d.ts")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static async Task<string> LoadLanguageServiceJsAsync()
    {
        await using var stream = typeof(ScriptEngineResources).Assembly
            .GetManifestResourceStream("Duets.Resources.ReplStaticFiles.language-service.js")!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
