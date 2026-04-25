using System.Reflection;

namespace Duets;

/// <summary>
/// Session-level repository of <see cref="IJsDocProvider"/> instances.
/// Providers are tried in registration order; the first non-null result wins.
/// Itself implements <see cref="IJsDocProvider"/> for use as the single provider passed to
/// <see cref="ClrDeclarationGenerator"/>.
/// </summary>
public sealed class JsDocProviders : IJsDocProvider
{
    private readonly object _sync = new();
    private readonly List<IJsDocProvider> _providers = [];

    /// <summary>Raised after a provider is successfully added.</summary>
    internal event Action? ProviderAdded;

    /// <summary>
    /// Downloads the XML documentation for <paramref name="packageId"/>/<paramref name="version"/>
    /// from NuGet and adds the resulting provider. Does nothing if the package has no documentation
    /// or the download fails.
    /// </summary>
    public async Task AddAsync(
        string packageId,
        string version,
        string? tfm = null,
        string? cacheDirectory = null,
        HttpClient? httpClient = null)
    {
        var provider = await XmlDocumentationProvider.FetchFromNuGetAsync(
            packageId,
            version,
            tfm,
            cacheDirectory,
            httpClient,
            packageId
        );
        if (provider != null)
        {
            this.Add(provider);
        }
    }

    /// <summary>
    /// Adds XML documentation for <paramref name="assembly"/>.
    /// First tries to load from an adjacent <c>.xml</c> file next to the assembly on disk.
    /// If not found, falls back to downloading from NuGet using <c>assembly.GetName().Name</c>
    /// as the package ID and the assembly's three-part version as the NuGet version —
    /// this inference is unreliable when the assembly name and NuGet package ID differ.
    /// Does nothing if no documentation is found.
    /// </summary>
    public async Task AddAsync(
        Assembly assembly,
        string? tfm = null,
        string? cacheDirectory = null,
        HttpClient? httpClient = null)
    {
        var location = assembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            var xmlPath = Path.ChangeExtension(location, ".xml");
            if (File.Exists(xmlPath))
            {
                try
                {
                    this.Add(await File.ReadAllTextAsync(xmlPath));
                    return;
                }
                catch
                {
                }
            }
        }

        var name = assembly.GetName();
        var packageId = name.Name;
        var version = name.Version?.ToString(3);
        if (packageId == null || version == null) return;
        var provider = await XmlDocumentationProvider.FetchFromNuGetAsync(
            packageId,
            version,
            tfm,
            cacheDirectory,
            httpClient,
            packageId
        );
        if (provider != null)
        {
            this.Add(provider);
        }
    }

    /// <summary>Adds a provider built from raw XML documentation content.</summary>
    public void Add(string xmlContent)
    {
        this.Add(new XmlDocumentationProvider(xmlContent));
    }

    /// <summary>Adds a provider.</summary>
    public void Add(IJsDocProvider provider)
    {
        lock (this._sync)
        {
            this._providers.Add(provider);
        }

        this.ProviderAdded?.Invoke();
    }

    /// <inheritdoc/>
    public string? Get(MemberInfo member)
    {
        IJsDocProvider[] snapshot;
        lock (this._sync)
        {
            snapshot = [.. this._providers];
        }

        foreach (var provider in snapshot)
        {
            try
            {
                var result = provider.Get(member);
                if (result != null) return result;
            }
            catch
            {
            }
        }

        return null;
    }
}
