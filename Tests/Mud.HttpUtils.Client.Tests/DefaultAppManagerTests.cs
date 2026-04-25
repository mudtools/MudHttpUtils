namespace Mud.HttpUtils.Client.Tests;

public class DefaultAppManagerTests
{
    [Fact]
    public void RegisterApp_ValidApp_RegistersSuccessfully()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var context = new TestAppContext("app1");

        manager.RegisterApp("app1", context);

        manager.HasApp("app1").Should().BeTrue();
    }

    [Fact]
    public void RegisterApp_AsDefault_SetsDefaultApp()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var context = new TestAppContext("app1");

        manager.RegisterApp("app1", context, isDefault: true);

        manager.GetDefaultApp().Should().BeSameAs(context);
    }

    [Fact]
    public void RegisterApp_NullAppKey_Throws()
    {
        var manager = new DefaultAppManager<TestAppContext>();

        var act = () => manager.RegisterApp(null!, new TestAppContext("app1"));

        act.Should().Throw<ArgumentException>().WithParameterName("appKey");
    }

    [Fact]
    public void RegisterApp_NullAppContext_Throws()
    {
        var manager = new DefaultAppManager<TestAppContext>();

        var act = () => manager.RegisterApp("app1", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("appContext");
    }

    [Fact]
    public void GetApp_ExistingApp_ReturnsContext()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var context = new TestAppContext("app1");
        manager.RegisterApp("app1", context);

        var result = manager.GetApp("app1");

        result.Should().BeSameAs(context);
    }

    [Fact]
    public void GetApp_NonExistingApp_Throws()
    {
        var manager = new DefaultAppManager<TestAppContext>();

        var act = () => manager.GetApp("nonexistent");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nonexistent*");
    }

    [Fact]
    public void TryGetApp_ExistingApp_ReturnsTrue()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));

        var result = manager.TryGetApp("app1", out var context);

        result.Should().BeTrue();
        context.Should().NotBeNull();
    }

    [Fact]
    public void TryGetApp_NonExistingApp_ReturnsFalse()
    {
        var manager = new DefaultAppManager<TestAppContext>();

        var result = manager.TryGetApp("nonexistent", out var context);

        result.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void RemoveApp_ExistingApp_ReturnsTrue()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));

        var result = manager.RemoveApp("app1");

        result.Should().BeTrue();
        manager.HasApp("app1").Should().BeFalse();
    }

    [Fact]
    public void RemoveApp_DefaultApp_ClearsDefault()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"), isDefault: true);

        manager.RemoveApp("app1");

        var act = () => manager.GetDefaultApp();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetAllApps_ReturnsAllRegisteredApps()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));
        manager.RegisterApp("app2", new TestAppContext("app2"));

        var apps = manager.GetAllApps();

        apps.Should().HaveCount(2);
    }

    [Fact]
    public void RegisterApp_UpdateExisting_TriggersUpdatedEvent()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));

        AppConfigurationChangedEventArgs? eventArgs = null;
        manager.ConfigurationChanged += (_, e) => eventArgs = e;

        manager.RegisterApp("app1", new TestAppContext("app1-updated"));

        eventArgs.Should().NotBeNull();
        eventArgs!.AppKey.Should().Be("app1");
        eventArgs.ChangeType.Should().Be(AppConfigurationChangeType.Updated);
    }

    [Fact]
    public void RegisterApp_NewApp_TriggersAddedEvent()
    {
        var manager = new DefaultAppManager<TestAppContext>();

        AppConfigurationChangedEventArgs? eventArgs = null;
        manager.ConfigurationChanged += (_, e) => eventArgs = e;

        manager.RegisterApp("app1", new TestAppContext("app1"));

        eventArgs.Should().NotBeNull();
        eventArgs!.AppKey.Should().Be("app1");
        eventArgs.ChangeType.Should().Be(AppConfigurationChangeType.Added);
    }

    [Fact]
    public void RemoveApp_TriggersRemovedEvent()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));

        AppConfigurationChangedEventArgs? eventArgs = null;
        manager.ConfigurationChanged += (_, e) => eventArgs = e;

        manager.RemoveApp("app1");

        eventArgs.Should().NotBeNull();
        eventArgs!.AppKey.Should().Be("app1");
        eventArgs.ChangeType.Should().Be(AppConfigurationChangeType.Removed);
    }

    [Fact]
    public async Task RegisterAppAsync_RegistersSuccessfully()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var context = new TestAppContext("app1");

        await manager.RegisterAppAsync("app1", context);

        manager.HasApp("app1").Should().BeTrue();
    }

    private class TestAppContext : IMudAppContext
    {
        public string AppId { get; }

        public TestAppContext(string appId)
        {
            AppId = appId;
        }

        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();

        public ITokenManager GetTokenManager(string tokenType = "") => null!;

        public T? GetService<T>() where T : class => null;
    }
}
