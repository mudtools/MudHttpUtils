# Mud.HttpUtils.JsonContextScaffolder

扫描实体/DTO 上标注的 `[HttpJsonSerializable]` 以及 `[HttpClientApi]` 接口的方法返回类型和 `[Body]` 参数类型，自动产出 `JsonSerializerContext` 源文件，使 STJ 源生成在 Native AOT 下获得类型元数据。面向**消费方（第三方开发者）**分发，以 `dotnet tool` 形式提供。

## 安装（已发布到 NuGet）

```bash
dotnet tool install --global Mud.HttpUtils.JsonContextScaffolder
```

或作为本地工具：

```bash
dotnet new tool-manifest
dotnet tool install Mud.HttpUtils.JsonContextScaffolder
```

> 仓库贡献者在发布前也可直接运行源码（无需安装）：
> `dotnet run --project Tools/Mud.HttpUtils.JsonContextScaffolder -- --project <你的.csproj>`

## 使用流程

1. 在实体/DTO 上标注 `[HttpJsonSerializable]`：

```csharp
[HttpJsonSerializable(SerializerClassName = "FeishuAI", NamingPolicy = JsonNamingPolicyHint.SnakeCaseLower)]
public class ContractFileUploadRequest { ... }
```

2. 运行脚手架，扫描并生成 `XxxJsonContext.g.cs`：

```bash
mud-jsonctx --project src/MyApp.DataModels/MyApp.DataModels.csproj
# 指定输出目录
mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj -o src/MyApp.DataModels/Generated
# 仅预览不写入
mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj --dry-run
# 自动补全多态派生类（见下）
mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj --auto-derived-types
# 禁用 [HttpClientApi] 接口扫描（默认开启）
mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj --no-scan-http-client-api
```

3. 将生成的 `.g.cs` 提交到版本控制（仅在新增/变更标注时重跑）。

4. 在启动时注册 Context（仅 .NET 8+）：

```csharp
#if NET8_0_OR_GREATER
services.AddMudHttpClientJsonContext(FeishuAIJsonContext.Default);
#endif
```

库内置 `BuildJsonOptions` 会自动把消费方 resolver 与库内置 `MudHttpJsonContext.Default` 合并。

## [HttpClientApi] 接口自动扫描（默认开启）

除了扫描 `[HttpJsonSerializable]` 标注的实体/DTO 外，脚手架还会自动扫描 `[HttpClientApi]` 接口，提取方法返回类型和 `[Body]` 参数类型中的**闭合泛型**（如 `FeishuApiResult<T>`）并注册到独立的 Context 文件中。

### 为什么需要这个功能？

像 `FeishuApiResult<T>` 这类泛型包装类型无法直接标注 `[HttpJsonSerializable]`（因为它们是构造类型，不是类型定义），但在 AOT 下又必须注册闭合泛型才能获得类型元数据。手动维护这些闭合泛型注册既繁琐又容易遗漏。

### 扫描规则

- 扫描所有标注了 `[HttpClientApi]` 的接口
- 解包 `Task<T>` / `ValueTask<T>` 返回类型，提取内层类型
- 提取标注了 `[Body]` 的参数类型
- **闭合泛型**（如 `FeishuApiResult<X>`）：始终注册，并递归处理类型参数
- **非泛型自定义类型**：仅当来自当前程序集且未标注 `[HttpJsonSerializable]` 时注册
- **框架类型**（`System.*` 命名空间、基元类型）：跳过
- **已标注 `[HttpJsonSerializable]` 的类型**：不重复注册

### 生成的 Context

发现类型会生成到独立的 Context 文件中，命名规则为 `{程序集简称}HttpClientApiJsonContext`。例如对 `Mud.Feishu.csproj` 扫描会生成 `FeishuHttpClientApiJsonContext.g.cs`。

### 示例

```csharp
// 扫描前：需手写闭合泛型注册
[JsonSerializable(typeof(FeishuApiResult<GetUserDataResult>))]
[JsonSerializable(typeof(FeishuApiResult<WsEndpointResult>))]
// ... 数百个手动注册
internal partial class FeishuApiResultJsonContext : JsonSerializerContext { }

// 扫描后：自动生成 FeishuHttpClientApiJsonContext.g.cs，包含所有闭合泛型
// 无需手写，运行 mud-jsonctx 即可
```

### 禁用扫描

如需禁用此功能（例如项目不使用 `[HttpClientApi]` 或只需扫描标注类型）：

```bash
mud-jsonctx --project src/MyApp/MyApp.csproj --no-scan-http-client-api
```

## 重要限制与手写补充

- **框架泛型包装无法标注**：如 `List<UserDto>` 这类不是你定义的、或本身为开放/框架泛型包装的根类型，不能（也不应）打 `[HttpJsonSerializable]`。需手写一个 partial 补充（类名须与脚手架生成的 Context 类名一致）：

