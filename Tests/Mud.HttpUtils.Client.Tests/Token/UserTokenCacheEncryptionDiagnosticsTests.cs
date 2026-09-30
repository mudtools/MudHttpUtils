// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Client.Tests.Infrastructure;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P1-06：用户令牌缓存回退到明文时的显式告警，以及"注册了加密器即自动生效"的可验证性。
/// </summary>
/// <remarks>
/// 原实现在未注入 <see cref="IEncryptionProvider"/> 时<b>静默</b>使用明文缓存（无任何日志），
/// 且宿主注册了加密器而管理器未声明可接收它的公共构造函数时"看似启用实则未生效"。
/// 本组用例固定：① 无加密器 → 明文告警；② 有加密器但管理器无法接收 → 未生效告警；
/// ③ 有加密器且管理器可接收 → 无告警且缓存已加密。
/// </remarks>
public class UserTokenCacheEncryptionDiagnosticsTests
{
    private const int PlaintextWarningEventId = 189;
    private const int EncryptionIgnoredWarningEventId = 190;

    [Fact]
    public void WithoutEncryptionProvider_ShouldLogPlaintextWarning()
    {
        var collector = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(collector));
        services.AddMudHttpTokenManager<NoEncryptionProviderProbeUserTokenManager>();

        using var provider = services.BuildServiceProvider();

        // 经接口解析以触达告警接线（两条注入通道都会挂告警）
        provider.GetRequiredService<IUserTokenManager>().Should().NotBeNull();

        collector.Entries.Should().Contain(
            e => e.EventId == PlaintextWarningEventId && e.Level == LogLevel.Warning,
            "未注册 IEncryptionProvider 时必须显式告警，而非静默使用明文缓存");
    }

    [Fact]
    public void WithEncryptionProvider_ButManagerCannotAcceptIt_ShouldLogIgnoredWarning()
    {
        var collector = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(collector));
        services.AddSingleton<IEncryptionProvider, PassthroughEncryptionProvider>();
        services.AddMudHttpTokenManager<EncryptionIgnoredProbeUserTokenManager>();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IUserTokenManager>();

        manager.Should().NotBeNull();
        collector.Entries.Should().Contain(
            e => e.EventId == EncryptionIgnoredWarningEventId,
            "容器中已注册加密器但管理器未接受它时必须显式告警（加密看似启用实则未生效）");
    }

    [Fact]
    public void WithEncryptionProvider_AndManagerAcceptsIt_ShouldNotWarn_AndCacheIsEncrypted()
    {
        var collector = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(collector));
        services.AddSingleton<IEncryptionProvider, PassthroughEncryptionProvider>();
        services.AddMudHttpTokenManager<EncryptingProbeUserTokenManager>();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IUserTokenManager>();

        manager.Should().BeOfType<EncryptingProbeUserTokenManager>();
        ((EncryptingProbeUserTokenManager)manager).UsesEncryptedCacheForTest.Should().BeTrue(
            "公共构造函数接受 IEncryptionProvider 时容器会自动注入并启用加密缓存");
        collector.Entries.Should().NotContain(
            e => e.EventId == PlaintextWarningEventId || e.EventId == EncryptionIgnoredWarningEventId,
            "加密已生效时不得产生误导性告警");
    }

    /// <summary>可逆的最简加密器（前缀标记），足以验证"缓存确实走了加密包装"。</summary>
    private sealed class PassthroughEncryptionProvider : IEncryptionProvider
    {
        private const string Prefix = "enc:";

        public string Encrypt(string plainText) => Prefix + plainText;

        public string Decrypt(string cipherText)
            => cipherText.StartsWith(Prefix, StringComparison.Ordinal) ? cipherText.Substring(Prefix.Length) : cipherText;

        public byte[] EncryptBytes(byte[] data) => data;

        public byte[] DecryptBytes(byte[] encryptedData) => encryptedData;
    }

    private abstract class ProbeUserTokenManagerBase : UserTokenManagerBase
    {
        // 构造函数不继承：显式转发到基类的加密构造函数入口，供"可接收加密器"的派生类使用。
        protected ProbeUserTokenManagerBase()
        {
        }

        protected ProbeUserTokenManagerBase(
            ITokenCache<UserTokenInfo>? userTokenCache,
            UserTokenCacheOptions? cacheOptions,
            IEncryptionProvider? encryption)
            : base(userTokenCache, cacheOptions, encryption)
        {
        }

        public bool UsesEncryptedCacheForTest => UsesEncryptedCache;

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
                AccessToken = "user-token",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });

        public override Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    /// <summary>
    /// 仅有公共无参构造 + 容器中<b>未注册</b>加密器 ⇒ 明文缓存，触发 EventId 189 告警。
    /// 注：告警按"管理器类型"一次性去重，故两种明文场景各用一个独立类型，避免用例间相互抑制。
    /// </summary>
    private sealed class NoEncryptionProviderProbeUserTokenManager : ProbeUserTokenManagerBase
    {
    }

    /// <summary>
    /// 仅有公共无参构造 + 容器中<b>已注册</b>加密器 ⇒ 加密"看似启用实则未生效"，触发 EventId 190 告警。
    /// </summary>
    private sealed class EncryptionIgnoredProbeUserTokenManager : ProbeUserTokenManagerBase
    {
    }

    /// <summary>公共构造函数接受 <see cref="IEncryptionProvider"/> ⇒ 容器自动注入并启用加密缓存。</summary>
    private sealed class EncryptingProbeUserTokenManager : ProbeUserTokenManagerBase
    {
        public EncryptingProbeUserTokenManager(IEncryptionProvider encryption)
            : base(null, null, encryption)
        {
        }
    }
}
