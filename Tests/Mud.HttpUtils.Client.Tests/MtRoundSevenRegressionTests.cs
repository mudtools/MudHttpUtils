// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Client.Tests;   // CollectingLoggerProvider

namespace Mud.HttpUtils.Tests;

/// <summary>
/// L-7：MT-06 / MT-08 / MT-09 / MT-13 四项修复的回归用例。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><b>MT-06</b>：用户退避表 <c>_userRefreshFailures</c> 原为无界增长
///     （唯一清扫入口 <c>CleanupOrphanedLocks</c> 无调用者）。</description></item>
///   <item><description><b>MT-08</b>：<c>UpdateAppAsync</c> 在 <c>await</c> 初始化期间被 <c>RemoveApp</c> 抢占，
///     原实现会"复活已删除应用"并泄漏新上下文。</description></item>
///   <item><description><b>MT-09</b>：revoke / introspect 端点仅在选项绑定期校验 HTTPS，
///     编程式构造 <c>OAuth2Options</c> 时 <c>client_secret</c> 与令牌可经明文 HTTP 发出。</description></item>
///   <item><description><b>MT-13</b>：客户端名区分大小写（Ordinal）；仅大小写不同的键是误配高发区。</description></item>
/// </list>
/// </remarks>
public class MtRoundSevenRegressionTests
{
    // ---------------------------------------------------------------- MT-06

    /// <summary>用户令牌刷新永久失败的管理器（用于驱动退避表增长）。</summary>
    private sealed class FailingUserTokenManager(UserTokenCacheOptions options) : UserTokenManagerBase(options)
    {
        public int FailureCount => UserRefreshFailureCountForTest;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("用户令牌管理器不应走租户刷新路径");

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
            string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        /// <summary>刷新恒失败 —— 驱动 <c>RecordUserRefreshFailure</c>。</summary>
        public override Task<UserTokenInfo?> RefreshUserTokenAsync(
            string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task UserRefreshFailureTable_ShouldStayBounded_UnderHighCardinalityFailures()
    {
        const int sizeLimit = 8;
        using var manager = new FailingUserTokenManager(new UserTokenCacheOptions { SizeLimit = sizeLimit });

        // 模拟 IdP 故障 + 高基数用户：持续写入远超上限的失败条目
        for (var i = 0; i < sizeLimit * 8; i++)
            await manager.GetTokenAsync($"user-{i}");

        manager.FailureCount.Should().BeLessThanOrEqualTo(sizeLimit,
            "MT-06：退避表必须按 UserTokenCacheOptions.SizeLimit 有界收缩。" +
            $"实际 {manager.FailureCount} —— 无界增长会在高基数用户 + IdP 故障下持续吃内存");
    }

    [Fact]
    public async Task UserRefreshFailureTable_ShouldBeBounded_EvenWhenBackoffWindowNotExpired()
    {
        // 退避窗口内条目不会因"已过期"被第一阶段清理，必须由第二阶段批量移除兜底。
        const int sizeLimit = 4;
        using var manager = new FailingUserTokenManager(new UserTokenCacheOptions { SizeLimit = sizeLimit });

        for (var i = 0; i < 20; i++)
            await manager.GetTokenAsync($"u{i}");

        manager.FailureCount.Should().BeLessThanOrEqualTo(sizeLimit,
            "MT-06：退避窗口内的条目同样必须受上限约束（否则退避表等价于无界）");
    }

    // ---------------------------------------------------------------- MT-08

    private sealed class DelayedAppContext(string appKey) : IMudAppContext, IAsyncInitializable, IDisposable
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public bool Disposed { get; private set; }

        public Task InitializationGate { get; set; } = Task.CompletedTask;

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => InitializationGate;

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task UpdateAppAsync_WhenRemovedDuringInitialization_ShouldNotResurrectApp()
    {
        var manager = new DefaultAppManager<DelayedAppContext>();
        var original = new DelayedAppContext("app-1");
        manager.RegisterApp("app-1", original, isDefault: true);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new DelayedAppContext("app-1") { InitializationGate = gate.Task };

        // 初始化挂起期间移除应用
        var updateTask = manager.UpdateAppAsync("app-1", replacement);
        manager.RemoveApp("app-1").Should().BeTrue("前置条件：应用已移除");

        gate.SetResult();

        var act = async () => await updateTask;

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*被移除或替换*", "MT-08：初始化期间被移除必须放弃更新（原实现会复活已删除应用）");

        manager.HasApp("app-1").Should().BeFalse("MT-08：不得复活已删除的应用");
        replacement.Disposed.Should().BeTrue("MT-08：放弃更新时必须释放新上下文，避免上下文泄漏");
    }

    [Fact]
    public async Task UpdateAppAsync_WhenNotRemoved_ShouldReplaceAndNotDisposeNewContext()
    {
        var manager = new DefaultAppManager<DelayedAppContext>();
        var original = new DelayedAppContext("app-1");
        manager.RegisterApp("app-1", original, isDefault: true);

        var replacement = new DelayedAppContext("app-1");

        await manager.UpdateAppAsync("app-1", replacement);

        manager.HasApp("app-1").Should().BeTrue();
        replacement.Disposed.Should().BeFalse("正常替换路径不得释放新上下文");
    }

    [Fact]
    public async Task UpdateAppAsync_WhenInitializationFails_ShouldDisposeNewContextAndKeepOld()
    {
        var manager = new DefaultAppManager<DelayedAppContext>();
        var original = new DelayedAppContext("app-1");
        manager.RegisterApp("app-1", original, isDefault: true);

        var failing = new DelayedAppContext("app-1")
        {
            InitializationGate = Task.FromException(new InvalidOperationException("初始化失败")),
        };

        var act = async () => await manager.UpdateAppAsync("app-1", failing);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*初始化失败*");
        failing.Disposed.Should().BeTrue("初始化失败必须释放半成品上下文");
        manager.HasApp("app-1").Should().BeTrue("初始化失败不得影响原上下文");
    }

    // ---------------------------------------------------------------- MT-09

    private static StandardOAuth2TokenManager CreateOAuth2Manager(OAuth2Options options)
        => new(new HttpClient(), Options.Create(options), NullLogger<StandardOAuth2TokenManager>.Instance);

    [Fact]
    public async Task RevokeTokenAsync_WithInsecureEndpoint_ShouldThrowBeforeSending()
    {
        var manager = CreateOAuth2Manager(new OAuth2Options
        {
            RequireHttps = true,
            RevocationEndpoint = "http://insecure.example.com/oauth/revoke",
        });

        var act = async () => await manager.RevokeTokenAsync("some-token");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*HTTPS*",
                "MT-09：撤销端点运行期必须校验传输安全 —— 否则 client_secret（Basic 头）与待撤销令牌会经明文 HTTP 发出");
    }

