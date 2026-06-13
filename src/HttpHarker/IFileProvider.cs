namespace HttpHarker;

/// <summary>Provides raw file bytes by normalized relative path.</summary>
public interface IFileProvider
{
    /// <param name="relativePath">Forward-slash-delimited path relative to the provider root (e.g. <c>"assets/app.js"</c>).</param>
    /// <returns>File bytes, or <c>null</c> if the path does not exist.</returns>
    public byte[]? GetFileContent(string relativePath);
}
