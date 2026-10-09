// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// G2：<see cref="ITokenStoreCodec"/> / <see cref="DefaultTokenStoreCodec"/> 的编解码契约，
/// 以及便捷桥接工厂 <c>CreateForCredentialToken</c> / <c>CreateForUserTokenInfo</c>。
/// </summary>
public class TokenStoreCodecTests
{
    [Fact]
    public void RoundTrip_ShouldPreserveTokenAndTtl()
    {
        var codec = DefaultTokenStoreCodec.Instance;

        var encoded = codec.Encode("token-value", 3600);
        var decoded = codec.Decode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Value.Token.Should().Be("token-value");
        decoded.Value.ExpiresInSeconds.Should().Be(3600);
    }

    [Fact]
    public void Encode_EmptyToken_ShouldReturnNull()
    {
        DefaultTokenStoreCodec.Instance.Encode(null, 3600).Should().BeNull("墓碑语义：不写入持久层");
        DefaultTokenStoreCodec.Instance.Encode("", 3600).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("|token-only")]
    [InlineData("not-a-number|token")]
    public void Decode_InvalidInput_ShouldReturnNull(string? encoded)
    {
        DefaultTokenStoreCodec.Instance.Decode(encoded).Should().BeNull("非法格式不得抛异常");
    }

    [Fact]
    public void RoundTrip_ZeroTtl_ShouldBePreserved()
    {
        var codec = DefaultTokenStoreCodec.Instance;

        var decoded = codec.Decode(codec.Encode("t", 0));

        decoded.Should().NotBeNull();
        decoded!.Value.ExpiresInSeconds.Should().Be(0, "0 = 无 TTL 信息（桥接器据此跳过写穿）");
    }

    // ── 便捷桥接工厂 ────────────────────────────────────────────────────

    private sealed class ProbeStore : ITokenStore
    {
        public Dictionary<string, (string Access, long Expires)> Access { get; } = new();
        public Dictionary<string, string> Refresh { get; } = new();

        public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => Task.FromResult(Access.TryGetValue(tokenType, out var v) ? v.Access : null);

        public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
        {
            Access[tokenType] = (accessToken, expiresInSeconds);
            return Task.CompletedTask;
        }

        public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => Task.FromResult(Refresh.TryGetValue(tokenType, out var v) ? v : null);

        public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
        {
            Refresh[tokenType] = refreshToken;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
        {
            Access.Remove(tokenType);
            Refresh.Remove(tokenType);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(Access.Keys.ToList());

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Access.Clear();
            Refresh.Clear();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 便捷工厂应等价于"手写 valueAdapter / valueFactory"：Set 后持久层收到访问令牌与刷新令牌。
    /// </summary>
    [Fact]
    public void CreateForCredentialToken_ShouldBridgeToStore()
    {
        var store = new ProbeStore();
        using var cache = TokenStoreBackedTokenCache<CredentialToken>.CreateForCredentialToken(store);

        cache.Set("tenant", new CredentialToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            Expire = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds()
        }, TimeSpan.FromMinutes(10), null);

        cache.TryGet("tenant", out var cached).Should().BeTrue();
        cached!.AccessToken.Should().Be("access-1");
        store.Access.Should().ContainKey("tenant");
        store.Refresh.Should().ContainKey("tenant");
    }
}
