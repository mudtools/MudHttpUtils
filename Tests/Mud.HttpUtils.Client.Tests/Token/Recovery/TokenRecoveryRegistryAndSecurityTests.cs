// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// SR-M6（P2.4）恢复执行器注册表路由 + SR-M7（P2.5）userId 一致性校验 + SR-M8（P3.4）加密缓存测试。
/// </summary>
public class TokenRecoveryRegistryAndSecurityTests
{
    private static HttpRequestMessage CreateAuthorizedRequest(string uri = "https://api.example.com/data")
        => new(HttpMethod.Get, uri)
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

    private static Mock<ITokenManager> CreateManagerReturning(string token)
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        return mock;
    }

    [Fact]
    public async Task Recovery_WithRegistry_ShouldRouteToKeyedManager()
    {
        var managerA = CreateManagerReturning("token-from-A");
        var managerB = CreateManagerReturning("token-from-B");
        var injected = CreateManagerReturning("token-from-injected");

        var registry = new DelegateTokenManagerRegistry(key =>
            key == "manager-A" ? managerA.Object : (key == "manager-B" ? managerB.Object : null));
        var executor = new TokenRecoveryExecutor(
            injected.Object, userTokenManager: null, managerRegistry: registry);

        // 请求 A 的上下文 → 恢复链路走 managerA
        var requestA = CreateAuthorizedRequest();
        requestA.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { TokenManagerKey = "manager-A" });

        var responseA = await executor.ExecuteAsync(
            requestA,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "token-from-A"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        responseA.StatusCode.Should().Be(HttpStatusCode.OK, "注册表命中：刷新/重试全链路走 key 对应管理器");
        managerA.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        injected.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "错误实例零调用（凭据错配防线）");
        responseA.Dispose();
    }

    [Fact]
    public async Task Recovery_RegistryMiss_ShouldFallbackWithWarning()
    {
        var injected = CreateManagerReturning("token-from-injected");
        var registry = new DelegateTokenManagerRegistry(_ => null);   // 未知 key → null
        var logger = new CapturingLogger();
        var executor = new TokenRecoveryExecutor(
            injected.Object, userTokenManager: null, options: null, logger: logger, managerRegistry: registry);

        var request = CreateAuthorizedRequest();
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { TokenManagerKey = "unknown-key" });

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "token-from-injected"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "解析失败回退注入实例：恢复仍成功（不 fail-fast）");
        injected.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);

        // SR-M6（D9）可观测性：解析失败必须记 Warning（EventId 160，TokenManagerUnresolved）
        logger.Messages.Should().Contain(m => m.Contains("unknown-key") && m.Contains("回退"),
            "解析失败需记 Warning 便于排查注册表键配置错误");
        response.Dispose();
    }

    [Fact]
    public async Task Recovery_WithoutRegistryKey_ShouldNotTouchRegistry()
    {
        // 未显式指定 TokenManagerKey 时不得用类型名等可观测性键去查询注册表（语义：键缺失 = 单管理器绑定）
        var injected = CreateManagerReturning("token-from-injected");
        var registryCalled = 0;
        var registry = new DelegateTokenManagerRegistry(_ => { Interlocked.Increment(ref registryCalled); return null; });
        var executor = new TokenRecoveryExecutor(
            injected.Object, userTokenManager: null, managerRegistry: registry);

        var request = CreateAuthorizedRequest();   // 无 TokenRecoveryContext

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "token-from-injected"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        registryCalled.Should().Be(0, "键缺失时回退注入实例，不应触发注册表查询");
        response.Dispose();
    }

    [Fact]
    public async Task Recovery_UserLevel_RegistryHit_ShouldRouteToKeyedUserManager()
    {
        // SR-M6（D9）用户级恢复同样经注册表路由：命中 IUserTokenManager 时走 key 对应实例
        var injectedUser = new Mock<IUserTokenManager>();
        var keyedUser = new Mock<IUserTokenManager>();
        foreach (var manager in new[] { injectedUser, keyedUser })
        {
            manager.Setup(m => m.RemoveTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            manager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("keyed-user-token");
        }

        var tenantManager = CreateManagerReturning("tenant-token");
        var registry = new DelegateTokenManagerRegistry(key => key == "user-mgr" ? keyedUser.Object : null);
        var executor = new TokenRecoveryExecutor(
            tenantManager.Object, injectedUser.Object, currentUserContext: null, managerRegistry: registry);

        var request = CreateAuthorizedRequest();
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { UserId = "user-1", TokenManagerKey = "user-mgr" });

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "keyed-user-token"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "用户级恢复命中 key 对应的用户管理器");
        keyedUser.Verify(m => m.GetOrRefreshTokenAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
        injectedUser.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "注入实例零调用（全链路走 key 对应管理器）");
        tenantManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        response.Dispose();
    }

    [Fact]
    public async Task Recovery_UserLevel_RegistryResolvesTenantManager_ShouldFallbackToInjected()
    {
        // TK-06 防线：注册表解析到非 IUserTokenManager（租户实例）时绝不用它执行用户级恢复
        var injectedUser = new Mock<IUserTokenManager>();
        injectedUser.Setup(m => m.RemoveTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        injectedUser.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("injected-user-token");

        var tenantManager = CreateManagerReturning("tenant-token");
        var logger = new CapturingLogger();
        var registry = new DelegateTokenManagerRegistry(key => key == "user-mgr" ? tenantManager.Object : null);
        var executor = new TokenRecoveryExecutor(
            tenantManager.Object, injectedUser.Object, currentUserContext: null, options: null,
            logger: logger, managerRegistry: registry);

        var request = CreateAuthorizedRequest();
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { UserId = "user-1", TokenManagerKey = "user-mgr" });

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "injected-user-token"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "解析到租户管理器 → 回退注入的用户管理器（凭据错配防线）");
        injectedUser.Verify(m => m.GetOrRefreshTokenAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
        tenantManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "绝不用租户管理器执行用户级恢复");
        logger.Messages.Should().Contain(m => m.Contains("user-mgr") && m.Contains("回退"), "回退需记 Warning");
        response.Dispose();
    }

    [Fact]
    public async Task Recovery_UserIdMismatch_ShouldReject()
    {
        var manager = CreateManagerReturning("should-not-be-used");
        var userContext = new StubUserContext("principal-user");
        var userTokenManager = new Mock<IUserTokenManager>();
        var executor = new TokenRecoveryExecutor(
            manager.Object, userTokenManager.Object, userContext);

        var request = CreateAuthorizedRequest();
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { UserId = "attacker-controlled-id" });   // 与主体身份不一致

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "身份不一致即拒绝（失败安全）");
        userTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "不触发任何刷新（防任意用户令牌读取原语）");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        response.Dispose();
    }

    [Fact]
    public async Task Recovery_UserIdConsistent_ShouldProceed()
    {
        var manager = CreateManagerReturning("tenant-token");
        var userContext = new StubUserContext("user-1");
        var userTokenManager = new Mock<IUserTokenManager>();
        userTokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-token");
        userTokenManager.As<IUserTokenManager>()
            .Setup(m => m.RemoveTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var executor = new TokenRecoveryExecutor(manager.Object, userTokenManager.Object, userContext);

        var request = CreateAuthorizedRequest();
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { UserId = "user-1" });   // 与主体身份一致

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "user-token"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "一致身份的用户级恢复正常进行");
        response.Dispose();
    }

    [Fact]
    public async Task Recovery_NoContextUserId_ShouldFallbackToCurrentUserContext()
    {
        // SR-L2：请求无 TokenRecoveryContext 但上下文有 UserId → 用户级恢复正常发起（死代码激活验证）
        var manager = CreateManagerReturning("tenant-token");
        var userContext = new StubUserContext("context-user");
        var userTokenManager = new Mock<IUserTokenManager>();
        userTokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-token");
        userTokenManager.As<IUserTokenManager>()
            .Setup(m => m.RemoveTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var executor = new TokenRecoveryExecutor(manager.Object, userTokenManager.Object, userContext);

        var request = CreateAuthorizedRequest();   // 无恢复上下文

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(req.Headers.Authorization?.Parameter == "user-token"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        userTokenManager.Verify(m => m.GetOrRefreshTokenAsync("context-user", It.IsAny<CancellationToken>()), Times.Once,
            "上下文 UserId 回退激活：用户级恢复以 context-user 发起");
        response.Dispose();
    }

    private sealed class StubUserContext : ICurrentUserContext
    {
        private readonly string? _userId;
        public StubUserContext(string? userId) => _userId = userId;
        public string? UserId => _userId;
        public void SetUserId(string? userId) { }
    }

    #region SR-M8 EncryptedTokenCache

    private static IEncryptionProvider CreateEncryptionProvider()
        => new DefaultAesEncryptionProvider(Options.Create(new AesEncryptionOptions
        {
            Key = Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")  // 32 字节测试密钥
        }));

    [Fact]
    public void EncryptedTokenCache_Roundtrip()
    {
        var inner = new MemoryCacheTokenCache<string>();
        using var cache = new EncryptedTokenCache<UserTokenInfo>(inner, CreateEncryptionProvider());

        var info = new UserTokenInfo
        {
            UserId = "user-1",
            AccessToken = "secret-access-token",
            RefreshToken = "secret-refresh-token",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };

        cache.Set("user-1", info);

        // 底层检查到的是密文（无明文令牌子串）
        inner.TryGet("user-1", out var cipher).Should().BeTrue();
        cipher.Should().NotContain("secret-access-token", "内存态不落明文凭据");
        cipher.Should().NotContain("secret-refresh-token");

        // 往返等值
        cache.TryGet("user-1", out var restored).Should().BeTrue();
        restored!.AccessToken.Should().Be("secret-access-token");
        restored.RefreshToken.Should().Be("secret-refresh-token");
        restored.UserId.Should().Be("user-1");
    }

    [Fact]
    public void EncryptedTokenCache_CorruptedCipher_ShouldMissNotThrow()
    {
        var inner = new MemoryCacheTokenCache<string>();
        using var cache = new EncryptedTokenCache<UserTokenInfo>(inner, CreateEncryptionProvider());

        cache.Set("user-1", new UserTokenInfo { UserId = "user-1", AccessToken = "tok" });

        // 篡改底层密文
        inner.TryGet("user-1", out var cipher).Should().BeTrue();
        var corrupted = cipher!.Substring(0, Math.Max(1, cipher.Length - 2)) + (cipher.EndsWith("aa") ? "bb" : "aa");
        inner.Set("user-1", corrupted);

        var act = () => cache.TryGet("user-1", out var value);
        act.Should().NotThrow("密文损坏按 miss 处理（触发重新获取），不抛出");
    }

    [Fact]
    public void EncryptedTokenCache_Missing_ShouldReturnFalse()
    {
        using var cache = new EncryptedTokenCache<UserTokenInfo>(
            new MemoryCacheTokenCache<string>(), CreateEncryptionProvider());

        cache.TryGet("nobody", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    /// <summary>
    /// P3（M6 阶段五）：解密失败的 Warning 日志只输出脱敏后的缓存键 ——
    /// 键形如 <c>userId + 分隔符 + scope</c>（含 PII），明文写入日志即泄漏用户标识。
    /// </summary>
    [Fact]
    public void EncryptedTokenCache_DecryptFailure_WarningLog_ShouldMaskKey()
    {
        var inner = new MemoryCacheTokenCache<string>();
        var logger = new CapturingLogger();
        using var cache = new EncryptedTokenCache<UserTokenInfo>(inner, CreateEncryptionProvider(), logger);

        const string key = "13812345678:read write";
        cache.Set(key, new UserTokenInfo { UserId = "13812345678", AccessToken = "tok" });

        inner.TryGet(key, out _).Should().BeTrue();
        inner.Set(key, "not-a-valid-cipher");

        cache.TryGet(key, out _).Should().BeFalse();

        logger.Messages.Should().Contain(m => m.Contains("加密令牌缓存条目解密/反序列化失败"),
            "解密失败必须记 Warning（MT-25）");
        logger.Messages.Should().NotContain(m => m.Contains("13812345678"),
            "日志中的缓存键必须脱敏，不得输出 userId 明文");
    }

    #endregion
}
