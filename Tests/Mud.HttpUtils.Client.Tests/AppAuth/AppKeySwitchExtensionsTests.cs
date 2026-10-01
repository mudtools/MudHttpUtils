// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// `SW-15`（3.0.0 补缺）：<see cref="AppKeySwitchExtensions"/> 的守卫与语义。
/// </summary>
/// <remarks>
/// <para>
/// 该扩展补齐的是 3.0.0 的能力缺口：「不可信 appKey + 完整守卫 + <b>无作用域</b>（切换并保持）」。
/// 因此本组用例同时钉死两件事：
/// </para>
/// <list type="number">
/// <item><description><b>守卫顺序与内容</b>必须与生成代码（<c>GenerateAppKeyGuard</c>）一致 ——
/// 格式校验 → 授权器默认拒绝 → 业务判定 → 才允许 <c>GetApp</c>（前两步任一失败时**不得**发生任何切换或应用解析）；</description></item>
/// <item><description><b>无归还语义</b>：方法返回后上下文仍保持在目标应用（这正是它与
/// <see cref="IAppScopeSwitcher.UseAppScope"/> 的区别，也是 <c>IAppManager.GetWebApi</c> 类场景所需）。</description></item>
/// </list>
/// </remarks>
public class AppKeySwitchExtensionsTests
{
    private const string AppA = "switch-app-a";
    private const string AppB = "switch-app-b";

    [Fact]
    public void SwitchToApp_WithInvalidAppKey_ThrowsArgumentException_BeforeAnySideEffect()
    {
        var (holder, manager) = CreateSut();
        manager.RegisterApp(AppA, new TestAppContext(AppA), isDefault: true);

        var act = () => holder.SwitchToApp("!!! 非法 appKey", manager, new AllowAllAppAccessAuthorizer());

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("appKey");
        holder.Current.Should().BeNull("格式校验必须先于任何上下文切换副作用发生");
    }

    [Fact]
    public void SwitchToApp_WithoutAuthorizer_DefaultDeny_AndNoSideEffect()
    {
        var (holder, manager) = CreateSut();
        manager.RegisterApp(AppA, new TestAppContext(AppA), isDefault: true);

        var act = () => holder.SwitchToApp(AppA, manager, authorizer: null);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("AllowAllAppAccessAuthorizer",
                "默认拒绝必须同时给出『显式放行』的逃生门（MT-02 / BC-18）");
        holder.Current.Should().BeNull("未注册授权器时不得发生任何切换");
    }

    [Fact]
    public void SwitchToApp_WhenAuthorizerDenies_ThrowsUnauthorized_WithoutResolvingApp()
    {
        var (holder, manager) = CreateSut();
        // 刻意**不注册**任何应用：若实现先调 GetApp，本用例会得到「未注册应用」异常而非授权异常，
        // 从而暴露守卫顺序错误。
        var act = () => holder.SwitchToApp("unregistered-app", manager, new AlwaysDenyAuthorizer());

        act.Should().Throw<UnauthorizedAccessException>(
            "授权判定必须早于应用解析：拒绝路径不得触碰 IAppManager");
        holder.Current.Should().BeNull();
    }

    [Fact]
    public void SwitchToApp_WithValidInput_SwitchesAndKeepsContext_WithoutReturningScope()
    {
        var (holder, manager) = CreateSut();
        var ctxA = new TestAppContext(AppA);
        var ctxB = new TestAppContext(AppB);
        manager.RegisterApp(AppA, ctxA, isDefault: true);
        manager.RegisterApp(AppB, ctxB);

        var returned = holder.SwitchToApp(AppB, manager, new AllowAllAppAccessAuthorizer());

        returned.Should().BeSameAs(ctxB, "应返回切换后的上下文实例（与 GetWebApi 类场景的用法一致）");
        holder.Current.Should().BeSameAs(ctxB, "切换应立即生效");
        // 「无作用域」= 不返回 IDisposable ⇒ 无法自动归还；此处再次读取仍为目标应用。
        holder.Current.Should().BeSameAs(ctxB,
            "本扩展无作用域语义：不会自动归还上下文（这是它与 UseAppScope 的核心区别，必须显式切回）");

        // 显式切回受信路径（Holder 面）应当生效 —— 证明调用方确有手动归还手段。
        holder.SwitchTo(ctxA);
        holder.Current.Should().BeSameAs(ctxA);
    }

