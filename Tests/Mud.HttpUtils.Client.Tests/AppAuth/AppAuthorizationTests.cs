// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 应用切换授权链路的单元测试。
/// 验证 IAppAccessAuthorizer 守卫在 UseApp / BeginScope 中正确拦截未授权请求，
/// 且被拦截时 IAppManager.GetApp 不被调用（零副作用）。
/// </summary>
/// <remarks>
/// 生成器产出的 UseApp/BeginScope(appKey) 方法守卫结构如下（与
/// <c>ApplicationSwitchGuardContractTests</c> + 生成快照一致，MT-02 默认拒绝）：
/// <code>
/// if (!global::Mud.HttpUtils.AppKey.IsValid(appKey))           // 1. 格式前置校验
///     throw new ArgumentException("appKey 格式非法：…", nameof(appKey));
/// if (_appAuthorizer == null)                                    // 2. 默认拒绝（无授权器不放行）
///     throw new InvalidOperationException("多应用切换需要授权器：请注册 IAppAccessAuthorizer 实现 …AllowAllAppAccessAuthorizer…");
/// if (!_appAuthorizer.CanSwitchTo(appKey))                       // 3. 授权判定
///     throw new UnauthorizedAccessException("当前调用主体无权切换到目标应用…");
/// var context = _appManager.GetApp(appKey);                      // 4. 才允许查找应用
/// </code>
/// 本测试通过 Mock 模拟此链路，验证守卫顺序与副作用约束（GetApp 不得在拒绝路径被调用）。
/// </remarks>
public class AppAuthorizationTests
{
    #region 授权拒绝

    [Fact]
    public void UseApp_DeniedByAuthorizer_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var holder = new AsyncLocalAppContextSwitcher();
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        mockAuthorizer.Setup(a => a.CanSwitchTo("other")).Returns(false);

        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        var existingContext = CreateTestContext("app1");
        holder.SwitchTo(existingContext);

        // Act — 模拟生成器产出的 UseApp 守卫链路（MT-02 默认拒绝）
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("other"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (mockAuthorizer.Object == null)
                throw new InvalidOperationException("多应用切换需要授权器：请注册 IAppAccessAuthorizer 实现。");
            if (!mockAuthorizer.Object.CanSwitchTo("other"))
                throw new UnauthorizedAccessException("当前调用主体无权切换到应用 'other'。");
            var context = mockAppManager.Object.GetApp("other");
            holder.SwitchTo(context);
            return context;
        };

        // Assert
        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*other*");

        // holder.Current 不变
        holder.Current.Should().BeSameAs(existingContext);

        // IAppManager.GetApp 未被调用（守卫位于 GetApp 之前）
        mockAppManager.Verify(m => m.GetApp(It.IsAny<string>()), Times.Never);

