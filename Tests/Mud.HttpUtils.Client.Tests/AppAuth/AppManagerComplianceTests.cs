// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Testing;

namespace Mud.HttpUtils.Client.Tests.AppAuth;

/// <summary>
/// F-5：官方合规测试套件（<see cref="AppManagerCompliance{TAppContext}"/>）的自检 ——
/// 套件对<b>基准实现</b>（DefaultAppManager + 默认注册表）必须全绿；
/// 对<b>违规实现</b>必须能指出违规点（套件不是橡皮图章）。
/// 同时演示下游用法：继承套件 + 覆写 <see cref="AppManagerCompliance{TAppContext}.Fail"/> 接到 xUnit 断言。
/// </summary>
public class AppManagerComplianceTests
{
    private sealed class TestAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;
        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();
        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();
        public T? GetService<T>() where T : class => null;
    }

    /// <summary>基准派生套件：DefaultAppManager（默认注册表）。下游真实用法即此形态。</summary>
    private sealed class BaselineCompliance : AppManagerCompliance<IMudAppContext>
    {
        protected override IAppManager<IMudAppContext> CreateManager() => new DefaultAppManager<IMudAppContext>();
        protected override IMudAppContext CreateContext(string appKey) => new TestAppContext(appKey);
        protected override void Fail(string message) => Assert.Fail(message);
    }

    /// <summary>
    /// 违规实现：appKey 大小写不敏感（把键规整为小写后委托）—— 违反 G8-11 序数语义契约。
    /// 套件必须抓到（经 <c>Fail</c> 出口抛 <see cref="ComplianceFailureException"/>），证明套件不是橡皮图章。
    /// </summary>
    private sealed class CaseInsensitiveManager : IAppManager<IMudAppContext>
    {
        private readonly DefaultAppManager<IMudAppContext> _inner = new();
        private static string Norm(string appKey) => appKey.ToLowerInvariant();

        public void RegisterApp(string appKey, IMudAppContext appContext, bool isDefault = false) => _inner.RegisterApp(Norm(appKey), appContext, isDefault);
        public Task RegisterAppAsync(string appKey, IMudAppContext appContext, bool isDefault = false, CancellationToken cancellationToken = default) => _inner.RegisterAppAsync(Norm(appKey), appContext, isDefault, cancellationToken);
        public IMudAppContext GetApp(string appKey) => _inner.GetApp(Norm(appKey));
        public bool TryGetApp(string appKey, out IMudAppContext? appContext) => _inner.TryGetApp(Norm(appKey), out appContext);
        public IMudAppContext GetDefaultApp() => _inner.GetDefaultApp();
        public IEnumerable<IMudAppContext> GetAllApps() => _inner.GetAllApps();
        public bool HasApp(string appKey) => _inner.HasApp(Norm(appKey));
        public bool RemoveApp(string appKey) => _inner.RemoveApp(Norm(appKey));
        public void UpdateApp(string appKey, IMudAppContext appContext) => _inner.UpdateApp(Norm(appKey), appContext);
        public Task UpdateAppAsync(string appKey, IMudAppContext appContext, CancellationToken cancellationToken = default) => _inner.UpdateAppAsync(Norm(appKey), appContext, cancellationToken);
        public event EventHandler<AppConfigurationChangedEventArgs>? ConfigurationChanged
        {
            add => _inner.ConfigurationChanged += value;
            remove => _inner.ConfigurationChanged -= value;
        }
        public string? DefaultAppKey => _inner.DefaultAppKey;
        public void SetDefaultApp(string appKey) => _inner.SetDefaultApp(Norm(appKey));
        public bool TrySetDefaultApp(string appKey) => _inner.TrySetDefaultApp(Norm(appKey));
    }

    // ── ① 基准实现全绿（含覆写 Fail 接到 xUnit 的下游用法演示） ─────────

    [Fact]
    public void BaselineManager_ShouldPassFullComplianceSuite()
    {
        var suite = new BaselineCompliance();
        suite.VerifyAll();   // 任何违规经 Assert.Fail 抛出
    }

    [Fact]
    public void BaselineManager_ShouldPassLazySemanticsCheck()
    {
        // 懒加载是 DefaultAppManager 扩展面：基准实现必须过；不支持的实现自动跳过。
        var suite = new BaselineCompliance();
        suite.Verify_LazyRegistrationSemantics();
    }

    // ── ② 套件默认 Fail 出口（框架无关） ─────────────────────────────

    [Fact]
    public void DefaultFail_ShouldThrowInvalidOperationException()
    {
        var suite = new BaselineComplianceNoOverride();
        var act = () => suite.FailProbe("boom");
        act.Should().Throw<InvalidOperationException>().WithMessage("*boom*");
    }

    /// <summary>暴露默认 Fail（不覆写）的探测壳。</summary>
    private sealed class BaselineComplianceNoOverride : AppManagerCompliance<IMudAppContext>
    {
        protected override IAppManager<IMudAppContext> CreateManager() => new DefaultAppManager<IMudAppContext>();
        protected override IMudAppContext CreateContext(string appKey) => new TestAppContext(appKey);
        public void FailProbe(string message) => Fail(message);
    }

    // ── ③ 套件能抓违规（防橡皮图章） ─────────────────────────────────

    [Fact]
    public void ViolatingManager_ShouldBeRejectedBySuite()
    {
        var suite = new CatchingCompliance();
        suite.ManagerFactory = () => new CaseInsensitiveManager();

        var act = () => suite.Verify_AppKeyIsCaseSensitive();
        act.Should().Throw<ComplianceFailureException>("大小写不敏感的违规实现必须被套件抓到");
    }

    /// <summary>捕获式套件：把违规点收集为 ComplianceFailureException 供断言。</summary>
    private sealed class CatchingCompliance : AppManagerCompliance<IMudAppContext>
    {
        public Func<IAppManager<IMudAppContext>> ManagerFactory = () => new DefaultAppManager<IMudAppContext>();
        protected override IAppManager<IMudAppContext> CreateManager() => ManagerFactory();
        protected override IMudAppContext CreateContext(string appKey) => new TestAppContext(appKey);
        protected override void Fail(string message) => throw new ComplianceFailureException(message);
    }

    private sealed class ComplianceFailureException(string message) : Exception(message);
}
