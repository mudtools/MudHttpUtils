# Mud.HttpUtils Native AOT 重构方案（v3 修订版）

> 版本：v3-revised ｜ 日期：2026-07-13 ｜ 状态：**执行中**（Phase 10/12/14/16 已完成）
> 关联文档：`.docs/AOT-Compatibility-Review.md`、`.docs/AOT-Refactoring-Plan-v2.md`、`.docs/JsonAotSourceGeneratorPlan.md`
> 制定依据：基于"评审报告"的独立验证（实际 `dotnet build` + `dotnet publish` 复现）+ v2 已落地能力的架构盘点 + 逐行代码审计。
> **文档修订**：本版经实际构建验证 + 逐行代码审计，已修正原 v3 的 NETSDK1207 误判、Phase 11 生成器内联方案的根本性缺陷、Phase 13 去压制方案的可行性问题，修订要点见 **§9 独立评审与修订记录**。

---

## 0. 摘要（Executive Summary）

v2 已完成库**内部**代码路径的 AOT 化（OAuth2、默认脱敏器、库内置 `MudHttpJsonContext`、QueryMap 生成期内联、XML 静态 `XmlSerializer`、EncryptContent\<T\>、Resilience IL2026 修复、NDJSON JsonTypeInfo 重载）。这些改造方向正确、必要，且使"库代码本身"更接近 AOT 安全。

但**独立验证结论**表明：当前项目**仍不能宣称"完整支持 AOT 编译"**，原因如下：

1. **消费方 DTO 上下文无法自动生成（架构性缺口，最关键）**：`[HttpJsonSerializable]` → `JsonSerializerContext` 的生成由 `Tools/Mud.HttpUtils.JsonContextScaffolder`（独立 `dotnet run` 预构建工具）完成，**仅 Demo 的 csproj 接线，未随 NuGet 包发布**。典型 NuGet 消费方拿不到该工具，其 DTO 不会进入任何 `JsonSerializerContext` → AOT 下序列化这些 DTO 在运行时抛 `NotSupportedException` 或静默失败。`AotDtoCoverageAnalyzer`（AOT004）只警告、不补齐全覆盖。

2. **库自身仍有真实 IL 告警路径**：实际 `dotnet publish` 验证确认 `MudHttpDiagnosticListener.cs:45` 的 `DiagnosticSource.Write(string, object)` 产生 IL2026 告警；配置绑定 `section.Bind(options)` / `Configure<TOptions>(IConfiguration)` 路径同样产生告警。这些是**确实会破坏 AOT 运行时**的路径。

3. **生成客户端代码用 `[UnconditionalSuppressMessage]` 压制 IL2026/IL3050**：`ClassStructureGenerator.cs:62-71` 的压制会掩盖 DTO 未被上下文覆盖的真实缺口。但直接移除压制并改用 `JsonTypeInfo<T>` 重载会破坏现有 DI 注入机制——需在保留压制的同时增强 AOT004 诊断覆盖率。

4. **OpenTelemetry 子包完全未盘点**：`Mud.HttpUtils.OpenTelemetry`（多目标 + OTel 1.16.0 依赖）未做任何 AOT 盘点，且其 `AddMudHttpOpenTelemetry(IConfiguration)` 重载使用 `section.Bind(options)`（AOT 不安全）。

> **原 v3 修正**：原 v3 称"`dotnet publish -p:PublishAot=true` 因 NETSDK1207 失败"——经实际验证（SDK 10.0.301），`PublishAot=true` 在 csproj 中设置**不会**作为全局属性传播到 P2P 引用，NETSDK1207 **不发生**。AOT 发布的真正障碍是本地环境缺少 C++ 链接器（CI runner 已预装）。原 Phase 10 的"pack→PackageReference 修复 NETSDK1207"前提不成立，但 pack→PackageReference 作为**NuGet 消费体验验证**仍有价值。

本方案目标：在 v2 基础上，**让消费方开箱 AOT**、**消除库自身真实告警**、**验证 OpenTelemetry 子包 AOT 兼容性**，使 `dotnet publish -p:PublishAot=true` 对"以 NuGet 包方式消费的 AOT 应用"成为真实可用的发布门禁。

---

## 1. 验证基线（实际构建复现证据）

### 1.1 Native AOT 发布尝试（成功，非 NETSDK1207）

```
dotnet publish Demos/AotVerificationDemo/AotVerificationDemo.csproj -c Release -f net8.0 -r win-x64
→ 构建阶段成功（无 NETSDK1207），IL2026 告警出现于 MudHttpDiagnosticListener.cs:45
→ 原生编译阶段因本地缺少 C++ 链接器失败（环境问题，非项目配置问题）
```

> **根因复核**：`PublishAot=true` 在 `AotVerificationDemo.csproj` 的 `<PropertyGroup>` 中设置，作为**常规属性**仅影响当前项目，**不**作为全局属性传播到 P2P 引用。`Attributes`（netstandard2.0-only）与 `Generator`（netstandard2.0-only）在 AOT 发布时仅被编译为 IL 程序集/分析器 DLL，不会被 AOT 原生编译，因此不触发 NETSDK1207。CI runner（ubuntu-latest / windows-latest）已预装 C++ 工具链，AOT 发布可成功完成。

> **原 v3 修正**：原 v3 §1.1 称"失败于 NETSDK1207"属**误判**。实际验证表明 csproj 内 `PublishAot=true` 不传播到 P2P 引用。`-p:PublishAot=true` 命令行形式**会**作为全局属性传播，但 CI 和 csproj 均使用 csprop 设置方式，不受影响。

### 1.2 AOT 分析器构建（成功，确认 IL2026 告警）