        // 清理
        holder.SwitchTo(null);
    }

    [Fact]
    public void BeginScope_DeniedByAuthorizer_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var holder = new AsyncLocalAppContextSwitcher();
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        mockAuthorizer.Setup(a => a.CanSwitchTo("forbidden")).Returns(false);

        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        var existingContext = CreateTestContext("app1");
        holder.SwitchTo(existingContext);

        // Act — 模拟生成器产出的 BeginScope(appKey) 守卫链路
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("forbidden"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (mockAuthorizer.Object == null)
                throw new InvalidOperationException("多应用切换需要授权器：请注册 IAppAccessAuthorizer 实现。");
            if (!mockAuthorizer.Object.CanSwitchTo("forbidden"))
                throw new UnauthorizedAccessException("当前调用主体无权切换到应用 'forbidden'。");
            var context = mockAppManager.Object.GetApp("forbidden");
            return holder.BeginScope(context);
        };

        // Assert
        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*forbidden*");

        holder.Current.Should().BeSameAs(existingContext);
        mockAppManager.Verify(m => m.GetApp(It.IsAny<string>()), Times.Never);

        holder.SwitchTo(null);
    }

    [Fact]
    public void UseApp_DeniedByAuthorizer_AppManagerGetAppNotCalled()
    {
        // Arrange
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        mockAuthorizer.Setup(a => a.CanSwitchTo(It.IsAny<string>())).Returns(false);

        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        mockAppManager
            .Setup(m => m.GetApp(It.IsAny<string>()))
            .Throws(new InvalidOperationException("GetApp should not be called when authorization fails"));

        // Act
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("target"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (mockAuthorizer.Object == null)
                throw new InvalidOperationException();
            if (!mockAuthorizer.Object.CanSwitchTo("target"))
                throw new UnauthorizedAccessException();
            // 以下代码不应被执行
            _ = mockAppManager.Object.GetApp("target");
        };

        // Assert
        act.Should().Throw<UnauthorizedAccessException>();
        mockAppManager.Verify(m => m.GetApp(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region 授权允许

    [Fact]
    public void UseApp_AllowedByAuthorizer_SwitchesContext()
    {
        // Arrange
        var holder = new AsyncLocalAppContextSwitcher();
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        mockAuthorizer.Setup(a => a.CanSwitchTo("app2")).Returns(true);

        var targetContext = CreateTestContext("app2");
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        mockAppManager.Setup(m => m.GetApp("app2")).Returns(targetContext);

        var original = CreateTestContext("app1");
        holder.SwitchTo(original);

        // Act — 模拟生成器产出的 UseApp 链路
        IMudAppContext result;
        if (!Mud.HttpUtils.AppKey.IsValid("app2"))
            throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
        if (mockAuthorizer.Object == null)
            throw new InvalidOperationException();
        if (!mockAuthorizer.Object.CanSwitchTo("app2"))
            throw new UnauthorizedAccessException();
        result = mockAppManager.Object.GetApp("app2");
        holder.SwitchTo(result);

        // Assert
        result.Should().BeSameAs(targetContext);
        holder.Current.Should().BeSameAs(targetContext);
        mockAppManager.Verify(m => m.GetApp("app2"), Times.Once);

        holder.SwitchTo(null);
    }

    [Fact]
    public void BeginScope_AllowedByAuthorizer_CreatesScopeAndRestores()
    {
        // Arrange
        var holder = new AsyncLocalAppContextSwitcher();
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        mockAuthorizer.Setup(a => a.CanSwitchTo("app2")).Returns(true);

        var targetContext = CreateTestContext("app2");
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        mockAppManager.Setup(m => m.GetApp("app2")).Returns(targetContext);

        var original = CreateTestContext("app1");
        holder.SwitchTo(original);

        // Act — 模拟生成器产出的 BeginScope(appKey) 链路
        IDisposable scope;
        if (!Mud.HttpUtils.AppKey.IsValid("app2"))
            throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
        if (mockAuthorizer.Object == null)
            throw new InvalidOperationException();
        if (!mockAuthorizer.Object.CanSwitchTo("app2"))
            throw new UnauthorizedAccessException();
        var context = mockAppManager.Object.GetApp("app2");
        scope = holder.BeginScope(context);

        // Assert
        holder.Current.Should().BeSameAs(targetContext);
        mockAppManager.Verify(m => m.GetApp("app2"), Times.Once);

        scope.Dispose();
        holder.Current.Should().BeSameAs(original);

        holder.SwitchTo(null);
    }

    #endregion

    #region 无授权器（null authorizer → 默认拒绝）

    // G7-05：原 UseApp_NoAuthorizerRegistered_FallsThroughToGetApp 断言「null 放行」，
    // 与 MT-02 默认拒绝语义（生成守卫 _appAuthorizer==null → InvalidOperationException）相悖，已删除。
    [Fact]
    public void UseApp_NoAuthorizerRegistered_ThrowsInvalidOperation()
    {
        // Arrange — 当 IAppAccessAuthorizer 未注册时，生成代码中的 _appAuthorizer 为 null，
        // 生成守卫第一步即抛 InvalidOperationException（默认拒绝），不得调用 GetApp。
        var targetContext = CreateTestContext("app2");
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        mockAppManager.Setup(m => m.GetApp("app2")).Returns(targetContext);

        // Act — 模拟 _appAuthorizer = null 时的链路（MT-02 / G7-05）
        IAppAccessAuthorizer? nullAuthorizer = null;
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("app2"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (nullAuthorizer == null)
                throw new InvalidOperationException(
                    "多应用切换需要授权器：请注册 IAppAccessAuthorizer 实现（例如 services.AddSingleton<IAppAccessAuthorizer, YourAuthorizer>()）；" +
                    "若为单应用或完全受信场景，请显式注册 Mud.HttpUtils.AllowAllAppAccessAuthorizer 以表明放行意图。");
            if (!nullAuthorizer.CanSwitchTo("app2"))
                throw new UnauthorizedAccessException();
            return mockAppManager.Object.GetApp("app2");
        };

        // Assert — 默认拒绝：null → IOE，且异常消息给出显式放行逃生门（AllowAllAppAccessAuthorizer）
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowAllAppAccessAuthorizer*");
        mockAppManager.Verify(m => m.GetApp("app2"), Times.Never);
    }

    [Fact]
    public void BeginScope_NoAuthorizerRegistered_ThrowsInvalidOperation()
    {
        // Arrange
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();

        // Act — null 授权器 → IOE（与 UseApp 同守卫）
        IAppAccessAuthorizer? nullAuthorizer = null;
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("app2"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (nullAuthorizer == null)
                throw new InvalidOperationException("多应用切换需要授权器：请注册 IAppAccessAuthorizer 实现。");
            var context = mockAppManager.Object.GetApp("app2");
            return new AsyncLocalAppContextSwitcher().BeginScope(context);
        };

        // Assert
        act.Should().Throw<InvalidOperationException>();
        mockAppManager.Verify(m => m.GetApp(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region 格式校验（守卫顺序：格式 → 授权器缺失 → 授权判定 → GetApp）

    [Fact]
    public void UseApp_InvalidAppKeyFormat_ThrowsArgumentException_BeforeAuthorization()
    {
        // Arrange — 非法格式必须最先被拦截：授权器不得被调用（授权器不负责任何格式校验）
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();

        // Act
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("非法 key!"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (mockAuthorizer.Object == null)
                throw new InvalidOperationException();
            if (!mockAuthorizer.Object.CanSwitchTo("非法 key!"))
                throw new UnauthorizedAccessException();
            return mockAppManager.Object.GetApp("非法 key!");
        };

        // Assert
        act.Should().Throw<ArgumentException>();
        mockAuthorizer.Verify(a => a.CanSwitchTo(It.IsAny<string>()), Times.Never);
        mockAppManager.Verify(m => m.GetApp(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region IAppAccessAuthorizer 接口行为

    [Fact]
    public void IAppAccessAuthorizer_CanSwitchTo_CalledWithAppKey()
    {
        // Arrange
        var mockAuthorizer = new Mock<IAppAccessAuthorizer>();
        mockAuthorizer.Setup(a => a.CanSwitchTo(It.IsAny<string>())).Returns(true);

        // Act
        mockAuthorizer.Object.CanSwitchTo("test-app");

        // Assert
        mockAuthorizer.Verify(a => a.CanSwitchTo("test-app"), Times.Once);
    }

    [Fact]
    public void Authorizer_ThatAlwaysAllows_DoesNotBlock()
    {
        // Arrange
        var alwaysAllow = new AlwaysAllowAuthorizer();
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        var context = CreateTestContext("app1");
        mockAppManager.Setup(m => m.GetApp("app1")).Returns(context);

        // Act — 显式放行授权器（显式注册 = 有意图的放行，非默认拒绝的例外）
        if (!Mud.HttpUtils.AppKey.IsValid("app1"))
            throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
        if (alwaysAllow == null)
            throw new InvalidOperationException();
        if (!alwaysAllow.CanSwitchTo("app1"))
            throw new UnauthorizedAccessException();
        _ = mockAppManager.Object.GetApp("app1");

        // Assert
        mockAppManager.Verify(m => m.GetApp("app1"), Times.Once);
    }

    [Fact]
    public void Authorizer_ThatAlwaysDenies_AlwaysBlocks()
    {
        // Arrange
        var alwaysDeny = new AlwaysDenyAuthorizer();
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();

        // Act
        var act = () =>
        {
            if (!Mud.HttpUtils.AppKey.IsValid("any-app"))
                throw new ArgumentException("appKey 格式非法。", nameof(AppKey));
            if (alwaysDeny == null)
                throw new InvalidOperationException();
            if (!alwaysDeny.CanSwitchTo("any-app"))
                throw new UnauthorizedAccessException();
            _ = mockAppManager.Object.GetApp("any-app");
        };

        // Assert
        act.Should().Throw<UnauthorizedAccessException>();
        mockAppManager.Verify(m => m.GetApp(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region 辅助

    private static TestAppContext CreateTestContext(string appKey) => new(appKey);

    private sealed class AlwaysAllowAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => true;
    }

    private sealed class AlwaysDenyAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => false;
    }

    private sealed class TestAppContext : IMudAppContext
    {
        public TestAppContext(string appKey) { AppKey = appKey; }
        public string AppKey { get; }
        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }

    #endregion
}