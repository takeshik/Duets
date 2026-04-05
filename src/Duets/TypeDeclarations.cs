using System.Security.Cryptography;
using System.Text;

namespace Duets;

/// <summary>TypeScript declaration file passed to Monaco's <c>addExtraLib</c>.</summary>
public record TypeDeclaration(string FileName, string Content);

/// <summary>Read-only view of runtime type declarations.</summary>
public interface ITypeDeclarationProvider
{
    /// <summary>
    /// Fires when a declaration file is added or updated.
    /// Namespace placeholders are re-fired when replaced by an empty namespace declaration.
    /// </summary>
    event Action<TypeDeclaration>? DeclarationChanged;

    /// <summary>Returns a snapshot of all registered declaration files.</summary>
    IReadOnlyCollection<TypeDeclaration> GetDeclarations();
}

/// <summary>Write-only API for registering runtime type declarations.</summary>
public interface ITypeDeclarationRegistrar
{
    /// <summary>
    /// Registers a .NET type as a declaration target.
    /// Base types are registered first. Duplicate registrations are ignored.
    /// </summary>
    void RegisterType(Type type);

    /// <summary>
    /// Registers an arbitrary TypeScript declaration.
    /// Duplicate content is ignored.
    /// </summary>
    void RegisterDeclaration(string content);

    /// <summary>
    /// Registers a namespace placeholder so the namespace appears in completions
    /// without registering any type members yet.
    /// </summary>
    void RegisterNamespace(string namespaceName);
}

/// <summary>
/// Thread-safe store for runtime TypeScript declarations generated from CLR types or provided as raw d.ts text.
/// This component is independent from <see cref="TypeScriptService"/> so it can also be used with
/// <see cref="BabelTranspiler"/> or any other <see cref="ITranspiler"/>.
/// </summary>
public sealed class TypeDeclarations : ITypeDeclarationProvider,
    ITypeDeclarationRegistrar
{
    private readonly object _sync = new();
    private readonly HashSet<Type> _registeredTypes = [];
    private readonly Dictionary<string, string> _placeholderNamespaces = new();
    private readonly HashSet<string> _coveredNamespaces = [];
    private readonly Dictionary<string, TypeDeclaration> _declarations = new();
    private readonly ClrDeclarationGenerator _generator = new();

    /// <summary>
    /// Fires when a declaration file is added or updated.
    /// Namespace placeholders are re-fired when replaced by an empty namespace declaration.
    /// </summary>
    public event Action<TypeDeclaration>? DeclarationChanged;

    /// <summary>Returns a snapshot of all registered declaration files.</summary>
    public IReadOnlyCollection<TypeDeclaration> GetDeclarations()
    {
        lock (this._sync)
        {
            return this._declarations.Values.ToArray();
        }
    }

    /// <summary>
    /// Registers a .NET type as a declaration target.
    /// Base types are registered first. Duplicate registrations are ignored.
    /// </summary>
    public void RegisterType(Type type)
    {
        List<TypeDeclaration> changed;
        lock (this._sync)
        {
            changed = this.RegisterTypeCore(type);
        }

        this.Notify(changed);
    }

    /// <summary>
    /// Registers an arbitrary TypeScript declaration.
    /// Duplicate content is ignored.
    /// </summary>
    public void RegisterDeclaration(string content)
    {
        TypeDeclaration? changed;
        lock (this._sync)
        {
            changed = this.RegisterDeclarationCore(content);
        }

        if (changed != null)
        {
            this.DeclarationChanged?.Invoke(changed);
        }
    }

    /// <summary>
    /// Registers a namespace placeholder so the namespace appears in completions
    /// without registering any type members yet.
    /// </summary>
    public void RegisterNamespace(string namespaceName)
    {
        TypeDeclaration? changed;
        lock (this._sync)
        {
            changed = this.RegisterNamespaceCore(namespaceName);
        }

        if (changed != null)
        {
            this.DeclarationChanged?.Invoke(changed);
        }
    }

    private static string ComputeSha1Hex(string input)
    {
        return ComputeSha1Hex(Encoding.UTF8.GetBytes(input));
    }

    private static string ComputeSha1Hex(byte[] bytes)
    {
#if NETSTANDARD2_1
        using var sha1 = SHA1.Create();
        return BitConverter.ToString(sha1.ComputeHash(bytes)).Replace("-", string.Empty);
#else
        return Convert.ToHexString(SHA1.HashData(bytes));
#endif
    }

    private List<TypeDeclaration> RegisterTypeCore(Type type)
    {
        var changed = new List<TypeDeclaration>();
        if (!this._registeredTypes.Add(type)) return changed;

        var baseType = type.BaseType;
        if (baseType != null && baseType != typeof(object) && baseType != typeof(ValueType))
        {
            changed.AddRange(this.RegisterTypeCore(baseType));
        }

        var declaration = new TypeDeclaration(
            $"clr-{ComputeSha1Hex(type.ToString())}.d.ts",
            this._generator.GenerateTypeDefTs(type)
        );
        this._declarations[declaration.FileName] = declaration;
        changed.Add(declaration);

        if (type.Namespace != null && this._placeholderNamespaces.TryGetValue(type.Namespace, out var placeholderFile))
        {
            var emptyNamespace = new TypeDeclaration(
                placeholderFile,
                $"declare namespace {type.Namespace} {{ }}\n"
            );
            this._declarations[placeholderFile] = emptyNamespace;
            this._placeholderNamespaces.Remove(type.Namespace);
            this._coveredNamespaces.Add(type.Namespace);
            changed.Add(emptyNamespace);
        }
        else if (type.Namespace != null)
        {
            this._coveredNamespaces.Add(type.Namespace);
        }

        return changed;
    }

    private TypeDeclaration? RegisterDeclarationCore(string content)
    {
        var declaration = new TypeDeclaration(
            $"decl-{ComputeSha1Hex(content)}.d.ts",
            content
        );
        if (this._declarations.ContainsKey(declaration.FileName)) return null;

        this._declarations[declaration.FileName] = declaration;
        return declaration;
    }

    private TypeDeclaration? RegisterNamespaceCore(string namespaceName)
    {
        if (this._coveredNamespaces.Contains(namespaceName)) return null;
        if (this._placeholderNamespaces.ContainsKey(namespaceName)) return null;

        var declaration = new TypeDeclaration(
            $"clr-ns-{ComputeSha1Hex($"ns:{namespaceName}")}.d.ts",
            $"declare namespace {namespaceName} {{ const $name: '{namespaceName}'; }}\n"
        );
        this._declarations[declaration.FileName] = declaration;
        this._placeholderNamespaces[namespaceName] = declaration.FileName;
        return declaration;
    }

    private void Notify(IEnumerable<TypeDeclaration> changed)
    {
        foreach (var declaration in changed)
        {
            this.DeclarationChanged?.Invoke(declaration);
        }
    }
}