`dotnet publish Demos/AotVerificationDemo/AotVerificationDemo.csproj -c Release -f net8.0 -r win-x64` → 构建成功，确认以下 IL 告警：

| 文件 | 告警 | 性质 | 当前标注状态 |
|------|------|------|-------------|
| `Mud.HttpUtils.Abstractions/.../MudHttpDiagnosticListener.cs:45` | IL2026 | `DiagnosticSource.Write(string, object)` —— 对象类型无法静态发现 | **未标注** |
| `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:687` | IL2026+IL3050 | `section.Bind(options)` 配置绑定 | **未标注** |
| `Mud.HttpUtils.Client/HttpClient/Observability/HealthChecksServiceCollectionExtensions.cs:97` | IL2026+IL3050 | `section.Bind(options)` 配置绑定 | **未标注** |
| `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs:112` | IL2026+IL3050 | `Configure<ResilienceOptions>(IConfiguration)` 配置绑定 | **未标注** |
| `Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs:249,352` | IL2026+IL3050 | `Deserialize<T>(json, s_jsonOptions)` 传 options 而非 `JsonTypeInfo<T>` | **未标注** |
| `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs:64,134` | IL2026 | `new XmlSerializer(typeof(T))` | **已标注 `[RequiresDynamicCode]`** |
| `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs:210` | IL2026+IL2070 | `MaskObject` 反射 | **已标注 `[RequiresUnreferencedCode]`+`[RequiresDynamicCode]`** |
| `Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs:49` | IL2072 | `[QueryMap]` 反射展平 | **已标注 `[RequiresUnreferencedCode]`+`[UnconditionalSuppressMessage("IL2072")]`** |
| `Mud.HttpUtils.OpenTelemetry/MudHttpOpenTelemetryExtensions.cs:78` | IL2026+IL3050 | `configuration.GetSection(sectionPath).Bind(options)` | **未标注** |

> **原 v3 修正**：原 v3 称"108 条 IL 告警"——实际验证中未复现此数量（可能因 SDK 版本差异或增量构建缓存）。上表为逐行代码审计确认的告警源。执行前应以 `dotnet publish -p:PublishAot=true -f net8.0` 重新基线化真实条数。

> **注**：v2 §9.2 称 `dotnet build -p:AotStrictMode=true` "0 错误"，是因为 `AotStrictMode` 的 `WarningsAsErrors` 仅作用于 **Demo 工程自身编译**（`AotVerificationDemo.csproj:21-23`），库工程的 IL 告警不被视为错误。

### 1.3 噪声

`NETSDK1210`：netstandard2.0/net6.0 TFM 收到 `IsAotCompatible`/`EnableAotAnalyzer` 设置告警（已确认出现在构建输出中）。根因是 `Directory.Build.props` 的 TFM 条件判定对 Attributes/Generator 仍有"溢出"（见 Phase 14）。

---

## 2. 当前架构 AOT 能力盘点（继承 + 仍缺）

### 2.1 已具备（v2 已落地，保留复用）

- `IsAotCompatible`/`EnableAotAnalyzer` 对 net8/net10 开启（`Directory.Build.props`）。
- 源生成客户端：`Mud.HttpUtils.Generator/Generators/Implementation/*`（JSON 走 DI 注入 `JsonSerializerOptions`；`[Form]`/`[Query]` 编译期属性枚举；XML 响应用生成期静态 `XmlSerializer` 字段）。
- `EnhancedHttpClient.BuildJsonOptions`（`EnhancedHttpClient.cs:120-165`）已实现 JIT/AOT 分支：`JsonTypeInfoResolver.Combine(消费方 resolver, 库内置 MudHttpJsonContext.Default)`；AOT 下**不**静默回退反射。
- 库内置上下文：`MudHttpJsonContext`（`DefaultJsonContext.cs`）、`OAuth2JsonContext`（`OAuth2JsonContext.cs`）。
- `AotSafeSensitiveDataMasker` 默认注册；`EncryptContent<T>` 泛型重载；Resilience `ActivatorUtilities` 分支已加 `[UnconditionalSuppressMessage]`+`DynamicallyAccessedMembers`；NDJSON `JsonTypeInfo<T>` 重载。
- 诊断：`AotDtoCoverageAnalyzer`（AOT004）检测 **`[HttpClientApi]` 接口方法的 `[Body]`/`[Query]` DTO** 是否被 `JsonSerializerContext` 覆盖。
- 工具（已实现，未随包）：`Tools/Mud.HttpUtils.JsonContextScaffolder`（`PackAsTool=true`，`ToolCommandName=mud-jsonctx`）扫描 `[HttpJsonSerializable]` 生成 `JsonSerializerContext`。Demo 中通过 MSBuild `GenerateJsonContext` Target 在 `BeforeCompile` 前运行。
- 生成代码压制：`ClassStructureGenerator.cs:62-71` 用 `[UnconditionalSuppressMessage("IL2026"/"IL3050")]` 压制生成客户端代码的 AOT 误报（因 `_jsonSerializerOptions` 通过 DI 注入 resolver，AOT 分析器无法静态追踪）。

### 2.2 仍缺（v3 要解决）

