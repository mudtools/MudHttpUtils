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
/// F3 + G6：<c>AddMudHttpClientInfrastructure</c> 公开化后的契约，以及 SSRF 白名单"并集"语义。
/// </summary>
public class MudHttpClientInfrastructureTests
{
    // ── F3：公开入口 ───────────────────────────────────────────────────

    [Fact]
    public void AddMudHttpClientInfrastructure_ShouldRegisterInfrastructureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var returned = services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());

        returned.Should().BeSameAs(services, "应返回同一集合以支持链式调用");
        services.Should().Contain(d => d.ServiceType == typeof(IAppContextHolder),
            "基础设施必须补齐应用上下文持有器");
    }

    [Fact]
    public void AddMudHttpClientInfrastructure_CalledTwice_ShouldBeIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());
        services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());

        services.Count(d => d.ServiceType == typeof(IAppContextHolder))
            .Should().Be(1, "内部注册全部为 TryAdd ⇒ 不叠加");
    }

    [Fact]
    public void AddMudHttpClientInfrastructure_NullServices_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpClientInfrastructure_NullOptions_Throws()
    {
        var act = () => new ServiceCollection().AddMudHttpClientInfrastructure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    // ── G6：白名单并集（此前为整体替换 ⇒ 后注册清空前注册） ──────────────

    [Fact]
    public void AddMudHttpClientInfrastructure_Twice_AllowedDomainsShouldBeUnion()
    {
        var original = UrlValidator.GetAllowedDomains().ToArray();
        try
        {
            UrlValidator.ConfigureAllowedDomains(Array.Empty<string>());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions
            {
                AllowedDomains = { "product-a.example.com" }
            });
            // 第二条产品线（或第二个宿主）注册：修复前此处会**清空**上一条的域名
            services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions
            {
                AllowedDomains = { "product-b.example.com" }
            });

            var allowed = UrlValidator.GetAllowedDomains();
            allowed.Should().Contain("product-a.example.com", "并集：先注册的域名不得被清空");
            allowed.Should().Contain("product-b.example.com");
        }
        finally
        {
            UrlValidator.ConfigureAllowedDomains(original);
        }
    }

    [Fact]
    public void AddMudHttpClientInfrastructure_WithNoAllowedDomains_ShouldNotClearExisting()
    {
        var original = UrlValidator.GetAllowedDomains().ToArray();
        try
        {
            UrlValidator.ConfigureAllowedDomains(new[] { "preexisting.example.com" });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());

            UrlValidator.GetAllowedDomains().Should().Contain("preexisting.example.com",
                "未配置 AllowedDomains 时不得清空既有白名单");
        }
        finally
        {
            UrlValidator.ConfigureAllowedDomains(original);
        }
    }
}
