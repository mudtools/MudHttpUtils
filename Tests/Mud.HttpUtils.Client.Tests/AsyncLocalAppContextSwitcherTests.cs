namespace Mud.HttpUtils.Client.Tests;

public class AsyncLocalAppContextSwitcherTests
{
    private readonly AsyncLocalAppContextSwitcher _switcher = new();

    [Fact]
    public void Current_DefaultIsNull()
    {
        _switcher.Current.Should().BeNull();
    }

    [Fact]
    public void Current_SetValue_ReturnsSameValue()
    {
        var context = CreateTestContext("app1");

        _switcher.Current = context;

        _switcher.Current.Should().BeSameAs(context);

        _switcher.Current = null;
    }

    [Fact]
    public void Current_SetNull_ClearsValue()
    {
        _switcher.Current = CreateTestContext("app1");
        _switcher.Current = null;

        _switcher.Current.Should().BeNull();
    }

    [Fact]
    public void BeginScope_SetsCurrentAndRestoresOnDispose()
    {
        var original = CreateTestContext("original");
        var scoped = CreateTestContext("scoped");
        _switcher.Current = original;

        using (_switcher.BeginScope(scoped))
        {
            _switcher.Current.Should().BeSameAs(scoped);
        }

        _switcher.Current.Should().BeSameAs(original);

        _switcher.Current = null;
    }

    [Fact]
    public void BeginScope_NullContext_ThrowsArgumentNullException()
    {
        var act = () => _switcher.BeginScope((IMudAppContext)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void BeginScope_NestedScopes_RestoresInCorrectOrder()
    {
        var level0 = CreateTestContext("level0");
        var level1 = CreateTestContext("level1");
        var level2 = CreateTestContext("level2");
        _switcher.Current = level0;

        using (_switcher.BeginScope(level1))
        {
            _switcher.Current.Should().BeSameAs(level1);

            using (_switcher.BeginScope(level2))
            {
                _switcher.Current.Should().BeSameAs(level2);
            }

            _switcher.Current.Should().BeSameAs(level1);
        }

        _switcher.Current.Should().BeSameAs(level0);

        _switcher.Current = null;
    }

    [Fact]
    public void BeginScope_DisposeCalledMultipleTimes_DoesNotCorruptState()
    {
        var original = CreateTestContext("original");
        var scoped = CreateTestContext("scoped");
        _switcher.Current = original;

        var scope = _switcher.BeginScope(scoped);
        scope.Dispose();
        scope.Dispose();
        scope.Dispose();

        _switcher.Current.Should().BeSameAs(original);

        _switcher.Current = null;
    }

    [Fact]
    public async Task BeginScope_FlowsAcrossAsyncBoundary()
    {
        var context = CreateTestContext("async-context");
        _switcher.Current = context;

        await Task.Yield();

        _switcher.Current.Should().BeSameAs(context);

        _switcher.Current = null;
    }

    [Fact]
    public async Task BeginScope_IsolatedAcrossConcurrentTasks()
    {
        _switcher.Current = null;

        var task1 = Task.Run(async () =>
        {
            var ctx1 = CreateTestContext("task1");
            using (_switcher.BeginScope(ctx1))
            {
                await Task.Delay(50);
                return _switcher.Current;
            }
        });

        var task2 = Task.Run(async () =>
        {
            var ctx2 = CreateTestContext("task2");
            using (_switcher.BeginScope(ctx2))
            {
                await Task.Delay(50);
                return _switcher.Current;
            }
        });

        var results = await Task.WhenAll(task1, task2);

        results[0].Should().NotBeSameAs(results[1]);
    }

    [Fact]
    public void UseApp_ThrowsNotSupportedException()
    {
        var act = () => _switcher.UseApp("app1");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void UseDefaultApp_ThrowsNotSupportedException()
    {
        var act = () => _switcher.UseDefaultApp();

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void BeginScopeString_ThrowsNotSupportedException()
    {
        var act = () => _switcher.BeginScope("app1");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task GetTokenAsync_ThrowsNotSupportedException()
    {
        var act = () => _switcher.GetTokenAsync();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    private static TestAppContext CreateTestContext(string appId) => new(appId);

    private class TestAppContext : IMudAppContext
    {
        public string AppId { get; }

        public TestAppContext(string appId)
        {
            AppId = appId;
        }

        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();

        public ITokenManager GetTokenManager(string tokenType = "") => null!;

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();

        public T? GetService<T>() where T : class => null;
    }
}