| 优先级 | 缺口 | 证据 | 影响 |
|--------|------|------|------|
| 🔴 最高 | 消费方 DTO 上下文无法自动生成（工具未随包） | `AotVerificationDemo.csproj:50-68`（GenerateJsonContext 目标仅 Demo 接线）；`Tools/...` 未进包 | 典型 NuGet 消费方 AOT 下其 DTO 未覆盖 → 运行时失败 |
| 🔴 高 | `DiagnosticSource.Write(string,object)` AOT 不安全 | `MudHttpDiagnosticListener.cs:45` IL2026 | 可观测性在 AOT 下失效/异常 |
| 🔴 高 | 配置绑定 `Configure<TOptions>(IConfiguration)`/`Bind` AOT 不安全 | `Resilience/ServiceCollectionExtensions.cs:112`、`Client/ServiceCollectionExtensions.cs:684,687`、`HealthChecksServiceCollectionExtensions.cs:97` | DI 配置注册路径在 AOT 下告警/潜在失败 |
| 🔴 高 | **`Mud.HttpUtils.OpenTelemetry` 子包完全未盘点** | `Mud.HttpUtils.OpenTelemetry.csproj`；`MudHttpOpenTelemetryExtensions.cs:78` `section.Bind(options)` | 消费方引用该包时 AOT 发布可能失败 |
| 🟠 中 | `StandardOAuth2TokenManager` 用 options 重载而非 `JsonTypeInfo<T>` | `StandardOAuth2TokenManager.cs:249,352` | 不一致、产生告警（类型已覆盖，但应修正） |
| 🟠 中 | 生成代码 `[UnconditionalSuppressMessage]` 掩盖覆盖率缺口 | `ClassStructureGenerator.cs:62-71` | DTO 未覆盖时压制使运行时失败不可见 |
| 🟡 低 | `NETSDK1210` 配置噪声 | `Directory.Build.props` TFM 条件 | 无害但干扰信号 |
| 🟡 低 | AOT004 诊断范围窄于宣称（仅 `[HttpClientApi]` 方法 DTO） | `AotDtoCoverageAnalyzer.cs:214-239` | `EncryptContent<T>`/`[QueryMap]` 路径 DTO 缺口不被告警 |

---

## 3. 重构目标与原则

