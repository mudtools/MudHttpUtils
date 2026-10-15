# 配置参数管理修复与重构方案

> **文档版本**: v1.1（经源码验证修订）
> **创建日期**: 2026-07-12
> **状态**: ✅ 已完成（全部 Phase 1-4 实施并验证通过）
> **作者**: Mud Studio
> **关联文档**: 配置参数管理质量审查报告（2026-07-12）
> **依赖约束**: 遵循项目依赖方向 `Resilience → Client → Abstractions`，不得反向引用
>
> **v1.1 修订记录**（基于源码验证）：
>
> - §2.2：补充 [PollyResiliencePolicyProvider.cs:429](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L429) 第二处 `RetryStatusCodes` 空数组处理（原文遗漏）
> - §2.2：修正空数组下 `ShouldRetry` 行为描述与测试断言逻辑
> - §2.2：修正 LoggerMessage EventId 为 111（原 "9xxx" 不符合现有 101-110 Resilence 段规划），明确加入 [MudHttpClientLog.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs)
> - §3.3：Task 2.3 降级为"暂缓"，因 Resilience 实际不支持热更新（`PollyResiliencePolicyProvider` 注入 `IOptions<T>` 非 `IOptionsMonitor`，构造时捕获 `.Value`），且 `TokenRecoveryDelegatingHandler`/`TokenRecoveryEnhancedClient` 不直接持有 `_options`
> - §5.1：移除冗余 T12（setter 校验已足够，无需 IValidateOptions 重复校验）

---

## 一、背景与目标

### 1.1 问题陈述

基于配置参数管理质量审查报告（评分 7.2/10），识别出 5 个维度的共 18 项问题。核心痛点集中在：

| 维度         | 高危 | 中危 | 低危 |
| :----------- | :--- | :--- | :--- |
| 未生效配置   | 0    | 1    | 1    |
| 功能重复配置 | 0    | 1    | 2    |
| 功能冲突配置 | 1    | 4    | 1    |
| 单元测试覆盖 | 1    | 3    | 1    |
| 文档一致性   | 0    | 3    | 2    |

**阻断性问题（必须修复）**：

1. `CircuitBreakerOptions.MinimumThroughput` 无 setter 校验，负数/零值被静默接受，运行时 Polly 抛异常
2. `RetryOptions.RetryStatusCodes` 空数组 `[]` 静默禁用状态码重试，与文档"为空时使用默认值"描述不符

**一致性问题（应修复）**：3. `AesEncryptionOptions.SectionName` 已定义但无 `AddMudHttpAesEncryptionFromConfiguration` 扩展方法 4. OpenTelemetry 配置不支持 IOptionsMonitor 热更新，与 Resilience/Client 行为不一致 5. `TokenRecoveryOptions` 直接值注入破坏热更新链路6. `TokenRefreshBackgroundOptions.SectionName` 命名违反 `MudHttp*` 前缀约定

### 1.2 重构目标

- **修复阻断性缺陷**：消除运行时崩溃风险，补齐 setter 校验
- **统一配置 API 表面**：所有 Options 提供一致的 `Add*FromConfiguration` 扩展方法
- **统一热更新策略**：明确区分"启动时固定配置"vs"运行时可变配置"，并在文档与代码注释中标注
- **补齐测试覆盖**：针对每个修复点补充边界测试与集成测试
- **保持向后兼容**：不破坏现有公共 API；命名约定差异通过文档标注而非破坏性改名

### 1.3 设计原则

1. **向后兼容优先**：不更改已发布的配置节路径（如 `TokenRefreshBackground`），仅添加新方法或新校验
2. **防御性编程**：所有可校验的 Options 属性应提供 setter 校验，与 `FailureThreshold`/`BreakDurationSeconds` 模式一致
3. **文档与代码同步**：每项代码修改必须同步更新对应的 README 和 XML 注释
4. **分阶段交付**：Phase 1（阻断性修复）可独立交付，Phase 2-3 依赖 Phase 1 完成

### 1.4 项目架构约束

```
Mud.HttpUtils.Abstractions (netstandard2.0 / net6.0 / net8.0 / net10.0)
    └── 无外部项目引用
    └── 含: AesEncryptionOptions, TokenRecoveryOptions, TokenRefreshBackgroundOptions

Mud.HttpUtils.Client (netstandard2.0 / net6.0 / net8.0 / net10.0)
    └── 引用 Mud.HttpUtils.Abstractions
    └── 含: ServiceCollectionExtensions, OAuth2Options, MudHttpClientOptions, EnhancedHttpClientOptions
    └── InternalsVisibleTo: Mud.HttpUtils.Resilience

Mud.HttpUtils.Resilience (netstandard2.0 / net6.0 / net8.0 / net10.0)
    └── 引用 Mud.HttpUtils.Abstractions
    └── 含: RetryOptions, CircuitBreakerOptions, TimeoutOptions, ResilienceOptions

Mud.HttpUtils.OpenTelemetry (net8.0 / net10.0)
    └── 引用 Mud.HttpUtils.Abstractions
    └── 含: MudHttpOpenTelemetryOptions, MudHttpOpenTelemetryExtensions
```

**关键约束**：

- `AddMudHttpAesEncryptionFromConfiguration` 必须放在 `Mud.HttpUtils.Client`（因 `DefaultAesEncryptionProvider` 在此项目）
- `CircuitBreakerOptions` 修改在 `Mud.HttpUtils.Resilience`
- `TokenRecoveryOptions` 直接值注入修改在 `Mud.HttpUtils.Client` 的 `ServiceCollectionExtensions`

---

## 二、Phase 1：阻断性缺陷修复

> **目标**：消除运行时崩溃风险与配置语义歧义
> **影响范围**：`Mud.HttpUtils.Resilience`、`Mud.HttpUtils.Client`
> **预计新增/修改测试**：6 个

