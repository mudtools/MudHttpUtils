// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mud.HttpUtils.Observability;

// 禁用 Client 测试程序集的 Collection 间并行执行。
// 原因：MeterListener / ActivityListener 是进程全局的，EnhancedHttpClientTests、
// TokenManagerBaseTests 等测试类触发的 MudHttpMeter 测量会被 ObservabilityTests
// 的 MeterListener 捕获，导致 ContainSingle 等精确计数断言偶发失败。
// 串行执行消除并行干扰，保证指标断言的稳定性。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 健康检查单元测试：验证 TokenRefreshHealthCheck / MudCircuitBreakerHealthCheck /
/// AddMudHttpHealthChecks 注册与判定逻辑。
/// </summary>
/// <remarks>
/// 此测试类与 <see cref="ObservabilityTests"/> 共享静态状态（CircuitBreakerStateObserver / TokenRefreshStatsCollector），
/// 通过 <see cref="ObservabilityTestCollection"/> 串行执行，避免并发竞态导致状态被相互清空。
/// </remarks>
[Collection(ObservabilityTestCollection.Name)]
public class HealthChecksTests
{
    // ============ TokenRefreshStatsCollector ============

    [Fact]
    public void StatsCollector_Record_Success_Increments_Total_Successes()
    {
        TokenRefreshStatsCollector.Clear();

        TokenRefreshStatsCollector.Record(success: true, "test_manager", 100.0);
        TokenRefreshStatsCollector.Record(success: true, "test_manager", 200.0);

        var stats = TokenRefreshStatsCollector.GetSnapshot(300);
        stats.Total.Should().Be(2);
        stats.Successes.Should().Be(2);
        stats.Failures.Should().Be(0);
        stats.Fallbacks.Should().Be(0);
        stats.FailureRate.Should().Be(0);
        stats.LastRefreshAt.Should().NotBeNull();

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public void StatsCollector_Record_Failure_Increments_Failures()
    {
        TokenRefreshStatsCollector.Clear();

        TokenRefreshStatsCollector.Record(success: false, "test_manager", 50.0);

        var stats = TokenRefreshStatsCollector.GetSnapshot(300);
        stats.Total.Should().Be(1);
        stats.Failures.Should().Be(1);
        stats.Successes.Should().Be(0);
        stats.FailureRate.Should().Be(1.0);
        stats.LastFailureAt.Should().NotBeNull();

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public void StatsCollector_Record_Fallback_Increments_Fallbacks()
    {
        TokenRefreshStatsCollector.Clear();

        TokenRefreshStatsCollector.Record(success: true, "test_manager", 80.0, isFallback: true);

        var stats = TokenRefreshStatsCollector.GetSnapshot(300);
        stats.Total.Should().Be(1);
        stats.Fallbacks.Should().Be(1);
        stats.Successes.Should().Be(0); // fallback 不计入 Successes
        stats.Failures.Should().Be(0);
        stats.FailureRate.Should().Be(0); // fallback 不算失败

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public void StatsCollector_FailureRate_Calculation()
    {
        TokenRefreshStatsCollector.Clear();

        // 5 次成功，3 次失败，2 次降级
        for (int i = 0; i < 5; i++)
            TokenRefreshStatsCollector.Record(success: true, "m", 10.0);
        for (int i = 0; i < 3; i++)
            TokenRefreshStatsCollector.Record(success: false, "m", 10.0);
        for (int i = 0; i < 2; i++)
            TokenRefreshStatsCollector.Record(success: true, "m", 10.0, isFallback: true);

        var stats = TokenRefreshStatsCollector.GetSnapshot(300);
        stats.Total.Should().Be(10);
        stats.Successes.Should().Be(5);
        stats.Failures.Should().Be(3);
        stats.Fallbacks.Should().Be(2);
        stats.FailureRate.Should().Be(0.3);

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public void StatsCollector_GetSnapshot_Respects_Window()
    {
        TokenRefreshStatsCollector.Clear();

        // 窗口期为 1 秒，记录后立即查询应能查到
        TokenRefreshStatsCollector.Record(success: true, "m", 10.0);

        var stats = TokenRefreshStatsCollector.GetSnapshot(1);
        stats.Total.Should().Be(1);

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public void StatsCollector_Clear_Removes_All_Events()
    {
        TokenRefreshStatsCollector.Record(success: true, "m", 10.0);
        TokenRefreshStatsCollector.Record(success: false, "m", 10.0);

        TokenRefreshStatsCollector.Clear();

        var stats = TokenRefreshStatsCollector.GetSnapshot(300);
        stats.Total.Should().Be(0);
    }

    [Fact]
    public void StatsCollector_ToDictionary_Contains_Expected_Keys()
    {
        TokenRefreshStatsCollector.Clear();
        TokenRefreshStatsCollector.Record(success: true, "m", 10.0);

        var stats = TokenRefreshStatsCollector.GetSnapshot(300);
        var dict = stats.ToDictionary();

        dict.Should().ContainKey("total");
        dict.Should().ContainKey("successes");
        dict.Should().ContainKey("failures");
        dict.Should().ContainKey("fallbacks");
        dict.Should().ContainKey("failure_rate");
        dict.Should().ContainKey("window_start_utc");
        dict.Should().ContainKey("last_refresh_at_utc");
        dict.Should().ContainKey("last_failure_at_utc");

        TokenRefreshStatsCollector.Clear();
    }

    // ============ TokenRefreshHealthCheck ============

    [Fact]
    public async Task TokenRefreshHealthCheck_LowSampleSize_Returns_Healthy()
    {
        TokenRefreshStatsCollector.Clear();

        // 只记录 2 次（低于默认 MinSampleSize=5）
        TokenRefreshStatsCollector.Record(success: false, "m", 10.0);
        TokenRefreshStatsCollector.Record(success: false, "m", 10.0);

        var options = new TokenRefreshHealthCheckOptions { MinSampleSize = 5 };
        var check = new TokenRefreshHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("样本数");

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public async Task TokenRefreshHealthCheck_HighFailureRate_Returns_Unhealthy()
    {
        TokenRefreshStatsCollector.Clear();

        // 6 次失败，4 次成功，失败率 60% > CriticalThreshold 50%
        for (int i = 0; i < 6; i++)
            TokenRefreshStatsCollector.Record(success: false, "m", 10.0);
        for (int i = 0; i < 4; i++)
            TokenRefreshStatsCollector.Record(success: true, "m", 10.0);

        var options = new TokenRefreshHealthCheckOptions
        {
            MinSampleSize = 5,
            DegradedThreshold = 0.2,
            CriticalThreshold = 0.5,
        };
        var check = new TokenRefreshHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("超过临界阈值");

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public async Task TokenRefreshHealthCheck_MediumFailureRate_Returns_Degraded()
    {
        TokenRefreshStatsCollector.Clear();

        // 3 次失败，7 次成功，失败率 30% > DegradedThreshold 20%, < CriticalThreshold 50%
        for (int i = 0; i < 3; i++)
            TokenRefreshStatsCollector.Record(success: false, "m", 10.0);
        for (int i = 0; i < 7; i++)
            TokenRefreshStatsCollector.Record(success: true, "m", 10.0);

        var options = new TokenRefreshHealthCheckOptions
        {
            MinSampleSize = 5,
            DegradedThreshold = 0.2,
            CriticalThreshold = 0.5,
        };
        var check = new TokenRefreshHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("超过告警阈值");

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public async Task TokenRefreshHealthCheck_LowFailureRate_Returns_Healthy()
    {
        TokenRefreshStatsCollector.Clear();

        // 1 次失败，9 次成功，失败率 10% < DegradedThreshold 20%
        TokenRefreshStatsCollector.Record(success: false, "m", 10.0);
        for (int i = 0; i < 9; i++)
            TokenRefreshStatsCollector.Record(success: true, "m", 10.0);

        var options = new TokenRefreshHealthCheckOptions
        {
            MinSampleSize = 5,
            DegradedThreshold = 0.2,
            CriticalThreshold = 0.5,
        };
        var check = new TokenRefreshHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("正常");

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public async Task TokenRefreshHealthCheck_NoData_Returns_Healthy()
    {
        TokenRefreshStatsCollector.Clear();

        var options = new TokenRefreshHealthCheckOptions { MinSampleSize = 5 };
        var check = new TokenRefreshHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("样本数 0");

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public async Task TokenRefreshHealthCheck_Data_Contains_Stats()
    {
        TokenRefreshStatsCollector.Clear();
        TokenRefreshStatsCollector.Record(success: true, "m", 10.0);
        TokenRefreshStatsCollector.Record(success: false, "m", 20.0);

        var options = new TokenRefreshHealthCheckOptions { MinSampleSize = 1 };
        var check = new TokenRefreshHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Data.Should().NotBeNull();
        result.Data!.ContainsKey("total").Should().BeTrue();
        result.Data["total"].Should().Be(2);
        result.Data["failures"].Should().Be(1);

        TokenRefreshStatsCollector.Clear();
    }

    [Fact]
    public void TokenRefreshHealthCheck_InvalidOptions_Throws()
    {
        var act1 = () => new TokenRefreshHealthCheck(new TokenRefreshHealthCheckOptions { WindowSeconds = 0 });
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new TokenRefreshHealthCheck(new TokenRefreshHealthCheckOptions
        {
            DegradedThreshold = 1.5,
        });
        act2.Should().Throw<ArgumentOutOfRangeException>();

        var act3 = () => new TokenRefreshHealthCheck(new TokenRefreshHealthCheckOptions
        {
            DegradedThreshold = 0.5,
            CriticalThreshold = 0.2,
        });
        act3.Should().Throw<ArgumentException>();
    }

    // ============ MudCircuitBreakerHealthCheck ============

    [Fact]
    public async Task CircuitBreakerHealthCheck_NoStates_Returns_Healthy()
    {
        CircuitBreakerStateObserver.Clear();

        var check = new MudCircuitBreakerHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data!["total_policies"].Should().Be(0);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public async Task CircuitBreakerHealthCheck_OpenState_Returns_Unhealthy()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);

        var check = new MudCircuitBreakerHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Open");
        result.Data!["open_count"].Should().Be(1);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public async Task CircuitBreakerHealthCheck_HalfOpenState_Returns_Degraded()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.HalfOpen);

        var check = new MudCircuitBreakerHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("HalfOpen");
        result.Data!["half_open_count"].Should().Be(1);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public async Task CircuitBreakerHealthCheck_ClosedState_Returns_Healthy()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Closed);

        var check = new MudCircuitBreakerHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data!["closed_count"].Should().Be(1);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public async Task CircuitBreakerHealthCheck_AllowOneOpen_Returns_Healthy()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);

        var options = new MudCircuitBreakerHealthCheckOptions { MaxOpenCount = 1 };
        var check = new MudCircuitBreakerHealthCheck(options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public async Task CircuitBreakerHealthCheck_OpenTakesPrecedence_Over_HalfOpen()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);
        CircuitBreakerStateObserver.SetState("policy_b", CircuitBreakerState.HalfOpen);

        var check = new MudCircuitBreakerHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Open 优先级高于 HalfOpen
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data!["open_count"].Should().Be(1);
        result.Data!["half_open_count"].Should().Be(1);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public async Task CircuitBreakerHealthCheck_Data_Contains_PolicyDetails()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);
        CircuitBreakerStateObserver.SetState("policy_b", CircuitBreakerState.Closed);

        var check = new MudCircuitBreakerHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Data!["total_policies"].Should().Be(2);
        result.Data!["open_count"].Should().Be(1);
        result.Data!["closed_count"].Should().Be(1);
        result.Data!.ContainsKey("policies").Should().BeTrue();

        CircuitBreakerStateObserver.Clear();
    }

    // ============ AddMudHttpHealthChecks DI ============

    [Fact]
    public void AddMudHttpHealthChecks_Registers_HealthCheckServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpHealthChecks();

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetService<HealthCheckService>();
        healthCheckService.Should().NotBeNull();
    }

    [Fact]
    public async Task AddMudHttpHealthChecks_Registers_BothChecks()
    {
        TokenRefreshStatsCollector.Clear();
        CircuitBreakerStateObserver.Clear();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpHealthChecks();

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        report.Entries.Should().ContainKey(TokenRefreshHealthCheck.Name);
        report.Entries.Should().ContainKey(MudCircuitBreakerHealthCheck.Name);

        // 无数据时应返回 Healthy
        report.Entries[TokenRefreshHealthCheck.Name].Status.Should().Be(HealthStatus.Healthy);
        report.Entries[MudCircuitBreakerHealthCheck.Name].Status.Should().Be(HealthStatus.Healthy);

        TokenRefreshStatsCollector.Clear();
        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public void AddMudHttpHealthChecks_WithConfiguration_Binds_Options()
    {
        var configDict = new Dictionary<string, string?>
        {
            ["MudHttpHealthChecks:TokenRefresh:WindowSeconds"] = "600",
            ["MudHttpHealthChecks:TokenRefresh:DegradedThreshold"] = "0.3",
            ["MudHttpHealthChecks:TokenRefresh:CriticalThreshold"] = "0.7",
            ["MudHttpHealthChecks:TokenRefresh:MinSampleSize"] = "10",
            ["MudHttpHealthChecks:CircuitBreaker:MaxOpenCount"] = "2",
            ["MudHttpHealthChecks:CircuitBreaker:MaxHalfOpenCount"] = "1",
        };
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpHealthChecks(configuration);

        var provider = services.BuildServiceProvider();
        var tokenRefreshOptions = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<TokenRefreshHealthCheckOptions>>().Value;

        tokenRefreshOptions.WindowSeconds.Should().Be(600);
        tokenRefreshOptions.DegradedThreshold.Should().Be(0.3);
        tokenRefreshOptions.CriticalThreshold.Should().Be(0.7);
        tokenRefreshOptions.MinSampleSize.Should().Be(10);

        var cbOptions = provider.GetRequiredService<MudCircuitBreakerHealthCheckOptions>();
        cbOptions.MaxOpenCount.Should().Be(2);
        cbOptions.MaxHalfOpenCount.Should().Be(1);
    }

    [Fact]
    public void AddMudHttpHealthChecks_WithNullServices_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddMudHttpHealthChecks();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudHttpHealthChecks_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var act = () => services.AddMudHttpHealthChecks(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AddMudHttpHealthChecks_FromConfiguration_BindsFailureStatus()
    {
        // Arrange — 验证 FailureStatus 通过 IConfiguration 绑定
        var configDict = new Dictionary<string, string?>
        {
            ["MudHttpHealthChecks:TokenRefresh:FailureStatus"] = "Degraded",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpHealthChecks(configuration);

        // Act & Assert — 不抛异常即表示绑定成功
        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();
        var report = await healthCheckService.CheckHealthAsync();
        report.Entries.Should().ContainKey(TokenRefreshHealthCheck.Name);
    }

    [Fact]
    public void AddMudHttpHealthChecks_FromConfiguration_WithCustomSectionPath_Works()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["Custom:TokenRefresh:WindowSeconds"] = "120",
            ["Custom:TokenRefresh:MinSampleSize"] = "3",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpHealthChecks(configuration, "Custom");

        // Assert
        var provider = services.BuildServiceProvider();
        var tokenRefreshOptions = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<TokenRefreshHealthCheckOptions>>().Value;
        tokenRefreshOptions.WindowSeconds.Should().Be(120);
        tokenRefreshOptions.MinSampleSize.Should().Be(3);
    }

    [Fact]
    public void AddMudHttpHealthChecks_FromConfiguration_WithEmptySection_UsesDefaults()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpHealthChecks(configuration);

        // Assert — 空配置节应使用默认值
        var provider = services.BuildServiceProvider();
        var tokenRefreshOptions = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<TokenRefreshHealthCheckOptions>>().Value;
        tokenRefreshOptions.WindowSeconds.Should().Be(300);
        tokenRefreshOptions.DegradedThreshold.Should().Be(0.2);
        tokenRefreshOptions.CriticalThreshold.Should().Be(0.5);
        tokenRefreshOptions.MinSampleSize.Should().Be(5);
    }
}

/// <summary>
/// 可观测性测试集合定义。
/// </summary>
/// <remarks>
/// <see cref="HealthChecksTests"/> 与 <see cref="ObservabilityTests"/> 都操作静态状态
/// （<see cref="CircuitBreakerStateObserver"/> / <see cref="TokenRefreshStatsCollector"/>），
/// 若并发执行会产生竞态（一个测试的 Clear() 清空另一个测试刚 SetState 的状态）。
/// 通过此 collection 强制 xUnit 串行执行这两个测试类。
/// </remarks>
[CollectionDefinition(Name)]
public class ObservabilityTestCollection
{
    public const string Name = "Observability";
}
