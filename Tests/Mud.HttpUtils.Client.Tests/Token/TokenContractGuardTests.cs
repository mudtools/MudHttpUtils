// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P3-06（§5.2 E2）：OAuth2 JSON 源生成上下文的完整性与 AOT 安全性守卫。
/// </summary>
/// <remarks>
/// <c>StandardOAuth2TokenManager.OAuth2TokenResponse</c> 等 DTO <b>不</b>注册到
/// <see cref="OAuth2JsonContext"/> 时，在 Native AOT / 裁剪环境下反序列化会抛
/// <c>NotSupportedException</c>（"no metadata for type"）—— 且该故障只在运行时暴露。
/// 本组用例以反射遍历 DTO 类型，把"漏注册"变成<b>编译后即可发现</b>的测试失败。
/// </remarks>
public class OAuth2JsonContextGuardTests
{
    [Fact]
    public void OAuth2JsonContext_ShouldCoverAllJsonPropertyNameAnnotatedDtos()
    {
        // 判定口径：任何声明了 [JsonPropertyName] 的嵌套类型都是需要源生成元数据的 DTO
        // （[JsonPropertyName] 只能用于 JsonSerializer 参与序列化的成员）。
        var dtoTypes = typeof(StandardOAuth2TokenManager)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsClass
                && t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() != null))
            .ToArray();

        dtoTypes.Should().NotBeEmpty("StandardOAuth2TokenManager 内应存在 JSON DTO（口径失效时本守卫需同步更新）");

        foreach (var dtoType in dtoTypes)
        {
            OAuth2JsonContext.Default.GetTypeInfo(dtoType).Should().NotBeNull(
                $"{dtoType.Name} 未在 OAuth2JsonContext 注册源生成元数据 → AOT/裁剪下反序列化会失败");
        }
    }

    [Fact]
    public void OAuth2JsonContext_ShouldCoverTokenIntrospectionResult()
    {
        OAuth2JsonContext.Default.GetTypeInfo(typeof(TokenIntrospectionResult)).Should().NotBeNull();
    }

    [Fact]
    public void OAuth2JsonContext_ShouldUseSnakeCaseNaming()
    {
        // 契约守卫：OAuth2 令牌端点返回 snake_case（access_token / refresh_token / expires_in）。
        // 若命名策略被误改为默认（PascalCase），反序列化会静默得到全 null 字段。
        var info = OAuth2JsonContext.Default.GetTypeInfo(typeof(StandardOAuth2TokenManager.OAuth2TokenResponse));
        info.Should().NotBeNull();

        var json = System.Text.Json.JsonSerializer.Serialize(
            new StandardOAuth2TokenManager.OAuth2TokenResponse { AccessToken = "t", ExpiresIn = 3600 },
            info!);

        json.Should().Contain("\"access_token\"").And.Contain("\"expires_in\"");
    }
}

/// <summary>
/// R-P3-06（§5.2 E4）：<c>TokenExpiryPolicy</c> 的精确边界与阈值回退分支。
/// </summary>
/// <remarks>
/// 该策略是"是否复用缓存令牌 / 是否清理过期条目"的单一真相；边界写错（&lt; 与 &lt;= 之差）
/// 会直接导致"降级令牌刷新风暴"或"短 TTL 令牌永不命中缓存"两类线上故障。
/// </remarks>
public class TokenExpiryPolicyBoundaryTests
{
    private const long Now = 1_700_000_000_000L;

    [Fact]
    public void IsExpired_ExactBoundary_ShouldBeExpired()
    {
        // expire - threshold == now 是边界：按契约（<=）判定为已过期
        TokenExpiryPolicy.IsExpired(Now + 300_000L, Now, thresholdSeconds: 300).Should().BeTrue();
    }

    [Fact]
    public void IsValid_OneMillisecondAfterBoundary_ShouldBeValid()
    {
        TokenExpiryPolicy.IsValid(Now + 300_001L, Now, thresholdSeconds: 300).Should().BeTrue();
    }