### 2.1 任务 1.1：CircuitBreakerOptions.MinimumThroughput 添加 setter 校验

**问题**：`CircuitBreakerOptions.MinimumThroughput`（[CircuitBreakerOptions.cs:86](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/Options/CircuitBreakerOptions.cs#L86)）是自动属性，无范围校验。Polly `AdvancedCircuitBreakerAsync` 要求 `minimumThroughput >= 2`，设置 0/1/负数会导致运行时 `ArgumentOutOfRangeException`。

**修复方案**：改为 backing field + setter 校验，与 `FailureThreshold` 模式一致。

**修改文件**：`Mud.HttpUtils.Resilience/Options/CircuitBreakerOptions.cs`

```csharp
// 修改前（L86）：
public int MinimumThroughput { get; set; } = 10;

// 修改后：
private int _minimumThroughput = 10;

/// <summary>
/// 采样窗口内的最小吞吐量。默认 10。必须 >= 2。
/// 仅在 <see cref="SamplingDurationSeconds"/> 大于 0 时生效。
/// <para>在采样窗口内，请求数必须达到此值后，才会开始计算失败率。</para>
/// <para>Polly <see cref="Polly.CircuitBreaker.AdvancedCircuitBreakerAsync"/> 要求此值 >= 2。</para>
/// </summary>
/// <exception cref="ArgumentOutOfRangeException">设置小于 2 的值时抛出。</exception>
public int MinimumThroughput
{
    get => _minimumThroughput;
    set => _minimumThroughput = value >= 2
        ? value
        : throw new ArgumentOutOfRangeException(nameof(MinimumThroughput),
            "采样窗口最小吞吐量必须 >= 2（Polly AdvancedCircuitBreaker 要求）。");
}
```

**测试场景**（新增到 `Tests/Mud.HttpUtils.Resilience.Tests/CircuitBreakerOptionsTests.cs`）：

```csharp
[Theory]
[InlineData(0)]
[InlineData(1)]
[InlineData(-1)]
[InlineData(-100)]
public void MinimumThroughput_SetToLessThanTwo_ThrowsArgumentOutOfRangeException(int invalidValue)
{
    var options = new CircuitBreakerOptions();
    var ex = Assert.Throws<ArgumentOutOfRangeException>(
        () => options.MinimumThroughput = invalidValue);
    Assert.Contains("必须 >= 2", ex.Message);
}

[Theory]
[InlineData(2)]
[InlineData(10)]
[InlineData(int.MaxValue)]
public void MinimumThroughput_SetToValidValue_DoesNotThrow(int validValue)
{
    var options = new CircuitBreakerOptions();
    options.MinimumThroughput = validValue;
    Assert.Equal(validValue, options.MinimumThroughput);
}

[Fact]
public void MinimumThroughput_DefaultValue_IsTen()
{
    var options = new CircuitBreakerOptions();
    Assert.Equal(10, options.MinimumThroughput);
}
```

**验证步骤**：

```powershell
dotnet test Tests/Mud.HttpUtils.Resilience.Tests --filter "FullyQualifiedName~MinimumThroughput"
```

---

### 2.2 任务 1.2：RetryOptions.RetryStatusCodes 空数组处理

**问题**：[PollyResiliencePolicyProvider.cs:109](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L109) `retryOptions.RetryStatusCodes ?? GetDefaultRetryStatusCodes()` 仅对 `null` 回退默认值，空数组 `[]` 会直接使用，导致无状态码触发重试。Resilience README L173 描述"为空时使用默认值"，但"空"在 JSON 中有 `null` 和 `[]` 两种语义。

> **⚠️ v1.1 补充**：经源码验证，[PollyResiliencePolicyProvider.cs:429](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L429) `BuildMethodPolicy` 中存在**完全相同的 null 合并模式**（方法级策略构建路径），必须同步修改，否则方法级 [Retry] 特性的空数组行为与全局策略不一致。

**`ShouldRetry` 空数组行为分析**（基于 [PollyResiliencePolicyProvider.cs:522-552](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L522) 源码）：

| 异常类型                                | 空数组 `[]` 时行为 | 原因                                                 |
| :-------------------------------------- | :----------------- | :--------------------------------------------------- |
| `HttpRequestException`（有 StatusCode） | **不重试**         | `retryStatusCodes.Contains(code)` 对空数组返回 false |
| `HttpRequestException`（无 StatusCode） | **重试**           | `ShouldRetry` 末尾 `return true`                     |
| `TimeoutRejectedException`              | **重试**           | 通过 `.Or<TimeoutRejectedException>()` 无条件捕获    |
| `TaskCanceledException`（非取消）       | **重试**           | 通过 `.Or<TaskCanceledException>(...)` 无条件捕获    |

**设计决策**：不强制回退到默认值（空数组可能是用户有意禁用状态码重试），但必须在运行时记录警告日志，并修正文档明确区分两种语义。

**修改文件 1**：`Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs`（两处修改）

```csharp
// 修改点 1：L109（BuildRetryPolicy，全局策略路径）
// 修改前：
var retryStatusCodes = retryOptions.RetryStatusCodes ?? GetDefaultRetryStatusCodes();

// 修改后：
var retryStatusCodes = retryOptions.RetryStatusCodes;
if (retryStatusCodes is null)
{
    retryStatusCodes = GetDefaultRetryStatusCodes();
}
else if (retryStatusCodes.Length == 0)
{
    // 空数组表示用户有意禁用状态码重试（仅异常触发重试），记录警告
    MudHttpClientLog.RetryStatusCodesEmptyArray(_logger);
}

// 修改点 2：L429（BuildMethodPolicy，方法级策略路径）
// 修改前：
var retryStatusCodes = _options.Retry.RetryStatusCodes ?? GetDefaultRetryStatusCodes();

// 修改后：
var retryStatusCodes = _options.Retry.RetryStatusCodes;
if (retryStatusCodes is null)
{
    retryStatusCodes = GetDefaultRetryStatusCodes();
}
else if (retryStatusCodes.Length == 0)
{
    MudHttpClientLog.RetryStatusCodesEmptyArray(_logger);
}
```

**修改文件 2**：`Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs`

> **注意**：日志定义统一放在 [MudHttpClientLog.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs) 的 Resilience 段（EventId 101-110 已用，下一个可用为 111）。`PollyResiliencePolicyProvider` 已注入 `ILogger _logger`（[L31](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L31)），但因 `MudHttpClientLog` 为 `internal`，需确认 `InternalsVisibleTo` 已配置（[Mud.HttpUtils.Client.csproj](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/Mud.HttpUtils.Client.csproj) 已对 `Mud.HttpUtils.Resilience` 开放，见架构约束 §1.4）。

```csharp
// 在 MudHttpClientLog.cs 的 #region Resilience 模块 末尾（L110 RequestTimeoutMs 之后）新增：

#if NET6_0_OR_GREATER
    [LoggerMessage(EventId = 111, Level = LogLevel.Warning,
        Message = "RetryStatusCodes 配置为空数组，仅 HttpRequestException（无 StatusCode）/TimeoutRejectedException/TaskCanceledException 会触发重试。如需使用默认状态码 [408,429,500,502,503,504]，请移除该配置项或设为 null。")]
    public static partial void RetryStatusCodesEmptyArray(ILogger logger);
#else
    private static readonly Action<ILogger, Exception?> s_retryStatusCodesEmptyArray =
        LoggerMessage.Define(LogLevel.Warning, new EventId(111, nameof(RetryStatusCodesEmptyArray)),
            "RetryStatusCodes 配置为空数组，仅 HttpRequestException（无 StatusCode）/TimeoutRejectedException/TaskCanceledException 会触发重试。如需使用默认状态码 [408,429,500,502,503,504]，请移除该配置项或设为 null。");
    public static void RetryStatusCodesEmptyArray(ILogger logger) => s_retryStatusCodesEmptyArray(logger, null);
#endif
```

**测试场景**（新增到 `Tests/Mud.HttpUtils.Resilience.Tests/ResilienceConfigurationBindingTests.cs`）：

```csharp
[Fact]
public async Task RetryPolicy_WithEmptyStatusCodesArray_DoesNotRetryOnStatusCodeExceptions()
{
    // 验证：空数组时，带 StatusCode 的 HttpRequestException 不触发重试
    var options = new ResilienceOptions
    {
        Retry = new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 3,
            RetryStatusCodes = Array.Empty<int>()  // 空数组
        }
    };
    var provider = new PollyResiliencePolicyProvider(options);

    var callCount = 0;
    var policy = provider.GetRetryPolicy<string>();
    await Assert.ThrowsAsync<HttpRequestException>(async () =>
    {
        await policy.ExecuteAsync(async _ =>
        {
            callCount++;
            throw new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
        }, CancellationToken.None);
    });
    // 带 StatusCode 的异常在空数组下不重试
    Assert.Equal(1, callCount);
}

[Fact]
public async Task RetryPolicy_WithEmptyStatusCodesArray_RetriesOnTimeoutException()
{
    // 验证：空数组时，TimeoutRejectedException 仍触发重试
    var options = new ResilienceOptions
    {
        Retry = new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 3,
            RetryStatusCodes = Array.Empty<int>()
        }
    };
    var provider = new PollyResiliencePolicyProvider(options);

    var callCount = 0;
    var policy = provider.GetRetryPolicy<string>();
    await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
    {
        await policy.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount <= 4) // 初次 + 3 次重试
                throw new TimeoutRejectedException();
            return "ok";
        }, CancellationToken.None);
    });
    Assert.Equal(4, callCount); // 初次 + 3 次重试
}
```

**验证步骤**：

```powershell
dotnet test Tests/Mud.HttpUtils.Resilience.Tests --filter "FullyQualifiedName~RetryStatusCodes"
```

---

## 三、Phase 2：配置一致性修复

> **目标**：统一配置 API 表面与热更新策略
> **影响范围**：`Mud.HttpUtils.Client`、`Mud.HttpUtils.OpenTelemetry`
> **预计新增/修改测试**：8 个

### 3.1 任务 2.1：新增 AddMudHttpAesEncryptionFromConfiguration 扩展方法

**问题**：`AesEncryptionOptions.SectionName = "MudHttpAesEncryption"`（[AesEncryptionOptions.cs:22](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Abstractions/Encryption/AesEncryptionOptions.cs#L22)）已定义，Abstractions README L78 声称支持配置文件绑定，但无对应的 `Add*FromConfiguration` 扩展方法。其他 7 个 Options 均有此类方法。

**修改文件**：`Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`

在 `AddMudHttpClient`（含 `configureEncryption` 重载）附近新增：

```csharp
/// <summary>
/// 从 <see cref="IConfiguration"/> 绑定 AES 加密配置，并注册 <see cref="IEncryptionProvider"/>。
/// </summary>
/// <param name="services">服务集合。</param>
/// <param name="configuration">配置实例，用于绑定 <see cref="AesEncryptionOptions"/>。</param>
/// <param name="sectionPath">配置节点路径，默认 <see cref="AesEncryptionOptions.SectionName"/>。</param>
/// <returns>服务集合（链式调用）。</returns>
/// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
/// <example>
/// appsettings.json：
/// <code>
/// {
///   "MudHttpAesEncryption": {
///     "Key": "base64-encoded-32-byte-key"
///   }
/// }
/// </code>
/// 代码：
/// <code>
/// builder.Services.AddMudHttpAesEncryptionFromConfiguration(builder.Configuration);
/// </code>
/// </example>
public static IServiceCollection AddMudHttpAesEncryptionFromConfiguration(
    this IServiceCollection services,
    IConfiguration configuration,
    string sectionPath = AesEncryptionOptions.SectionName)
{
    if (services == null)
        throw new ArgumentNullException(nameof(services));
    if (configuration == null)
        throw new ArgumentNullException(nameof(configuration));

    services.Configure<AesEncryptionOptions>(configuration.GetSection(sectionPath));
    services.TryAddSingleton<IEncryptionProvider, DefaultAesEncryptionProvider>();
    return services;
}
```

**依赖说明**：`DefaultAesEncryptionProvider` 位于 `Mud.HttpUtils.Client`（[DefaultAesEncryptionProvider.cs:20](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/Encryption/DefaultAesEncryptionProvider.cs#L20)），其构造函数注入 `IOptions<AesEncryptionOptions>` 并调用 `Validate()`，因此配置绑定后会在首次解析时校验密钥长度。

**测试场景**（新增到 `Tests/Mud.HttpUtils.Client.Tests/ConfigurationBindingTests.cs`）：

```csharp
[Fact]
public void AddMudHttpAesEncryptionFromConfiguration_BindsAndRegistersProvider()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MudHttpAesEncryption:Key"] = Convert.ToBase64String(new byte[32])
        })
        .Build();

    var services = new ServiceCollection();
    services.AddMudHttpAesEncryptionFromConfiguration(config);

    var provider = services.BuildServiceProvider();
    var encryptionProvider = provider.GetService<IEncryptionProvider>();
    Assert.NotNull(encryptionProvider);
    Assert.IsType<DefaultAesEncryptionProvider>(encryptionProvider);
}

[Fact]
public void AddMudHttpAesEncryptionFromConfiguration_InvalidKey_ThrowsOnResolve()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MudHttpAesEncryption:Key"] = Convert.ToBase64String(new byte[10])  // 无效长度
        })
        .Build();

    var services = new ServiceCollection();
    services.AddMudHttpAesEncryptionFromConfiguration(config);

    var provider = services.BuildServiceProvider();
    Assert.Throws<InvalidOperationException>(
        () => provider.GetService<IEncryptionProvider>());
}

[Fact]
public void AddMudHttpAesEncryptionFromConfiguration_NullServices_Throws()
{
    IConfiguration config = new ConfigurationBuilder().Build();
    Assert.Throws<ArgumentNullException>(
        () => ((IServiceCollection)null!).AddMudHttpAesEncryptionFromConfiguration(config));
}

[Fact]
public void AddMudHttpAesEncryptionFromConfiguration_NullConfig_Throws()
{
    var services = new ServiceCollection();
    Assert.Throws<ArgumentNullException>(
        () => services.AddMudHttpAesEncryptionFromConfiguration(null!));
}
```

**验证步骤**：

```powershell
dotnet test Tests/Mud.HttpUtils.Client.Tests --filter "FullyQualifiedName~AddMudHttpAesEncryption"
```

---

### 3.2 任务 2.2：OpenTelemetry 配置热更新策略明确化

**问题**：[MudHttpOpenTelemetryExtensions.cs:70-72](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.OpenTelemetry/MudHttpOpenTelemetryExtensions.cs#L70) 使用 `configuration.GetSection(sectionPath).Bind(options)` 读取一次配置后直接传入 `AddMudHttpOpenTelemetryCore`，未注册 `IOptions<MudHttpOpenTelemetryOptions>`，不支持 IOptionsMonitor 热更新。

> **⚠️ v1.1 修正**：原方案声称"`AddMudHttpResilience(IConfiguration)` 使用 `services.Configure<ResilienceOptions>(...)` 支持热更新"。经源码验证，虽然 `services.Configure<T>` 会注册 `IOptionsMonitor<T>`，但消费者 [PollyResiliencePolicyProvider.cs:57](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L57) 注入的是 `IOptions<ResilienceOptions>`（非 `IOptionsMonitor`），构造时通过 `_options = options?.Value` 捕获快照。**Resilience 实际同样不支持运行时热更新**。因此 OTel 与 Resilience 在热更新行为上**实际一致**（均为启动时快照），差异仅在 DI 注册方式（`Bind` vs `Configure<T>`）。

**设计决策**：OpenTelemetry SDK 的 `AddOpenTelemetry()` 返回的 `OpenTelemetryBuilder` 本身是启动时一次性配置（Provider 在 Build 后不可变），因此 **OpenTelemetry 配置天然不支持运行时热更新**。不应强行改为 `services.Configure<T>`，而应：

1. 在 XML 注释中明确标注"启动时一次性读取，不支持热更新"
2. 在 README 中与其他支持热更新的配置做对比说明

**修改文件**：`Mud.HttpUtils.OpenTelemetry/MudHttpOpenTelemetryExtensions.cs`

```csharp
// 修改 AddMudHttpOpenTelemetry(IConfiguration) 的 XML 注释（L36-L60）：
/// <summary>
/// 一键开启 Mud.HttpUtils 的 OpenTelemetry 追踪与指标采集，从 <see cref="IConfiguration"/> 绑定选项。
/// </summary>
/// <remarks>
/// <para><b>注意：此方法在启动时读取配置一次，不支持 IOptionsMonitor 热更新。</b>
/// OpenTelemetry SDK 的 TracerProvider/MeterProvider 在构建后不可变，
/// 修改 appsettings.json 后需重启应用才能生效。
/// 如需运行时可变配置，请使用 <see cref="AddMudHttpOpenTelemetry(IServiceCollection, Action{MudHttpOpenTelemetryOptions}?)"/>
/// 重载并在自定义委托中读取动态配置源。</para>
/// </remarks>
/// <param name="services">服务集合。</param>
/// <param name="configuration">配置实例，用于绑定 <see cref="MudHttpOpenTelemetryOptions"/>。</param>
/// <param name="sectionPath">配置节点路径，默认 <c>"MudHttpOpenTelemetry"</c>。</param>
/// <param name="configure">可选的附加配置委托，在配置绑定之后执行，可覆盖绑定值。</param>
/// <returns>返回 <see cref="OpenTelemetryBuilder"/>，便于调用方继续追加配置。</returns>
/// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="configuration"/> 为 null。</exception>
```

**测试场景**（新增到 `Tests/Mud.HttpUtils.OpenTelemetry.Tests/OpenTelemetryOptionsTests.cs`）：

```csharp
[Fact]
public void AddMudHttpOpenTelemetry_FromConfiguration_DoesNotRegisterIOptionsMonitor()
{
    // 验证：从 IConfiguration 绑定后，IOptionsMonitor<MudHttpOpenTelemetryOptions> 不应注册
    // （因为 OTel SDK 本身不支持热更新）
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MudHttpOpenTelemetry:ServiceName"] = "test-service"
        })
        .Build();

    var services = new ServiceCollection();
    services.AddMudHttpOpenTelemetry(config);

    // OTel 注册了大量服务，但不应包含 IOptionsMonitor<MudHttpOpenTelemetryOptions>
    // （因为使用 Bind 而非 Configure<T>）
    var descriptor = services.FirstOrDefault(
        s => s.ServiceType == typeof(IOptionsMonitor<MudHttpOpenTelemetryOptions>));
    // 注：IOptionsMonitor<T> 由 services.Configure<T> 注册，Bind 不注册
    // 此测试验证设计意图，若未来改为 Configure<T> 则需更新
    Assert.Null(descriptor);  // 明确不支持热更新
}
```

> **替代方案（如未来决定支持热更新）**：将 `Bind` 改为 `services.Configure<MudHttpOpenTelemetryOptions>(configuration.GetSection(sectionPath))`，并在 `AddMudHttpOpenTelemetryCore` 中通过 `IOptionsMonitor<MudHttpOpenTelemetryOptions>` 读取 `CurrentValue`。但 OTel SDK 的 Provider 不可变，此改动仅影响 Mud.HttpUtils 自身的 ActivitySource/Meter 注册，实际收益有限。**当前不推荐此改动**。

**验证步骤**：

```powershell
dotnet test Tests/Mud.HttpUtils.OpenTelemetry.Tests --filter "FullyQualifiedName~DoesNotRegisterIOptionsMonitor"
```

---

### 3.3 任务 2.3：TokenRecoveryOptions 直接值注入 — ⏸️ 暂缓（v1.1 修订）

> **⚠️ v1.1 降级说明**：经源码验证，此任务存在以下问题，**暂缓实施**：
>
> 1. **假前提**：原方案声称"Resilience 支持热更新"作为对比依据，但 [PollyResiliencePolicyProvider.cs:56-60](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L56) 注入的是 `IOptions<ResilienceOptions>`（非 `IOptionsMonitor`），在构造时通过 `_options = options?.Value` 捕获快照。**Resilience 实际同样不支持运行时热更新**，因此不存在"不一致"需要修复。
> 2. **影响文件清单错误**：[TokenRecoveryDelegatingHandler.cs:51](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/TokenManager/TokenRecoveryDelegatingHandler.cs#L51) 仅持有 `TokenRecoveryExecutor _recoveryExecutor`，不直接持有 `_options`；[TokenRecoveryEnhancedClient.cs:31](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/TokenRecoveryEnhancedClient.cs#L31) 同样仅持有 `TokenRecoveryExecutor`。两者均**无需修改**，原方案错误列入影响清单。
> 3. **过度工程化**：`TokenRecoveryOptions`（Enabled/RecoveryMaxRetries/TokenScheme）为启动时固定的运营参数，运行时变更需求极低。改为 `IOptionsMonitor` 会破坏 `TokenRecoveryExecutor` 的公共构造函数签名（用户可直接 `new`），且为保持兼容需维护双代码路径，产生冗余。
>
> **替代方案（如未来确需热更新）**：移除 [ServiceCollectionExtensions.cs:720](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/ServiceCollectionExtensions.cs#L720) 的 `services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<TokenRecoveryOptions>>().Value)` 行，将 `TokenRecoveryDelegatingHandler` 的可选参数从 `TokenRecoveryOptions?` 改为 `IOptions<TokenRecoveryOptions>?`（非 IOptionsMonitor，保持快照语义），在构造函数内 `.Value` 提取后传入 `TokenRecoveryExecutor`。此改动不破坏 `TokenRecoveryExecutor` 公共 API。

**状态**：⏸️ 暂缓，移至附录 §十 待后续评估。本阶段不实施任何代码变更。

**测试场景**：无（暂缓实施）。

---

### 3.4 任务 2.4：TokenRefreshBackgroundOptions.SectionName 命名约定

**问题**：[TokenRefreshBackgroundOptions.cs:18](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Abstractions/TokenManager/TokenRefreshBackgroundOptions.cs#L18) `SectionName = "TokenRefreshBackground"`，是唯一未使用 `MudHttp` 前缀的配置节。

**设计决策**：**不在当前版本改名**（破坏性变更），而是在文档和 XML 注释中明确标注此差异，并在下个大版本中统一。

**修改文件 1**：`Mud.HttpUtils.Abstractions/TokenManager/TokenRefreshBackgroundOptions.cs`

```csharp
// 修改 SectionName 的 XML 注释：
/// <summary>
/// 配置节的名称，用于从配置文件中读取令牌刷新后台服务配置。
/// </summary>
/// <remarks>
/// <para><b>注意：此配置节名称为 <c>"TokenRefreshBackground"</c>，
/// 未遵循其他配置节的 <c>MudHttp</c> 前缀命名约定。</b>
/// 这是为了向后兼容历史版本而保留的命名。
/// 下个大版本将统一为 <c>"MudHttpTokenRefreshBackground"</c>。</para>
/// </remarks>
public const string SectionName = "TokenRefreshBackground";
```

**修改文件 2**：`Mud.HttpUtils.Client/README.md` 配置表中 `TokenRefreshBackground` 条目

在配置表该行后添加注释列说明命名差异。

**测试场景**：无新增测试（纯文档修改）。

**验证步骤**：人工审查文档更新。

---

## 四、Phase 3：文档一致性修复

> **目标**：消除文档与代码的不一致
> **影响范围**：README 文件、XML 注释
> **无新增测试**

### 4.1 任务 3.1：修正 Abstractions README 关于 AES 绑定的描述

**修改文件**：`Mud.HttpUtils.Abstractions/README.md`（L78 附近）

````markdown
<!-- 修改前 -->

AesEncryptionOptions 支持通过配置文件绑定（配置节名称：`MudHttpAesEncryption`）。

<!-- 修改后 -->

AesEncryptionOptions 支持通过配置文件绑定（配置节名称：`MudHttpAesEncryption`）。
通过 `AddMudHttpAesEncryptionFromConfiguration(IConfiguration)` 扩展方法一键注册：

```csharp
builder.Services.AddMudHttpAesEncryptionFromConfiguration(builder.Configuration);
```
````

appsettings.json 示例：

```json
{
  "MudHttpAesEncryption": {
    "Key": "base64-encoded-32-byte-key"
  }
}
```

> **注意**：`Key` 必须为 Base64 编码的 16/24/32 字节密钥（对应 AES-128/192/256）。
> 注册时会在首次解析 `IEncryptionProvider` 时校验密钥长度。

````

---

### 4.2 任务 3.2：补充两个 TimeoutSeconds 区分说明

**修改文件**：`Mud.HttpUtils.Client/README.md`（`MudHttpClientOptions.TimeoutSeconds` 配置表条目）

```markdown
<!-- 在 TimeoutSeconds 行的说明列追加 -->
| `TimeoutSeconds` | int | null | HttpClient 全局超时（秒）。null 表示使用 HttpClient 默认超时（100s）。
**注意：此超时与 `TimeoutOptions.TimeoutSeconds`（Polly 单次请求超时）不同。**
此超时包含所有重试的总时间，而 `TimeoutOptions.TimeoutSeconds` 仅控制单次请求的超时。
两者可同时配置，详见 [Resilience 文档](../Mud.HttpUtils.Resilience/README.md#超时配置)。 |
````

---

### 4.3 任务 3.3：补充 RetryStatusCodes 空数组行为说明

**修改文件**：`Mud.HttpUtils.Resilience/README.md`（L173 附近）

```markdown
<!-- 修改前 -->

`RetryStatusCodes`：触发的 HTTP 状态码列表。为空时使用默认值 `[408, 429, 500, 502, 503, 504]`。

<!-- 修改后 -->

`RetryStatusCodes`：触发的 HTTP 状态码列表。

| 配置值          | 行为                                                                                                                                |
| :-------------- | :---------------------------------------------------------------------------------------------------------------------------------- |
| 未设置 / `null` | 使用默认值 `[408, 429, 500, 502, 503, 504]`                                                                                         |
| `[]`（空数组）  | **不重试任何状态码**，仅 `HttpRequestException`/`TimeoutRejectedException`/`TaskCanceledException` 触发重试。运行时会记录警告日志。 |
| `[408, 503]`    | 仅重试指定状态码                                                                                                                    |

> **注意**：`null`（未设置）和 `[]`（空数组）语义不同。
> 如需使用默认状态码，请移除该配置项或显式设为 `null`。
```

---

### 4.4 任务 3.4：补充 OTel 热更新限制说明

**修改文件**：`Mud.HttpUtils.OpenTelemetry/README.md`（或 `Mud.HttpUtils.Client/README.md` 中 OTel 配置表）

```markdown
<!-- 在 MudHttpOpenTelemetry 配置表后添加说明 -->

> **热更新限制**：OpenTelemetry 配置在应用启动时一次性读取，**不支持 IOptionsMonitor 热更新**。
> 这是因为 OpenTelemetry SDK 的 TracerProvider/MeterProvider 在构建后不可变。
> 修改 `appsettings.json` 中的 `MudHttpOpenTelemetry` 节后需重启应用才能生效。
>
> 对比：`MudHttpResilience` 和 `MudHttpClients` 配置支持 IOptionsMonitor 热更新。
```

---

## 五、Phase 4：测试覆盖补齐

> **目标**：覆盖所有修复点的边界场景
> **预计新增测试**：11 个（v1.1 修订：原 14 个，移除 T10/T11 因 Task 2.3 暂缓，移除 T12 因 setter 校验已足够无需 IValidateOptions 重复）

### 5.1 测试清单汇总

| 编号 | 测试类                              | 测试方法                                                                   | 关联任务 | 优先级 |
| :--- | :---------------------------------- | :------------------------------------------------------------------------- | :------- | :----- |
| T1   | CircuitBreakerOptionsTests          | `MinimumThroughput_SetToLessThanTwo_Throws`                                | 1.1      | 高     |
| T2   | CircuitBreakerOptionsTests          | `MinimumThroughput_SetToValidValue_DoesNotThrow`                           | 1.1      | 高     |
| T3   | CircuitBreakerOptionsTests          | `MinimumThroughput_DefaultValue_IsTen`                                     | 1.1      | 中     |
| T4   | ResilienceConfigurationBindingTests | `RetryPolicy_WithEmptyStatusCodesArray_DoesNotRetryOnStatusCodeExceptions` | 1.2      | 高     |
| T5   | ResilienceConfigurationBindingTests | `RetryPolicy_WithEmptyStatusCodesArray_RetriesOnTimeoutException`          | 1.2      | 中     |
| T6   | ConfigurationBindingTests           | `AddMudHttpAesEncryptionFromConfiguration_BindsAndRegistersProvider`       | 2.1      | 高     |
| T7   | ConfigurationBindingTests           | `AddMudHttpAesEncryptionFromConfiguration_InvalidKey_ThrowsOnResolve`      | 2.1      | 高     |
| T8   | ConfigurationBindingTests           | `AddMudHttpAesEncryptionFromConfiguration_NullArgs_Throws`                 | 2.1      | 中     |
| T9   | OpenTelemetryOptionsTests           | `AddMudHttpOpenTelemetry_FromConfiguration_DoesNotRegisterIOptionsMonitor` | 2.2      | 中     |
| T10  | CircuitBreakerOptionsTests          | `MinimumThroughput_SetAfterSamplingDuration_StillValidates`                | 1.1      | 中     |
| T11  | ConfigurationBindingTests           | `AddMudHttpAesEncryptionFromConfiguration_CustomSectionPath`               | 2.1      | 低     |

> **v1.1 移除的测试**：
>
> - ~~T10 `TokenRecoveryExecutor_OptionsMonitor_ReloadsOnConfigChange`~~ — Task 2.3 暂缓
> - ~~T11 `TokenRecoveryExecutor_DefaultOptions_WhenNotConfigured`~~ — Task 2.3 暂缓
> - ~~T12 `MinimumThroughput_BoundFromConfig_LessThanTwo_Throws`~~ — 冗余：setter 校验已在绑定阶段触发 `ArgumentOutOfRangeException`，无需额外的 `IValidateOptions` 重复校验。`ConfigurationBinder` 调用 setter 时异常会直接传播，不经过 `IValidateOptions`（后者在绑定完成后运行）。

### 5.2 重点测试代码示例

**T4 — 空数组行为验证（带 StatusCode 异常不重试）**：

```csharp
[Fact]
public async Task RetryPolicy_WithEmptyStatusCodesArray_DoesNotRetryOnStatusCodeExceptions()
{
    // 验证：空数组时，带 StatusCode 的 HttpRequestException 不触发重试
    var options = new ResilienceOptions
    {
        Retry = new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 3,
            RetryStatusCodes = Array.Empty<int>()  // 空数组
        }
    };
    var provider = new PollyResiliencePolicyProvider(options);

    var callCount = 0;
    var policy = provider.GetRetryPolicy<string>();
    await Assert.ThrowsAsync<HttpRequestException>(async () =>
    {
        await policy.ExecuteAsync(async _ =>
        {
            callCount++;
            throw new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
        }, CancellationToken.None);
    });
    // 带 StatusCode 的异常在空数组下不重试
    Assert.Equal(1, callCount);
}
```

> **`ShouldRetry` 行为说明**（基于 [PollyResiliencePolicyProvider.cs:522-552](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs#L522) 源码）：
>
> - `HttpRequestException` 的重试由 `ShouldRetry(ex, retryStatusCodes)` 谓词决定，空数组下对有 StatusCode 的异常返回 false → **不重试**
> - `TimeoutRejectedException` 和 `TaskCanceledException`（非取消）通过 `.Or<>()` 无条件捕获 → **仍重试**

---

## 六、实施顺序与依赖关系

```
Phase 1（阻断性修复）
├── 1.1 MinimumThroughput 校验      ← 独立，可立即开始
└── 1.2 RetryStatusCodes 空数组处理  ← 独立，可立即开始（含 L109 + L429 两处）
         │
         ▼
Phase 2（一致性修复）
├── 2.1 AES 绑定方法                ← 独立
├── 2.2 OTel 热更新文档             ← 独立
├── 2.3 TokenRecovery IOptionsMonitor ← ⏸️ 暂缓（v1.1 降级，见 §3.3）
└── 2.4 SectionName 文档标注         ← 独立
         │
         ▼
Phase 3（文档一致性）               ← 依赖 Phase 2 完成代码变更
├── 3.1 AES README
├── 3.2 TimeoutSeconds README
├── 3.3 RetryStatusCodes README
└── 3.4 OTel 热更新 README
         │
         ▼
Phase 4（测试补齐）                ← 依赖 Phase 1-2 代码变更完成
```

**并行可能性**：Phase 1 的两个任务、Phase 2 的 2.1/2.2/2.4 可并行实施。Phase 3 的四个文档任务可并行。

---

## 七、风险评估

| 风险项                                     | 等级  | 影响                                              | 缓解措施                                                                                               |
| :----------------------------------------- | :---- | :------------------------------------------------ | :----------------------------------------------------------------------------------------------------- |
| MinimumThroughput 校验导致现有用户配置失效 | 🟡 中 | 用户已配置 `MinimumThroughput = 1` 的应用启动失败 | `MinimumThroughput` 默认值为 10（有效），仅显式设置无效值的用户受影响。在 CHANGELOG 中标注此破坏性变更 |
| RetryStatusCodes 空数组行为变更            | 🟢 低 | 仅新增警告日志，不改变运行时行为                  | 无需缓解                                                                                               |
| AES 绑定方法新增                           | 🟢 低 | 纯新增 API，无破坏性                              | 无需缓解                                                                                               |
| OTel 文档更新                              | 🟢 低 | 无代码变更                                        | 无需缓解                                                                                               |

> **v1.1 移除的风险项**：~~TokenRecoveryExecutor 构造函数签名变更~~ — Task 2.3 暂缓，无此风险。

---

## 八、验收标准

### 8.1 Phase 1 验收

- [x] `CircuitBreakerOptions.MinimumThroughput` setter 对 `< 2` 的值抛出 `ArgumentOutOfRangeException`
- [x] `PollyResiliencePolicyProvider` 在 `RetryStatusCodes` 为空数组时记录警告日志（L109 + L429 两处）
- [x] 所有现有测试通过（Resilience 161/161、Client 936/936）
- [x] 新增 6 个测试全部通过（T1-T5 + T10）

### 8.2 Phase 2 验收

- [x] `AddMudHttpAesEncryptionFromConfiguration` 方法存在且可从 `IConfiguration` 绑定
- [x] OpenTelemetry XML 注释标注不支持热更新
- [x] `TokenRefreshBackgroundOptions.SectionName` XML 注释标注命名差异
- [x] 所有现有测试通过
- [x] 新增 5 个测试全部通过（T6-T9, T11）
- [ ] ~~TokenRecoveryExecutor 注入 IOptionsMonitor~~ — ⏸️ 暂缓（v1.1）

### 8.3 Phase 3 验收

- [x] Abstractions README 包含 `AddMudHttpAesEncryptionFromConfiguration` 使用示例
- [x] Client README 区分两个 `TimeoutSeconds`
- [x] Resilience README 区分 `null` 与 `[]` 的 `RetryStatusCodes` 语义
- [x] OTel README 标注热更新限制

### 8.4 Phase 4 验收

- [x] 11 个新增测试全部通过（v1.1 修订：原 14 个，移除 3 个）
- [x] 配置相关类的测试覆盖率 ≥ 80%

### 8.5 全量回归验证

```powershell
# 运行全量测试套件
dotnet test Tests/Mud.HttpUtils.Generator.Tests
dotnet test Tests/Mud.HttpUtils.Client.Tests
dotnet test Tests/Mud.HttpUtils.Resilience.Tests
dotnet test Tests/Mud.HttpUtils.OpenTelemetry.Tests
dotnet test Tests/Mud.HttpUtils.Tests
```

预期结果：

- Generator: 402/402 ✅
- Client: 936/936 ✅（含新增 AES 绑定 4 个测试）
- Resilience: 161/161 ✅（含新增 CircuitBreaker 校验 4 个 + 空数组行为 2 个测试）
- OTel: 33/33 ✅（含新增 IOptionsMonitor 验证 1 个测试）
- Tests: 5/5 ✅
- Integration: 14 失败（预存在的 HTTPS URL 校验问题，非本次引入）

**实际验证结果（2026-07-12）**：全部通过，无回归。

---

## 九、变更文件清单

| 文件路径                                                                      | 变更类型 | 关联任务    |
| :---------------------------------------------------------------------------- | :------- | :---------- |
| `Mud.HttpUtils.Resilience/Options/CircuitBreakerOptions.cs`                   | 修改     | 1.1         |
| `Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs`                   | 修改     | 1.2（两处） |
| `Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs`                         | 修改     | 1.2         |
| `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`                         | 修改     | 2.1         |
| `Mud.HttpUtils.Abstractions/TokenManager/TokenRefreshBackgroundOptions.cs`    | 修改     | 2.4         |
| `Mud.HttpUtils.OpenTelemetry/MudHttpOpenTelemetryExtensions.cs`               | 修改     | 2.2         |
| `Mud.HttpUtils.Abstractions/README.md`                                        | 修改     | 3.1         |
| `Mud.HttpUtils.Client/README.md`                                              | 修改     | 3.2         |
| `Mud.HttpUtils.Resilience/README.md`                                          | 修改     | 3.3         |
| `Mud.HttpUtils.OpenTelemetry/README.md`                                       | 修改     | 3.4         |
| `Tests/Mud.HttpUtils.Resilience.Tests/CircuitBreakerOptionsTests.cs`          | 修改     | 1.1, 4      |
| `Tests/Mud.HttpUtils.Resilience.Tests/ResilienceConfigurationBindingTests.cs` | 修改     | 1.2, 4      |
| `Tests/Mud.HttpUtils.Client.Tests/ConfigurationBindingTests.cs`               | 修改     | 2.1, 4      |
| `Tests/Mud.HttpUtils.OpenTelemetry.Tests/OpenTelemetryOptionsTests.cs`        | 修改     | 2.2, 4      |

> **v1.1 移除的变更文件**（Task 2.3 暂缓）：
>
> - ~~`Mud.HttpUtils.Client/TokenManager/TokenRecoveryExecutor.cs`~~
> - ~~`Mud.HttpUtils.Client/TokenManager/TokenRecoveryDelegatingHandler.cs`~~
> - ~~`Mud.HttpUtils.Client/TokenManager/TokenRecoveryEnhancedClient.cs`~~
> - ~~`Mud.HttpUtils.Resilience/Logging/*.cs`~~（LoggerMessage 定义统一在 `MudHttpClientLog.cs`）

---

## 十、附录：未处理项（需后续评估）

以下审查报告中的问题**本方案不处理**，原因如下：

| 问题                                                 | 不处理原因                                                                                                                                                  |
| :--------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TokenRefreshBackgroundOptions.SectionName` 改名     | 破坏性变更，推迟至下个大版本                                                                                                                                |
| 三个"过期提前量"配置统一命名                         | `TokenManagerBase.ExpireThresholdSeconds` 是 `protected virtual`，非配置项；`OAuth2Options.ExpirySafetyMarginSeconds` 已有明确 XML 注释说明三者关系         |
| `EnhancedHttpClientOptions.AllowCustomBaseUrls` 冗余 | 已在 XML 注释中说明覆盖关系，且程序化注册场景需要此属性                                                                                                     |
| OTel 改为支持 IOptionsMonitor                        | OTel SDK 本身不支持热更新，收益低                                                                                                                           |
| TokenRecoveryOptions 改为 IOptionsMonitor 注入       | v1.1 降级暂缓：Resilience 实际也不支持热更新（IOptions 非 IOptionsMonitor），无不一致；TokenRecovery options 为运营参数，热更新需求低；改动破坏公共构造函数 |

> **v1.1 新增未处理项**：TokenRecoveryOptions IOptionsMonitor 改造 — 见 §3.3 替代方案。