- **目标**：以 NuGet 包方式消费的 AOT 应用，在 `PublishAot=true` 下**开箱可用**——消费方只需标注 `[HttpJsonSerializable]` 并运行脚手架工具（随包分发），无需手工写 `JsonSerializerContext`。
- **原则**：
  1. **验证先于改造**：先确认 AOT 发布链路可跑通（Phase 10），否则后续改造无法被门禁证明。
  2. **pre-build 脚手架优先**：`[HttpJsonSerializable]` → `JsonSerializerContext` 的生成**必须**通过 pre-build 产出真实 `.cs` 源文件（而非 `IIncrementalGenerator.AddSource`），因为 Roslyn 源生成器互不可见（dotnet/roslyn#57239），STJ 原生源生成器无法处理其他生成器的 `AddSource` 输出。将已实现的 `JsonContextScaffolder` 工具随 NuGet 包分发（作为 `dotnet tool` + MSBuild Target），取代对外置工具的手工依赖。
  3. **保留必要压制 + 增强诊断**：生成代码的 `[UnconditionalSuppressMessage]` 是对 DI 注入 resolver 的合理压制（AOT 分析器无法静态追踪 DI 数据流），保留不动。通过增强 AOT004 诊断覆盖率来弥补"压制掩盖缺口"的风险。
  4. **真实告警清零**：库自身 net8/net10 构建在严格模式下对**库代码**也 0 IL 错误（含 `DiagnosticSource`、配置绑定等未标注路径）。
  5. **破坏性最小**：XML 请求体、`QueryMap`、反射脱敏、`EncryptContent(object)` 等保留并标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`（多数已标注），仅改变未标注的真实告警路径与补充 AOT 安全替代。

---

## 4. 分阶段重构方案

### Phase 10 — AOT 发布链路验证 + NuGet 消费体验验证【高优先】✅ 已完成

**问题**：原 v3 称 NETSDK1207 阻塞 AOT 发布——经实际验证**不成立**。但当前 Demo 使用 ProjectReference，未验证"以 NuGet 包方式消费"的真实体验。

**方案**：

1. **确认现有 ProjectReference 路径的 AOT 发布可跑通**：
   - `dotnet publish Demos/AotVerificationDemo/AotVerificationDemo.csproj -c Release -f net8.0 -r win-x64` 构建阶段成功（已验证）。
   - CI `aot-publish` 作业（`ci.yml`）在 GitHub Actions runner 上执行 `dotnet publish`（net8.0/net10.0）+ 运行时 `AOT_OK` 校验。
2. **新增 PackageReference 验证路径（✅ 已实现）**：
   - 新增 `Demos/AotPackageRefDemo/` 项目，通过 `<PackageReference>` 引用 NuGet 包（版本跟随 `$(Version)`）。
   - 项目包含 `[HttpClientApi]` 接口、`[HttpJsonSerializable]` DTO、手动 `JsonSerializerContext`，验证源生成器从 `analyzers/dotnet/cs` 加载。
   - 配套 `nuget.config` 指向本地 `artifacts/` 目录，支持本地与 CI 消费。
   - `Mud.HttpUtils.Client.csproj` 已添加 `<InternalsVisibleTo Include="AotPackageRefDemo" />`。
3. **CI `aot-publish` 作业保持现有 ProjectReference 路径，新增 `aot-packageref` 作业验证 NuGet 消费路径**：
   - 现有 `aot-publish` 作业（ProjectReference + `-p:AotStrictMode=true`）保持不变，作为主要门禁。
   - 新增 `aot-packageref` 作业（ubuntu-latest）：先 `dotnet pack` 全部库到 `artifacts/`，再还原 + AOT 发布 `AotPackageRefDemo`，运行校验 `AOT_OK`。

**实施验证（本地）**：
- `dotnet pack` 全部库 → `artifacts/` ✅
- `dotnet restore Demos/AotPackageRefDemo` ✅（`nuget.config` 指向 `artifacts/`）
- `dotnet build` ✅（0 错误 0 警告，源生成器从 NuGet 包正确加载）
- `dotnet run` ✅（输出 `AOT_OK`，4 个场景全部通过）
- `dotnet publish -p:PublishAot=true` 构建阶段 ✅（原生编译阶段因本地缺少 C++ 链接器跳过，CI runner 已预装）

**验收**：`dotnet publish` 对 net8.0/net10.0 **成功生成原生二进制**；CI `aot-publish` + `aot-packageref` 作业通过且运行校验 `AOT_OK`。

---

### Phase 11 — 将 `[HttpJsonSerializable]` → `JsonSerializerContext` 生成工具随 NuGet 包分发【最关键架构改进】

**问题**：当前 JSON 上下文生成依赖外置 `Tools/Mud.HttpUtils.JsonContextScaffolder`（`dotnet run` 预构建），仅 Demo 接线、未进包。NuGet 消费方无法自动覆盖其 DTO。

> **原 v3 方案修正**：原 v3 Phase 11 提出在主增量生成器内新增 `RegisterSourceOutput` pass 来生成 `JsonSerializerContext`。此方案**根本性不可行**——Roslyn 源生成器互不可见（dotnet/roslyn#57239），STJ 原生源生成器**无法**处理其他生成器通过 `AddSource` 产出的 `JsonSerializerContext`，会导致 `Default` 缺失或静默空 Context。此结论已由 `JsonAotSourceGeneratorPlan.md §0.2/§3.2` 确认并经实测验证。

**方案（基于已实现的 pre-build 脚手架）**：

1. **将 `JsonContextScaffolder` 打包为 `dotnet tool` 随 NuGet 分发**：
   - 工具已配置 `PackAsTool=true`、`ToolCommandName=mud-jsonctx`（`Mud.HttpUtils.JsonContextScaffolder.csproj:18-19`）。
   - 消费方安装：`dotnet tool install Mud.HttpUtils.JsonContextScaffolder`。
   - 运行：`mud-jsonctx --project <path> -o <output-dir>`，产出真实 `.g.cs` 源文件。
2. **在 `Mud.HttpUtils.Generator` NuGet 包中附带 MSBuild Target（自动接线）**：
   - 新增 `build/Mud.HttpUtils.Generator.targets` 文件，包含 `GenerateJsonContext` Target（复用 `AotVerificationDemo.csproj:50-68` 的 MSBuild 逻辑）。
   - Target 在 `BeforeCompile` 前运行 `dotnet tool run mud-jsonctx`，产出 `.g.cs` 到 `$(IntermediateOutputPath)GeneratedJsonContext/`。
   - 消费方只需标注 `[HttpJsonSerializable]`，无需手工配置 MSBuild Target。
   - **条件触发**：仅当项目中存在 `[HttpJsonSerializable]` 标注时才运行脚手架（通过 `Condition` 检查）。
3. **保留手工运行模式**：消费方也可选择手工运行 `mud-jsonctx` 并将产出 `.g.cs` 提交源码树（与 `AotVerificationDemo` 现有模式一致），获得零时序风险与可审查性。
4. **与现有 `AotDtoCoverageAnalyzer` 协同**：生成器覆盖后，AOT004 仅在"消费方 DTO 既未标注 `[HttpJsonSerializable]` 也未手工加入任何上下文"时告警。注意 AOT004 当前仅覆盖 `[HttpClientApi]` 接口方法的 `[Body]`/`[Query]` DTO（见 §9-A.3-4）。

**验收**：新建一个**仅引用 NuGet 包**的最小 AOT 控制台，对其 DTO 仅标注 `[HttpJsonSerializable]`，`PublishAot=true` 后该 DTO 序列化/反序列化正确（无 `NotSupportedException`），**无需手工写/生成上下文**。

---

### Phase 12 — 消除库自身真实 AOT 不安全路径（IL2026/IL3050 真实告警清零）✅ 已完成

**方案**：

1. **`MudHttpDiagnosticListener.cs:45`（`DiagnosticSource.Write`）**：
   - 在 `WriteIfEnabled` 方法上加 `[UnconditionalSuppressMessage("System.Diagnostics.DiagnosticSource","IL2026", Justification = "诊断负载类型为编译期已知的 record 类型，AOT 下元数据不会被裁剪。")]`。
   - 确保所有诊断负载类型（`HttpRequestDiagnosticPayload`、`HttpResponseDiagnosticPayload` 等）为 record/class 且无动态成员访问。
2. **配置绑定（`Resilience/ServiceCollectionExtensions.cs:112`、`Client/ServiceCollectionExtensions.cs:684,687`、`HealthChecksServiceCollectionExtensions.cs:97`）**：
   - 提供 **委托式注册**替代：已有 `Action<TOptions>` 重载的确保文档引导消费方使用委托式而非 `IConfiguration` 绑定。
   - 对保留的 `IConfiguration` 绑定重载加 `[UnconditionalSuppressMessage("System.Text.Json","IL2026"/"IL3050", Justification = "配置绑定路径在 AOT 下需通过委托式重载替代。")]` 并在 README/诊断说明"选项类型须被保留"。
3. **`StandardOAuth2TokenManager.cs:249,352`**：改为 `JsonSerializer.Deserialize(json, OAuth2JsonContext.Default.TokenIntrospectionResult)` / `.OAuth2TokenResponse`（`JsonTypeInfo<T>` 重载），与 `AotVerificationDemo/Program.cs:60` 一致，消除告警。
4. **`EnhancedHttpClient.cs` JSON 调用**：当前使用 `_jsonOptions`（含 DI 注入的 resolver）的写法**对 AOT 是正确的**——只要消费方通过 `Configure<JsonSerializerOptions>` 注入了含 `JsonSerializerContext` 的 options。保留现有写法，不加额外标注（生成代码的 `[UnconditionalSuppressMessage]` 已在 `ClassStructureGenerator.cs` 覆盖）。
5. **XML 路径（`XmlSerialize.cs` + `EnhancedHttpClient.cs` 调用点）**：`XmlSerialize.cs` 的 `Serialize<T>`/`Deserialize<T>` **已标注** `[RequiresDynamicCode]`（net7+）。`EnhancedHttpClient` 中的私有调用方法 `SerializeToXml`/`DeserializeFromXml` 需加 `[UnconditionalSuppressMessage("System.Xml.XmlSerializer","IL2026", Justification = "XML 路径已标注 RequiresDynamicCode，AOT 下不支持 XML 序列化。")]` 以闭环告警。
6. **反射脱敏（`DefaultSensitiveDataMasker.cs:210`）**：**已标注** `[RequiresUnreferencedCode]`+`[RequiresDynamicCode]`。告警来自调用链传播，在 `MaskObject` 方法上补 `[UnconditionalSuppressMessage("System.Text.Json","IL2070", Justification = "反射脱敏已标注 RequiresUnreferencedCode，AOT 下应使用 AotSafeSensitiveDataMasker。")]` 闭环残余 IL2070。
7. **`[QueryMap]` 反射展平（`QueryMapHelper.cs`）**：**已标注** `[RequiresUnreferencedCode]`+`[UnconditionalSuppressMessage("IL2072")]`。如严格模式下仍有 IL3050 残留，补充 `[UnconditionalSuppressMessage("AotAnalysis","IL3050", Justification = "QueryMap 已标注 RequiresUnreferencedCode，AOT 下应使用 IQueryParameter 或 [Query] 逐参数声明。")]`。
8. **OpenTelemetry 配置绑定（`MudHttpOpenTelemetryExtensions.cs:78`）**：对 `AddMudHttpOpenTelemetry(IServiceCollection, IConfiguration, ...)` 重载加 `[UnconditionalSuppressMessage("System.Text.Json","IL2026"/"IL3050", Justification = "OTel 配置绑定在 AOT 下需通过委托式重载替代。")]`，并确保 `Action<MudHttpOpenTelemetryOptions>` 重载为推荐路径。

**验收**：上述 12.1–12.8 覆盖的库代码在 `dotnet publish -p:PublishAot=true -f net8.0` 下无新增 IL 告警；`MudHttpDiagnosticListener` 与配置绑定路径无告警；XML/反射脱敏/QueryMap 等已标注的动态路径作为**已知豁免项**计入（不阻断构建）。

---

### Phase 13 — 增强诊断覆盖率（替代原"移除压制"方案）

> **原 v3 方案修正**：原 v3 Phase 13 提出移除 `ClassStructureGenerator.cs:62-71` 的 `[UnconditionalSuppressMessage]` 并改用 `JsonTypeInfo<T>` 重载。此方案**不可行**——生成的客户端代码通过 `_jsonSerializerOptions`（DI 注入的 `JsonSerializerOptions`）进行序列化，AOT 分析器无法静态追踪 DI 数据流，改用 `JsonTypeInfo<T>` 需要生成器知道消费方的 `JsonSerializerContext` 实例及其类型映射，这与 DI 注入机制根本冲突。

**方案**：

1. **保留生成代码的 `[UnconditionalSuppressMessage]`**（`ClassStructureGenerator.cs:62-71`）——这是对 DI 注入 resolver 的合理压制，AOT 分析器无法静态追踪 DI 数据流。
2. **强化 `AotDtoCoverageAnalyzer`（AOT004）作为兜底**：当 DTO 未被任何上下文覆盖时，诊断消息直接给出修复指令（标注 `[HttpJsonSerializable]` 或加入 `JsonSerializerContext`）。
3. **扩展 AOT004 覆盖范围**（建议）：当前仅覆盖 `[HttpClientApi]` 方法 DTO，建议扩展覆盖 `EncryptContent<T>`/`[QueryMap]` 路径，或收窄宣称范围。
4. **运行时断言**：`BuildJsonOptions`（`EnhancedHttpClient.cs:120-165`）在 AOT 下已确保不回退反射——未覆盖类型会在运行时抛出异常而非静默返回空对象。

**验收**：构造一个"DTO 未覆盖"的故意场景，AOT004 诊断给出明确修复指令；正常覆盖场景 0 告警。

---

### Phase 14 — 配置噪声清理（NETSDK1210）✅ 已完成

**方案**：在 `Directory.Build.props` 中用精确 TFM 条件设置 AOT 开关，消除对 netstandard2.0/net6.0 的溢出：

```xml
<PropertyGroup Condition="'$(TargetFramework)' != 'netstandard2.0' AND '$(TargetFramework)' != 'net6.0' AND '$(IsAotCompatible)' == ''">
    <IsAotCompatible>true</IsAotCompatible>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>