    [Fact]
    public void SwitchToApp_ViaServiceProvider_ResolvesAuthorizerFromContainer()
    {
        var (holder, manager) = CreateSut();
        manager.RegisterApp(AppA, new TestAppContext(AppA), isDefault: true);
        using var provider = new ServiceCollection()
            .AddSingleton<IAppAccessAuthorizer>(new AllowAllAppAccessAuthorizer())
            .BuildServiceProvider();

        var returned = holder.SwitchToApp(AppA, manager, provider);

        returned.AppKey.Should().Be(AppA);
        holder.Current.Should().BeSameAs(returned);
    }

    [Fact]
    public void SwitchToApp_ViaServiceProvider_WhenAuthorizerMissing_DefaultDeny()
    {
        var (holder, manager) = CreateSut();
        manager.RegisterApp(AppA, new TestAppContext(AppA), isDefault: true);
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => holder.SwitchToApp(AppA, manager, provider);

        act.Should().Throw<InvalidOperationException>(
            "容器未注册 IAppAccessAuthorizer ⇒ 按默认拒绝处理，不得静默放行");
        holder.Current.Should().BeNull();
    }

    [Fact]
    public void SwitchToDefaultApp_SwitchesToDefaultContext_AndKeepsIt()
    {
        var (holder, manager) = CreateSut();
        var ctxA = new TestAppContext(AppA);
        manager.RegisterApp(AppA, ctxA, isDefault: true);
        manager.RegisterApp(AppB, new TestAppContext(AppB));

        holder.SwitchToDefaultApp(manager).Should().BeSameAs(ctxA);
        holder.Current.Should().BeSameAs(ctxA, "默认应用路径同样为『切换并保持』语义");
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var (holder, manager) = CreateSut();

        ((Action)(() => AppKeySwitchExtensions.SwitchToApp(null!, AppA, manager, (IAppAccessAuthorizer?)null)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("holder");
        // appManager 为 null ⇒ 无法从实参推断 TAppContext，必须显式指定类型参数（泛型化后的正常约束）。
        ((Action)(() => holder.SwitchToApp<IMudAppContext>(AppA, null!, (IAppAccessAuthorizer?)null)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("appManager");
        ((Action)(() => AppKeySwitchExtensions.SwitchToApp(null!, AppA, manager, (IServiceProvider)null!)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("serviceProvider");
        ((Action)(() => AppKeySwitchExtensions.SwitchToDefaultApp(null!, manager)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("holder");
    }

    #region 辅助

    [Fact]
    public void SwitchToApp_WithSdkOwnContextType_IsAcceptedAndReturnsTypedContext()
    {
        // 本条用例是本扩展**泛型化**（SW-15 订正）的编译期契约守卫：
        // SDK 以自己的上下文类型声明管理器（IAppManager<TSdkContext>），而 IAppManager<T> 是<b>不变</b>的，
        // 若扩展参数固定为 IAppManager<IMudAppContext>，此处的调用将无法编译（CS1503）——
        // 而那正是本扩展的目标用户（Mud.Feishu / Mud.Wechat 的 GetWebApi 类场景）。
        var holder = new AsyncLocalAppContextSwitcher();
        var manager = new DefaultAppManager<IProbeAppContext>();
        var ctx = new ProbeAppContext(AppA);
        manager.RegisterApp(AppA, ctx, isDefault: true);

        IProbeAppContext returned = holder.SwitchToApp(AppA, manager, new AllowAllAppAccessAuthorizer());

        returned.Should().BeSameAs(ctx, "返回值必须保持 SDK 自有的上下文类型，调用方无需向下转型");
        holder.Current.Should().BeSameAs(ctx);
        holder.SwitchToDefaultApp(manager).Should().BeSameAs(ctx);
    }

    /// <summary>模拟 SDK 自有上下文接口（如 <c>IFeishuAppContext : IMudAppContext</c>）。</summary>
    private interface IProbeAppContext : IMudAppContext
    {
    }

    private sealed class ProbeAppContext(string appKey) : IProbeAppContext
    {
        public string AppKey => appKey;
        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }

    private static (AsyncLocalAppContextSwitcher Holder, DefaultAppManager<IMudAppContext> Manager) CreateSut()
        => (new AsyncLocalAppContextSwitcher(), new DefaultAppManager<IMudAppContext>());

    private sealed class AlwaysDenyAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => false;
    }

    private sealed class TestAppContext : IMudAppContext
    {
        public TestAppContext(string appKey) => AppKey = appKey;
        public string AppKey { get; }
        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }

    #endregion
}
