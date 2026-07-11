# Mud.HttpUtils.OpenTelemetry

## 概述

`Mud.HttpUtils.OpenTelemetry` 是 Mud.HttpUtils 的 OpenTelemetry 适配包，提供 **一键开启** Mud.HttpUtils 内置的分布式追踪（Tracing）与指标（Metrics）采集能力，并自动关联 .NET `HttpClient` 与 ASP.NET Core 的内置 Instrumentation。

## 目标框架

- `net8.0`
- `net10.0`

> 由于 OpenTelemetry SDK 1.10.0 的传递依赖（`Microsoft.Extensions.*` 9.0.0）已不再支持 `net6.0`，本包仅面向 `net8.0+`。如需在 `net6.0` 中使用，请直接引用 `OpenTelemetry.*` 包并按本包源码自行配置。

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.OpenTelemetry" Version="x.x.x" />
```

## 快速开始

### 1. ASP.NET Core 主机

```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册 Mud.HttpUtils 客户端（详见 Mud.HttpUtils.Client 文档）
builder.Services.AddMudHttpClient("myApi", c =>
{
    c.BaseAddress = new Uri("https://api.example.com");
});

// 一键开启 Mud.HttpUtils 的 OpenTelemetry 追踪与指标
builder.Services.AddMudHttpOpenTelemetry(options =>
{
    options.OtlpEndpoint = new Uri("http://otel-collector:4317");
});

var app = builder.Build();
app.Run();
```

### 2. 控制台应用

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddMudHttpClient("myApi", c => c.BaseAddress = new Uri("https://api.example.com"));
services.AddMudHttpOpenTelemetry();

using var provider = services.BuildServiceProvider();
// 使用 IHttpClientFactory 或 IEnhancedHttpClientFactory 发起请求
```

## 配置选项

### `MudHttpOpenTelemetryOptions`

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableTracing` | `bool` | `true` | 是否启用分布式追踪 |
| `EnableMetrics` | `bool` | `true` | 是否启用指标采集 |
| `EnableLogging` | `bool` | `false` | 是否启用 OTLP 日志导出（向后兼容；依赖 .NET 8+ 的 ILogger 集成） |
| `EnableHttpClientInstrumentation` | `bool` | `true` | 关联 .NET HttpClient 内置 ActivitySource |
| `EnableAspNetCoreInstrumentation` | `bool` | `true` | 启用 ASP.NET Core 入站请求 Instrumentation（控制台应用无效） |
| `OtlpEndpoint` | `Uri?` | `http://localhost:4317` | OTLP 导出端点，`null` 表示不配置 OTLP 导出器 |
| `OtlpExportProtocol` | `OtlpExportProtocol` | `Grpc` | OTLP 导出协议（`Grpc` 或 `HttpProtobuf`） |
| `UseShortExporterTimeout` | `bool` | `false` | 是否使用 5 秒短超时（开发调试用） |
| `ServiceName` | `string` | `"Mud.HttpUtils.Application"` | OTel Resource 属性 `service.name` |
| `ServiceVersion` | `string` | `MudHttpActivitySource.Version` | OTel Resource 属性 `service.version` |
| `DeploymentEnvironment` | `string` | `"production"` | OTel Resource 属性 `deployment.environment` |
| `SamplingRatio` | `double` | `1.0` | 采样比率（0.0~1.0），生产环境建议 0.1~0.3 |
| `ExportBatchSize` | `int?` | `null` | OTLP 每批导出最大条目数（映射到 `BatchExportProcessorOptions.MaxExportBatchSize`），`null` 使用 SDK 默认值（512） |
| `ExportIntervalMilliseconds` | `int?` | `null` | OTLP 批量导出间隔毫秒数（映射到 `BatchExportProcessorOptions.ScheduledDelayMilliseconds`），`null` 使用 SDK 默认值（5000ms） |
| `OtlpHeaders` | `IDictionary<string, string>?` | `null` | 自定义 OTLP Headers（如认证头） |
| `ConfigureTracing` | `Action<TracerProviderBuilder>?` | `null` | 自定义追踪配置委托，在 Mud 默认配置之后执行 |
| `ConfigureMetrics` | `Action<MeterProviderBuilder>?` | `null` | 自定义指标配置委托，在 Mud 默认配置之后执行 |
| `ConfigureLogging` | `Action<LoggerProviderBuilder>?` | `null` | 自定义日志配置委托，在 Mud 默认配置之后执行 |

### 从 IConfiguration 绑定

除代码配置外，还支持从 `appsettings.json` 绑定选项：

```csharp
builder.Services.AddMudHttpOpenTelemetry(builder.Configuration);
```

对应 `appsettings.json`：

