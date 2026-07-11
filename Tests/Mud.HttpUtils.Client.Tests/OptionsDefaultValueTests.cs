// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 配置选项默认值验证测试。
/// </summary>
public class OptionsDefaultValueTests
{
    [Fact]
    public void TokenRefreshBackgroundOptions_DefaultValues_AreCorrect()
    {
        var options = new TokenRefreshBackgroundOptions();

        options.Enabled.Should().BeFalse();
        options.RefreshIntervalSeconds.Should().Be(300);
        options.RetryDelaySeconds.Should().Be(60);
        options.StopOnError.Should().BeFalse();
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_DoesNotHave_RefreshBeforeExpirySeconds()
    {
        // RefreshBeforeExpirySeconds 已被删除（死配置）
        var type = typeof(TokenRefreshBackgroundOptions);
        type.GetProperty("RefreshBeforeExpirySeconds").Should().BeNull();
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_RefreshIntervalSeconds_SetToZero_Throws()
    {
        var act = () => new TokenRefreshBackgroundOptions { RefreshIntervalSeconds = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_RefreshIntervalSeconds_SetToNegative_Throws()
    {
        var act = () => new TokenRefreshBackgroundOptions { RefreshIntervalSeconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_RefreshIntervalSeconds_SetToPositive_Succeeds()
    {
        var options = new TokenRefreshBackgroundOptions { RefreshIntervalSeconds = 600 };
        options.RefreshIntervalSeconds.Should().Be(600);
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_RetryDelaySeconds_SetToZero_Throws()
    {
        var act = () => new TokenRefreshBackgroundOptions { RetryDelaySeconds = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_RetryDelaySeconds_SetToNegative_Throws()
    {
        var act = () => new TokenRefreshBackgroundOptions { RetryDelaySeconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TokenRefreshBackgroundOptions_RetryDelaySeconds_SetToPositive_Succeeds()
    {
        var options = new TokenRefreshBackgroundOptions { RetryDelaySeconds = 120 };
        options.RetryDelaySeconds.Should().Be(120);
    }

    [Fact]
    public void UserTokenCacheOptions_DefaultValues_AreCorrect()
    {
        var options = new UserTokenCacheOptions();

        options.SizeLimit.Should().Be(UserTokenCacheOptions.DefaultSizeLimit);
        options.ExpireThresholdSeconds.Should().Be(UserTokenCacheOptions.DefaultExpireThresholdSeconds);
        options.CleanupIntervalSeconds.Should().Be(UserTokenCacheOptions.DefaultCleanupIntervalSeconds);
        options.SlidingExpirationSeconds.Should().Be(UserTokenCacheOptions.DefaultSlidingExpirationSeconds);
        options.CompactionPercentage.Should().Be(UserTokenCacheOptions.DefaultCompactionPercentage);
    }

    [Fact]
    public void UserTokenCacheOptions_DefaultConstants_HaveExpectedValues()
    {
        UserTokenCacheOptions.DefaultSizeLimit.Should().Be(10000);
        UserTokenCacheOptions.DefaultExpireThresholdSeconds.Should().Be(300);
        UserTokenCacheOptions.DefaultCleanupIntervalSeconds.Should().Be(300);
        UserTokenCacheOptions.DefaultSlidingExpirationSeconds.Should().Be(3600);
        UserTokenCacheOptions.DefaultCompactionPercentage.Should().Be(0.2);
    }

    [Fact]
    public void UserTokenCacheOptions_SectionName_HasExpectedValue()
    {
        UserTokenCacheOptions.SectionName.Should().Be("MudHttpUserTokenCache");
    }

    [Fact]
    public void UserTokenCacheOptions_SizeLimit_SetToZero_Throws()
    {
        var act = () => new UserTokenCacheOptions { SizeLimit = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_SizeLimit_SetToNegative_Throws()
    {
        var act = () => new UserTokenCacheOptions { SizeLimit = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_SizeLimit_SetToPositive_Succeeds()
    {
        var options = new UserTokenCacheOptions { SizeLimit = 5000 };
        options.SizeLimit.Should().Be(5000);
    }

    [Fact]
    public void UserTokenCacheOptions_ExpireThresholdSeconds_SetToNegative_Throws()
    {
        var act = () => new UserTokenCacheOptions { ExpireThresholdSeconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_ExpireThresholdSeconds_SetToZero_Succeeds()
    {
        var options = new UserTokenCacheOptions { ExpireThresholdSeconds = 0 };
        options.ExpireThresholdSeconds.Should().Be(0);
    }

    [Fact]
    public void UserTokenCacheOptions_CleanupIntervalSeconds_SetToZero_Throws()
    {
        var act = () => new UserTokenCacheOptions { CleanupIntervalSeconds = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_SlidingExpirationSeconds_SetToZero_Throws()
    {
        var act = () => new UserTokenCacheOptions { SlidingExpirationSeconds = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_CompactionPercentage_SetToZero_Throws()
    {
        var act = () => new UserTokenCacheOptions { CompactionPercentage = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_CompactionPercentage_SetAboveOne_Throws()
    {
        var act = () => new UserTokenCacheOptions { CompactionPercentage = 1.5 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UserTokenCacheOptions_CompactionPercentage_SetToOne_Succeeds()
    {
        var options = new UserTokenCacheOptions { CompactionPercentage = 1.0 };
        options.CompactionPercentage.Should().Be(1.0);
    }

    [Fact]
    public void OAuth2Options_DefaultValues_AreCorrect()
    {
        var options = new OAuth2Options();

        options.ClientId.Should().BeEmpty();
        options.ClientSecret.Should().BeEmpty();
        options.ClientSecretProviderName.Should().BeNull();
        options.TokenEndpoint.Should().BeEmpty();
        options.RevocationEndpoint.Should().BeEmpty();
        options.IntrospectionEndpoint.Should().BeEmpty();
        options.RequireHttps.Should().BeTrue();
        options.ExpirySafetyMarginSeconds.Should().Be(60);
    }

    [Fact]
    public void OAuth2Options_SectionName_HasExpectedValue()
    {
        OAuth2Options.SectionName.Should().Be("MudHttpOAuth2");
    }

    [Fact]
    public void OAuth2Options_GetConflictWarning_WhenBothSecretsSet_ReturnsWarning()
    {
        var options = new OAuth2Options
        {
            ClientSecret = "plain-secret",
            ClientSecretProviderName = "vault-provider"
        };

        var warning = options.GetConflictWarning();
        warning.Should().NotBeNull();
        warning.Should().Contain("ClientSecretProviderName");
        warning.Should().Contain("优先");
    }

    [Fact]
    public void OAuth2Options_GetConflictWarning_WhenOnlyClientSecret_ReturnsNull()
    {
        var options = new OAuth2Options
        {
            ClientSecret = "plain-secret"
        };

        options.GetConflictWarning().Should().BeNull();
    }

    [Fact]
    public void OAuth2Options_GetConflictWarning_WhenOnlyProviderName_ReturnsNull()
    {
        var options = new OAuth2Options
        {
            ClientSecretProviderName = "vault-provider"
        };

        options.GetConflictWarning().Should().BeNull();
    }

    [Fact]
    public void TokenRecoveryOptions_DefaultValues_AreCorrect()
    {
        var options = new TokenRecoveryOptions();

        options.Enabled.Should().BeTrue();
        options.RecoveryMaxRetries.Should().Be(1);
        options.TokenScheme.Should().Be("Bearer");
    }

    [Fact]
    public void MudHttpClientOptions_DefaultValues_AreCorrect()
    {
        var options = new MudHttpClientOptions();

        options.BaseAddress.Should().BeNull();
        options.TimeoutSeconds.Should().BeNull();
        options.DefaultHeaders.Should().BeNull();
        options.AllowCustomBaseUrls.Should().BeFalse();
    }

    [Fact]
    public void MudHttpClientApplicationOptions_DefaultValues_AreCorrect()
    {
        var options = new MudHttpClientApplicationOptions();

        MudHttpClientApplicationOptions.SectionName.Should().Be("MudHttpClients");
        options.Clients.Should().NotBeNull();
        options.Clients.Should().BeEmpty();
        options.DefaultClientName.Should().BeNull();
        options.AllowedDomains.Should().NotBeNull();
        options.AllowedDomains.Should().BeEmpty();
    }

    [Fact]
    public void AesEncryptionOptions_Key_ReturnsClone()
    {
        var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var options = new AesEncryptionOptions { Key = key };

        var returnedKey = options.Key;
        returnedKey.Should().NotBeSameAs(key);
        returnedKey.Should().Equal(key);

        // 修改返回的 key 不应影响内部状态
        returnedKey[0] = 99;
        options.Key[0].Should().Be(1);
    }

    [Fact]
    public void AesEncryptionOptions_Validate_WithValidKey_DoesNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[16] // AES-128
        };

        var act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void AesEncryptionOptions_Validate_WithInvalidKey_Throws()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[10] // 无效长度
        };

        var act = () => options.Validate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AesEncryptionOptions_Validate_WithNullKey_Throws()
    {
        var options = new AesEncryptionOptions();

        var act = () => options.Validate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AesEncryptionOptions_IV_Setter_IsObsoleteButDoesNotThrow()
    {
        // IV 属性已标记 [Obsolete]，但 setter 仍可调用不抛异常（向后兼容）
        var options = new AesEncryptionOptions();
        var iv = new byte[16];
#pragma warning disable CS0618
        options.IV = iv;
#pragma warning restore CS0618
        // Getter 返回副本
#pragma warning disable CS0618
        options.IV.Should().Equal(iv);
#pragma warning restore CS0618
    }

    [Fact]
    public void AesEncryptionOptions_BindFromConfiguration_KeyAsBase64_Succeeds()
    {
        // 验证 AesEncryptionOptions 通过 IConfiguration 绑定 byte[] Key
        var keyBytes = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var keyBase64 = Convert.ToBase64String(keyBytes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpAesEncryption:Key"] = keyBase64,
            })
            .Build();

        var options = new AesEncryptionOptions();
        config.GetSection("MudHttpAesEncryption").Bind(options);

        // IConfiguration 绑定 byte[] 时将 Base64 字符串解码
        options.Key.Should().Equal(keyBytes);
        options.Validate();
    }

    [Fact]
    public void OAuth2OptionsValidator_WithEmptyClientId_Fails()
    {
        var options = new OAuth2Options
        {
            ClientId = "",
            TokenEndpoint = "https://auth.example.com/token",
        };
        var validator = new OAuth2OptionsValidator();

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ClientId");
    }

    [Fact]
    public void OAuth2OptionsValidator_WithEmptyTokenEndpoint_Fails()
    {
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            TokenEndpoint = "",
        };
        var validator = new OAuth2OptionsValidator();

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TokenEndpoint");
    }

    [Fact]
    public void OAuth2OptionsValidator_WithHttpTokenEndpointWhenRequireHttps_Fails()
    {
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            TokenEndpoint = "http://auth.example.com/token",
            RequireHttps = true,
        };
        var validator = new OAuth2OptionsValidator();

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("HTTPS");
    }

    [Fact]
    public void OAuth2OptionsValidator_WithHttpTokenEndpointWhenRequireHttpsFalse_Succeeds()
    {
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            TokenEndpoint = "http://auth.example.com/token",
            RequireHttps = false,
        };
        var validator = new OAuth2OptionsValidator();

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OAuth2OptionsValidator_WithValidOptions_Succeeds()
    {
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            TokenEndpoint = "https://auth.example.com/token",
            RequireHttps = true,
        };
        var validator = new OAuth2OptionsValidator();

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OAuth2OptionsValidator_WithNegativeExpirySafetyMargin_Fails()
    {
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            TokenEndpoint = "https://auth.example.com/token",
            ExpirySafetyMarginSeconds = -1,
        };
        var validator = new OAuth2OptionsValidator();

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ExpirySafetyMarginSeconds");
    }
}
