// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

using Microsoft.Extensions.Logging;

/// <summary>
/// G9-02：运行时安全异常消息统一文本化（appKey / userId）的行为钉子。
/// </summary>
/// <remarks>
/// 库纪律：异常消息回显外部标识（appKey / userId）必须经安全文本化（截断 128 + 控制字符替换为 <c>_</c>）——
/// <c>DefaultAppManager</c> 六处先行使用 <c>AppKeyValidator.ToSafeText</c>，生成侧由
/// <c>ApplicationSwitchGuardContractTests</c> 钉死「不插值原始 appKey」。本类钉住此前遗漏的两个离群点：
/// <c>TokenManagerBase.BindTenantGuard</c>（租户键）与 <c>DefaultTokenProvider</c> 用户身份不一致 / 获取用户令牌失败
/// （userId）。注入向量：这些值来自用户实现的 <c>IMudAppContext.AppKey</c> / <c>ICurrentUserContext.UserId</c>，
/// 框架不强制再校验，恶意实现可返回含 CRLF / 控制字符的字符串，原文插值构成日志注入面。
/// </remarks>
public class SafeTextExceptionMessageTests
{
    private readonly ILogger<DefaultTokenProvider> _logger = new Mock<ILogger<DefaultTokenProvider>>().Object;

    #region BindTenantGuard 租户键文本化

    [Fact]
    public void BindTenantGuard_MalformedTenantKey_NoControlCharsInMessage()
    {
        using var manager = new MalformedKeyTestUserTokenManager();

        // 首绑定写入恶意形态的租户键（模拟 IMudAppContext.AppKey 返回不可信值；
        // 经 InternalsVisibleTo 直调 BindTenantGuard，与 DefaultTokenProvider 路径等价）
        manager.BindTenantGuard("t\u0001A\r\nB");

        // 不同租户键 → 拒绝：异常消息回显「已绑定租户」（即恶意原值）
        var act = () => manager.BindTenantGuard("app-B");
        var ex = act.Should().Throw<InvalidOperationException>().And;

        // 消息中不得出现任何控制字符（G9-02：已替换为 _）
        ex.Message.Should().NotContain("\r", "CR 不得进入异常消息");
        ex.Message.Should().NotContain("\n", "LF 不得进入异常消息");
        ex.Message.Should().NotContain("\u0001", "控制字符不得进入异常消息");
        ex.Message.Should().NotContain("\u007F", "DEL 不得进入异常消息");

        // 文本化后的可读形态仍在（截断 128 + 控制字符 → _），保证消息可诊断
        ex.Message.Should().Contain("t_A__B", "恶意租户键应被文本化为可读形态而非原文");
        ex.Message.Should().Contain("app-B", "触发拒绝的第二个租户键同样经文本化后回显");
    }

    [Fact]
    public void BindTenantGuard_OverlongTenantKey_TruncatedToSafeText()
    {
        using var manager = new MalformedKeyTestUserTokenManager();

        var overlongKey = new string('k', 200); // 超过 AppKey.MaxLength（128）
        manager.BindTenantGuard(overlongKey);

        var act = () => manager.BindTenantGuard("other");
        var message = act.Should().Throw<InvalidOperationException>().And.Message;

        message.Should().Contain(new string('k', 128), "超长键应被截断到 128 字符");
        message.Should().NotContain(new string('k', 129), "超过 128 字符的部分不得回显");
    }

    #endregion

    #region DefaultTokenProvider userId 文本化

    [Fact]
    public async Task UserTokenIdentityMismatch_ExceptionMessage_NoControlCharsInUserId()
    {
        var currentUserContext = new Mock<ICurrentUserContext>();
        currentUserContext.Setup(c => c.UserId).Returns("principal\u0001X");

        var userTokenManager = new Mock<IUserTokenManager>();
        var appContext = new Mock<IMudAppContext>();
        appContext.Setup(c => c.GetTokenManager("test")).Returns(userTokenManager.Object);

        var provider = new DefaultTokenProvider(_logger, currentUserContext.Object);
        var request = new TokenRequest { TokenManagerKey = "test", UserId = "requester\r\nY" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetTokenAsync(appContext.Object, request));

        ex.Message.Should().NotContain("\r", "userId 中的 CR 不得进入异常消息");
        ex.Message.Should().NotContain("\n", "userId 中的 LF 不得进入异常消息");
        ex.Message.Should().NotContain("\u0001", "userId 中的控制字符不得进入异常消息");
        ex.Message.Should().Contain("principal_X", "主体 userId 应文本化回显");
        ex.Message.Should().Contain("requester__Y", "请求 userId 应文本化回显");
    }

    [Fact]
    public async Task UserTokenRetrievalFailed_ExceptionMessage_NoControlCharsInUserId()
    {
        var userTokenManager = new Mock<IUserTokenManager>();
        userTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty); // 获取失败（空令牌）→ 触发「获取用户令牌失败」异常

        var appContext = new Mock<IMudAppContext>();
        appContext.Setup(c => c.GetTokenManager("test")).Returns(userTokenManager.Object);

        var provider = new DefaultTokenProvider(_logger);
        var request = new TokenRequest { TokenManagerKey = "test", UserId = "u\u0007Bad\r\nId" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetTokenAsync(appContext.Object, request));

        ex.Message.Should().NotContain("\u0007", "BEL 不得进入异常消息");
        ex.Message.Should().NotContain("\r", "CR 不得进入异常消息");
        ex.Message.Should().NotContain("\n", "LF 不得进入异常消息");
        ex.Message.Should().Contain("u_Bad__Id", "userId 应文本化回显");
    }

    #endregion

    /// <summary>
    /// 最小 TokenManagerBase 派生桩（与 TokenIsolationAndGuardTests 的 P2TestUserTokenManager 同型），
    /// 供 <c>BindTenantGuard</c> 直调使用。
    /// </summary>
    private sealed class MalformedKeyTestUserTokenManager : UserTokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken { AccessToken = "core", Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() });

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }
    }
}
