using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Duets;

/// <summary>
/// <see cref="IJsDocProvider"/> implementation backed by a .NET XML documentation file.
/// </summary>
public sealed class XmlDocumentationProvider : IJsDocProvider
{
    /// <summary>Initializes a new instance from raw XML documentation content.</summary>
    public XmlDocumentationProvider(string xmlContent)
    {
        this._members = ParseXml(xmlContent);
    }

    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Dictionary<string, XElement> _members;

    /// <summary>
    /// Downloads the NuGet package for <paramref name="packageId"/>/<paramref name="version"/>,
    /// extracts the XML documentation file, caches it on disk, and returns a provider instance.
    /// Returns <c>null</c> if the package has no XML documentation or the download fails.
    /// </summary>
    public static async Task<XmlDocumentationProvider?> FetchFromNuGetAsync(
        string packageId,
        string version,
        string? tfm = null,
        string? cacheDirectory = null,
        HttpClient? httpClient = null,
        string? assemblyName = null)
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();
        var cacheDir = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "duets-xmldoc-cache");
        var tfmSuffix = tfm != null ? $".{tfm.ToLowerInvariant()}" : "";
        var asmSuffix = assemblyName != null ? $".{assemblyName.ToLowerInvariant()}" : "";
        var cacheFile = Path.Combine(cacheDir, $"{id}.{ver}{tfmSuffix}{asmSuffix}.xml");

        if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetCreationTimeUtc(cacheFile) < TimeSpan.FromDays(7))
        {
            return new XmlDocumentationProvider(await File.ReadAllTextAsync(cacheFile));
        }

        try
        {
            var client = httpClient ?? s_httpClient;
            var url = $"https://api.nuget.org/v3-flatcontainer/{id}/{ver}/{id}.{ver}.nupkg";
            var nupkgBytes = await client.GetByteArrayAsync(url);

            using var zip = new ZipArchive(new MemoryStream(nupkgBytes), ZipArchiveMode.Read);
            var xmlContent = FindXmlInNupkg(zip, tfm, assemblyName);
            if (xmlContent == null) return null;

            Directory.CreateDirectory(cacheDir);
            await File.WriteAllTextAsync(cacheFile, xmlContent);
            return new XmlDocumentationProvider(xmlContent);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public string? Get(MemberInfo member)
    {
        try
        {
            var memberId = GetMemberId(member);
            return memberId != null && this._members.TryGetValue(memberId, out var element)
                ? FormatJsDoc(element)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindXmlInNupkg(ZipArchive zip, string? tfm, string? assemblyName)
    {
        // lib/ is the conventional location; ref/ is used by some packages for reference assemblies
        return FindXmlUnderPrefix(zip, "lib/", tfm, assemblyName)
            ?? FindXmlUnderPrefix(zip, "ref/", tfm, assemblyName);
    }

    private static string? FindXmlUnderPrefix(ZipArchive zip, string prefix, string? tfm, string? assemblyName)
    {
        var xmlEntries = zip.Entries
            .Where(e =>
                e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        if (xmlEntries.Count == 0) return null;

        // When assemblyName is known, prefer the XML file whose stem matches it to avoid
        // picking up another assembly's docs in multi-assembly packages.
        ZipArchiveEntry? PickEntry(IEnumerable<ZipArchiveEntry> candidates)
        {
            var list = candidates.ToList();
            if (assemblyName != null)
            {
                var match = list.FirstOrDefault(e =>
                    Path.GetFileNameWithoutExtension(e.Name)
                        .Equals(assemblyName, StringComparison.OrdinalIgnoreCase)
                );
                if (match != null) return match;
            }

            return list.FirstOrDefault();
        }

        if (tfm != null)
        {
            var entry = PickEntry(
                xmlEntries.Where(e =>
                    e.FullName.StartsWith($"{prefix}{tfm}/", StringComparison.OrdinalIgnoreCase)
                )
            );
            if (entry != null) return ReadEntry(entry);
        }

        string[] preferredTfms =
        [
            $"net{Environment.Version.Major}.{Environment.Version.Minor}",
            "netstandard2.1",
            "netstandard2.0",
            "net8.0",
        ];
        foreach (var preferred in preferredTfms)
        {
            var entry = PickEntry(
                xmlEntries.Where(e =>
                    e.FullName.StartsWith($"{prefix}{preferred}/", StringComparison.OrdinalIgnoreCase)
                )
            );
            if (entry != null) return ReadEntry(entry);
        }

        var fallback = PickEntry(xmlEntries);
        return fallback != null ? ReadEntry(fallback) : null;
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, XElement> ParseXml(string xmlContent)
    {
        var dict = new Dictionary<string, XElement>(StringComparer.Ordinal);
        try
        {
            var doc = XDocument.Parse(xmlContent);
            foreach (var member in doc.Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;
                if (name != null) dict[name] = member;
            }
        }
        catch
        {
        }

        return dict;
    }

    private static string? GetMemberId(MemberInfo member)
    {
        return member switch
        {
            Type type => $"T:{GetTypeDocId(type)}",
            FieldInfo field => $"F:{GetTypeDocId(field.DeclaringType!)}.{field.Name}",
            PropertyInfo property => $"P:{GetTypeDocId(property.DeclaringType!)}.{property.Name}",
            ConstructorInfo ctor =>
                $"M:{GetTypeDocId(ctor.DeclaringType!)}.#ctor{BuildParamListId(ctor.GetParameters(), null)}",
            MethodInfo method =>
                $"M:{GetTypeDocId(method.DeclaringType!)}.{method.Name}" +
                $"{(method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : "")}" +
                $"{BuildParamListId(method.GetParameters(), method)}",
            _ => null,
        };
    }

    private static string GetTypeDocId(Type type)
    {
        // Use the open generic form so that MemberInfo from closed generic types
        // (e.g. List<int>) maps to the same XML ID as List`1 in the doc file.
        var t = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
        return (t.FullName ?? t.Name).Replace('+', '.');
    }

    private static string BuildParamListId(ParameterInfo[] parameters, MethodInfo? method)
    {
        if (parameters.Length == 0) return "";
        return $"({string.Join(",", parameters.Select(p => GetParamTypeId(p.ParameterType, method)))})";
    }

    private static string GetParamTypeId(Type type, MethodInfo? method)
    {
        if (type.IsByRef)
        {
            return $"{GetParamTypeId(type.GetElementType()!, method)}@";
        }

        if (type.IsPointer)
        {
            return $"{GetParamTypeId(type.GetElementType()!, method)}*";
        }

        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var suffix = rank == 1 ? "[]" : $"[{string.Join(",", Enumerable.Repeat("0:", rank))}]";
            return $"{GetParamTypeId(type.GetElementType()!, method)}{suffix}";
        }

        if (type.IsGenericTypeParameter)
        {
            return $"`{type.GenericParameterPosition}";
        }

        if (type.IsGenericMethodParameter)
        {
            return $"``{type.GenericParameterPosition}";
        }

        if (type.IsConstructedGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var defId = GetTypeDocId(def);
            var args = string.Join(",", type.GetGenericArguments().Select(a => GetParamTypeId(a, method)));
            return $"{defId}{{{args}}}";
        }

        return GetTypeDocId(type);
    }

    private static string? FormatJsDoc(XElement memberElement)
    {
        var parts = new List<string>();

        var summary = memberElement.Element("summary");
        if (summary != null)
        {
            var text = NormalizeWhitespace(ExtractText(summary));
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }

        var remarks = memberElement.Element("remarks");
        if (remarks != null)
        {
            var text = NormalizeWhitespace(ExtractText(remarks));
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }

        foreach (var param in memberElement.Elements("param"))
        {
            var name = param.Attribute("name")?.Value;
            var text = NormalizeWhitespace(ExtractText(param));
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(text))
            {
                parts.Add($"@param {name} {text}");
            }
        }

        var returns = memberElement.Element("returns");
        if (returns != null)
        {
            var text = NormalizeWhitespace(ExtractText(returns));
            if (!string.IsNullOrEmpty(text)) parts.Add($"@returns {text}");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    private static string ExtractText(XElement element)
    {
        var sb = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement el:
                    switch (el.Name.LocalName)
                    {
                        case "see":
                            var langword = el.Attribute("langword")?.Value;
                            var cref = el.Attribute("cref")?.Value;
                            if (langword != null)
                            {
                                sb.Append(langword);
                            }
                            else if (cref != null)
                            {
                                sb.Append(GetCrefSimpleName(cref));
                            }

                            break;
                        case "paramref":
                        case "typeparamref":
                            sb.Append(el.Attribute("name")?.Value ?? el.Value);
                            break;
                        case "c":
                        case "code":
                            sb.Append(el.Value);
                            break;
                        default:
                            sb.Append(ExtractText(el));
                            break;
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private static string GetCrefSimpleName(string cref)
    {
        var colonIdx = cref.IndexOf(':');
        var name = colonIdx >= 0 ? cref[(colonIdx + 1)..] : cref;
        // Strip parameter list and generic type arguments before splitting on dots, so that
        // M:System.String.IndexOf(System.Char) and T:System.Collections.Generic.List{T}
        // don't split inside the parameter or argument types.
        var parenIdx = name.IndexOf('(');
        if (parenIdx >= 0) name = name[..parenIdx];
        var braceIdx = name.IndexOf('{');
        if (braceIdx >= 0) name = name[..braceIdx];
        var backtickIdx = name.LastIndexOf('`');
        if (backtickIdx >= 0) name = name[..backtickIdx];
        name = name.Replace('+', '.');
        var dotIdx = name.LastIndexOf('.');
        return dotIdx >= 0 ? name[(dotIdx + 1)..] : name;
    }

    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var prevWasSpace = true; // treat start as space to trim leading whitespace
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevWasSpace) sb.Append(' ');
                prevWasSpace = true;
            }
            else
            {
                sb.Append(c);
                prevWasSpace = false;
            }
        }

        // Trim trailing space
        if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}
