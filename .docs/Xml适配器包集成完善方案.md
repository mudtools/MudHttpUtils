# Mud.HttpUtils.Xml 适配器包集成完善方案

> 依据：XmlContentSerializer 独立项目架构评审（2026-09-14）。
> 结论：`XmlContentSerializer` 保留独立项目承载，方向正确；但当前处于"半集成"状态，本方案补齐全部集成缺口。

## 一、评审结论回顾

| 结论 | 说明 |
|------|------|
| 独立成包 ✔ | 与 `Newtonsoft.Json` 适配器对称，服务于 `IHttpContentSerializer` 可选序列化引擎抽象 |
| AOT 契约隔离 ✔ | 全仓库 net8+ 强制 `IsAotCompatible=true`，而 `XmlSerializer` 本质非 AOT，必须隔离避免污染核心包 |
| netstandard2.0 隐性依赖 | 实测撤销：`System.Xml.XmlSerializer` 由 SDK 隐式引用集覆盖（含 netstandard2.0），显式声明反而触发 NU1510 |

## 二、问题清单与行动项

| # | 问题 | 位置 | 优先级 | 行动 |
|---|------|------|--------|------|
| 1 | 未加入解决方案 | `Mud.HttpUtils.slnx` | P0 | `/Src/` 添加项目 |
| 2 | 未加入打包清单（README 已挂 NuGet 徽章） | `pack.ps1` | P0 | `$Projects` 添加 `Mud.HttpUtils.Xml` |
| 3 | AOT/裁剪分析器未关闭；AOT 严格模式（`Directory.Build.targets` IL3050 清单）会将本项目编译为错误 | `Mud.HttpUtils.Xml.csproj` | P0 | `IsAotCompatible=false` + 关闭分析器 + `MudExcludeFromAotStrictMode=true`，附设计注释（仿 Newtonsoft） |
| 4 | netstandard2.0 依赖传递解析 | `Mud.HttpUtils.Xml.csproj` | ~~P0~~ 已撤销 | 实测 NU1510：`System.Xml.XmlSerializer` 由 SDK 隐式引用集覆盖（含 netstandard2.0），显式引用反而告警，**不声明** |
| 5 | README 仅一行标题，但根 README:745 直接链接 | `Mud.HttpUtils.Xml/README.md` | P1 | 补用途、DI 示例、AOT 限制声明、与生成客户端 XML 路径的定位澄清 |
| 6 | CHANGELOG 无记录 | `CHANGELOG.md` | P1 | Unreleased 增补条目 |
| 7 | 零测试覆盖 | `Tests/Mud.HttpUtils.Client.Tests` | P1 | 新增 `XmlContentSerializerTests`（不新建测试项目，2 个类型不值得独立工程） |

## 三、实施细节

### 3.1 csproj 修正（P0）

```xml
<!-- XmlSerializer 按设计即为非 AOT：运行时生成动态程序集，所有公共成员
     已标注 [RequiresDynamicCode]/[RequiresUnreferencedCode] 向消费方传递契约。
     本项目启用 AOT/裁剪分析器只会产生"已知且预期"的 IL3050/IL2026 噪声，故关闭。 -->
<PropertyGroup>
  <IsAotCompatible>false</IsAotCompatible>
  <EnableAotAnalyzer>false</EnableAotAnalyzer>
  <EnableTrimAnalyzer>false</EnableTrimAnalyzer>
  <MudExcludeFromAotStrictMode>true</MudExcludeFromAotStrictMode>
</PropertyGroup>
```

说明：`Directory.Build.props` 的条件 `$(IsAotCompatible) == ''` 在项目内置 false 后自然短路，无需修改全局文件。另实测 NU1510 确认 `System.Xml.XmlSerializer` 无需（也不应）显式引用。

## 三点五、执行中发现并修复的源码缺陷（评审遗漏）

1. **CS0234（net6.0 编译失败）**：`RequiresDynamicCodeAttribute` 为 .NET 7 引入，原源码 `#if NET6_0_OR_GREATER` 守卫错误 → 改为 `#if NET7_0_OR_GREATER` 标注 RDC，net6.0 仅保留 `[RequiresUnreferencedCode]`。
2. **XML 声明编码失真（真实库缺陷，被新增测试捕获）**：`XmlWriter.Create(StringWriter, settings)` 时声明编码由 TextWriter 决定，恒为 `utf-16`，而 `StringContent` 按 UTF-8 编码字节 → 声明与字节流不一致，`FromHttpContentAsync` 基于流的反序列化抛 `XmlException: There is no Unicode byte order mark`。
   修复：`ToHttpContent` 改为写入 `MemoryStream` 取字节后构造 `ByteArrayContent`（声明与字节均由 `WriterSettings.Encoding` 决定，避免双重编码）。
3. **BOM 问题（修复 2 的连带）**：`XmlWriter` 直接写字节流时按 `Encoding.UTF8`（emitBOM=true）输出 BOM，`Deserialize(string)` 的 TextReader 路径无法剥离 → 字符串输出路径改用 `EncodingAwareStringWriter`（`Encoding` 重载返回 `WriterSettings.Encoding`，无 BOM 且声明一致），不经过字节中转。

### 3.2 README 定位澄清（P1）

- `XmlContentSerializer` 服务于 `IHttpContentSerializer` DI 抽象（请求体序列化引擎替换）；
- 生成客户端的 XML 响应走 `ResponseDescriptor.XmlSerializer` 元数据直通路径（`ConstructorGenerator` 发射 `XmlSerializer` 字段）；
- 二者互补不替代；Native AOT 场景请用 `SystemTextJsonContentSerializer` + `JsonSerializerContext`。

### 3.3 测试用例（P1）

`XmlContentSerializerTests` 覆盖：序列化/反序列化往返、`ToHttpContent(null)` 返回 `null`、Content-Type 断言、`WriterSettings` 生效、`Serialize(object, Type)` 重载、`GetFieldNameForProperty` 解析 `[XmlElement(ElementName=...)]`。测试文件顶部 `#pragma warning disable IL2026, IL3050`（测试代码调用 RUC/RDC 成员的已知豁免）。

### 3.4 验证出口条件（P0）

```powershell
dotnet build Mud.HttpUtils.Xml -c Release          # 四个 TFM 全绿、无 IL3050/IL2026
dotnet build Mud.HttpUtils.slnx                     # 解决方案整体无回归
dotnet test Tests/Mud.HttpUtils.Client.Tests        # 新增用例通过
.\pack.ps1 Release                                  # 产出 Mud.HttpUtils.Xml.nupkg
```

## 四、执行顺序

阶段 1（slnx + pack.ps1）与阶段 2（csproj）并行 → 阶段 5 部分验证（build）→ 阶段 3（README/CHANGELOG）→ 阶段 4（测试）→ 全量验证。
