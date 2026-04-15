using Jint;
using Jint.Native;

namespace Duets.Jint;

/// <summary>
/// Configuration options for <see cref="BabelTranspiler"/>, controlling how the Babel bundle is fetched.
/// </summary>
public record BabelTranspilerOptions
{
    /// <summary>
    /// Asset source for the Babel standalone bundle (<c>babel.js</c>).
    /// Defaults to fetching version 7.29.2 from unpkg with a 7-day disk cache in the system temp directory.
    /// </summary>
    public IAssetSource BabelJs { get; init; } =
        AssetSources.Unpkg("@babel/standalone", "7.29.2", "babel.js")
            .WithDiskCache(Path.Combine(Path.GetTempPath(), "babel-standalone.js"));
}

/// <summary>
/// <see cref="ITranspiler"/> implementation backed by Babel standalone running on Jint.
/// Provides an alternative to <see cref="TypeScriptService"/> that does not depend on the
/// official TypeScript compiler, enabling forward compatibility when <c>typescript.js</c>
/// is no longer available.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="CreateAsync(BabelTranspilerOptions)"/>.
/// Call <see cref="InitializeAsync"/> only to reinitialize an existing instance.
/// Supports TypeScript features with runtime semantics (enum, namespace, constructor
/// parameter properties) via <c>@babel/preset-typescript</c>.
/// Unlike <see cref="TypeScriptService"/>, this implementation does not provide
/// language service features (completions, type declarations).
/// </remarks>
public class BabelTranspiler : ITranspiler,
    IDisposable
{
    private BabelTranspiler(BabelTranspilerOptions? options = null)
    {
        this._options = options ?? new BabelTranspilerOptions();
    }

    private readonly BabelTranspilerOptions _options;
    private Engine? _engine;
    private JsValue? _babel;
    private JsValue? _babelTransform;

    public string? Version { get; private set; }

    /// <inheritdoc/>
    public string Description => $"Babel {this.Version ?? ""}".TrimEnd();

    public static async Task<BabelTranspiler> CreateAsync(BabelTranspilerOptions? options = null)
    {
        var transpiler = new BabelTranspiler(options);
        try
        {
            await transpiler.InitializeAsync();
            return transpiler;
        }
        catch
        {
            transpiler.Dispose();
            throw;
        }
    }

    public async Task InitializeAsync(bool forceDownload = false)
    {
        var newEngine = new Engine(opts => opts.Strict(false));

        // Babel standalone requires browser-like globals that Jint does not provide by default.
        try
        {
            await newEngine.ExecuteAsync(
                """
                var process = { env: { NODE_ENV: 'production' } };
                var console = { log: function() {}, warn: function() {}, error: function() {}, info: function() {} };
                """
            );

            await newEngine.ExecuteAsync(await this._options.BabelJs.GetAsync(forceDownload));

            var babel = newEngine.GetValue("Babel");
            var babelTransform = babel.Get("transform");
            var v = babel.Get("version");

            var previousEngine = this._engine;
            this._engine = newEngine;
            this._babel = babel;
            this._babelTransform = babelTransform;
            this.Version = v.Equals(JsValue.Undefined) ? null : v.AsString();
            previousEngine?.Dispose();
        }
        catch
        {
            newEngine.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        this._engine?.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Maps TypeScript compiler options to Babel equivalents on a best-effort basis.
    /// Options with no Babel counterpart are ignored. The <paramref name="diagnostics"/>
    /// list is populated when Babel reports a parse or transform error; the exception is
    /// also re-thrown since Babel does not produce output on error (unlike <c>ts.transpile</c>).
    /// </remarks>
    public string Transpile(
        string input,
        string? fileName = null,
        IList<Diagnostic>? diagnostics = null,
        string? moduleName = null)
    {
        if (this._engine == null) throw new InvalidOperationException("Call InitializeAsync() first.");

        try
        {
            var result = this._engine.Call(
                this._babelTransform!,
                this._babel!,
                [
                    input,
                    JsValue.FromObject(
                        this._engine,
                        new
                        {
                            presets = new[] { "typescript" },
                            filename = fileName ?? "input.ts",
                            retainLines = true,
                        }
                    ),
                ]
            );
            return result.Get("code").AsString();
        }
        catch (Exception ex)
        {
            diagnostics?.Add(new Diagnostic(0, input.Length, ex.Message, 1, 0));
            throw;
        }
    }
}
