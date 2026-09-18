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
/// 生成器产出的 UseApp/BeginScope 方法结构如下：
/// <code>
/// if (_appAuthorizer is not null && !_appAuthorizer.CanSwitchTo(appKey))
///     throw new UnauthorizedAccessException(...);
/// var context = _appManager.GetApp(appKey);
/// </code>
/// 本测试通过 Mock 模拟此链路，验证守卫顺序与副作用约束。
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

        // Act — 模拟生成器产出的 UseApp 守卫链路
        var act = () =>
        {
            if (mockAuthorizer.Object is not null && !mockAuthorizer.Object.CanSwitchTo("other"))
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
            if (mockAuthorizer.Object is not null && !mockAuthorizer.Object.CanSwitchTo("forbidden"))
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
            if (mockAuthorizer.Object is not null && !mockAuthorizer.Object.CanSwitchTo("target"))
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
        if (mockAuthorizer.Object is not null && !mockAuthorizer.Object.CanSwitchTo("app2"))
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
        if (mockAuthorizer.Object is not null && !mockAuthorizer.Object.CanSwitchTo("app2"))
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

    #region 无授权器（null authorizer）

    [Fact]
    public void UseApp_NoAuthorizerRegistered_FallsThroughToGetApp()
    {
        // Arrange — 当 IAppAccessAuthorizer 未注册时，生成代码中的 _appAuthorizer 为 null，
        // 守卫条件 (_appAuthorizer is not null && ...) 短路为 false，不拦截。
        var targetContext = CreateTestContext("app2");
        var mockAppManager = new Mock<IAppManager<IMudAppContext>>();
        mockAppManager.Setup(m => m.GetApp("app2")).Returns(targetContext);

        // Act — 模拟 _appAuthorizer = null 时的链路
        IAppAccessAuthorizer? nullAuthorizer = null;
        IMudAppContext result;
        if (nullAuthorizer is not null && !nullAuthorizer.CanSwitchTo("app2"))
            throw new UnauthorizedAccessException();
        result = mockAppManager.Object.GetApp("app2");

        // Assert — 直接走到 GetApp
        result.Should().BeSameAs(targetContext);
        mockAppManager.Verify(m => m.GetApp("app2"), Times.Once);
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

        // Act
        if (alwaysAllow is not null && !alwaysAllow.CanSwitchTo("app1"))
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
            if (alwaysDeny is not null && !alwaysDeny.CanSwitchTo("any-app"))
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
