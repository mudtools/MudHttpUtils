using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// AM2-13：<c>AddMudHttpAppScopeSwitcher&lt;T&gt;()</c> 必须以<b>同一实例</b>暴露
/// <see cref="IAppContextHolder"/> 与 <see cref="IAppScopeSwitcher"/> 两个契约。
/// </summary>
/// <remarks>
/// 两个契约各解析出一个实例 ⇒ "两个 <see cref="AsyncLocal{T}"/> 互不可见"（G7 治过的病）。
/// 本文件把"同一实例"钉死。
/// </remarks>
public class AppScopeSwitcherRegistrationTests
{
    private sealed class StubSwitcher : IAppScopeSwitcher
    {
        private readonly AsyncLocal<IMudAppContext?> _current = new();

        public IMudAppContext? Current
        {
            get => _current.Value;
            init => _current.Value = value;
        }

        public void SwitchTo(IMudAppContext? context) => _current.Value = context;

        public IDisposable BeginScope(IMudAppContext context)
        {
            var previous = _current.Value;
            _current.Value = context;
            return new Restore(() => _current.Value = previous);
        }

        public IDisposable UseAppScope(string appKey)
        {
            if (!AppKey.IsValid(appKey))
                throw new ArgumentException("appKey 格式非法", nameof(appKey));

            return BeginScope(new StubContext(appKey));
        }

        public IDisposable UseDefaultAppScope() => UseAppScope("default-app");

        private sealed class Restore(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    private sealed class StubContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    [Fact]
    public void AddMudHttpAppScopeSwitcher_ExposesSameInstanceForBothContracts()
    {
        var services = new ServiceCollection();

        services.AddMudHttpAppScopeSwitcher<StubSwitcher>();

        using var sp = services.BuildServiceProvider();

        var asSwitcher = sp.GetRequiredService<IAppScopeSwitcher>();
        var asHolder = sp.GetRequiredService<IAppContextHolder>();
        var asConcrete = sp.GetRequiredService<StubSwitcher>();

        asSwitcher.Should().BeSameAs(asHolder,
            "AM2-13：两个契约必须是同一实例，否则出现两个互不可见的 AsyncLocal");
        asConcrete.Should().BeSameAs(asSwitcher);
    }

    [Fact]
    public void AddMudHttpAppScopeSwitcher_AfterContextHolder_DelegatingAdapterForwardsToSwitcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 先注册委托式适配器（AddMudHttpClient 的既有路径），再注册宿主切换器。
        services.AddMudHttpAppContextHolder();
        services.AddMudHttpAppScopeSwitcher<StubSwitcher>();

        using var sp = services.BuildServiceProvider();

        var holder = sp.GetRequiredService<IAppContextHolder>();
        var switcher = sp.GetRequiredService<IAppScopeSwitcher>();

        using (switcher.UseAppScope("app-a"))
        {
            holder.Current.Should().NotBeNull(
                "AM2-13：委托适配器必须转发到宿主的切换器（顺序不再影响语义，G7）");
            holder.Current!.AppKey.Should().Be("app-a");
        }

        holder.Current.Should().BeNull("作用域释放后必须回退");
    }
}
