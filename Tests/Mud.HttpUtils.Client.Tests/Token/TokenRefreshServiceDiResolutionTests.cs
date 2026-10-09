// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// TMX-19/TMX-20 回归护栏：令牌刷新后台服务的 DI 解析确定性 与 polyfill 特性公开性。
/// <para>
/// TMX-19（P0）：TokenRefreshHostedService / TokenRefreshBackgroundService 曾存在多个公共构造，
/// 容器默认构造选择在解析时抛 "The following constructors are ambiguous"
/// （[ActivatorUtilitiesConstructor] 仅对 ActivatorUtilities 生效，不参与容器默认构造选择）。
/// 修复后两类型各保留唯一公共构造，默认注册必须可解析。
/// </para>
/// </summary>
public class TokenRefreshServiceDiResolutionTests
{
    [Fact]
    public void AddTokenRefreshBackgroundService_ShouldResolveConcreteHostedService_WithoutAmbiguousConstructors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenRefreshBackgroundService(options => options.Enabled = true);

        // Act
        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<TokenRefreshHostedService>();

        // Assert
        concrete.Should().NotBeNull();
        concrete.Should().BeAssignableTo<ITokenRefreshBackgroundService>();
    }

    [Fact]
    public void AddTokenRefreshBackgroundService_ShouldExposeSameInstance_AsHostedServiceAndInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenRefreshBackgroundService();

        // Act
        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<TokenRefreshHostedService>();
        var iface = provider.GetRequiredService<ITokenRefreshBackgroundService>();
#if NET6_0_OR_GREATER
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<TokenRefreshHostedService>()
            .Single();
#endif

        // Assert
        iface.Should().BeSameAs(concrete);
#if NET6_0_OR_GREATER
        hosted.Should().BeSameAs(concrete, "IHostedService 与具体类型应解析到同一单例");
#endif
    }

    // ============================================================
    // B4：注册幂等（多产品线各调一次不得叠加多个 IHostedService）
    // ============================================================

    /// <summary>
    /// B4：<c>AddTokenRefreshBackgroundService</c> 重复调用必须只产生一份注册 ——
    /// 修复前 <c>AddSingleton</c> + <c>AddHostedService(工厂委托)</c> 均为非幂等，
    /// 同一实例会被注册为多个 <c>IHostedService</c>（宿主重复 StartAsync ⇒ 多个刷新循环并发）。
    /// </summary>
    [Fact]
    public void AddTokenRefreshBackgroundService_CalledTwice_ShouldRegisterExactlyOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act：模拟多产品线各调一次（微信自建"已注册即跳过"守卫的动因）
        services.AddTokenRefreshBackgroundService();
        services.AddTokenRefreshBackgroundService();

        // Assert
        services.Count(d => d.ServiceType == typeof(TokenRefreshHostedService))
            .Should().Be(1, "具体实现类型只应有一条注册（幂等守卫判据）");
        services.Count(d => d.ServiceType == typeof(ITokenRefreshBackgroundService))
            .Should().Be(1, "接口只应有一条注册（不叠加双实例）");
        services.Count(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
            .Should().Be(1, "IHostedService 只应有一条注册（否则宿主对同一实例重复 StartAsync）");
    }

    /// <summary>
    /// B4：重复调用后，<c>IHostedService</c> 与 <c>ITokenRefreshBackgroundService</c> 仍解析到<b>同一实例</b>
    /// （保持 :542-547 明确的既有不变量），且 <c>IHostedService</c> 集合中只有一个本服务实例。
    /// </summary>
    [Fact]
    public void AddTokenRefreshBackgroundService_CalledTwice_ShouldKeepSingleInstanceInvariant()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenRefreshBackgroundService();
        services.AddTokenRefreshBackgroundService();

        // Act
        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<TokenRefreshHostedService>();
        var iface = provider.GetRequiredService<ITokenRefreshBackgroundService>();
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<TokenRefreshHostedService>()
            .ToList();

        // Assert
        hostedServices.Should().HaveCount(1, "重复注册不得把同一实例塞入托管服务集合两次");
        hostedServices[0].Should().BeSameAs(concrete);
        iface.Should().BeSameAs(concrete);
    }

    [Fact]
    public void GetRequiredService_TimerBasedService_ShouldResolve_WithoutAmbiguousConstructors()
    {
        // Arrange
        // Timer 版（netstandard2.0 兼容实现）同样收敛为唯一公共构造（IOptions 重载）。
        // 该测试在 net6/net8/net10 上运行时直接针对同一类型资产，防止 ns2.0 路径歧义回归：
        // 直接以容器默认构造选择注册具体类型，多公共构造时解析必抛 ambiguous。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<TokenRefreshBackgroundOptions>();
        services.AddSingleton<TokenRefreshBackgroundService>();

        // Act
        using var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<TokenRefreshBackgroundService>();

        // Assert
        concrete.Should().NotBeNull();
        concrete.Should().BeAssignableTo<ITokenRefreshBackgroundService>();
    }

    [Fact]
    public void PolyfillAttributes_ShouldBePublic_AndApplicableDownstream()
    {
        // TMX-20（P1）：polyfill 特性必须保持 public（下游 netstandard2.0/net6.0 需直接标注）；
        // 若再次 internal 化，本类型应用标注处将在 net6.0 编译失败（Abstractions 无 InternalsVisibleTo）。
        typeof(PolyfillAnnotatedProbe).Should().NotBeNull();

#if NET6_0
        // net6.0 资产上 RequiresDynamicCodeAttribute 即 Abstractions 的 polyfill（BCL .NET 6 不含此类型），
        // IsPublic=true 即证明其公开性。
        typeof(System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute)
            .IsPublic.Should().BeTrue("polyfill 必须公开，否则下游无法标注（CS0122）");
#endif
    }

    /// <summary>
    /// 探针类型：直接应用两个 polyfill 特性。
    /// 若 polyfill 被 internal 化，net6.0（RequiresDynamicCode）编译即失败——编译期回归护栏。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("polyfill 可见性探针")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("polyfill 可见性探针")]
    private static class PolyfillAnnotatedProbe
    {
    }
}
