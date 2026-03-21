namespace HttpHarker;

/// <summary>Provides raw file bytes by normalized relative path.</summary>
public interface IFileProvider
{
    /// <returns>File bytes, or <c>null</c> if the path does not exist.</returns>
    byte[]? GetFileContent(string relativePath);
}
