// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-04（架构不变式 I1 / 缺陷 B10）：用户标识控制字符守卫，以及"键形态不变"的登出语义守卫。
/// </summary>
/// <remarks>
/// <para>
/// B10 碰撞构造：用户复合键为 <c>userId + U+001F + scopeKey</c>，而<b>空 scopes 走裸 userId 键</b>；
/// 于是 <c>userId="victim\u001Fread:admin"</c>（裸键）会与
/// <c>userId="victim"</c> + <c>scopes=["read:admin"]</c>（复合键）<b>碰撞</b>。
/// </para>
/// <para>
/// 修复策略为"输入守卫 + 保留键形态"（而非键形态变更），因此必须同时固定
/// "登出/失效仍能按 <c>userId + U+001F</c> 前缀清除该用户全部作用域条目"这一既有语义 ——
/// 若未来把用户键改为长度前缀编码而忘记同步引入用户键索引，本用例会立即失败。
/// </para>
/// </remarks>
public class UserTokenKeyInjectionGuardTests
{
    private const string InjectedUserId = "victim\u001Fread:admin";

    [Fact]
    public async Task UserIdWithSeparator_ShouldBeRejected_OnScopedPath()
    {
        using var manager = new CountingUserTokenManager();

        var act = async () => await manager.GetOrRefreshTokenAsync(InjectedUserId, new[] { "read:admin" });

        await act.Should().ThrowAsync<ArgumentException>(
            "userId 含复合键分隔符即可跨越裸键 / 复合键两个键空间（B10）");
        manager.RefreshCount.Should().Be(0, "守卫必须在刷新之前生效");
    }

    [Fact]
    public async Task UserIdWithSeparator_ShouldBeRejected_OnBarePath()
    {
        using var manager = new CountingUserTokenManager();

        var act = async () => await manager.GetOrRefreshTokenAsync(InjectedUserId);

        await act.Should().ThrowAsync<ArgumentException>();
        manager.RefreshCount.Should().Be(0);
    }

    [Fact]
    public async Task UserIdWithOtherControlCharacters_ShouldBeRejected()
    {
        using var manager = new CountingUserTokenManager();

        foreach (var userId in new[] { "user\u001E1", "user\u0000", "user\n1", "user\t1", "user\u007F1" })
        {
            var captured = userId;
            var act = async () => await manager.GetOrRefreshTokenAsync(captured);
            await act.Should().ThrowAsync<ArgumentException>($"'{captured}' 含控制字符，应被拒绝");
        }
    }

    [Fact]
    public async Task Logout_WithControlCharFreeUser_ShouldStillClearScopedEntries()
    {
        using var manager = new CountingUserTokenManager();

        (await manager.GetOrRefreshTokenAsync("u", new[] { "read:admin" })).Should().Be("token-for-u");
        (await manager.GetOrRefreshTokenAsync("u", new[] { "read:basic" })).Should().Be("token-for-u");
        (await manager.GetOrRefreshTokenAsync("u")).Should().Be("token-for-u");

        (await manager.HasValidTokenAsync("u")).Should().BeTrue();

        (await manager.RemoveTokenAsync("u")).Should().BeTrue();

        (await manager.HasValidTokenAsync("u")).Should().BeFalse(
            "登出必须清除该用户全部作用域条目（依赖 userId + U+001F 前缀扫描；键形态不得变更）");
    }

    [Fact]
    public async Task InvalidateUserToken_ShouldStillClearScopedEntries()
    {
        using var manager = new CountingUserTokenManager();

        await manager.GetOrRefreshTokenAsync("u", new[] { "read:admin" });
        (await manager.HasValidTokenAsync("u")).Should().BeTrue();

        await manager.InvalidateUserTokenAsync("u");

        (await manager.HasValidTokenAsync("u")).Should().BeFalse();
    }

    private sealed class CountingUserTokenManager : UserTokenManagerBase
    {
        private int _refreshCount;

        public int RefreshCount => Volatile.Read(ref _refreshCount);

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
        {
            Interlocked.Increment(ref _refreshCount);
            return Task.FromResult(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"token-for-{userId}",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                RefreshToken = "refresh-token",
            });
        }

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
