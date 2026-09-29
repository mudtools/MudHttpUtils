// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// F-5：配置审计回归基线（跨包）。把本轮 CFG-28 ~ CFG-39 的结论固化为可回归资产。
/// </summary>
/// <remarks>
/// <para>
/// 本文件位于 <c>Integration.Tests</c>（唯一同时引用 Client / Abstractions / Attributes / Resilience 的测试工程），
/// 因此可对<b>全部</b>含 <c>SectionName</c> 的选项类型做一次性契约断言。
/// </para>
/// <para>行为级用例（T1 ~ T21）分列于各自工程；本文件只固化「易被静默破坏、且分散在多个包」的契约。</para>
/// </remarks>
public class ConfigAuditRegressionTests
{
    private const string ClientReadmePath = @"../../../../Mud.HttpUtils.Client/README.md";

    // ========================================================================
    // 配置节名契约：重命名会静默使现有 appsettings.json 失效（用户侧无错误提示）
    // ========================================================================

    [Fact]
    public void OptionsSectionNames_AreStable()
    {
        MudHttpClientApplicationOptions.SectionName.Should().Be("MudHttpClients");
        ResilienceOptions.SectionName.Should().Be("MudHttpResilience");
        OAuth2Options.SectionName.Should().Be("MudHttpOAuth2");
        TokenRefreshBackgroundOptions.SectionName.Should().Be("TokenRefreshBackground");
        TokenRecoveryOptions.SectionName.Should().Be("MudHttpTokenRecovery");
        AesEncryptionOptions.SectionName.Should().Be("MudHttpAesEncryption");
        UserTokenCacheOptions.SectionName.Should().Be("MudHttpUserTokenCache");
        MudCircuitBreakerHealthCheckOptions.SectionName.Should().Be("CircuitBreaker");
    }

    // ========================================================================
    // I-13：AllowedDomains 配置重放不得清除运行期 AddAllowedDomain 增量
    // ========================================================================

    [Fact]
    public void I13_ConfigurationReplay_DoesNotClearRuntimeAddedDomain()
    {
        try
        {
            UrlValidator.ConfigureAllowedDomains(["config.example.com"]);
            UrlValidator.AddAllowedDomain("runtime.example.com");

            // 配置热更新重放（只替换配置桶）
            UrlValidator.SetConfigurationDomains(["config2.example.com"]);

            var whitelist = UrlValidator.GetAllowedDomains();
            whitelist.Should().Contain("runtime.example.com");
            whitelist.Should().Contain("config2.example.com");
            whitelist.Should().NotContain("config.example.com");

            var act = () => UrlValidator.ValidateUrl("https://runtime.example.com/api");
            act.Should().NotThrow();
        }
        finally
        {
            UrlValidator.ConfigureAllowedDomains(Array.Empty<string>());
        }
    }

    // ========================================================================
    // I-14：HttpVersion 未显式配置时不得干预请求版本（两路径默认值一致）
    // ========================================================================

    [Fact]
    public void I14_HttpVersionDefaults_AreConsistentAcrossPaths()
    {
        var diOptions = new EnhancedHttpClientOptions();
        var generatedOptions = new GeneratedClientOptions();

        diOptions.HttpVersion.Should().BeNull();
        diOptions.HttpVersionPolicy.Should().BeNull();
        generatedOptions.HttpVersion.Should().BeNull();
        generatedOptions.HttpVersionPolicy.Should().BeNull();

        // 语义说明：null 表示「不干预」——HttpRequestMessage 的构造默认值是 1.1 / RequestVersionOrLower，
        // 故该默认值与「显式赋 Version11 / RequestVersionOrLower」可观察行为一致（CFG-30 复核结论）。
        new HttpRequestMessage(HttpMethod.Get, "https://api.example.com")
            .Version.Should().Be(HttpVersion.Version11);
    }

    // ========================================================================
    // I-15 + 文档即契约：热更新矩阵 / 默认值 / 掩码器三态 必须留在 README 中
    // ========================================================================

    [Fact]
    public void I15_ClientReadme_KeepsDocumentedContracts()
    {
        var readme = File.ReadAllText(Path.GetFullPath(ClientReadmePath));

        // CFG-36 / F-3：配置热更新能力矩阵（消除「改配置就生效」的错误预期）
        readme.Should().Contain("配置热更新能力矩阵");

        // CFG-30：DefaultRequestVersion 不适用于 SendAsync 的说明
        readme.Should().Contain("DefaultRequestVersion");

        // CFG-33：三种掩码器前提必须完整（未注册 / AddSensitiveDataMasker() / DefaultSensitiveDataMasker）
        readme.Should().Contain("AotSafeSensitiveDataMasker");
        readme.Should().Contain("AddSensitiveDataMasker<DefaultSensitiveDataMasker>");

        // CFG-39：fast-path 回退必须有日志说明
        readme.Should().Contain("回退默认路径");
    }
}
