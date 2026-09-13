// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// P0 配置整改验证用例（CFG-01 / CFG-02 / CFG-03 / CFG-04 / CFG-05）。
/// </summary>
public class ConfigP0FixTests
{
    // ---------------------------------------------------------------
    // CFG-03：HttpClientApiAttribute 默认超时单一真相源
    // ---------------------------------------------------------------

    [Fact]
    public void CFG03_HttpClientApiAttribute_DefaultTimeout_Is50()
    {
        HttpClientApiAttribute.DefaultTimeoutSeconds.Should().Be(50);
        new HttpClientApiAttribute().Timeout.Should().Be(50);
    }

    // ---------------------------------------------------------------
    // CFG-11：SensitiveDataAttribute 仅对属性生效
    // ---------------------------------------------------------------

    [Fact]
    public void CFG11_SensitiveDataAttribute_OnlyTargetsProperty()
    {
        var usage = typeof(SensitiveDataAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        // 标注在方法参数上会产生编译错误 CS0592（原允许但无任何掩码效果 → 静默失效）。
        usage!.ValidOn.Should().Be(AttributeTargets.Property);
    }

    // ---------------------------------------------------------------
    // CFG-04：QueryParameterBuilder.AddAllowNull
    // ---------------------------------------------------------------

    [Fact]
    public void CFG04_AddAllowNull_NullValue_EmitsEmptyKey()
    {
        var builder = new QueryParameterBuilder();

        builder.AddAllowNull("q", null);

        builder.Build().Should().Be("q=");
    }

    [Fact]
    public void CFG04_Add_NullValue_IsStillSkipped()
    {
        var builder = new QueryParameterBuilder();

        builder.Add("q", (string?)null);

        builder.Build().Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // CFG-01：克隆器覆盖全部可写属性（I-5 反射守卫） + DI 合并
    // ---------------------------------------------------------------

    private static readonly HashSet<string> DiResolvedProperties = new()
    {
        nameof(EnhancedHttpClientOptions.Logger),
        nameof(EnhancedHttpClientOptions.RequestInterceptors),
        nameof(EnhancedHttpClientOptions.ResponseInterceptors),
        nameof(EnhancedHttpClientOptions.SensitiveDataMasker),
    };

    private static readonly HashSet<string> ExpectedCopiedProperties = new()
    {
        nameof(EnhancedHttpClientOptions.AllowCustomBaseUrls),
        nameof(EnhancedHttpClientOptions.RequestBodySerialization),
        nameof(EnhancedHttpClientOptions.ExceptionRedactor),
        nameof(EnhancedHttpClientOptions.MaxExceptionContentLength),
        nameof(EnhancedHttpClientOptions.CaptureRequestContent),
        nameof(EnhancedHttpClientOptions.UrlResolution),
        nameof(EnhancedHttpClientOptions.MaxSuccessResponseBytes),
        nameof(EnhancedHttpClientOptions.HttpVersion),
        nameof(EnhancedHttpClientOptions.HttpVersionPolicy),
        nameof(EnhancedHttpClientOptions.HttpRequestMessageOptions),
        nameof(EnhancedHttpClientOptions.JsonTypeInfoResolver),
    };

    [Fact]
    public void CFG01_Cloner_CoversAllWritableProperties()
    {
        var writable = typeof(EnhancedHttpClientOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .ToHashSet();

        var accountedFor = DiResolvedProperties.Concat(ExpectedCopiedProperties).ToHashSet();

        // 新增可写属性若未分类（既未加入克隆器，也未声明为 DI 解析），本断言失败。
        writable.Should().BeSubsetOf(accountedFor);
        // 防止清单出现陈旧条目。
        accountedFor.Should().BeSubsetOf(writable);
    }

    [Fact]
    public void CFG01_Cloner_CopiesRuntimeProperties_AndSkipsDiResolved()
    {
        var source = new EnhancedHttpClientOptions
        {
            UrlResolution = UrlResolutionMode.Rfc3986,
            RequestBodySerialization = RequestBodySerializationMode.Buffered,
            MaxExceptionContentLength = 4096,
            CaptureRequestContent = true,
            MaxSuccessResponseBytes = 2048,
            AllowCustomBaseUrls = true,
            HttpRequestMessageOptions = new Dictionary<string, object?> { ["x"] = 1 },
        };

        var clone = EnhancedHttpClientOptionsCloner.Clone(source);

        clone.UrlResolution.Should().Be(UrlResolutionMode.Rfc3986);
        clone.RequestBodySerialization.Should().Be(RequestBodySerializationMode.Buffered);
        clone.MaxExceptionContentLength.Should().Be(4096);
        clone.CaptureRequestContent.Should().BeTrue();
        clone.MaxSuccessResponseBytes.Should().Be(2048);
        clone.AllowCustomBaseUrls.Should().BeTrue();
        clone.HttpRequestMessageOptions.Should().ContainKey("x");

        // DI 解析项由容器覆盖，克隆器不应拷贝。
        clone.Logger.Should().BeNull();
        clone.RequestInterceptors.Should().BeNull();
        clone.ResponseInterceptors.Should().BeNull();
        clone.SensitiveDataMasker.Should().BeNull();
    }

    [Fact]
    public void CFG01_Cloner_NullSource_ReturnsDefaultInstance()
    {
        var clone = EnhancedHttpClientOptionsCloner.Clone(null);

        clone.UrlResolution.Should().Be(UrlResolutionMode.Default);
        clone.RequestBodySerialization.Should().Be(RequestBodySerializationMode.Default);
    }

    [Fact]
    public void CFG01_CreateEnhancedClient_MergesIOptionsEnhancedHttpClientOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.Configure<EnhancedHttpClientOptions>(o => o.UrlResolution = UrlResolutionMode.Rfc3986);
        services.AddMudHttpClient("test", "https://api.example.com");

        using var provider = services.BuildServiceProvider();

        // Act
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        // Assert：修复前 CreateEnhancedClient 用局部 new，编程式配置被丢弃（_urlResolution 恒为 Default）。
        var field = typeof(EnhancedHttpClient)
            .GetField("_urlResolution", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.GetValue(client).Should().Be(UrlResolutionMode.Rfc3986);
    }

    [Fact]
    public void CFG01_CreateEnhancedClient_DoesNotMutateIOptionsSingleton()
    {
        var services = new ServiceCollection();
        services.Configure<EnhancedHttpClientOptions>(o => o.AllowCustomBaseUrls = true);
        services.AddMudHttpClient("test", "https://api.example.com");
        services.Configure<MudHttpClientApplicationOptions>(o =>
        {
            o.Clients["test"] = new MudHttpClientOptions
            {
                BaseAddress = "https://api.example.com",
                AllowCustomBaseUrls = false,
            };
        });

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IEnhancedHttpClient>();

        // 命名客户端配置覆盖的是克隆副本，共享单例必须保持不变。
        var current = provider.GetRequiredService<IOptionsMonitor<EnhancedHttpClientOptions>>().CurrentValue;
        current.AllowCustomBaseUrls.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // CFG-02：BaseAddress 缺失诊断
    // ---------------------------------------------------------------

    [Fact]
    public void CFG02_DefaultClientNameWithoutBaseAddress_ThrowsOptionsValidationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:DefaultClientName"] = "broken",
                ["MudHttpClients:Clients:broken:TimeoutSeconds"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMudHttpClientsFromConfiguration(config);

        using var provider = services.BuildServiceProvider();

        var act = () => _ = provider.GetRequiredService<IOptions<MudHttpClientApplicationOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void CFG02_NonDefaultClientWithoutBaseAddress_DoesNotFail()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:no-addr:TimeoutSeconds"] = "30",
                ["MudHttpClients:Clients:ok:BaseAddress"] = "https://ok.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMudHttpClientsFromConfiguration(config);

        using var provider = services.BuildServiceProvider();

        var act = () => _ = provider.GetRequiredService<IOptions<MudHttpClientApplicationOptions>>().Value;

        act.Should().NotThrow();
    }

    /// <summary>
    /// T-04（CFG-02）：被跳过的无 BaseAddress 客户端必须有启动期警告（不静默），且包含客户端名。
    /// </summary>
    [Fact]
    public void CFG02_ClientWithoutBaseAddress_LogsWarning()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:no-addr:TimeoutSeconds"] = "30",
                ["MudHttpClients:Clients:ok:BaseAddress"] = "https://ok.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        var loggerProvider = new CollectingLoggerProvider();
        services.AddLogging(b => b.AddProvider(loggerProvider));
        services.AddMudHttpClientsFromConfiguration(config);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<MudHttpClientApplicationOptions>>().Value;

        loggerProvider.GetLogRecords(LogLevel.Warning)
            .Should().Contain(r => r.Message.Contains("no-addr"),
                "未配置 BaseAddress 的客户端被跳过注册时必须产生警告（CFG-02 不静默原则）");
    }

    /// <summary>
    /// T-14（覆盖空洞）：各 Options 的 <c>SectionName</c> 常量必须与绑定默认参数/文档示例一致，
    /// 防止意外改名导致既有 appsettings 键静默失效。
    /// </summary>
    [Fact]
    public void T14_SectionName_MatchesBindingKey()
    {
        MudHttpClientApplicationOptions.SectionName.Should().Be("MudHttpClients");
        TokenRecoveryOptions.SectionName.Should().Be("MudHttpTokenRecovery");
        TokenRefreshBackgroundOptions.SectionName.Should().Be("TokenRefreshBackground");
        OAuth2Options.SectionName.Should().Be("MudHttpOAuth2");
        AesEncryptionOptions.SectionName.Should().Be("MudHttpAesEncryption");

        // 以常量为键做一次真实绑定回环（命名差异见 TokenRefreshBackgroundOptions XML 标注）。
        var bound = new TokenRefreshBackgroundOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TokenRefreshBackgroundOptions.SectionName}:RefreshIntervalSeconds"] = "77",
            })
            .Build()
            .GetSection(TokenRefreshBackgroundOptions.SectionName)
            .Bind(bound);

        bound.RefreshIntervalSeconds.Should().Be(77);
    }

