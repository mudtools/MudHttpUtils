// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// L-5：多应用管理「启动期接线自检」的行为级用例（MT-02）。
/// </summary>
/// <remarks>
/// <para>
/// 修复前 <see cref="MudHttpAppManagementOptions"/> 被挂在 <c>ValidateOnStart()</c> 上，但其校验器
/// 只做 appKey 格式校验，三个 <c>Require*</c> 开关与 <c>RegisteredAppKeys</c> <b>完全没有被消费</b> ——
/// 对外制造了"已做启动期自检"的假象。
/// </para>
/// <para>
/// 本组用例锁定 <see cref="MudHttpAppManagementOptions"/> 各开关的真实语义：
/// 为 <c>true</c> ⇒ 缺失即阻断启动；为 <c>false</c>（默认）⇒ 仅告警。
/// </para>
/// </remarks>
public class AppManagementStartupValidatorTests
{
    private sealed class FakeAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    private static IServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static AppManagementStartupValidator CreateValidator(
        IServiceProvider sp, MudHttpAppManagementOptions options)
        => new(sp, Options.Create(options), NullLogger<AppManagementStartupValidator>.Instance);

    [Fact]
    public async Task MissingAuthorizer_WithRequireAppAccessAuthorizer_ShouldThrowAtStartup()
    {
        var options = new MudHttpAppManagementOptions
        {
            RequireAppContextHolder = false,
            RequireAppAccessAuthorizer = true,
        };

        using var sp = (ServiceProvider)BuildProvider(s => s.AddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>());
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "MT-02：RequireAppAccessAuthorizer=true 时缺失授权器必须阻断启动"))
            .WithMessage("*IAppAccessAuthorizer*AllowAllAppAccessAuthorizer*",
                "错误消息需给出契约名与显式放行逃生门");
    }

    [Fact]
    public async Task MissingAuthorizer_WithDefaultOptions_ShouldOnlyWarn()
    {
        var options = new MudHttpAppManagementOptions
        {
            RequireAppContextHolder = false,   // 关闭 Holder 检查以隔离本用例关注点
        };

        using var sp = (ServiceProvider)BuildProvider();
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            "默认（Require* 全为 false）只告警不阻断，避免破坏既有单应用宿主的启动");
    }

    [Fact]
    public async Task RegisteredAuthorizer_ShouldPass()
    {
        var options = new MudHttpAppManagementOptions
        {
            RequireAppContextHolder = false,
            RequireAppAccessAuthorizer = true,
        };

        using var sp = (ServiceProvider)BuildProvider(s =>
            s.AddSingleton<IAppAccessAuthorizer, AllowAllAppAccessAuthorizer>());
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("注册 AllowAllAppAccessAuthorizer 即满足显式放行声明");
    }

    [Fact]
    public async Task MissingAppContextHolder_WithRequireAppContextHolder_ShouldThrow()
    {
        var options = new MudHttpAppManagementOptions { RequireAppContextHolder = true };

        using var sp = (ServiceProvider)BuildProvider();
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IAppContextHolder*");
    }

    [Fact]
    public async Task RegisteredAppKeys_NotRegisteredInManager_WithRequire_ShouldThrow()
    {
        var options = new MudHttpAppManagementOptions
        {
            RequireAppContextHolder = false,
            RequireRegisteredAppKeys = true,
        };
        options.RegisteredAppKeys.Add("app-declared-but-missing");

        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-1", new FakeAppContext("app-1"), isDefault: true);

        using var sp = (ServiceProvider)BuildProvider(s => s.AddSingleton<IAppManager<IMudAppContext>>(manager));
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*app-declared-but-missing*",
                "MT-02：RegisteredAppKeys 闭合校验必须真正执行（此前该属性从未被消费）");
    }

    [Fact]
    public async Task RegisteredAppKeys_AllRegistered_ShouldPass()
    {
        var options = new MudHttpAppManagementOptions
        {
            RequireAppContextHolder = false,
            RequireRegisteredAppKeys = true,
        };
        options.RegisteredAppKeys.Add("app-1");

        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-1", new FakeAppContext("app-1"), isDefault: true);

        using var sp = (ServiceProvider)BuildProvider(s => s.AddSingleton<IAppManager<IMudAppContext>>(manager));
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequireAppManager_ShouldBeHonored()
    {
        var options = new MudHttpAppManagementOptions
        {
            RequireAppContextHolder = false,
            RequireAppManager = true,
        };

        using var sp = (ServiceProvider)BuildProvider();
        var validator = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IAppManager*");
    }

    [Fact]
    public async Task AddMudHttpAppManagementStartupValidation_ShouldRegisterHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpAppManagementStartupValidation();

        using var sp = services.BuildServiceProvider();

        sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should().ContainSingle(h => h is AppManagementStartupValidator,
                "MT-02：该扩展方法必须真正注册启动期校验托管服务");
    }
}