    [Fact]
    public async Task IntrospectTokenAsync_WithInsecureEndpoint_ShouldThrowBeforeSending()
    {
        var manager = CreateOAuth2Manager(new OAuth2Options
        {
            RequireHttps = true,
            IntrospectionEndpoint = "http://insecure.example.com/oauth/introspect",
        });

        var act = async () => await manager.IntrospectTokenAsync("some-token");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*HTTPS*", "MT-09：内省端点运行期同样必须校验传输安全");
    }

    [Fact]
    public async Task RevokeTokenAsync_WithRequireHttpsDisabled_ShouldNotThrowForHttp()
    {
        // 逃生门：开发环境显式 RequireHttps=false 时不拦截（避免修复变成破坏性变更）。
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var manager = new StandardOAuth2TokenManager(
            new HttpClient(handler),
            Options.Create(new OAuth2Options
            {
                RequireHttps = false,
                RevocationEndpoint = "http://localhost:8080/oauth/revoke",
            }),
            NullLogger<StandardOAuth2TokenManager>.Instance);

        var result = await manager.RevokeTokenAsync("some-token");

        result.Should().BeTrue("RequireHttps=false 时 http 端点应放行（显式逃生门）");
    }

    // ---------------------------------------------------------------- MT-13

    [Fact]
    public void ClientsDictionary_ShouldBeOrdinal_NotIgnoreCase()
    {
        var options = new MudHttpClientApplicationOptions();

        options.Clients.Comparer.Should().Be(StringComparer.Ordinal,
            "MT-13：客户端名区分大小写（与命名 HttpClient / keyed DI / _clientCache 一致）。" +
            "若此处用 OrdinalIgnoreCase 而其它环节用 Ordinal，配置名与解析名大小写不一致会静默失配");
    }

    [Fact]
    public void PostConfigure_WithCaseCollidingClientNames_ShouldWarn()
    {
        var logProvider = new CollectingLoggerProvider();
        var logger = new TypedCollectingLogger<MudHttpClientApplicationOptionsPostConfigure>(logProvider);

        var options = new MudHttpClientApplicationOptions();
        options.Clients["Default"] = new MudHttpClientOptions { BaseAddress = "https://a.example.com" };
        options.Clients["default"] = new MudHttpClientOptions { BaseAddress = "https://b.example.com" };

        new MudHttpClientApplicationOptionsPostConfigure(logger)
            .PostConfigure(null, options);

        logProvider.GetLogRecords(LogLevel.Warning)
            .Should().Contain(
                r => r.Message.Contains("Default", StringComparison.Ordinal)
                     && r.Message.Contains("default", StringComparison.Ordinal),
                "MT-13：仅大小写不同的客户端名是误配高发区，必须显式告警（否则只有一个能被解析到）");
    }

    [Fact]
    public void PostConfigure_WithCaseDistinctClientNames_ShouldNotWarn()
    {
        var logProvider = new CollectingLoggerProvider();
        var logger = new TypedCollectingLogger<MudHttpClientApplicationOptionsPostConfigure>(logProvider);

        var options = new MudHttpClientApplicationOptions();
        options.Clients["alpha"] = new MudHttpClientOptions { BaseAddress = "https://a.example.com" };
        options.Clients["beta"] = new MudHttpClientOptions { BaseAddress = "https://b.example.com" };

        new MudHttpClientApplicationOptionsPostConfigure(logger)
            .PostConfigure(null, options);

        logProvider.GetLogRecords(LogLevel.Warning)
            .Should().NotContain(r => r.Message.Contains("大小写", StringComparison.Ordinal),
                "无大小写碰撞时不得产生噪音告警");
    }

    /// <summary>把既有 <see cref="CollectingLoggerProvider"/> 适配为类型化 <see cref="ILogger{T}"/>。</summary>
    private sealed class TypedCollectingLogger<T>(CollectingLoggerProvider inner) : ILogger<T>
    {
        private readonly ILogger _inner = inner.CreateLogger(typeof(T).FullName!);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>最简单的可编程 HTTP 处理器。</summary>
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
