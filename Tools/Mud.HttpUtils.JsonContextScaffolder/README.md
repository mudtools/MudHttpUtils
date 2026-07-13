# Mud.HttpUtils.JsonContextScaffolder

扫描 `[HttpJsonSerializable]` 标注类型，自动产出 `JsonSerializerContext` 源文件，支持 Native AOT。

## 安装

```bash
dotnet tool install --global Mud.HttpUtils.JsonContextScaffolder
```

或本地工具：

```bash
dotnet new tool-manifest
dotnet tool install Mud.HttpUtils.JsonContextScaffolder
```

## 用法

```bash
# 基本用法
mud-jsonctx --project src/MyApp.DataModels/MyApp.DataModels.csproj

# 指定输出目录
mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj -o src/MyApp.DataModels/Generated

# 预览（不写入文件）
mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj --dry-run
```

## 工作流程

1. 在实体/DTO 上标注 `[HttpJsonSerializable]`：

```csharp
[HttpJsonSerializable(SerializerClassName = "FeishuAI", NamingPolicy = JsonNamingPolicyHint.SnakeCaseLower)]
public class ContractFileUploadRequest { ... }
```

2. 运行 Scaffolder 生成 `JsonSerializerContext` 源文件：

```bash
mud-jsonctx --project src/MyApp.DataModels/MyApp.DataModels.csproj
```

3. 将生成的 `.g.cs` 文件提交到版本控制。

4. 在启动时注入 Context：

```csharp
#if NET8_0_OR_GREATER
services.AddMudHttpClientJsonContext(FeishuAIJsonContext.Default);
#endif
```

## 分组规则

- `SerializerClassName` 相同的类型合并到同一个 Context
- 留空时自动派生名称：`{程序集简称}{顶层命名空间}`
- 同一 Context 内共享命名策略

## 命名策略自动推导

未显式指定 `NamingPolicy` 时，Scaffolder 会检测实体上的 `[JsonPropertyName]` 模式：
- 超过 50% 的属性名符合 `snake_case_lower` → 自动选 `SnakeCaseLower`
- 否则默认 `CamelCase`（与库默认一致）