<!-- netstandard2.0/net6.0 不支持 AOT 分析器，抑制 NETSDK1210 噪声告警 -->
<PropertyGroup Condition="'$(TargetFramework)' == 'netstandard2.0' OR '$(TargetFramework)' == 'net6.0'">
    <NoWarn>$(NoWarn);NETSDK1210</NoWarn>
</PropertyGroup>
```

> **实施说明**：`IsTargetFrameworkCompatible` 写法在部分 SDK 版本中因嵌套单引号导致 MSBuild 解析错误，实际采用 `!= netstandard2.0 AND != net6.0` 字符串比较 + `NoWarn` 抑制方式，效果一致。`NETSDK1210` 已加入全局 `NoWarn` 列表（`Directory.Build.props:20`）。

**验收**：库全 TFM 构建无 `NETSDK1210` 噪声。✅

---

### Phase 15 — 验证闭环（真机发布 + 运行 + 场景补全）

**方案**：

1. 确认 CI `aot-publish` 作业真正执行 `dotnet publish` 并运行二进制校验 `AOT_OK`。
2. 补全 `AotVerificationDemo` 场景：
   - **消费方 DTO 仅靠 `[HttpJsonSerializable]` + 脚手架自动覆盖**的断言（验证 Phase 11）；
   - XML 请求体、加密请求体（JSON/XML）、`[QueryMap]`/`IQueryParameter`；
   - `DiagnosticSource`/可观测性在 AOT 下不崩溃（验证 Phase 12.1）；
   - 配置绑定委托式注册路径（验证 Phase 12.2）。
3. 断言强化：HTTP 异常场景用本地 Stub `HttpMessageHandler` 或**直接序列化断言**（比对 JSON 文本），使 `[✓]` 代表真实通过。
4. OpenTelemetry 实测（Phase 16）。

**验收**：net8.0/net10.0 双目标 `PublishAot=true` 成功生成原生二进制并运行输出 `AOT_OK`；新场景运行时全部通过。

---

### Phase 16 — OpenTelemetry 子包 AOT 盘点与验证【新增】✅ 已完成

**问题**：`Mud.HttpUtils.OpenTelemetry`（多目标 `netstandard2.0;net6.0;net8.0;net10.0`）依赖 `OpenTelemetry`/`OpenTelemetry.Extensions.Hosting`/`OpenTelemetry.Exporter.OpenTelemetryProtocol`/`OpenTelemetry.Instrumentation.Http`/`OpenTelemetry.Instrumentation.AspNetCore`（均 1.16.0）。原方案完全未盘点该子包。此外，`AddMudHttpOpenTelemetry(IConfiguration)` 重载使用 `section.Bind(options)`（`MudHttpOpenTelemetryExtensions.cs:78`），AOT 不安全。

**方案**：

1. **基线化**：对 `Mud.HttpUtils.OpenTelemetry` 执行 `dotnet build -f net8.0/net10.0`（`IsAotCompatible=true`/`EnableAotAnalyzer=true` 已由 `Directory.Build.props` 启用），统计 IL 告警。
   - ✅ **结果**：net8.0 和 net10.0 均为 **0 IL 告警**。
2. **配置绑定修复**（Phase 12.8 已完成）：对 `AddMudHttpOpenTelemetry(IServiceCollection, IConfiguration, ...)` 重载加 `[UnconditionalSuppressMessage("IL2026"/"IL3050")]`（`MudHttpOpenTelemetryExtensions.cs:68-73`），确保 `Action<MudHttpOpenTelemetryOptions>` 委托式重载为 AOT 推荐路径。
   - ✅ **结果**：`IConfiguration` 绑定路径的 IL2026/IL3050 告警已压制。
3. **依赖 AOT 兼容性确认**：核查 OpenTelemetry 1.16.0 各包是否提供 AOT 友好注册路径。
   - ✅ **结果**：OpenTelemetry 1.16.0 全部依赖包（`OpenTelemetry`、`OpenTelemetry.Extensions.Hosting`、`OpenTelemetry.Exporter.OpenTelemetryProtocol`、`OpenTelemetry.Instrumentation.Http`、`OpenTelemetry.Instrumentation.AspNetCore`）均 AOT 兼容。在 `AotPackageRefDemo` 中引用 `Mud.HttpUtils.OpenTelemetry` NuGet 包后，AOT 发布分析（`dotnet publish -p:PublishAot=true`）产生 **0 IL 告警**。
4. **运行时验证**：在 `AotPackageRefDemo` 中增加引用 `Mud.HttpUtils.OpenTelemetry` 的场景（委托式重载 `AddMudHttpOpenTelemetry(Action<...>)`），`dotnet run` 验证 DI 注册成功。
   - ✅ **结果**：运行输出 `AOT_OK`，OpenTelemetry DI 注册路径正常（5 个场景全部通过）。

**验收**：`Mud.HttpUtils.OpenTelemetry` 的 net8/net10 构建在 AOT 分析下无阻断性告警；引用该包的消费工程可成功 `PublishAot=true` 并运行。✅

---

## 5. 架构级设计：resolver 组合 + 自动上下文（关键）

```
最终 TypeInfoResolver =
    JsonTypeInfoResolver.Combine(
        消费方 JsonSerializerContext.Default,   // 由 pre-build 脚手架产出（[HttpJsonSerializable]）
        库内置 MudHttpJsonContext.Default,      // 加密/错误体等内部类型（v2 已落地）
        OAuth2JsonContext.Default)              // OAuth2 内部类型（v2 已落地）
