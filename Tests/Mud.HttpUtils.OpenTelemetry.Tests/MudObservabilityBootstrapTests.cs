// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mud.HttpUtils.OpenTelemetry.Tests;

/// <summary>
/// <see cref="MudObservabilityBootstrap"/> 的单元测试。
/// </summary>
/// <remarks>
/// 关键路径（源注册 / 去重 / Instrumentation 开关）以「真实采集」断言：
/// <c>TracerProvider</c> + <see cref="BaseProcessor{T}"/> 计数 Activity，
/// <c>MeterProvider</c> + <see cref="BaseExporter{T}"/> 收集 Metric 名称。
/// 仅断言「Provider 非 null」无法发现「源根本没被注册」这类缺陷。
/// </remarks>
public class MudObservabilityBootstrapTests
{
    /// <summary>Activity 采集计数器（注册进 TracerProvider 的处理器，OnEnd 同步回调）。</summary>
    private sealed class CountingActivityProcessor : BaseProcessor<Activity>
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override void OnEnd(Activity data) => Interlocked.Increment(ref _count);
    }

    /// <summary>Metric 采集名收集器（注册进 MeterProvider 的导出器）。</summary>
    private sealed class CountingMetricExporter : BaseExporter<Metric>
    {
        private readonly List<string> _names = new();

        public IReadOnlyList<string> Names
        {
            get
            {
                lock (_names)
                {
                    return _names.ToArray();
                }
            }
        }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                lock (_names)
                {
                    _names.Add(metric.Name);
                }
            }

            return ExportResult.Success;
        }
    }

    /// <summary>
    /// 用于测试的默认贡献描述。
    /// </summary>
    private static MudObservabilityContribution CreateTestContribution(string productName = "Test.Product") => new()
    {
        ProductName = productName,
        ActivitySourceName = "Test.Product.Source",
        MeterName = "Test.Product.Meter",
        DefaultServiceName = "Test.Product.Application",
        DefaultServiceVersion = "1.0.0",
    };

    /// <summary>
    /// 构造一批已注册 <paramref name="contribution"/> 贡献的服务集合，并返回采集器。
    /// </summary>
    private static (ServiceProvider Provider, CountingActivityProcessor Activities, CountingMetricExporter Metrics) BuildProbe(
        MudObservabilityContribution contribution,
        Action<MudObservabilityOptions>? mutate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var activities = new CountingActivityProcessor();
        var metrics = new CountingMetricExporter();

        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            // 关掉 HttpClient Instrumentation：避免 .NET 内置 Meter/源进入采集器干扰断言
            EnableHttpClientInstrumentation = false,
            // 默认不接 OTLP 导出器：本组用例验证的是「源/Meter 注册与去重」，
            // 接了导出器后 Provider 释放会真的向 localhost:4317 发起导出（每个用例数秒级空等）。
            OtlpEndpoint = null,
            ConfigureTracing = tp => tp.AddProcessor(activities),
            ConfigureMetrics = mp => mp.AddReader(new PeriodicExportingMetricReader(metrics, 100)),
        };
        mutate?.Invoke(options);

        services.AddMudObservability(contribution, options);

        var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();
        return (provider, activities, metrics);
    }

    /// <summary>在指定源上产生一个 Activity 并等待其结束（OnEnd 同步触发）。</summary>
    private static void EmitActivity(string sourceName)
    {
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("op");
    }

    /// <summary>在指定 Meter 上产生一次测量并强制刷出。</summary>
    private static void EmitMetric(MeterProvider meterProvider, string meterName, string instrumentName)
    {
        using var meter = new Meter(meterName);
        meter.CreateCounter<long>(instrumentName).Add(1);
        meterProvider.ForceFlush();
    }

    [Theory]
    [InlineData("", "source", "meter", null)]
    [InlineData("Product", "", "meter", null)]
    [InlineData("Product", "source", "", null)]
    [InlineData("Product", "source", null, "")]
    public void ContributionValidation_ShouldThrow_WhenRequiredFieldMissing(
        string productName, string activitySourceName, string? meterName, string? meterWildcard)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = new MudObservabilityContribution
        {
            ProductName = productName,
            ActivitySourceName = activitySourceName,
            MeterName = meterName,
            MeterWildcard = meterWildcard,
            DefaultServiceName = "fallback",
        };
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        var act = () => services.AddMudObservability(contribution, options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMudObservability_ShouldThrowArgumentNull_WhenArgsNull()
    {
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions { ServiceName = "test" };
        var services = new ServiceCollection();

        ((Action)(() => ((IServiceCollection)null!).AddMudObservability(contribution, options)))
            .Should().Throw<ArgumentNullException>();

        ((Action)(() => services.AddMudObservability((MudObservabilityContribution)null!, options)))
            .Should().Throw<ArgumentNullException>();

        ((Action)(() => services.AddMudObservability(contribution, (MudObservabilityOptions)null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DuplicateEntry_ShouldThrow_WhenDifferentProductRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution1 = CreateTestContribution("Mud.HttpUtils");
        var contribution2 = CreateTestContribution("Mud.Wechat");

        services.AddMudObservability(contribution1, new MudObservabilityOptions { ServiceName = "svc1" });

        var act = () => services.AddMudObservability(contribution2, new MudObservabilityOptions { ServiceName = "svc2" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Mud.HttpUtils*")
            .WithMessage("*Mud.Wechat*");
    }

    [Fact]
    public void DuplicateEntry_ShouldBeIdempotent_WhenSameProductRegisteredTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution("Mud.HttpUtils");
        var options = new MudObservabilityOptions { ServiceName = "svc1" };

        services.AddMudObservability(contribution, options);

        // 同产品再次注册不应抛异常
        var act = () => services.AddMudObservability(contribution, new MudObservabilityOptions { ServiceName = "svc1" });
        act.Should().NotThrow();
    }

    [Fact]
    public void DuplicateEntry_ShouldNotReassemble_WhenSameProductRegisteredTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution("Mud.HttpUtils");
        var baseline = new CountingActivityProcessor();
        var reassembled = new CountingActivityProcessor();

        var firstBuilder = services.AddMudObservability(contribution, new MudObservabilityOptions
        {
            ServiceName = "svc",
            EnableHttpClientInstrumentation = false,
            ConfigureTracing = tp => tp.AddProcessor(baseline),
        });

        // 第二次注册传入不同配置：必须被完全忽略（幂等短路），不得把第二份剧本叠加到同一 Provider 上
        var secondBuilder = services.AddMudObservability(contribution, new MudObservabilityOptions
        {
            ServiceName = "svc",
            EnableHttpClientInstrumentation = false,
            ConfigureTracing = tp => tp.AddProcessor(reassembled),
        });

        secondBuilder.Should().BeSameAs(firstBuilder, "同产品重复注册应原样返回首次装配的 builder");

        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();

        EmitActivity("Test.Product.Source");

        baseline.Count.Should().Be(1);
        reassembled.Count.Should().Be(0, "重复注册必须幂等短路——否则同一 Provider 会叠加第二份处理器/导出器（Span 重复导出）");
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void SamplingRatioOutOfRange_ShouldThrowArgumentOutOfRange(double invalidRatio)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            SamplingRatio = invalidRatio,
        };

        var act = () => services.AddMudObservability(contribution, options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SamplingRatioOutOfRange_ShouldThrow_EvenOnIdempotentSecondRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();

        services.AddMudObservability(contribution, new MudObservabilityOptions { ServiceName = "svc" });

        // 幂等短路发生在校验之后：非法参数仍要在启动期暴露，不能被静默吞掉
        var act = () => services.AddMudObservability(contribution, new MudObservabilityOptions
        {
            ServiceName = "svc",
            SamplingRatio = 2.0,
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InvalidServiceName_ShouldThrowOptionsValidationException_AtRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 贡献默认值也为空 → 回填后仍空白 → OptionsValidationException
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test",
            ActivitySourceName = "Test.Source",
            MeterName = "Test.Meter",
            DefaultServiceName = "", // 空默认值
        };
        var options = new MudObservabilityOptions
        {
            ServiceName = "", // 空，回填后仍空
        };

        var act = () => services.AddMudObservability(contribution, options);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ServiceNameShouldFallbackToContributionDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "", // 空 → 回填为 "Test.Product.Application"
        };

        services.AddMudObservability(contribution, options);

        // 验证 options 被回填
        options.ServiceName.Should().Be("Test.Product.Application");
    }

    [Fact]
    public void ServiceVersionShouldFallbackToContributionDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            ServiceVersion = "", // 空 → 回填为 "1.0.0"
        };

        services.AddMudObservability(contribution, options);

        options.ServiceVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void SourceDedup_ShouldNotDoubleRegister_WhenContributionIsMudHttpAndIncludeTrue()
    {
        // 贡献的 ActivitySourceName 就是 MudHttpActivitySource.Name，且 IncludeMudHttpSources = true
        // → 同一次装配内两个来源同名，去重后应只注册一次（Span 不得翻倍）
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.Dedup",
            ActivitySourceName = MudHttpActivitySource.Name,
            MeterName = "Test.Dedup.Meter",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
            IncludeMudHttpSources = true,
        };

        var (provider, activities, _) = BuildProbe(contribution);
        using (provider)
        {
            EmitActivity(MudHttpActivitySource.Name);
            activities.Count.Should().Be(1, "同一源重复注册不得使一次操作被采集两次（Span 翻倍）");

            EmitActivity(MudHttpActivitySource.Name);
            activities.Count.Should().Be(2);
        }
    }

    [Fact]
    public void Sources_ShouldRegisterContributionAndMudHttp()
    {
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.Multi",
            ActivitySourceName = "Test.Multi.Source",
            MeterName = "Test.Multi.Meter",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
            IncludeMudHttpSources = true,
        };

        var (provider, activities, _) = BuildProbe(contribution);
        using (provider)
        {
            EmitActivity("Test.Multi.Source");
            activities.Count.Should().Be(1, "贡献自身的 ActivitySource 必须被注册");

            EmitActivity(MudHttpActivitySource.Name);
            activities.Count.Should().Be(2, "IncludeMudHttpSources = true 时须同时注册 Mud.HttpUtils 源");
        }
    }

    [Fact]
    public void MeterWildcard_ShouldRegister_WhenSet()
    {
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.Wildcard",
            ActivitySourceName = "Test.Wildcard.Source",
            MeterWildcard = "Mud.Test*",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
        };

        var (provider, _, metrics) = BuildProbe(contribution);
        using (provider)
        {
            var meterProvider = provider.GetRequiredService<MeterProvider>();
            EmitMetric(meterProvider, "Mud.Test.Work", "wildcard.work");
            EmitMetric(meterProvider, "Mud.Test.Pay", "wildcard.pay");
            EmitMetric(meterProvider, "Other.Meter", "wildcard.other");

            metrics.Names.Should().Contain("wildcard.work").And.Contain("wildcard.pay");
            metrics.Names.Should().NotContain("wildcard.other", "通配符 Mud.Test* 不应命中 Other.Meter");
        }
    }

    [Fact]
    public void MeterName_ShouldRegister_WhenSet()
    {
        var (provider, _, metrics) = BuildProbe(CreateTestContribution());
        using (provider)
        {
            var meterProvider = provider.GetRequiredService<MeterProvider>();
            EmitMetric(meterProvider, "Test.Product.Meter", "exact.meter");
            EmitMetric(meterProvider, "Other.Meter", "exact.other");

            metrics.Names.Should().Contain("exact.meter");
            metrics.Names.Should().NotContain("exact.other");
        }
    }

    [Fact]
    public void IncludeMudHttpSourcesFalse_ShouldNotRegisterMudHttp()
    {
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.NoInclude",
            ActivitySourceName = "Test.NoInclude.Source",
            MeterName = "Test.NoInclude.Meter",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
            IncludeMudHttpSources = false,
        };

        var (provider, activities, metrics) = BuildProbe(contribution);
        using (provider)
        {
            EmitActivity("Test.NoInclude.Source");
            activities.Count.Should().Be(1);

            // Mud.HttpUtils 源未注册 → 该源上的操作不被采集
            EmitActivity(MudHttpActivitySource.Name);
            activities.Count.Should().Be(1, "IncludeMudHttpSources = false 时不得注册 Mud.HttpUtils 源");

            var meterProvider = provider.GetRequiredService<MeterProvider>();
            EmitMetric(meterProvider, MudHttpMeter.MeterName, "mudhttp.metric");
            metrics.Names.Should().NotContain("mudhttp.metric", "IncludeMudHttpSources = false 时不得注册 Mud.HttpUtils Meter");
        }
    }

    [Fact]
    public void OtlpEndpointNull_ShouldNotRegisterExporter()
    {
        var (provider, activities, _) = BuildProbe(CreateTestContribution(), o => o.OtlpEndpoint = null);

        using (provider)
        {
            // 不抛异常且链路仍可用（源已注册）→ 说明只是没注册导出器
            EmitActivity("Test.Product.Source");
            activities.Count.Should().Be(1);
        }
    }

    [Fact]
    public void ActionOverload_ShouldApplyConfigureBeforeValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();

        // configure 里置 SamplingRatio = 2.0 → 应抛 ArgumentOutOfRangeException
        var act = () => services.AddMudObservability(contribution, o =>
        {
            o.SamplingRatio = 2.0;
            o.ServiceName = "test-service";
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ActionOverload_ShouldWork_WithValidConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();

        services.AddMudObservability(contribution, o =>
        {
            o.ServiceName = "my-service";
            o.SamplingRatio = 0.5;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddMudObservabilitySources_ShouldAppendSourcesToExistingPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var activities = new CountingActivityProcessor();
        var metrics = new CountingMetricExporter();

        // 宿主自建管道：只注册自己的处理器/导出器，尚未包含任何产品源
        var builder = services.AddOpenTelemetry().WithTracing(tp => tp.AddProcessor(activities));
        builder.WithMetrics(mp => mp.AddReader(new PeriodicExportingMetricReader(metrics, 100)));

        var contribution = CreateTestContribution();
        var returned = builder.AddMudObservabilitySources(contribution);

        returned.Should().BeSameAs(builder, "只追加源与 Meter，不得替换调用方的 builder");

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        // 贡献的源与 Meter 被真正追加进既有管道
        EmitActivity("Test.Product.Source");
        activities.Count.Should().Be(1);

        EmitMetric(meterProvider, "Test.Product.Meter", "appended.metric");
        metrics.Names.Should().Contain("appended.metric");

        // 未追加的源不应被采集（证明本方法只追加贡献声明的源）
        EmitActivity(MudHttpActivitySource.Name);
        activities.Count.Should().Be(1);

        _ = tracerProvider;
    }

    [Theory]
    [InlineData("", "source")]
    [InlineData("Product", "")]
    public void AddMudObservabilitySources_ShouldThrow_WhenContributionInvalid(string productName, string sourceName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddOpenTelemetry();

        var contribution = new MudObservabilityContribution
        {
            ProductName = productName,
            ActivitySourceName = sourceName,
            MeterName = "M",
        };

        var act = () => builder.AddMudObservabilitySources(contribution);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMudObservabilitySources_ShouldThrowArgumentNull_WhenArgsNull()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpenTelemetry();
        var contribution = CreateTestContribution();

        ((Action)(() => ((OpenTelemetryBuilder)null!).AddMudObservabilitySources(contribution)))
            .Should().Throw<ArgumentNullException>();

        ((Action)(() => builder.AddMudObservabilitySources(null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudObservability_ReturnsBuilder_ForFluentConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        var builder = services.AddMudObservability(contribution, options);
        builder.Should().NotBeNull();
    }

    [Fact]
    public void EnableTracingFalse_ShouldNotRegisterTracerProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            EnableTracing = false,
            EnableMetrics = true,
        };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().BeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void EnableMetricsFalse_ShouldNotRegisterMeterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            EnableTracing = true,
            EnableMetrics = false,
        };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().BeNull();
    }

    [Fact]
    public void ConfigureTracing_ShouldBeInvoked()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var tracingInvoked = false;
        var metricsInvoked = false;

        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            ConfigureTracing = _ => tracingInvoked = true,
            ConfigureMetrics = _ => metricsInvoked = true,
        };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();

        tracingInvoked.Should().BeTrue("ConfigureTracing 委托应被调用");
        metricsInvoked.Should().BeTrue("ConfigureMetrics 委托应被调用");
    }
}
