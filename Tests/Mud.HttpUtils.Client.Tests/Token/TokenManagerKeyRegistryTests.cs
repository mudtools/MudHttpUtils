// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio  2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// B-1：令牌管理器键冲突诊断（<see cref="TokenManagerKeyRegistry"/> + 注册期诊断 A/B + 启动期升级）行为用例。
/// </summary>
/// <remarks>
/// 测试口径（方案钉死）：重复键注册 → 启动校验抛出/记 Error；逃生开关
/// （<see cref="MudHttpAppManagementOptions.AllowDuplicateTokenManagerKeys"/>）下降级为 Warning；
/// 大小写不同视为不同键（序数比较）。
/// </remarks>
public class TokenManagerKeyRegistryTests
{
    // ---- 测试替身 ----

    private sealed class StubTokenManager : TokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "stub-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
    }

    /// <summary>与 <see cref="StubTokenManager"/> 不同的类型：断言诊断消息回显「原管理器类型 → 新管理器类型」。</summary>
    private sealed class OtherStubTokenManager : TokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "other-stub-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
    }

    private sealed record LogRecord(EventId EventId, LogLevel Level, string FormattedMessage);

    private sealed class RecordingLogger(List<LogRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => records.Add(new LogRecord(eventId, logLevel, formatter(state, exception)));
    }

    private sealed class RecordingLogger<T>(List<LogRecord> records) : ILogger<T>
    {
        private readonly RecordingLogger _inner = new(records);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }

    private static (AppManagementStartupValidator Validator, List<LogRecord> Records) CreateValidator(
        IServiceProvider sp, MudHttpAppManagementOptions options)
    {
        var records = new List<LogRecord>();
        var validator = new AppManagementStartupValidator(
            sp, Options.Create(options), new RecordingLogger<AppManagementStartupValidator>(records));
        return (validator, records);
    }

    /// <summary>隔离本组关注点的最小选项：关闭 Require* 阻断，只观察 B-1 路径。</summary>
    private static MudHttpAppManagementOptions IsolatedOptions()
        => new() { RequireAppContextHolder = false };

    // ---- 注册表本体语义（序数比较 / 重复键三步处理）----

    [Fact]
    public void Register_DuplicateKey_Default_ThrowsAndRecordsKey()
    {
        var registry = new TokenManagerKeyRegistry();
        var first = new StubTokenManager();
        var second = new OtherStubTokenManager();
        registry.Register("Wechat.Channels.AccessToken", first);

        var act = () => registry.Register("Wechat.Channels.AccessToken", second);

        act.Should().Throw<InvalidOperationException>(
                "默认 throwOnDuplicateKey = true，注册期 fail-fast")
            .WithMessage("*Wechat.Channels.AccessToken*")
            .WithMessage("*StubTokenManager*")
            .WithMessage("*OtherStubTokenManager*");
        registry.DuplicateKeys.Should().ContainSingle("Wechat.Channels.AccessToken");
        registry.Resolve("Wechat.Channels.AccessToken").Should().BeSameAs(first,
            "抛出路径不得覆盖既有注册（先注册者保持生效）");
    }

    [Fact]
    public void Register_DuplicateKey_ThrowDisabled_LastWinsAndDiagnosticsCallback()
    {
        string? diagnosed = null;
        var registry = new TokenManagerKeyRegistry(throwOnDuplicateKey: false, duplicateKeyDiagnostics: m => diagnosed = m);
        var first = new StubTokenManager();
        var second = new OtherStubTokenManager();
        registry.Register("dup-key", first);

        var act = () => registry.Register("dup-key", second);

        act.Should().NotThrow("逃生路径记录 + 诊断 + 后键覆盖");
        registry.Resolve("dup-key").Should().BeSameAs(second, "后键覆盖（后注册者胜）");
        registry.DuplicateKeys.Should().ContainSingle("dup-key");
        diagnosed.Should().NotBeNull("重复键必须回调诊断消息").And.Contain("dup-key");
    }

    [Fact]
    public void Register_DifferentCase_AreDistinctKeys()
    {
        var registry = new TokenManagerKeyRegistry();
        var first = new StubTokenManager();
        var second = new OtherStubTokenManager();

        registry.Register("Wechat.Channels", first);
        registry.Register("wechat.channels", second);

        registry.Resolve("Wechat.Channels").Should().BeSameAs(first);
        registry.Resolve("wechat.channels").Should().BeSameAs(second);
        registry.DuplicateKeys.Should().BeEmpty("序数比较下大小写不同视为不同键");
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-key")]
    public void Resolve_UnknownOrEmpty_ReturnsNull(string key)
    {
        var registry = new TokenManagerKeyRegistry();
        registry.Register("known", new StubTokenManager());

        registry.Resolve(key).Should().BeNull();
    }

    [Fact]
    public void Register_EmptyKeyOrNullManager_Throws()
    {
        var registry = new TokenManagerKeyRegistry();

        ((Action)(() => registry.Register(" ", new StubTokenManager()))).Should().Throw<ArgumentException>();
        ((Action)(() => registry.Register("k", null!))).Should().Throw<ArgumentNullException>();
    }

    // ---- 启动期升级（AppManagementStartupValidator）----

    [Fact]
    public async Task StartupValidation_DuplicateKeys_Default_ThrowsError()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenManagerKeyRegistry(registry =>
        {
            registry.Register("dup-key", new StubTokenManager());
            registry.Register("dup-key", new OtherStubTokenManager());
        });
        using var sp = services.BuildServiceProvider();
        var (validator, _) = CreateValidator(sp, IsolatedOptions());

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "默认（AllowDuplicateTokenManagerKeys = false）重复键按 Error 阻断启动"))
            .WithMessage("*重复令牌管理器键*")
            .WithMessage("*dup-key*");
    }

    [Fact]
    public async Task StartupValidation_DuplicateKeys_EscapeHatch_DowngradesToWarning()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenManagerKeyRegistry(registry =>
        {
            registry.Register("dup-key", new StubTokenManager());
            registry.Register("dup-key", new OtherStubTokenManager());
        });
        using var sp = services.BuildServiceProvider();
        var options = IsolatedOptions();
        options.AllowDuplicateTokenManagerKeys = true;
        var (validator, records) = CreateValidator(sp, options);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("逃生开关把重复键降级为 Warning（后键覆盖）");
        records.Should().Contain(r =>
            r.Level == LogLevel.Warning && r.FormattedMessage.Contains("重复令牌管理器键") && r.FormattedMessage.Contains("dup-key"),
            "降级后仍须留下可检索的告警记录");
    }

    [Fact]
    public async Task StartupValidation_SecondRegistryRegistration_EmitsWarning194()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenManagerRegistry(_ => null);   // 先注册者胜
        services.AddTokenManagerRegistry(_ => null);   // 被 TryAdd 忽略 ⇒ 诊断 A
        using var sp = services.BuildServiceProvider();
        var (validator, records) = CreateValidator(sp, IsolatedOptions());

        await validator.StartAsync(CancellationToken.None);

        records.Should().Contain(r => r.EventId.Id == 194 && r.Level == LogLevel.Warning,
            "TryAdd 静默丢弃后注册者必须在启动期留下 EventId 194 告警");
    }

    [Fact]
    public async Task StartupValidation_SingleRegistryRegistration_DoesNotEmit194()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenManagerRegistry(_ => null);
        using var sp = services.BuildServiceProvider();
        var (validator, records) = CreateValidator(sp, IsolatedOptions());

        await validator.StartAsync(CancellationToken.None);

        records.Should().NotContain(r => r.EventId.Id == 194, "单次注册没有被忽略者，不应误报");
    }

    [Fact]
    public async Task StartupValidation_ManagerTypeRegisteredTwice_EmitsWarning195()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpTokenManager<StubTokenManager>();
        services.AddMudHttpTokenManager<StubTokenManager>();   // 描述符叠加 ⇒ 诊断 B
        using var sp = services.BuildServiceProvider();
        var (validator, records) = CreateValidator(sp, IsolatedOptions());

        await validator.StartAsync(CancellationToken.None);

        records.Should().Contain(r =>
            r.EventId.Id == 195 && r.Level == LogLevel.Warning && r.FormattedMessage.Contains(nameof(StubTokenManager)),
            "同类型重复注册必须在启动期留下 EventId 195 告警并回显类型名");
    }

    // ---- 手动校验（ValidateMudHttpAppManagement）同口径 ----

    [Fact]
    public void ValidateMudHttpAppManagement_DuplicateKeys_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        services.AddSingleton<IAppAccessAuthorizer, AllowAllAppAccessAuthorizer>();
        services.AddTokenManagerKeyRegistry(registry =>
        {
            registry.Register("dup-key", new StubTokenManager());
            registry.Register("dup-key", new OtherStubTokenManager());
        });
        using var sp = services.BuildServiceProvider();

        var act = () => sp.ValidateMudHttpAppManagement();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*重复令牌管理器键*")
            .WithMessage("*dup-key*", "手动校验与启动期校验同口径：默认重复键即 Error");
    }

    [Fact]
    public void ValidateMudHttpAppManagement_DuplicateKeys_EscapeHatch_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        services.AddSingleton<IAppAccessAuthorizer, AllowAllAppAccessAuthorizer>();
        services.Configure<MudHttpAppManagementOptions>(o => o.AllowDuplicateTokenManagerKeys = true);
        services.AddTokenManagerKeyRegistry(registry =>
        {
            registry.Register("dup-key", new StubTokenManager());
            registry.Register("dup-key", new OtherStubTokenManager());
        });
        using var sp = services.BuildServiceProvider();

        var act = () => sp.ValidateMudHttpAppManagement();

        act.Should().NotThrow("逃生开关在手动校验路径同样生效（降级为 Warning）");
    }

    [Fact]
    public void AddTokenManagerKeyRegistry_WiresInterfaceToSameInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenManagerKeyRegistry();
        using var sp = services.BuildServiceProvider();

        var concrete = sp.GetRequiredService<TokenManagerKeyRegistry>();
        var viaInterface = sp.GetRequiredService<ITokenManagerRegistry>();

        viaInterface.Should().BeSameAs(concrete, "两种解析路径必须是同一注册表实例");
    }
}