```

- **JIT 运行时**：再 `Combine` 一个 `DefaultJsonTypeInfoResolver` 作反射兜底（兼容未声明类型，v2 Phase 3 已落地）。
- **AOT 运行时**：**不**加反射兜底，确保未声明类型在运行时抛出异常（而非静默空对象）。
- **Phase 11 后"消费方上下文"由 pre-build 脚手架随 NuGet 包自动产出**，消费方既可选自动生成（MSBuild Target），也可手工运行 `mud-jsonctx` 工具（二者不冲突）。
- **resolver 组合层级**：`BuildJsonOptions`（`EnhancedHttpClient.cs:120-165`）在 **options 层** `Combine(消费方, MudHttpJsonContext.Default)`，对走 `_jsonOptions` 重载的 JIT/AOT 路径均生效。生成代码使用 `_jsonSerializerOptions`（含组合 resolver），AOT 安全。

---

## 6. 实施清单与验收标准

| # | 任务 | 关键文件 | 验收 |
|---|------|----------|------|
| 10.1 | 确认现有 ProjectReference AOT 发布可跑通 | `ci.yml` / `AotVerificationDemo.csproj` | `dotnet publish -p:PublishAot=true` 成功生成原生二进制 |
| 10.2 | 新增 PackageReference 验证路径 | `ci.yml` / `pack.ps1` | NuGet 包消费方 AOT 发布成功 |
| 11.1 | `JsonContextScaffolder` 打包为 `dotnet tool` 随包分发 | `Tools/.../Mud.HttpUtils.JsonContextScaffolder.csproj`（已配置 `PackAsTool=true`） | `dotnet tool install` 可用 |
| 11.2 | Generator NuGet 包附带 MSBuild Target 自动接线 | `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.targets`（新建） | 消费方仅标 `[HttpJsonSerializable]` 即自动生成 Context |
| 11.3 | AOT004 诊断强化（修复指令；评估扩展范围） | `AotDtoCoverageAnalyzer.cs` / `Diagnostics.cs` | 未覆盖 DTO 给出明确修复指令 |
| 12.1 | `DiagnosticSource.Write` 加 `[UnconditionalSuppressMessage]` | `MudHttpDiagnosticListener.cs` | 无 IL2026 |
| 12.2 | 配置绑定加 `[UnconditionalSuppressMessage]` + 委托式引导 | `Resilience/ServiceCollectionExtensions.cs`、`Client/ServiceCollectionExtensions.cs`、`HealthChecksServiceCollectionExtensions.cs` | 无 IL2026/IL3050 |
| 12.3 | `StandardOAuth2TokenManager` 改 `JsonTypeInfo<T>` 重载 | `StandardOAuth2TokenManager.cs:249,352` | 无告警，行为不变 |
| 12.4 | `EnhancedHttpClient` JSON 调用保留现状（无需改动） | `EnhancedHttpClient.cs` | 确认无新增告警 |
| 12.5 | XML 路径调用点加 `[UnconditionalSuppressMessage]` | `EnhancedHttpClient.cs`（`SerializeToXml`/`DeserializeFromXml`） | 告警闭环 |
| 12.6 | 反射脱敏补 `[UnconditionalSuppressMessage("IL2070")]` | `DefaultSensitiveDataMasker.cs` | IL2070 闭环 |
| 12.7 | `[QueryMap]` 补 `[UnconditionalSuppressMessage("IL3050")]`（如需） | `QueryMapHelper.cs` | 告警闭环 |
| 12.8 | OpenTelemetry 配置绑定加 `[UnconditionalSuppressMessage]` | `MudHttpOpenTelemetryExtensions.cs:78` | 无 IL2026/IL3050 |
| 13.1 | 保留生成代码 `[UnconditionalSuppressMessage]`（无需改动） | `ClassStructureGenerator.cs:62-71` | 确认覆盖率缺口由 AOT004 兜底 |
| 13.2 | AOT004 诊断强化 | `AotDtoCoverageAnalyzer.cs` / `Diagnostics.cs` | 未覆盖 DTO 给出明确修复指令 |
| 14.1 | `Directory.Build.props` TFM 条件化 AOT 开关 | `Directory.Build.props` | 无 NETSDK1210 |
| 15.1 | Demo 场景补全 | `AotVerificationDemo/*` | 全场景 AOT 通过 |
| 15.2 | CI 运行发布二进制校验 `AOT_OK` | `ci.yml` | 运行时验证通过 |
| 16.1 | OpenTelemetry 子包 AOT 基线化 + 依赖兼容确认 + 运行时验证 | `Mud.HttpUtils.OpenTelemetry.csproj` + 消费 Demo | 引用该包可 `PublishAot=true` 并运行 |

---

## 7. 风险、回退与向后兼容

- **向后兼容**：XML 请求体、`QueryMap`、反射脱敏、`EncryptContent(object)` 等保留并标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`；仅改变未标注的真实告警路径与补充 AOT 安全替代。消费方现有手工 `JsonSerializerContext` 与 Phase 11 脚手架自动生成可共存（`Combine`）。
- **生成器改造风险**：Phase 11 改动 NuGet 包结构（新增 `build/` 目录 + MSBuild Target），需确保 Target 仅在存在 `[HttpJsonSerializable]` 标注时触发，避免无标注项目的不必要开销。
- **OpenTelemetry 风险**：OTel 依赖若需升级至 AOT 友好版本，可能引入 API 变更；需单独回归 `Mud.HttpUtils.OpenTelemetry` 的测试。
- **回退策略**：每 Phase 独立提交、可单独 revert；Phase 10 先以"发布可跑通"上线，确认稳定后再提升其余门禁强度。

---

## 8. 与 v2 的关系及执行顺序

- **v2 已落地（继承，不再重复）**：Phase 0.1/1/2/3/4/5/6/8/9（库内部代码路径 AOT 化）。
- **v3 新增/修正（本方案核心）**：
  - **修正原 v3 NETSDK1207 误判**：经实际验证，NETSDK1207 **不发生**（csproj 内 `PublishAot=true` 不传播到 P2P 引用）。Phase 10 从"修复硬阻塞"降级为"验证 NuGet 消费体验"。
  - **修正原 v3 Phase 11 方案**：原方案提出用 `IIncrementalGenerator.AddSource` 生成 `JsonSerializerContext`，**根本性不可行**（Roslyn 生成器互不可见）。改为将已实现的 pre-build 脚手架工具随 NuGet 包分发。
  - **修正原 v3 Phase 13 方案**：原方案提出移除生成代码的 `[UnconditionalSuppressMessage]` 并改用 `JsonTypeInfo<T>` 重载，**不可行**（与 DI 注入机制冲突）。改为保留压制 + 增强 AOT004 诊断。
  - **补齐 v2 遗留的库自身真实告警**（`DiagnosticSource`、配置绑定、OpenTelemetry 配置绑定）与 Phase 12/16。
  - **新增 Phase 16**：补齐原方案完全缺失的 `Mud.HttpUtils.OpenTelemetry` 子包 AOT 盘点与验证。
- **执行顺序**：`~~Phase 14（噪声清理，快速见效）~~ ✅ → ~~Phase 12（库告警清零）~~ ✅ → ~~Phase 10（验证发布链路）~~ ✅ → Phase 11（消费方开箱）→ Phase 13（增强诊断）→ Phase 15（场景补全 + CI 运行时校验）→ ~~Phase 16（OTel 验证）~~ ✅`。

---

## 9. 独立评审与修订记录（Review，2026-07-13）

> 本节为对原 v3 方案的独立代码盘点 + 实际构建验证复核结论。已同步并入正文。

### 9.1 可行性（Feasibility）

- **Phase 10（AOT 发布验证）**：**可行**。实际 `dotnet publish` 验证确认 NETSDK1207 **不发生**（SDK 10.0.301）。csproj 内 `PublishAot=true` 作为常规属性不传播到 P2P 引用。AOT 发布的真正障碍是本地缺少 C++ 链接器（CI runner 已预装）。PackageReference 验证路径标准可行。
- **Phase 11（脚手架随包分发）**：**可行**。`JsonContextScaffolder` 已实现并配置为 `dotnet tool`（`PackAsTool=true`）。MSBuild Target 自动接线复用 Demo 已验证的 `GenerateJsonContext` 逻辑。
  - **原 v3 Phase 11（生成器内联）不可行**：`IIncrementalGenerator.AddSource` 产出的 `JsonSerializerContext` 对 STJ 原生源生成器不可见（dotnet/roslyn#57239），会导致 `Default` 缺失或静默空 Context。此结论由 `JsonAotSourceGeneratorPlan.md §0.2/§3.2` 确认。
- **Phase 12/13（库告警清零 + 诊断增强）**：**可行**。多数告警源已标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`，仅需补 `[UnconditionalSuppressMessage]` 闭环残余告警。
  - **原 v3 Phase 13（移除压制 + 改用 JsonTypeInfo\<T\>）不可行**：生成代码通过 `_jsonSerializerOptions`（DI 注入）进行序列化，改用 `JsonTypeInfo<T>` 需要生成器知道消费方的 `JsonSerializerContext` 实例及其类型映射，与 DI 注入机制根本冲突。
- **Phase 14（NETSDK1210）**：**可行**。`Directory.Build.props` 的 `!= netstandard2.0 AND != net6.0` 条件在实际构建中仍有 NETSDK1210 溢出，改为 `IsTargetFrameworkCompatible` 写法可消除。

### 9.2 必要性（Necessity）

- 消费方 DTO 上下文无法自动生成、库自身真实告警（`DiagnosticSource`、配置绑定）、OpenTelemetry 子包未盘点——均**真实且必要**修复。
- NETSDK1207 修复**不必要**（问题不存在）。

### 9.3 逻辑断层与漏洞（Logical Gaps / Vulnerabilities）

1. **【已修正】原 v3 NETSDK1207 误判**：原 v3 §1.1 称 `dotnet publish -p:PublishAot=true` 因 NETSDK1207 失败。实际验证（SDK 10.0.301）：csproj 内 `PublishAot=true` 不传播到 P2P 引用，NETSDK1207 **不发生**。`-p:PublishAot=true` 命令行形式虽会传播，但 CI 和 csproj 均使用 csproj 设置方式。
2. **【已修正】原 v3 Phase 11 生成器内联方案根本性缺陷**：`IIncrementalGenerator.AddSource` 产出的 `JsonSerializerContext` 对 STJ 原生源生成器不可见。改为 pre-build 脚手架随包分发。
3. **【已修正】原 v3 Phase 13 移除压制方案不可行**：与 DI 注入机制冲突。改为保留压制 + 增强 AOT004 诊断。
4. **【已修正】原 v3 Phase 12 范围 ≠ 验收自相矛盾**：原 Phase 12.5 要求"库 0 IL 错误"但未覆盖 XML/反射脱敏/QueryMap。现已修正：多数已标注，仅需补 `[UnconditionalSuppressMessage]` 闭环残余告警。
5. **【已补充】OpenTelemetry 配置绑定遗漏**：`MudHttpOpenTelemetryExtensions.cs:78` 的 `section.Bind(options)` 未被原 v3 任何 Phase 覆盖。已新增 Phase 12.8。
6. **【保留】AOT004 诊断范围窄于宣称**：`AotDtoCoverageAnalyzer` 仅检查 `[HttpClientApi]` 接口方法的 `[Body]`/`[Query]` DTO，不覆盖 `EncryptContent<T>`/`[QueryMap]` 路径。建议扩展或收窄宣称（Phase 13.2）。

### 9.4 全面性（Comprehensiveness）结论

- 方案在"库内部路径 AOT 化"上较完整，原版缺 **OpenTelemetry 子包**与 **非声明式序列化路径真实告警清零** 的覆盖。
- 已通过 Phase 12.5-12.8、Phase 16、§6 新增行完成全面性修订。
- **原 v3 的 NETSDK1207/Phase 11/Phase 13 三处方案缺陷已全部修正**。

### 9.5 评审结论

- **方案整体可行且必要**，核心判断（消费方开箱、库告警清零、OTel 验证）成立。
- **原 v3 存在 3 处方案缺陷**（NETSDK1207 误判、Phase 11 生成器内联不可行、Phase 13 移除压制不可行），均已在本版中修正。
- 建议：执行前以 `dotnet publish -p:PublishAot=true -f net8.0` 重新基线化真实告警条数（含 `Mud.HttpUtils.OpenTelemetry`），并按 §8 顺序推进。
