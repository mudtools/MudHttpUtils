# Mud.HttpUtils.CodeFixes

## 概述

`Mud.HttpUtils.CodeFixes` 是 Mud.HttpUtils 的 **IDE 代码修复器（Code Fix）包**，与源生成器/分析器（`Mud.HttpUtils.Generator`）解耦，仅按诊断 ID 匹配，为编译期诊断提供"一键修复"（灯泡操作）。

本包为 **Roslyn 组件**（不输出运行时程序集，`IncludeBuildOutput=false`），随 `Mud.HttpUtils.Generator` 一同被消费方引用，无需单独安装。

## 目标框架

- `netstandard2.0`

## 包含的代码修复器

| 修复器 | 可修复诊断 | 触发条件 | 修复动作 |
| ------ | ---------- | -------- | -------- |
| `AotJsonContextCodeFixProvider` | `AOT004` / `AOT005` / `AOT006` | DTO / 查询参数类型未被 `JsonSerializerContext` 覆盖（Native AOT 下会漏元数据） | 将缺失类型追加 `[JsonSerializable(typeof(T))]` 到用户可编辑的 `JsonSerializerContext`；若仅存在脚手架生成文件，则新建 `partial` 扩展类 |
| `AotXmlCodeFixProvider` | `AOT007` | Native AOT 上下文下使用 XML 序列化 | 将 `[SerializationMethod(SerializationMethod.Xml)]` 改为 `Json`，确保 AOT 兼容 |
| `HttpClientMutuallyExclusiveCodeFixProvider` | `HTTPCLIENT007` | `[HttpClientApi]` 同时指定 `HttpClient` 与 `TokenManage`（两者互斥） | 提供两个选项：移除 `HttpClient`（保留 `TokenManage`）或移除 `TokenManage`（保留 `HttpClient`） |
| `HttpClientInvalidUrlTemplateCodeFixProvider` | `HTTPCLIENT005` | URL 模板格式无效（常见为误用反斜杠，如 `\api\users`） | 将 URL 中的反斜杠（`\`）自动替换为正斜杠（`/`） |

## 使用方式

无需任何代码配置。在 Visual Studio / VS Code（含 C# Dev Kit）中，当源生成器报告上述诊断时，点击诊断旁的灯泡（或使用 `Ctrl + .` / `Quick Actions`）即可看到对应修复建议：

- `AOT004`/`AOT005`/`AOT006`：选择"将类型添加到 JsonSerializerContext（AOT 兼容）"，自动补齐 JSON 源生成上下文。
- `AOT007`：选择"将 XML 序列化改为 JSON（AOT 兼容）"。
- `HTTPCLIENT007`：选择移除 `HttpClient` 或 `TokenManage` 二选一。
- `HTTPCLIENT005`：选择"将 URL 反斜杠替换为正斜杠"。

> 修复器与诊断源完全解耦：只要诊断 ID 匹配，即使诊断来自其他扩展也会尝试修复。所有修复器均支持 `Fix All`（批量修复）操作。

## 诊断 ID 速查

| 诊断 ID | 严重级别 | 来源 | 可自动修复 |
|---------|----------|------|------------|
| `AOT004` | Warning | `Mud.HttpUtils.Generator`（`AotDtoCoverageAnalyzer`） | 是 |
| `AOT005` | Warning | `Mud.HttpUtils.Generator`（`AotDtoCoverageAnalyzer`） | 是 |
| `AOT006` | Warning | `Mud.HttpUtils.Generator`（`AotDtoCoverageAnalyzer`） | 是 |
| `AOT007` | Error | `Mud.HttpUtils.Generator`（`AotXmlRejectionAnalyzer`，仅 AOT 上下文） | 是 |
| `HTTPCLIENT005` | Error | `Mud.HttpUtils.Generator` | 是 |
| `HTTPCLIENT007` | Error | `Mud.HttpUtils.Generator` | 是 |

> 完整诊断说明见 [`Mud.HttpUtils.Generator` 文档](../Mud.HttpUtils.Generator/README.md#编译诊断)。

## 依赖项

| 包 | 说明 |
|----|------|
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | Roslyn Workspaces，提供代码修复基础设施 |
| `Microsoft.CodeAnalysis.Analyzers` | Roslyn 分析器 SDK |

## 相关项目

- [Mud.HttpUtils.Generator](../Mud.HttpUtils.Generator/) - 源生成器与诊断来源
- [Mud.HttpUtils.JsonContextScaffolder](../Tools/Mud.HttpUtils.JsonContextScaffolder/) - AOT JSON 上下文脚手架工具
