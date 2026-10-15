# Mud.HttpUtils OpenTelemetry AOT 兼容性说明

> 版本：v1 ｜ 日期：2026-07-16

---

## 1. 概述

`Mud.HttpUtils.OpenTelemetry` 包为 Mud.HttpUtils 提供分布式追踪与指标采集的 OpenTelemetry 集成。本文档说明该包在 Native AOT 场景下的兼容性状态与已知限制。

---

## 2. 兼容性矩阵

| 配置项 | AOT 兼容性 | 说明 |
|--------|-----------|------|
| `PublishAot=true` + `TrimMode=partial`（默认） | ✅ 支持 | 推荐配置，CI 已验证 |
| `PublishAot=true` + `TrimMode=full` | ⚠️ 需验证 | OTLP Exporter 的 Protobuf 序列化在 full 裁剪下可能有反射依赖 |
| `IsAotCompatible=true`（不发布 AOT） | ✅ 支持 | 程序集级别 AOT 兼容标注已通过 |

---

## 3. 已知风险

### 3.1 OTLP Exporter Protobuf 反射

`OpenTelemetry.Exporter.OpenTelemetryProtocol` 包内部使用 `Google.Protobuf` 进行 OTLP 协议序列化。Protobuf 运行时在 `TrimMode=full` 下可能触发以下问题：

- **IL2026 告警**：Protobuf 的反射式序列化器访问未被裁剪器保留的类型成员
- **IL3050 告警**：Protobuf 内部可能使用 `RuntimeHelpers.RunClassConstructor` 等动态代码路径

**影响范围**：仅在 `TrimMode=full` 且使用 OTLP 导出器时出现。使用 Console 导出器或 Zipkin 导出器不受影响。

**缓解措施**：
1. 保持 `TrimMode=partial`（默认值）
2. 如需 `TrimMode=full`，在项目文件中添加：
   ```xml
   <TrimmerRootDescriptor Include="otlp-roots.xml" />
   ```
   并在 `otlp-roots.xml` 中声明 Protobuf 类型的根：
   ```xml
   <linker>
     <assembly fullname="Google.Protobuf" preserve="all" />
   </linker>
   ```

### 3.2 OpenTelemetry.Instrumentation.Http / AspNetCore

这些仪表包使用 `DiagnosticSource` 订阅机制，在 AOT 下通过 `IsAotCompatible` 标注和源生成保证兼容。但 `TrimMode=full` 可能裁剪掉 HTTP 仪表化所需的内部类型。

**缓解措施**：同上，保持 `TrimMode=partial` 或添加根描述符。

---

## 4. 推荐配置

### 4.1 生产环境推荐

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <!-- 保持默认 partial 模式 -->
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

### 4.2 需要 full 裁剪时

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <TrimMode>full</TrimMode>
  <TrimmerSingleWarn>false</TrimmerSingleWarn>
</PropertyGroup>
<ItemGroup>
  <TrimmerRootDescriptor Include="otel-roots.xml" />
</ItemGroup>
```

---

## 5. 验证项目

`Demos/AotFullTrimVerificationDemo` 项目启用了 `TrimMode=full` + `TrimmerSingleWarn=false`，用于验证 Mud.HttpUtils 核心库在完整裁剪模式下的兼容性。该验证项目不包含 OpenTelemetry 包引用，以隔离验证库代码本身的裁剪安全性。

如需验证 OpenTelemetry 包在 full 裁剪下的行为，可在此项目基础上添加 `Mud.HttpUtils.OpenTelemetry` 引用并观察裁剪告警。

---

## 6. 上游参考

- [OpenTelemetry .NET AOT 支持状态](https://github.com/open-telemetry/opentelemetry-dotnet/issues/5544)
- [.NET Native AOT 裁剪文档](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options)
- [Google.Protobuf AOT 兼容性](https://github.com/protocolbuffers/protobuf/tree/main/csharp#aot-compatibility)
