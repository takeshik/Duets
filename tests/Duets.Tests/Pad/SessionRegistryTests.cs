using Duets.Pad;
using Duets.Tests.TestSupport;

namespace Duets.Tests.Pad;

public sealed class SessionRegistryTests
{
    [Fact]
    public async Task Dispose_continues_disposing_sessions_after_one_session_dispose_throws()
    {
        var throwingEngine = new TestEngine(throwOnDispose: true);
        var recordingEngine = new TestEngine();
        var engines = new Queue<IScriptEngine>([throwingEngine, recordingEngine]);
        using var registry = new SessionRegistry(
            new DuetsPadServiceOptions
            {
                SessionFactory = () =>
                    DuetsSession.CreateAsync(config =>
                        config
                            .UseTranspiler(_ =>
                                Task.FromResult<ITranspiler>(new IdentityTranspiler())
                            )
                            .UseEngine(_ => engines.Dequeue())
                    ),
            }
        );

        await registry.GetOrCreateSessionAsync(null);
        await registry.GetOrCreateSessionAsync(null);

        var exception = Record.Exception(registry.Dispose);

        Assert.Null(exception);
        Assert.True(throwingEngine.Disposed);
        Assert.True(recordingEngine.Disposed);
    }

    private sealed class TestEngine(bool throwOnDispose = false) : IScriptEngine
    {
        public bool CanRegisterTypeBuiltins => false;

        public bool Disposed { get; private set; }

        public event Action<ScriptConsoleEntry>? ConsoleLogged
        {
            add { }
            remove { }
        }

        public void SetValue(string name, object value) { }

        public void SetValue(string name, ScriptValue value) { }

        public IReadOnlyDictionary<ScriptValue, ScriptValue> GetGlobalVariables() =>
            new Dictionary<ScriptValue, ScriptValue>();

        public void RegisterTypeBuiltins(ITypeDeclarationRegistrar declarations) { }

        public void Execute(string tsCode) { }

        public Task ExecuteAsync(string tsCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ScriptValue Evaluate(string tsCode) => ScriptValue.Undefined;

        public Task<ScriptValue> EvaluateAsync(
            string tsCode,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ScriptValue.Undefined);

        public void Dispose()
        {
            this.Disposed = true;
            if (throwOnDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }
    }
}