    // ---------------------------------------------------------------
    // CFG-16：响应缓存双入口（AddHttpResponseCache + 配置节）
    // ---------------------------------------------------------------

    [Fact]
    public void CFG16_DualResponseCacheEntries_LogsWarning()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
                ["MudHttpClients:ResponseCache:MaxCacheSize"] = "2000",
            })
            .Build();

        var services = new ServiceCollection();
        var loggerProvider = new CollectingLoggerProvider();
        services.AddLogging(b => b.AddProvider(loggerProvider));

        services.AddHttpResponseCache(maxCacheSize: 5000, cleanupIntervalSeconds: 30);
        services.AddMudHttpClientsFromConfiguration(config);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<MudHttpClientApplicationOptions>>().Value;

        loggerProvider.GetLogRecords(LogLevel.Warning)
            .Should().Contain(r => r.Message.Contains("ResponseCache"));
    }

    // ---------------------------------------------------------------
    // CFG-26：AOT 友好的委托式重载
    // ---------------------------------------------------------------

    [Fact]
    public void CFG26_AddMudHttpAesEncryption_DelegateOverload_RegistersProvider()
    {
        var services = new ServiceCollection();

        services.AddMudHttpAesEncryption(o => o.Key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng=="));

        using var provider = services.BuildServiceProvider();
        provider.GetService<IEncryptionProvider>().Should().NotBeNull();
    }

    [Fact]
    public void CFG26_AddMudHttpOAuth2_DelegateOverload_BindsOptions()
    {
        var services = new ServiceCollection();

        services.AddMudHttpOAuth2(o =>
        {
            o.ClientId = "cid";
            o.ClientSecret = "csecret";
            o.TokenEndpoint = "https://auth.example.com/token";
        });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<OAuth2Options>>().Value.ClientId.Should().Be("cid");
    }
}
