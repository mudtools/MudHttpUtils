// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Tests;

/// <summary>
/// CFG-34 / 不变量 I-13：<see cref="UrlValidator"/> 白名单「来源分桶」语义。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景</b>：修复前 <c>ConfigureAllowedDomains</c> 是唯一写入入口且为整体替换，
/// 而配置热更新重放（<c>AllowedDomainsReloader</c>）也调用它 ⇒ 运行期
/// <c>AddAllowedDomain</c> 新增的域名会在任一次 <c>IConfigurationRoot.Reload()</c> 后消失。
/// </para>
/// <para>
/// <b>修复后语义</b>：
/// <list type="bullet">
///   <item><c>ConfigureAllowedDomains</c>（公开）：替换<b>配置桶</b>并清空<b>运行期桶</b>（保持原公开契约「调用后恰好等于传入集合」）；</item>
///   <item><c>SetConfigurationDomains</c>（internal）：只替换<b>配置桶</b>（供热更新重放使用）；</item>
///   <item><c>AddAllowedDomain</c>：只写<b>运行期桶</b>；</item>
///   <item><c>RemoveAllowedDomain</c>：两桶同时移除；</item>
///   <item><c>GetAllowedDomains</c> / 校验：两桶<b>并集</b>。</item>
/// </list>
/// </para>
/// </remarks>
[Collection("UrlValidator Collection")]
public class UrlValidatorDomainSourceBucketingTests : IDisposable
{
    public UrlValidatorDomainSourceBucketingTests()
    {
        UrlValidator.ConfigureAllowedDomains(Array.Empty<string>());
    }

    public void Dispose()
    {
        UrlValidator.ConfigureAllowedDomains(Array.Empty<string>());
    }

    [Fact]
    public void T11_RuntimeAddedDomain_SurvivesConfigurationReplay()
    {
        // Arrange：配置桶 = a，随后运行期新增 b
        UrlValidator.ConfigureAllowedDomains(["a.example.com"]);
        UrlValidator.AddAllowedDomain("b.example.com");

        // Act：配置热更新重放（只替换配置桶）
        UrlValidator.SetConfigurationDomains(["a.example.com"]);

        // Assert：运行期增量仍在，且配置值仍在
        UrlValidator.GetAllowedDomains().Should().Contain("b.example.com", "I-13：配置重放不得清除运行期增量");
        UrlValidator.GetAllowedDomains().Should().Contain("a.example.com");

        var act = () => UrlValidator.ValidateUrl("https://b.example.com/api");
        act.Should().NotThrow("运行期新增的域名必须仍然通过白名单校验");
    }

    [Fact]
    public void SetConfigurationDomains_ReplacesPreviousConfigurationValues()
    {
        UrlValidator.ConfigureAllowedDomains(["old.example.com"]);

        UrlValidator.SetConfigurationDomains(["new.example.com"]);

        var whitelist = UrlValidator.GetAllowedDomains();
        whitelist.Should().Contain("new.example.com");
        whitelist.Should().NotContain("old.example.com", "配置桶是整体替换，不是累加");
    }

    [Fact]
    public void ConfigureAllowedDomains_ReplacesBothBuckets()
    {
        // 公开 API 契约：调用后白名单「恰好」等于传入集合 ⇒ 运行期桶必须被清空
        UrlValidator.ConfigureAllowedDomains(["a.example.com"]);
        UrlValidator.AddAllowedDomain("runtime.example.com");

        UrlValidator.ConfigureAllowedDomains(["c.example.com"]);

        var whitelist = UrlValidator.GetAllowedDomains();
        whitelist.Should().Contain("c.example.com");
        whitelist.Should().NotContain("a.example.com");
        whitelist.Should().NotContain("runtime.example.com");
    }

    [Fact]
    public void AddAllowedDomain_Alone_IsUsableWithoutConfigurationBucket()
    {
        UrlValidator.AddAllowedDomain("only-runtime.example.com");

        UrlValidator.GetAllowedDomains().Should().Contain("only-runtime.example.com");

        var act = () => UrlValidator.ValidateUrl("https://only-runtime.example.com/api");
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveAllowedDomain_RemovesFromBothBuckets()
    {
        UrlValidator.ConfigureAllowedDomains(["config.example.com"]);
        UrlValidator.AddAllowedDomain("runtime.example.com");

        UrlValidator.RemoveAllowedDomain("config.example.com");
        UrlValidator.RemoveAllowedDomain("runtime.example.com");

        var whitelist = UrlValidator.GetAllowedDomains();
        whitelist.Should().NotContain("config.example.com");
        whitelist.Should().NotContain("runtime.example.com");
    }

    [Fact]
    public void RemoveAllowedDomain_RuntimeRemoval_IsPermanentAcrossReplay()
    {
        UrlValidator.AddAllowedDomain("runtime.example.com");
        UrlValidator.RemoveAllowedDomain("runtime.example.com");

        UrlValidator.SetConfigurationDomains(["config.example.com"]);

        UrlValidator.GetAllowedDomains().Should().NotContain("runtime.example.com");
    }

    [Fact]
    public void SubdomainMatching_AppliesToRuntimeBucket()
    {
        UrlValidator.SetConfigurationDomains(["config.example.com"]);
        UrlValidator.AddAllowedDomain("runtime.example.com");

        var act = () => UrlValidator.ValidateUrl("https://api.runtime.example.com/x");
        act.Should().NotThrow("子域匹配对运行期桶同样生效");
    }

    [Fact]
    public void EmptyBothBuckets_StrictMode_ThrowsWithUnconfiguredMessage()
    {
        var act = () => UrlValidator.ValidateUrl("https://anything.example.com/x");

        act.Should().Throw<InvalidOperationException>().WithMessage("*未配置域名白名单*");
    }
}