    [Fact]
    public void IsValid_And_IsExpired_ShouldBeExactNegations()
    {
        foreach (var offset in new[] { -1L, 0L, 1L, 299_999L, 300_000L, 300_001L })
        {
            var expire = Now + 300_000L + offset;
            TokenExpiryPolicy.IsValid(expire, Now, 300)
                .Should().Be(!TokenExpiryPolicy.IsExpired(expire, Now, 300), $"offset={offset}");
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void IsExpired_NonPositiveExpire_ShouldAlwaysBeExpired(long expire)
    {
        TokenExpiryPolicy.IsExpired(expire, Now, thresholdSeconds: 300).Should().BeTrue();
    }

    [Fact]
    public void EffectiveThreshold_WhenIssuedAtMissing_ShouldFallBackToConfigured()
    {
        // issuedAt <= 0（存量数据）⇒ 退化到配置阈值，与历史行为一致
        TokenExpiryPolicy.EffectiveThresholdSeconds(0, Now + 60_000L, 300).Should().Be(300);
    }

    [Fact]
    public void EffectiveThreshold_WhenTtlNonPositive_ShouldFallBackToConfigured()
    {
        // expire <= issuedAt（时间戳异常）⇒ ttl <= 0 ⇒ 退化到配置阈值
        TokenExpiryPolicy.EffectiveThresholdSeconds(Now, Now, 300).Should().Be(300);
        TokenExpiryPolicy.EffectiveThresholdSeconds(Now + 1000L, Now, 300).Should().Be(300);
    }

    [Fact]
    public void EffectiveThreshold_ShortTtl_ShouldClampToHalfTtl()
    {
        // TTL = 100s ⇒ 阈值钳位为 50s（否则 300s 提前量会让令牌"刚签发即需刷新"）
        TokenExpiryPolicy.EffectiveThresholdSeconds(Now, Now + 100_000L, 300).Should().Be(50);
    }

    [Fact]
    public void EffectiveThreshold_LongTtl_ShouldKeepConfigured()
    {
        // TTL = 3600s ⇒ half = 1800 > 300 ⇒ 保持配置阈值
        TokenExpiryPolicy.EffectiveThresholdSeconds(Now, Now + 3_600_000L, 300).Should().Be(300);
    }

    [Fact]
    public void TtlAware_IsValid_FreshlyIssuedShortTtlToken_ShouldBeValid()
    {
        // 短 TTL（300s）令牌刚签发即判定：固定 300s 提前量下恒判过期（缓存永不命中，MT-07 缺陷），
        // TTL 感知后有效阈值为 150s ⇒ expire - 150s = now + 150s > now ⇒ 有效。
        var expire = Now + 300_000L;
        TokenExpiryPolicy.IsValid(Now, expire, Now, configuredThresholdSeconds: 300).Should().BeTrue();
    }

    [Fact]
    public void TtlAware_IsExpired_ShouldHonorEffectiveThreshold()
    {
        var expire = Now + 300_000L;
        // 已消耗 200s（有效阈值 150s）⇒ 应判过期
        TokenExpiryPolicy.IsExpired(Now, expire, Now + 200_000L, configuredThresholdSeconds: 300).Should().BeTrue();
        // 仅消耗 100s ⇒ 仍有效
        TokenExpiryPolicy.IsExpired(Now, expire, Now + 100_000L, configuredThresholdSeconds: 300).Should().BeFalse();
    }
}

/// <summary>
/// R-P3-06（§5.2 E7）：加密用户令牌缓存 × 多用户组合隔离。
/// </summary>
/// <remarks>
/// 同时固定两条：① 加密包装不得破坏"按用户隔离"语义；② 底层存储中不得出现访问令牌明文
/// （否则"加密缓存"只是把明文换了个容器）。
/// </remarks>
public class EncryptedUserTokenCacheIsolationTests
{
    /// <summary>
    /// 可逆且<b>确实改变字节形态</b>的测试加密器（Base64 包装 + 前缀）。
    /// </summary>
    /// <remarks>
    /// 刻意不用"前缀 + 明文"这类伪加密：那会让"底层无明文"的断言变成空转
    /// （明文仍原样出现在值里）。Base64 的标准字母表不含 <c>-</c>，
    /// 故 <c>token-for-u1</c> 这类含连字符的探针<b>不可能</b>在密文中以字面量出现。
    /// </remarks>
    private sealed class ReversibleEncryptionProvider : IEncryptionProvider
    {
        private const string Prefix = "enc:v1:";

        public string Encrypt(string plainText)
            => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));

        public string Decrypt(string cipherText)
        {
            if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
                return cipherText;

            return System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(cipherText.Substring(Prefix.Length)));
        }

        public byte[] EncryptBytes(byte[] data) => data;

        public byte[] DecryptBytes(byte[] encryptedData) => encryptedData;
    }

    private sealed class EncryptedUserTokenManager : UserTokenManagerBase
    {
        public EncryptedUserTokenManager(ITokenCache<UserTokenInfo> cache) : base(cache)
        {
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "tenant-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
            string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(
            string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"token-for-{userId}",
                RefreshToken = $"refresh-for-{userId}",
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => base.CanRefreshTokenAsync(userId, cancellationToken);
    }

    [Fact]
    public async Task EncryptedCache_ShouldKeepPerUserIsolation_AndStoreNoPlaintext()
    {
        var inner = new MemoryCacheTokenCache<string>();
        var encrypted = new EncryptedTokenCache<UserTokenInfo>(inner, new ReversibleEncryptionProvider());

        using var manager = new EncryptedUserTokenManager(encrypted);

        (await manager.GetOrRefreshTokenAsync("u1")).Should().Be("token-for-u1");
        (await manager.GetOrRefreshTokenAsync("u2")).Should().Be("token-for-u2");

        // ① 按用户隔离：各自的令牌必须只命中自己的条目
        (await manager.GetOrRefreshTokenAsync("u1")).Should().Be("token-for-u1", "不得读到 u2 的令牌");
        (await manager.GetOrRefreshTokenAsync("u2")).Should().Be("token-for-u2");
        manager.UserKeyIndexTotalKeysForTest.Should().Be(2, "两个用户各有独立条目");

        // ② 底层存储无明文
        inner.Count.Should().BeGreaterThan(0, "底层缓存应确有条目（否则本用例的密文断言是空转）");
        foreach (var key in inner.Keys.ToArray())
        {
            inner.TryGet(key, out var raw).Should().BeTrue();
            raw.Should().NotBeNull();
            raw!.Should().NotContain("token-for-", "访问令牌不得以明文形式驻留底层缓存");
            raw.Should().NotContain("refresh-for-", "刷新令牌不得以明文形式驻留底层缓存");
            raw.Should().Contain("enc:v1:", "底层值应为加密包装后的密文");
        }
    }

    [Fact]
    public async Task EncryptedCache_Logout_ShouldClearBothLayers()
    {
        var inner = new MemoryCacheTokenCache<string>();
        var encrypted = new EncryptedTokenCache<UserTokenInfo>(inner, new ReversibleEncryptionProvider());

        using var manager = new EncryptedUserTokenManager(encrypted);

        await manager.GetOrRefreshTokenAsync("u1");
        inner.Count.Should().BeGreaterThan(0);

        await manager.RemoveTokenAsync("u1");

        (await manager.HasValidTokenAsync("u1")).Should().BeFalse();
        inner.Count.Should().Be(0, "登出必须同时清空加密层与底层存储（防'解除引用但密文仍在'）");
    }
}
