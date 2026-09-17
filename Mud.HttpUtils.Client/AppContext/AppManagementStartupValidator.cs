// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// MT-02：多应用管理接线的启动期校验托管服务。
/// </summary>
/// <remarks>
/// <para>
/// 背景：此前 <see cref="MudHttpAppManagementOptions"/> 被挂到 <c>ValidateOnStart()</c> 上，
/// 但其校验器只做 appKey 格式校验，三个属性
/// （<see cref="MudHttpAppManagementOptions.RequireAppContextHolder"/> /
/// <see cref="MudHttpAppManagementOptions.RequireAppManager"/> /
/// <see cref="MudHttpAppManagementOptions.RegisteredAppKeys"/>）除格式校验外<b>完全没有被消费</b> ——
/// 对外制造了"已做启动期自检"的假象。
/// </para>
/// <para>
/// 本服务在宿主启动时真正消费这三个属性：
/// <list type="bullet">
/// <item><description>按 <c>Require*</c> 决定缺失项是 Fail（抛异常阻断启动）还是 Warning（仅记日志）。</description></item>
/// <item><description><c>RegisteredAppKeys</c> 与已注册的 <see cref="IAppManager{TAppContext}"/> 做闭合校验，
/// 发现"配置声明了应用但注册表里没有"的静默降级。</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class AppManagementStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MudHttpAppManagementOptions _options;
    private readonly ILogger _logger;

    public AppManagementStartupValidator(
        IServiceProvider serviceProvider,
        IOptions<MudHttpAppManagementOptions> options,
        ILogger<AppManagementStartupValidator>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? new MudHttpAppManagementOptions();
        _logger = logger ?? NullLogger<AppManagementStartupValidator>.Instance;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var fatal = new List<string>();
        var warnings = new List<string>();

        var holder = _serviceProvider.GetService<IAppContextHolder>();
        if (holder == null)
        {
            Add(fatal, warnings, _options.RequireAppContextHolder,
                "IAppContextHolder 未注册。多应用/多租户场景下必须注册，请调用 services.AddMudHttpAppContextHolder()。");
        }

        var appManager = _serviceProvider.GetService<IAppManager<IMudAppContext>>();
        if (appManager == null)
        {
            Add(fatal, warnings, _options.RequireAppManager,
                "IAppManager<IMudAppContext> 未注册。应用切换（UseApp/BeginScope）不可用，请注册 DefaultAppManager<IMudAppContext> 或自定义实现。");
        }

        var authorizer = _serviceProvider.GetService<IAppAccessAuthorizer>();
        if (authorizer == null)
        {
            Add(fatal, warnings, _options.RequireAppAccessAuthorizer,
                "IAppAccessAuthorizer 未注册。MT-02（BC-18）起 UseApp/BeginScope(appKey) 在未注册授权器时直接抛异常；" +
                "多租户宿主请注册业务授权器，单应用/受信场景请显式注册 Mud.HttpUtils.AllowAllAppAccessAuthorizer。");
        }

        // RegisteredAppKeys 闭合校验：仅当应用管理器可用时有意义。
        if (appManager != null)
        {
            foreach (var appKey in _options.RegisteredAppKeys)
            {
                if (string.IsNullOrWhiteSpace(appKey))
                    continue;

                if (!appManager.HasApp(appKey))
                {
                    Add(fatal, warnings, _options.RequireRegisteredAppKeys,
                        $"MudHttpAppManagementOptions.RegisteredAppKeys 声明了应用 '{AppKeyValidator.ToSafeText(appKey)}'，但应用管理器中未注册该应用。");
                }
            }
        }

        foreach (var message in warnings)
        {
            _logger.LogWarning("多应用管理接线不完整（告警）：{Message}", message);
        }

        if (fatal.Count > 0)
        {
            throw new InvalidOperationException(
                "多应用管理接线不完整：\n" + string.Join("\n", fatal.Select((e, i) => $"  {i + 1}. {e}")));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Add(List<string> fatal, List<string> warnings, bool isFatal, string message)
    {
        if (isFatal)
            fatal.Add(message);
        else
            warnings.Add(message);
    }
}
