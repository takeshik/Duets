using Duets.Pad;
using Duets.Tests.TestSupport;

namespace Duets.Pad.Tests;

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

    [Fact]
    public async Task GetOrCreateSessionAsync_disposes_created_session_when_pad_bootstrap_throws()
    {
        var throwingEngine = new TestEngine(throwOnSetValueName: "__padDump__");
        var recordingEngine = new TestEngine();
        var engines = new Queue<IScriptEngine>([throwingEngine, recordingEngine]);
        using var registry = new SessionRegistry(
            new DuetsPadServiceOptions
            {
                MaxSessions = 1,
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.GetOrCreateSessionAsync(null)
        );
        Assert.True(throwingEngine.Disposed);

        // The failed construction must also release its MaxSessions reservation.
        var created = await registry.GetOrCreateSessionAsync(null);
        Assert.NotNull(created);
    }

    [Fact]
    public async Task MaxSessions_counter_remains_accurate_while_limit_is_unlimited()
    {
        var options = new DuetsPadServiceOptions
        {
            MaxSessions = null,
            SessionFactory = () =>
                DuetsSession.CreateAsync(config =>
                    config
                        .UseTranspiler(_ => Task.FromResult<ITranspiler>(new IdentityTranspiler()))
                        .UseEngine(_ => new TestEngine())
                ),
        };
        using var registry = new SessionRegistry(options);

        var first = await registry.GetOrCreateSessionAsync(null);
        var second = await registry.GetOrCreateSessionAsync(null);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(registry.TryDeleteSession(first.Value.Id));

        // One live session remains, so switching the retained options instance to a cap of one must
        // reject another create. A counter maintained only while the cap was non-null would be
        // negative here after the deletion and incorrectly admit extra sessions.
        options.MaxSessions = 1;
        Assert.Null(await registry.GetOrCreateSessionAsync(null));
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_disposes_session_completed_after_registry_disposal()
    {
        var engine = new TestEngine();
        var duetsSession = await DuetsSession.CreateAsync(config =>
            config
                .UseTranspiler(_ => Task.FromResult<ITranspiler>(new IdentityTranspiler()))
                .UseEngine(_ => engine)
        );
        var factoryCompletion = new TaskCompletionSource<DuetsSession>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var registry = new SessionRegistry(
            new DuetsPadServiceOptions { SessionFactory = () => factoryCompletion.Task }
        );

        var creation = registry.GetOrCreateSessionAsync(null);
        registry.Dispose();
        factoryCompletion.SetResult(duetsSession);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => creation);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task TryAcquireSession_prevents_eviction_from_the_pre_request_idle_timestamp()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        using var registry = new SessionRegistry(
            new DuetsPadServiceOptions
            {
                IdleTimeout = TimeSpan.FromMinutes(5),
                CleanupInterval = TimeSpan.FromDays(1),
                Clock = () => now,
                SessionFactory = () =>
                    DuetsSession.CreateAsync(config =>
                        config
                            .UseTranspiler(_ =>
                                Task.FromResult<ITranspiler>(new IdentityTranspiler())
                            )
                            .UseEngine(_ => new TestEngine())
                    ),
            }
        );
        var created = await registry.GetOrCreateSessionAsync(null);
        Assert.NotNull(created);

        now += TimeSpan.FromMinutes(10);
        var acquired = registry.TryAcquireSession(created.Value.Id);
        registry.RemoveIdleSessions();

        Assert.Same(created.Value.Session, acquired);
        Assert.Same(created.Value.Session, registry.TryGetSession(created.Value.Id));
    }

    private sealed class TestEngine(bool throwOnDispose = false, string? throwOnSetValueName = null)
        : IScriptEngine
    {
        public bool CanRegisterTypeBuiltins => false;

        public bool Disposed { get; private set; }

        public event Action<ScriptConsoleEntry>? ConsoleLogged
        {
            add { }
            remove { }
        }

        public void SetValue(string name, object value)
        {
            if (name == throwOnSetValueName)
            {
                throw new InvalidOperationException("set value failed");
            }
        }

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