```json
{
  "MudHttpOpenTelemetry": {
    "ServiceName": "my-service",
    "SamplingRatio": 0.1,
    "OtlpEndpoint": "http://otel-collector:4317",
    "EnableLogging": true,
    "OtlpHeaders": {
      "Authorization": "Bearer my-token"
    }
  }
}
```

> 也可同时使用配置绑定和代码配置：`AddMudHttpOpenTelemetry(builder.Configuration, configure: options => { ... })`，代码配置在配置绑定之后执行，可覆盖绑定值。

### 高级配置示例

```csharp
builder.Services.AddMudHttpOpenTelemetry(options =>
{
    options.OtlpEndpoint = new Uri("http://otel-collector:4318");
    options.OtlpExportProtocol = OtlpExportProtocol.HttpProtobuf;
    options.EnableAspNetCoreInstrumentation = false;

    // 追加自定义 ActivitySource
    options.ConfigureTracing = tp => tp.AddSource("MyApp.Business");

    // 追加 Prometheus 导出器（需额外引用 OpenTelemetry.Exporter.Prometheus.AspNetCore）
    options.ConfigureMetrics = mp => mp.AddPrometheusExporter();
});
```

## 自动采集的内容

### 追踪（Tracing）

| ActivitySource | 用途 |
|----------------|------|
| `Mud.HttpUtils.HttpClient` | Mud.HttpUtils 出站 HTTP 请求活动（含 method/url/status/duration） |
| `System.Net.Http`（.NET 内置） | .NET HttpClient 底层 socket 活动 |
| `Microsoft.AspNetCore`（.NET 内置） | ASP.NET Core 入站请求活动 |

### 指标（Metrics）

| Meter | 指标 | 说明 |
|-------|------|------|
| `Mud.HttpUtils.HttpClient` | `mud.http.requests` | HTTP 请求计数 |
| `Mud.HttpUtils.HttpClient` | `mud.http.request.duration` | HTTP 请求耗时直方图（ms） |
| `Mud.HttpUtils.HttpClient` | `mud.http.cache` | 缓存命中/未命中计数 |
| `Mud.HttpUtils.HttpClient` | `mud.token.refresh` | 令牌刷新次数 |
| `Mud.HttpUtils.HttpClient` | `mud.token.refresh.duration` | 令牌刷新耗时直方图（ms） |
| `Mud.HttpUtils.HttpClient` | `mud.http.retry` | 重试次数 |
| `Mud.HttpUtils.HttpClient` | `mud.http.circuit_breaker.state` | 熔断器状态 Gauge |
| `System.Net.Http`（.NET 内置） | `http.client.*` | .NET HttpClient 内置指标 |

## 与健康检查配合

`AddMudHttpOpenTelemetry` 与 `AddMudHttpHealthChecks` 可同时使用：

```csharp
builder.Services.AddMudHttpClient("myApi", c => c.BaseAddress = new Uri("https://api.example.com"));
builder.Services.AddMudHttpHealthChecks();
builder.Services.AddMudHttpOpenTelemetry();
```

## 设计原则

- **零侵入**：用户代码无需任何改动，仅在 DI 注册时调用一次扩展方法
- **可观测性零开销**：无监听器时 `ActivitySource.StartActivity` 返回 `null`，`Counter.Add` 直接短路
- **默认即生产可用**：默认开启 Tracing + Metrics + OTLP gRPC 导出至本地 4317
- **可扩展**：通过 `ConfigureTracing` / `ConfigureMetrics` 委托追加自定义配置
- **AOT 兼容**：所有 API 均为静态类型与委托，无反射

## 依赖项

| 包 | 说明 |
|----|------|
| `Mud.HttpUtils.Abstractions` | 提供 `MudHttpActivitySource` / `MudHttpMeter` 静态源 |
| `OpenTelemetry` | OpenTelemetry SDK 核心 |
| `OpenTelemetry.Extensions.Hosting` | DI 集成扩展 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP 导出器 |
| `OpenTelemetry.Instrumentation.Http` | HttpClient Instrumentation |
| `OpenTelemetry.Instrumentation.AspNetCore` | ASP.NET Core Instrumentation |

## 部署 OTLP 收集器

最简 Jaeger 部署（接收 OTLP gRPC 4317）：

```bash
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  jaegertracing/all-in-one:1.62
```

启动应用后访问 `http://localhost:16686` 查看 Mud.HttpUtils 出站请求 span。

Prometheus + Grafana 抓取 `Mud.HttpUtils.HttpClient` Meter：

```yaml
# otel-collector-config.yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
exporters:
  prometheus:
    endpoint: 0.0.0.0:8889
service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [prometheus]
    traces:
      receivers: [otlp]
      exporters: [otlp]  # 转发至 Jaeger
```