```csharp
#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
namespace MyApp.DataModels;
[JsonSerializable(typeof(List<UserDto>))]
internal partial class FeishuAIJsonContext;
#endif
```

- **闭合泛型自动发现**：`[HttpClientApi]` 接口返回类型中的闭合泛型（如 `FeishuApiResult<T>`）由 `[HttpClientApi]` 扫描自动发现并注册，无需手写。
- **多态（基类反序列化派生类）**：默认仅检查直接基类。使用 `--auto-derived-types` 可递归扫描并生成**完整派生层级**（Base → Mid → Leaf）。基于接口的 polymorphism 仍需手动标注 `[JsonDerivedType]`。
- **开放泛型**：`<T>` 类型以 `<>` 写入 context，仅在 NET8_0_OR_GREATER 下源生成；更低 TFM 走反射兜底，AOT 不可用（见 AOT002 警告）。
- 生成的 Context 为 `internal partial`，仅作用于标注类型所在的同一程序集；跨程序集需各自生成。

## 分组与命名规则

- `SerializerClassName` 相同的类型合并到同一 Context；留空时自动派生 `{程序集简称}{顶层命名空间}`。
- 未显式指定 `NamingPolicy` 时自动推导：超过 50% 的属性 `[JsonPropertyName]` 符合 `snake_case_lower` → `SnakeCaseLower`，否则 `CamelCase`（与库默认一致）。
- 同一 Context 内共享一个命名策略，冲突会报 AOT001 警告。

## 诊断（AOT001-AOT006）

- **AOT001**：同一 `SerializerClassName` 下存在冲突的 `NamingPolicy` 配置。
- **AOT002**：标注了开放泛型类型（低版本 TFM 不支持源生成，AOT 不可用）。
- **AOT003**：类型存在基类（多态）但未标注 `[JsonDerivedType]`（未启用 `--auto-derived-types` 时报告）。
- **AOT004**：`[HttpClientApi]` 接口扫描信息——当扫描发现类型并自动注册时，以 Info 级别报告发现数量和目标 Context。
- **AOT005**：`[Query]`/`[QueryMap]` 中以 JSON 序列化的复杂参数类型未被 `JsonSerializerContext` 覆盖。
- **AOT006**：类型标注了 `[HttpJsonSerializable]` 却未被任何已引用的 `JsonSerializerContext` 覆盖。**这正是“脚手架未运行 / 未接入构建”的编译期信号**——一旦出现即说明标注的实体没有对应生成的 Context，AOT 下会漏元数据。

> AOT004–AOT006 默认以 **Warning** 形式提示（不阻断生成）。在 CI 严格模式下（`-p:AotStrictMode=true`，见 `AotVerificationDemo`/`AotPackageRefDemo` 的 `WarningsAsErrors`）会升级为 **Error**，从而强制消费方构建必须接入脚手架或手写 Context。

## 自动接入构建（可选 MSBuild 目标）

手动跑工具并提交 `.g.cs` 适合一次性生成；若希望脚手架随消费方构建**自动**运行（避免遗忘），Mud.HttpUtils 已内置一个**默认关闭**的可选 MSBuild 目标，随 `Mud.HttpUtils.Attributes` 包自动导入消费方工程。

1. 安装脚手架工具（三选一）：

```bash
# a) 全局
dotnet tool install --global Mud.HttpUtils.JsonContextScaffolder
# b) 本地工具（需先 dotnet new tool-manifest）
dotnet tool install Mud.HttpUtils.JsonContextScaffolder
# c) .NET 8+ SDK 项目引用（推荐，可复现）
dotnet add package Mud.HttpUtils.JsonContextScaffolder   # 仅用于 DotNetToolReference 解析
# 然后在 .csproj 中：<DotNetToolReference Include="Mud.HttpUtils.JsonContextScaffolder" Version="x.y.z" />
```

2. 在标注了 `[HttpJsonSerializable]` 的实体项目 `.csproj` 中开启：

```xml
<PropertyGroup>
  <MudEnableJsonContextScaffolder>true</MudEnableJsonContextScaffolder>
  <!-- 可选：自定义生成输出目录，默认 obj/<tfm>/GeneratedJsonContext -->
  <!-- <MudJsonContextOutputPath>$(IntermediateOutputPath)GeneratedJsonContext</MudJsonContextOutputPath> -->
</PropertyGroup>
```

开启后，`BeforeCompile` 阶段会自动执行 `mud-jsonctx --project <本项目> -o <输出> --auto-derived-types` 并将生成的 `*.g.cs` 纳入编译。工具未安装时仅输出**警告**（不中断构建），你可改回手动运行并签入生成结果。

> 该目标默认不启用（`MudEnableJsonContextScaffolder=false`），对未选择该工作流的消费方**零影响**。
