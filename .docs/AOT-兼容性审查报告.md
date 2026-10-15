# Mud.HttpUtils AOT / 裁剪兼容性审查报告

- 审查角色：.NET Native AOT 与裁剪（trimming）专家
- 审查基线：仓库当前工作树（`dev` 分支，`git status` 干净，无未提交改动）
- 报告日期：2026-09-10
- 结论口径：每条结论均给出**可复现命令**或标注「需实测验证」，不做未经实测的通过判定

---

## 0. 审查方法与证据

### 0.1 已执行的实测（证据）

| 命令 | 结果 |
| --- | --- |
| `dotnet build "Mud.HttpUtils.Client\Mud.HttpUtils.Client.csproj" -f net8.0 -c Release --no-incremental -v n` | 0 错误；131 个警告，其中 IL 系列（AOT 分析器）实测统计：**IL2026×44、IL3050×38、IL3051×8、IL2091×14、IL2070×2、IL2046×4**（日志含汇总段重复计数），去重后涉及 **12 个文件、38 个位置** |

实测产生的 IL 告警位置（去重，`文件(行,列) => 诊断`）：

```
Helpers\XmlSerialize.cs(64,30)                       => IL2026
Helpers\XmlSerialize.cs(79,13)                       => IL2026
Helpers\XmlSerialize.cs(134,30)                      => IL2026
Helpers\XmlSerialize.cs(144,23)                      => IL2026
HttpClient\DefaultHttpRequestExecutor.cs(153,26)     => IL2026
HttpClient\DefaultHttpRequestExecutor.cs(222,31)     => IL2026
HttpClient\EnhancedHttpClient.cs(1260,19)            => IL3051
HttpClient\EnhancedHttpClient.cs(1806,20)            => IL2091 + IL3050
HttpClient\EnhancedHttpClient.cs(1825,20)            => IL2091 + IL3050
HttpClient\Query\DefaultUrlParameterFormatter.cs(45,20) => IL2046 + IL3051
HttpClient\Serialization\HttpContentSerializerFactory.cs(73,25) => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(58,20)  => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(73,26)  => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(81,16)  => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(88,16)  => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(101,16) => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(134,21) => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(193,38) => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(201,44) => IL2026 + IL3050
HttpClient\Serialization\SystemTextJsonContentSerializer.cs(266,13) => IL2026 + IL3050
Logging\DefaultSensitiveDataMasker.cs(128,30)        => IL2070
Logging\DefaultSensitiveDataMasker.cs(224,19)        => IL2046 + IL3051
Serialization\CamelCaseStringEnumConverter.cs(35,27) => IL3050
Serialization\ObjectToInferredTypesConverter.cs(44,9) => IL2026 + IL3050
ServiceCollectionExtensions.cs(205,9)                => IL2026 + IL3050
ServiceCollectionExtensions.cs(288,9)                => IL2026 + IL3050
ServiceCollectionExtensions.cs(372,9)                => IL2091
ServiceCollectionExtensions.cs(441,9)                => IL2091
ServiceCollectionExtensions.cs(495,9)                => IL2091
ServiceCollectionExtensions.cs(549,9)                => IL2091
ServiceCollectionExtensions.cs(605,9)                => IL2091
ServiceCollectionExtensions.cs(779,9)                => IL2026 + IL3050
ServiceCollectionExtensions.cs(806,9)                => IL2026 + IL3050
ServiceCollectionExtensions.cs(834,9)                => IL2026 + IL3050
```

### 0.2 未执行（标注为需实测验证）

- `dotnet publish -p:PublishAot=true`（AotVerificationDemo / AotFullTrimVerificationDemo / AotPackageRefDemo，net8.0+net10.0，win-x64/linux-x64/osx-x64）本次未执行，原因：耗时与跨 OS 运行限制。凡涉及"发布产物行为"的结论均标注。
- 结论中带 **【需实测】** 的条目需在 CI 严格模式下复现确认。

---

## 1. 问题清单（按严重级别）

### 1.1 Blocker

---

**B1. `Mud.HttpUtils.Client/HttpClient/Serialization/SystemTextJsonContentSerializer.cs:85-89`（调用在 88,16）**

- 命中规则：A-3、B-7、高危模式「新增 public API 触及反射却缺 `[RequiresUnreferencedCode]/[RequiresDynamicCode]`」
- 风险分类：**运行时 NotSupportedException + 标注缺失（裁剪后元数据丢失静默失败）**
- 证据：实测 `IL2026 + IL3050 @ (88,16)`。该重载实现为
  ```csharp
  public string Serialize(object? item, System.Type type, object? options = null)
      => JsonSerializer.Serialize(item, type, opts); // 非泛型反射重载
  ```
  接口声明 `IHttpContentSerializer.Serialize(object?, Type, object?)(IHttpContentSerializer.cs:70)` 与本实现**均无** RUC/RDC 标注。因此：
  - 库自身编译期产生 IL2026/IL3050（告警）；
  - 消费方调用该 API 时**拿不到任何编译告警**，AOT 下运行时抛 `NotSupportedException`（`Serialize(object, Type, options)` 在 AOT 下无法解析未注册类型的元数据）。
- 修复建议：
  1. 在 `IHttpContentSerializer` 与 `SystemTextJsonContentSerializer` 的该重载上补齐
     `[RequiresUnreferencedCode("...改用 Serialize<T>(item, JsonTypeInfo<T>)")]`
     `[RequiresDynamicCode("...AOT 下改用源生成 JsonTypeInfo<T>")]`；或
  2. 新增 AOT 安全重载 `string Serialize<T>(T item, JsonTypeInfo<T> typeInfo)`（`NET8_0_OR_GREATER` 条件编译）供生成代码/执行器使用，并把 `object/Type` 重载标注为非 AOT 路径（与 `EncryptContent(object)` 的处理保持一致）。

---

**B2. `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs:64,79,134,144`**

- 命中规则：B-7、B-9
- 风险分类：**运行时 PlatformNotSupportedException + 标注缺失（RUC 未传播 → 消费方无告警）**
- 证据：实测 4 处 `IL2026`。`XmlSerializer(Type)` / `Serialize` / `Deserialize` 在 BCL 中标注为 `[RequiresUnreferencedCode]`；而当前 4 个方法**只标注了 `[RequiresDynamicCode]`**（28-54、98-124 行）。对 `T` 施加的 `[DynamicallyAccessedMembers]` **不能**满足 `XmlSerializer` 的 RUC 需求（实测已证明仍报 IL2026）。
- 后果：`XmlSerialize.Serialize<T>` 是 public API，AOT 下会抛 `PlatformNotSupportedException`（引导性远弱于编译期告警），消费方调用处也不会收到 IL2026。
- 修复建议：为 4 个方法补充
  `[RequiresUnreferencedCode("XmlSerializer 需要运行时程序集生成，Native AOT 不支持。请改用 [SerializationMethod(Json)] 或 System.Text.Json。")]`。
  同时更新 `AotAttributeAnnotationTests`：现有测试只断言 `[RequiresDynamicCode]`，建议增加 RUC 断言。

---

**B3. `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:1799-1801` 与 `1818-1820`**

- 命中规则：B-7、B-8（压制的 checkId 写错，压制无效）
- 风险分类：**编译告警升级为错误（严格模式）+ 真实标注缺失**
- 证据：实测在 `SerializeToXml<T>`(1806,20) / `DeserializeFromXml<T>`(1825,20) 泄漏的是 **`IL2091`（DAM 泛型实参不满足）与 `IL3050`**，而压制属性写的是
  ```csharp
  [UnconditionalSuppressMessage("System.Xml.XmlSerializer", "IL2026:RequiresUnreferencedCode", ...)]
  ```
  checkId 与实际告警不匹配 → 压制完全未生效。
- 修复建议：二选一
  1. 给 `T` 补 `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]` 并加 `[RequiresDynamicCode]`；或
  2. 方法整体标注 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`；
  若确需压制，必须改为 `IL2091` / `IL3050` 且写明 justification（规则 B-8）。

---

**B4. `Mud.HttpUtils.Client/HttpClient/Serialization/HttpContentSerializerFactory.cs:66-75`（告警在 73,25）**

- 命中规则：A-4、B-7、B-8
- 风险分类：**编译告警升级为错误（严格模式）**
- 证据：实测 `IL2026 + IL3050 @ (73,25)`，来源 `new DefaultJsonTypeInfoResolver()`。
- 说明：`RuntimeFeature.IsDynamicCodeSupported` 的运行时分流**设计上是正确的**（AOT 分支只组合源生成 context，JIT 分支才 Combine 反射兜底），但 Roslyn AOT 分析器**不会**按运行时布尔分支做流分析，仍会报警。这类属"已知误报需人工确认后压制"，规则 A-4/B-8 允许（且要求）压制而非放任。
- 修复建议：为 `BuildOptions` 增加
  ```csharp
  [UnconditionalSuppressMessage("Trimming", "IL2026",
      Justification = "DefaultJsonTypeInfoResolver 仅在 RuntimeFeature.IsDynamicCodeSupported==true 的 JIT 分支实例化；AOT 分支只组合源生成 context。")]
  [UnconditionalSuppressMessage("AotAnalysis", "IL3050",
      Justification = "同上：JIT 专属分支，AOT 运行时不可达。")]
  ```

---

**B5. `Mud.HttpUtils.Client/HttpClient/Serialization/SystemTextJsonContentSerializer.cs:48-51`**

- 命中规则：A-2（禁止未设置 `TypeInfoResolver` 的裸 `new JsonSerializerOptions()` 序列化路径）
- 风险分类：**裁剪后元数据丢失静默失败 / 运行时异常**
- 证据：
  ```csharp
  public SystemTextJsonContentSerializer(JsonSerializerOptions? options = null)
      => _options = options ?? new JsonSerializerOptions();   // TypeInfoResolver == null
  ```
  `SystemTextJsonContentSerializer` 是公开类型且该构造函数无参可用。走 `new SystemTextJsonContentSerializer()` 时，`_options.TypeInfoResolver` 为 null，AOT 下 `JsonSerializer.Serialize/Deserialize` 无 context 可用，行为为运行时异常或静默空对象（取决于 TFM/STJ 版本）。
- 修复建议：将构造函数默认值改为经工厂合并内置 context：
  ```csharp
  _options = options ?? HttpContentSerializerFactory.BuildOptions(null);
  ```
  或在构造函数中对 `options.TypeInfoResolver == null && !RuntimeFeature.IsDynamicCodeSupported` 时抛 `InvalidOperationException` 并给出接入 `JsonSerializerContext` 的引导（避免静默）。

---

**B6. `Mud.HttpUtils.Client/Serialization/ObjectToInferredTypesConverter.cs:42-45`**

- 命中规则：A-1、A-3、B-7
- 风险分类：**运行时 NotSupportedException（该转换器被源生成上下文使用）**
- 证据：实测 `IL2026 + IL3050 @ (44,9)`。该转换器经
  `ProblemDetailsJsonContext.cs:28` 的 `[JsonSourceGenerationOptions(Converters = [typeof(ObjectToInferredTypesConverter)])]`
  被注册进**源生成**上下文，但 `Write` 走 `JsonSerializer.Serialize(writer, value, options)` 的 **object 反射式重载**（编译期类型为 `object?`，TValue=object）。AOT 下写 `ProblemDetails.Extensions` 的未知运行时类型时存在抛 `NotSupportedException` 的风险。
- 修复建议：
  1. 最简单：`Write` 改为按运行时类型分支，用 `Utf8JsonWriter` 原生写入（`string/decimal/double/bool/JsonElement/JsonDocument`），其余类型 `writer.WriteNullValue()` 或抛受控异常；或
  2. `NET8_0_OR_GREATER` 下使用 `options.GetTypeInfo(value.GetType())`（`JsonSerializerOptions.GetTypeInfo`）并配合 `JsonSerializer.Serialize(writer, value, typeInfo)`（需实测确认该重载语义与类型注册）。
  无论采用哪种，都应消除该 IL2026/IL3050。

---

**B7. `Mud.HttpUtils.Client/Serialization/CamelCaseStringEnumConverter.cs:33-36`**

- 命中规则：A-1、B-7
- 风险分类：**运行时 NotSupportedException（若进入 AOT 序列化路径）**
- 证据：实测 `IL3050 @ (35,27)`。`new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)` 在 AOT 下需要运行时动态代码；BCL 建议改用泛型 `JsonStringEnumConverter<TEnum>`。
- 修复建议：改为 `JsonStringEnumConverter<TEnum>`（需为每个枚举生成/注册），或改用源生成的枚举转换。先确认该类是否被 `ServiceCollectionExtensions` 默认注册到 AOT 可达路径（**【需实测】**：搜索默认注册点后在 AOT 下构造一次 `JsonSerializerOptions` 确认）。
- 严重级别取决于是否默认注册：默认注册则 Blocker，否则 Major。

---

### 1.2 Major

---

**M1. `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs:153,222`**

- 命中规则：B-7、C-10
- 风险分类：**编译告警升级为错误 / 消费方无 RUC 告警**
- 证据：实测 `IL2026 @ (153,26)` 与 `(222,31)`，来源 `xmlSerializer.Deserialize(reader)`。所在方法未标注 RUC。
- 补充：该路径的 `XmlSerializer` 实例来自 `ResponseDescriptor.XmlSerializer`（由 `ConstructorGenerator` 在 AOT 下改为抛 `PlatformNotSupportedException` 的属性），属"防御纵深"，但 IL2026 仍必须处理。
- 修复建议：对这两个方法标注 `[RequiresUnreferencedCode]`（消息指向 AOT007/JSON），或在方法内对 XML 分支增加 `#if` / 压制（需 justification）。

---

**M2. `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:205,288,372,441,495,549,605,779,806,834`**

- 命中规则：B-7、高危模式「新增 public API 触及反射却缺标注」
- 风险分类：**编译告警升级为错误 / 消费方无告警**
- 证据：实测 `IL2026 + IL3050`（205、288、779、806、834）与 `IL2091`（372、441、495、549、605）。来源 `OptionsConfigurationServiceCollectionExtensions.Configure<TOptions>(IServiceCollection, IConfiguration)` 与 `services.Configure<JsonSerializerOptions>(...)`。
- 修复建议：这些是 DI 扩展入口，需按方法逐个处理：
  - 对 `Configure<T>` 的调用点补 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`（若该方法面向 AOT 提供，则应改为 `Configure<T>(IConfiguration)` 的 AOT 友好变体或显式 `[UnconditionalSuppressMessage]` + justification）；
  - `IL2091` 处的泛型 `TOptions` 需补 `[DynamicallyAccessedMembers]`。

---

**M3. `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs:128`（IL2070 压制挂错方法）**

- 命中规则：B-7、B-8
- 风险分类：**编译告警升级为错误**
- 证据：实测 `IL2070 @ (128,30)`，来源 `GetOrCreatePropertyInfos` 中的 `t.GetProperties(BindingFlags.Public | BindingFlags.Instance)`。现有 `[UnconditionalSuppressMessage("System.Text.Json", "IL2070:DynamicallyAccessedMembers", ...)]` 挂在 `MaskObject`(224 行)，**对 128 行无效**。
- 修复建议：将压制（或 `[DynamicallyAccessedMembers]` 参数标注）移到 `GetOrCreatePropertyInfos`；同时把 category 由 `"System.Text.Json"` 改为 `"Trimming"`。

---

**M4. `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs:224` 与 `Mud.HttpUtils.Abstractions/Logging/ISensitiveDataMasker.cs:115`**

- 命中规则：B-7、高危模式「接口与实现标注不匹配」
- 风险分类：**编译告警升级为错误（IL2046/IL3051）**
- 证据：实测 `IL2046 + IL3051 @ (224,19)`：实现 `MaskObject` 带 RUC/RDC，接口 `ISensitiveDataMasker.MaskObject(object)` 未带。
- 修复建议：在接口方法上补齐 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`（与 `AotSafeSensitiveDataMasker.MaskObject` 的 AOT 安全语义并存：接口标注只表示"某些实现可能不安全"，消费方在 AOT 下应注入安全实现）。注意 `AotAttributeAnnotationTests` 已断言实现侧标注，接口侧需同步，避免破坏测试语义。

---

**M5. `Mud.HttpUtils.Client/HttpClient/Query/DefaultUrlParameterFormatter.cs:45` 与 `Mud.HttpUtils.Abstractions/HttpClient/Query/IUrlParameterFormatter.cs:35`**

- 命中规则：B-7
- 风险分类：**编译告警升级为错误（IL2046/IL3051）**
- 证据：实测 `IL2046 + IL3051 @ (45,20)`。
- 修复建议：接口 `Format` 补 RUC/RDC；或实现侧改压制（不推荐，因该实现确实反射不安全，接口标注更能把风险传给消费方）。

---

**M6. `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:1256-1260` 与 `IEncryptableHttpClient.EncryptContent(object,...)`**

- 命中规则：B-7、F-20
- 风险分类：**编译告警升级为错误（IL3051）+ 标注缺失（缺 RUC）**
- 证据：实测 `IL3051 @ (1260,19)`：实现带 RDC，接口未带。另外该实现内部同时调用 `XmlSerialize.Serialize(content)` 与 `_contentSerializer.Serialize(content, content.GetType())`（非泛型反射重载），但只标了 RDC、**缺 RUC**。
- 修复建议：接口 `EncryptContent(object,...)` 补 RDC；实现补 RUC。`EncryptContent<T>` 泛型重载保持无 RDC（已正确，见 F-20）。

---

**M7. `Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs:345-362`（AOT005 误报）**

- 命中规则：E-16
- 风险分类：**诊断误报（Major）**
- 证据：`[Query]` 复杂类型判定为
  ```csharp
  if (!IsPrimitiveOrString(paramType) && !IsArrayType(paramType) && !IsCovered(paramType, coveredTypes))
      Report AOT005
  ```
  未排除：
  1. **字典类型**（`IsArrayType` 不含 `Dictionary<,>`、`IsPrimitiveOrString` 不含）→ `[Query] Dictionary<string,string>` 误报；
  2. 其他泛型集合（`HashSet<T>`/`ICollection<T>`/`IList<T>` 等，`IsArrayType` 仅覆盖 `List`/`IEnumerable`）→ 误报；
  3. **实现 `IQueryParameter` 的类型**（生成器走 `ToQueryParameters()`，不做 JSON 序列化）→ 误报；而 AOT005 的 messageFormat 本身却建议"或实现 IQueryParameter 接口"，自相矛盾。
- 修复建议：在 `[Query]` 分支引入与生成器一致的判定：
  - `IsDictionaryType(paramType)`（已有私有方法，复用）；
  - 扩展 `IsArrayType` → 覆盖 `IList/ICollection/IReadOnlyList/IReadOnlyCollection/HashSet/...`（或改用 `AllInterfaces` 判定 `IEnumerable<T>` + 排除字符串）；
  - 新增 `ImplementsIQueryParameter(type)`（参考 `QueryParameterBinder.TryGenerateInlineQueryFlattening:333-335` 的判定实现）。
- 回归测试：新增分析器级测试覆盖上述三类"不误报"用例。

---

**M8. `Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs:353-359`（AOT005 缺 `TypeFullName`）**

- 命中规则：D-14
- 风险分类：**诊断误报/漏报（CodeFix 不可用）**
- 证据：`[Query]` 分支的 `Diagnostic.Create` 未传 `TypeProps(paramType)`；而 `[QueryMap]` 分支（382-389）已传。`AotJsonContextCodeFixProvider.RegisterCodeFixesAsync` 在取不到 `TypeFullName` 时直接 `return`（59-63）→ 该 AOT005 无任何 CodeFix。
- 修复建议：`[Query]` 分支补 `TypeProps(paramType)`，与 `[QueryMap]` 分支一致。

---

**M9. `Mud.HttpUtils.Generator/Analyzers/AotXmlRejectionAnalyzer.cs:107-112` × `Mud.HttpUtils.CodeFixes/AotXmlCodeFixProvider.cs:47-58`（AOT007 CodeFix 实际永不提供）**

- 命中规则：D-14
- 风险分类：**诊断修复能力缺失（Major）**
- 证据链：
  1. 分析器把 AOT007 定位到 `method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation()`（方法声明 / 接口声明），**不是** `[SerializationMethod]` 特性；
  2. CodeFix 从 `diagnostic.Location.SourceSpan.Start` 取 token → `FirstAncestorOrSelf<AttributeSyntax>()`，方法名 token 的祖先链中不存在 `AttributeSyntax` → 返回 null → `return`，**永不注册修复**。
  3. 现有测试 `Tests/Mud.HttpUtils.CodeFixes.Tests/CodeFixProviderTests.cs:43-46` 把诊断**人为挂在 `FindSerializationMethodAttribute`** 上，因此测试通过但掩盖了真实的定位不匹配。
- 修复建议：
  - 方案 A（推荐）：分析器在检测到 `[SerializationMethod(Xml)]` 时把位置定位到该特性（`attr.ApplicationSyntaxReference`），仅在"响应/Body 内容类型为 XML 但无特性"时才退回方法位置；
  - 方案 B：CodeFix 兼容方法级定位——在方法上搜索 `[SerializationMethod]` 特性（含接口级），若无特性则提供"添加 `[SerializationMethod(Json)]`"或"移除 XML 内容类型"的修复；
  - 并补一条**以真实 Analyzer 输出驱动**的端到端测试（而非手造诊断）。

---

**M10. `Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs:235-245`（引用程序集 Context 漏检）**

- 命中规则：E-18（"覆盖"集合的完整性）、D-11
- 风险分类：**诊断漏报 + 潜在误报（需实测确认）**
- 证据：`CollectCoveredTypes` 扫描引用程序集时只处理
  ```csharp
  if (reference is not CompilationReference compRef) continue;
  ```
  在 CLI `dotnet build` / `dotnet publish` 下，ProjectReference 与 PackageReference 都以 `PortableExecutableReference` 形式进入 `compilation.References`（`CompilationReference` 仅出现在 IDE/Workspaces 场景）。后果：
  - **漏报**：若覆盖 DTO 的 `JsonSerializerContext` 位于被引用项目（例如 `Mud.HttpUtils.Client` 内的 `MudHttpJsonContext`、或独立的 Contracts 项目），CLI 构建时 `coveredTypes` 可能不含它 → AOT004 因 `coveredTypes.Count == 0` 提前 return（99 行），`[Body]`/响应 DTO 的未覆盖问题被静默吞掉；
  - **误报**：AOT006 在同场景下可能对已被引用项目 Context 覆盖的 `[HttpJsonSerializable]` 类型报错。
- 修复建议：改为遍历全部引用并用符号解析：
  ```csharp
  foreach (var reference in compilation.References)
  {
      var asm = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
      if (asm == null) continue;
      CollectCoveredTypesFromNamespace(asm.GlobalNamespace, ...);
  }
  ```
  同时保留现有 `CompilationReference` 分支以兼容 IDE。**【需实测】**：在 `AotPackageRefDemo`（NuGet 消费方路径）下验证 AOT004/AOT006 的正确性。

---

**M11. `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props:15-22`（`Nullable`/`HttpClientOptionsName` 未注册）**

- 命中规则：D-11（"新增同类属性须一并注册"的既有违规）
- 风险分类：**配置静默失效（Major）**
- 证据：生成器读取 `build_property.Nullable`（`HttpInvokeClassSourceGenerator.cs:96-97`、`HttpInvokeRegistrationGenerator.cs:35-36`）与 `build_property.HttpClientOptionsName`（`HttpInvokeClassSourceGenerator.cs:85-86`），但 props 只注册了 `IsAotCompatible`、`PublishAot`、`DisableMudSourceGenerator`、`MudEmitGeneratedCodeMarkers`。未注册的属性对分析器不可见 → 两者**恒取默认值**：
  - `EmitNullableEnable` 恒为 true（`Nullable=disable` 的消费项目仍会发射 `#nullable enable`）→ 标注为 [v2.4 §3.4 D-03] 的修复实际是空操作；
  - `HttpClientOptionsName` 恒为 `HttpClientOptions`。
- 修复建议：在 props 中补
  ```xml
  <CompilerVisibleProperty Include="Nullable" />
  <CompilerVisibleProperty Include="HttpClientOptionsName" />
  ```
  并补一条单元测试验证 `build_property` 读取链路（可通过 AnalyzerConfigOptionsProvider 模拟）。

---

**M12. `Demos/AotVerificationDemo/AotVerificationDemo.csproj:28`、`Demos/AotFullTrimVerificationDemo/…csproj:22`、`Demos/AotPackageRefDemo/…csproj:22`（严格模式清单缺 IL3051/IL2091）**

- 命中规则：D-13、高危模式「为新诊断提升为 Error 却未同步 AotStrictMode 清单」
- 风险分类：**严格模式漏网（Major）**
- 证据：三份 `WarningsAsErrors` 均为
  `IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL3050;AOT004;AOT005;AOT006;AOT007`
  实测库内存在 **IL3051×8、IL2091×14**，两项均不在清单中，`-p:AotStrictMode=true` 不会将其升级为错误。
- 修复建议：清单追加 `IL3050` 邻近补上 `IL3051;IL2091`（如还有 `IL2091` 之外的 DAM 诊断一并补齐）。

---

**M13. `Directory.Build.props` + 三份 Demo csproj（严格模式作用域只覆盖 Demo）**

- 命中规则：G-21、D-13
- 风险分类：**严格模式漏网（Major）**
- 证据：`WarningsAsErrors` 定义在 Demo 项目内，MSBuild 项目属性**不会**传播到被 `ProjectReference` 的库项目；`AotStrictMode` 全局属性虽传递，但库项目未消费它。因此库自身实测产生的 IL2026(44)/IL3050(38)/IL3051(8)/IL2091(14) **永不**因 `-p:AotStrictMode=true` 而失败。
- 修复建议：在 `Directory.Build.props` 增加
  ```xml
  <PropertyGroup Condition="'$(AotStrictMode)' == 'true'">
    <WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL3050;IL3051;IL2091</WarningsAsErrors>
  </PropertyGroup>
  ```
  （或 CI 对库工程显式 `-p:TreatWarningsAsErrors=true`）。否则"CI 严格模式可复现"的前提不成立。

---

**M14. `test.ps1:99-143`（AOT 验证覆盖不足）**

- 命中规则：G-21、G-22、G-23
- 风险分类：**验证场景缺失（Major）**
- 证据：
  1. 发布命令 `dotnet publish $AotDemo -c Release -f $tfm -r $Rid` **未传 `-p:AotStrictMode=true`** → 严格模式从未被该脚本执行；
  2. 只发布 `AotVerificationDemo`，**未**覆盖 `AotFullTrimVerificationDemo`（TrimMode=full + TrimmerSingleWarn=false）与 `AotPackageRefDemo`（NuGet 消费方路径）；
  3. `$Rid` 由宿主 OS 决定（`$IsLinux`/`$IsMacOS`），单次运行只验证一个 RID，无法在同一环境覆盖 win-x64/linux-x64/osx-x64。
- 修复建议：
  - 增加 `-AotStrictMode` 开关并传入 `-p:AotStrictMode=true`；
  - 追加两个 Demo 的 publish + 运行断言；
  - CI 使用 OS 矩阵（`windows-latest` / `ubuntu-latest` / `macos-latest`）分别跑 `-AOT`，或在同 OS 上对可交叉编译的 RID 至少做 publish（不运行）验证。

---

**M15. `Demos/AotFullTrimVerificationDemo/Program.cs:42-44`（无 AOT_OK / 无退出码 / 未覆盖生成器路径）**

- 命中规则：G-21、G-22
- 风险分类：**验证场景缺失（Major）**
- 证据：仅 `Console.WriteLine("=== 所有验证场景通过 ===")`，不输出 `AOT_OK`，不设置 `Environment.ExitCode`；且全项目未使用 `[HttpClientApi]` / `[HttpJsonSerializable]`，因此 full-trim 场景**未覆盖源生成器、分析器、脚手架 Context** 的行为。
- 修复建议：
  - 断言成功时输出 `AOT_OK`，任一断言失败 `return 1`；
  - 增加至少一个生成 API 客户端 + 源生成 `JsonSerializerContext` 的 full-trim 场景（与 AotVerificationDemo 的 checker 对齐）。

---

**M16. `Demos/AotVerificationDemo/Program.cs:47-49`（AOT_OK 判定偏弱）**

- 命中规则：G-21
- 风险分类：**验证强度不足（Major）**
- 证据：`AOT_OK` 在 Main 末尾**无条件**打印；大量 HTTP 场景用 `catch (Exception ex) { Console.WriteLine(...) }` 吞掉所有异常（如 152-155、171-174、200-203、231-234、271-274…）。虽然断言型场景（如 `DemoOAuth2Serialization`）会抛出并阻断，但 `AOT_OK` 不能证明这些 HTTP/序列化路径真的可用。
- 修复建议：引入失败计数器（断言失败累加），仅在计数为 0 时打印 `AOT_OK`；对"预期失败（无服务器）"与"非预期失败（序列化异常）"区分：后者应计入失败。

---

**M17. 缺失 `AotDtoCoverageAnalyzer` 专用测试 & AOT005 分析器级测试**

- 命中规则：G-24
- 风险分类：**回归测试缺失（Major）**
- 证据：规则 24 要求的 7 项测试中，`AotDtoCoverageAnalyzer` 无独立测试文件；AOT004/AOT006 分散在 `Aot004FormUrlEncodedTests.cs`、`GeneratorDiagnosticsTests.cs`、`CodeFixProviderTests.cs`（后者用手造诊断，非真实 Analyzer）；**AOT005（`[Query]`/`[QueryMap]`）无分析器级回归测试**（`QueryParameterAotTests` 只测 `RequestBuilder` 生成代码文本）。
- 修复建议：新增 `AotDtoCoverageAnalyzerTests`，覆盖：
  - `Task<T>` / `ValueTask<T>` / `Nullable<T>` / `List<T>` 解包后判定；
  - 字典 / `IQueryParameter` / 泛型集合**不**触发 AOT005（配合 M7）；
  - `Diagnostic.Properties["TypeFullName"]` 在 AOT004/AOT005/AOT006 三处均存在（配合 M8）；
  - 跨程序集/同程序集 Scope 语义（配合 M10）。

---

**M18. `Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs:30` 与 `:244`（AOT 区间注释过时）**

- 命中规则：D-13（诊断编号/级别约定）
- 风险分类：**文档不一致（Minor，但影响可维护性）**
- 证据：第 30 行注释 `* - AOT*: AOT JSON 序列化诊断 (AOT001-005)`、第 244 行 `#region AOT JSON 序列化诊断信息 (AOT001-005)`，而实际已定义到 AOT007。
- 修复建议：更新为 `AOT001-007`。

---

### 1.3 Minor

| 位置 | 规则 | 风险分类 | 修复建议 |
| --- | --- | --- | --- |
| `Mud.HttpUtils.CodeFixes/AotXmlCodeFixProvider.cs:95` | — | 仅文档 | `document.GetSyntaxRootAsync(cancellationToken).Result` 阻塞式异步，改为 `async` + `await`（`ReplaceXmlWithJsonAsync` 已是 `Task<Document>`，无需 `.Result`） |
| `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs:221` | B-8 | 仅文档 | 压制 category `"System.Text.Json"` 与 checkId `"IL2070:DynamicallyAccessedMembers"` 写法不规范，建议 `"Trimming","IL2070"` |
| `Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs:32` | B-8 | 仅文档 | `[UnconditionalSuppressMessage("Trimming","IL2072:...")]` 与同方法 `[RequiresUnreferencedCode]` 并存；因方法已 RUC，IL2072 压制合理，建议统一 checkId 写法并确认 justification 表述 |
| `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs:493-511` | B-7、B-9 | 仅文档 | `TypeSymbol == null` 时回退到运行时反射 + `#pragma warning disable IL2072`。真实项目不会走到该分支，但建议改为生成期 `Debug.Assert`/直接抛生成诊断（`HTTPCLIENT001`），避免在生成产物中留下可达反射路径 |
| `Mud.HttpUtils.Generator/Generators/Implementation/Binders/QueryParameterBinder.cs:456-460` | E-17 | 仅文档 | 复杂属性递归深度超限（`MaxFlatteningDepth=5`）时回退 `FlattenObjectToQueryParams`（反射、RUC）；AOT 下这是**可达**的反射路径。建议：在 AOT 上下文（`GeneratorContext.IsAotEnabled`）下超限时改为生成编译期诊断或截断（不再回退反射），而非静默产出反射调用 |
| `.docs/*` 多份 AOT 方案文档 | — | 仅文档 | 文档中"CI 严格模式已验证"的表述需与实际脚本（M14）对齐，避免"声称验证但脚本未执行严格模式" |

---

## 2. 逐条规则核验结果

| 规则 | 结论 | 依据 |
| --- | --- | --- |
| A-1 JSON 元数据可获取 | ⚠ 部分 | 库内置 `MudHttpJsonContext`/`OAuth2JsonContext`/`ProblemDetailsJsonContext` 均为源生成；但 `ObjectToInferredTypesConverter`(B6)、`CamelCaseStringEnumConverter`(B7) 在源生成上下文内引入反射式路径 |
| A-2 禁止裸 `new JsonSerializerOptions()` | ❌ | `SystemTextJsonContentSerializer.cs:50`（B5） |
| A-3 优先泛型/JsonTypeInfo 重载 | ❌ | `SystemTextJsonContentSerializer.cs:88`（B1）；`ObjectToInferredTypesConverter.cs:44`（B6） |
| A-4 BuildOptions 分流 | ✅ 设计正确（⚠ 缺压制） | `HttpContentSerializerFactory.cs:63-92` 用 `RuntimeFeature.IsDynamicCodeSupported` 正确分流；实测 73,25 需压制（B4） |
| A-5 集合/闭合泛型/多态注册 | ✅ | 脚手架 `CollectSerializableTypes` 注册闭合泛型、解包 `Nullable<T>`、跳过开放泛型；`[JsonDerivedType]` 由 `autoDerivedTypes` 处理 |
| A-6 命名策略不混用 | ✅ | `CheckDuplicateSerializerClassNameConflicts`（AOT001） |
| B-7 反射三选一 | ❌ | B1/B2/B3/B6 处 RUC/RDC 缺失或压制无效 |
| B-8 压制须有理由且有效 | ❌ | B3（checkId 错）、M3（挂错方法）、M4/M5/M6（标注不匹配） |
| B-9 不新增 Emit/XmlSerializer | ⚠ | 未新增 `Reflection.Emit`；`XmlSerializer` 既有路径注释/压制问题见 B2/M1/M2 |
| C-10 AOT007 XML 判定一致 | ✅ | `AotXmlRejectionAnalyzer.cs:99-102` 复用 `MethodAnalyzer` + `ContentTypeHelper.IsXmlContentType`，覆盖 `SerializationMethod`/`ResponseContentType`/`GetEffectiveContentType()`（含 `BodyContentType`） |
| D-11 AOT 上下文属性 | ⚠ | `IsAotCompatible`/`PublishAot` 已注册（✅）；但 `Nullable`/`HttpClientOptionsName` 未注册（M11） |
| D-12 遍历模式/CancellationToken/try-catch | ✅ | 两分析器均 `SyntaxTrees → GetDeclaredSymbol`，检查 `CancellationToken`；`AotXmlRejectionAnalyzer.cs:85-93` 单方法 `try/catch continue` |
| D-13 诊断 ID/级别约定 | ⚠ | AOT004/005/006=Warning、AOT007=Error、脚手架 Info 均正确；但注释区间过时（M18），严格模式清单缺 IL3051/IL2091（M12） |
| D-14 CodeFix 依赖 Properties | ❌ | M8（AOT005 `[Query]` 缺 `TypeFullName`）、M9（AOT007 定位不匹配） |
| E-15 FormUrlEncoded 不报 AOT004 | ✅ | `AotDtoCoverageAnalyzer.cs:326-330` 提前 `continue`；`Aot004FormUrlEncodedTests` 覆盖 |
| E-16 `[Query]` 仅复杂 JSON 类型报 AOT005 | ❌ | M7（字典/泛型集合/`IQueryParameter` 误报） |
| E-17 解包 Task/ValueTask/Nullable/集合 | ✅ | `ExtractTaskInnerType`（511-526）、`IsCovered`（442-466）、`IsPrimitiveOrString`（472-506）；⚠ 深度超限回退反射（见 Minor 表） |
| E-18 只核验当前编译单元 | ✅ | `AnalyzeHttpJsonSerializableCoverage` 仅遍历 `compilation.SyntaxTrees`；⚠ 但 covered 集合漏检引用程序集（M10） |
| F-19 Masker 行为对齐 | ✅ | `AotSafeSensitiveDataMasker.Mask` 与 `DefaultSensitiveDataMasker.Mask` 逐分支一致（`MaskString="***"`、`Length <= prefix+suffix`、`TypeOnly → "[String, Length=n]"`、default → `"***"`）；Demo 侧以子类化 + `Register<T>` 方式注册 |
| F-20 `EncryptContent<T>` AOT 安全 | ✅ | 泛型重载无 RDC，使用 `_contentSerializer.Serialize<T>` + `Utf8JsonWriter`；`object` 重载保留 RDC + `[Obsolete]`；`AotAttributeAnnotationTests` 断言通过（但覆盖面见 B1/M6） |
| G-21 `test.ps1 -AOT` 覆盖 net8/net10 + 三 RID + AOT_OK | ⚠/❌ | AotVerificationDemo 有 `AOT_OK`（✅）；但未跑严格模式、未覆盖三 Demo、RID 受宿主限制（M14、M16） |
| G-22 AotFullTrimVerificationDemo | ⚠ | csproj 具备 `TrimMode=full` + `TrimmerSingleWarn=false`（✅）；但无 `AOT_OK`、未覆盖生成器路径（M15） |
| G-23 AotPackageRefDemo | ⚠ | 项目存在且消费 NuGet 包（✅）；但 `test.ps1` 不驱动它（M14） |
| G-24 单元测试齐全 | ⚠ | AotXmlRejection/QueryParameterAot/BodySerializerAot/Aot004FormUrlEncoded/JsonOptionsAot/AotAttributeAnnotation 均存在；缺 `AotDtoCoverageAnalyzer` 专用与 AOT005 分析器级测试（M17） |

---

## 3. 汇总清单

### 3.1 AOT 阻断项（必须修复后才能合入）

1. **B1** `SystemTextJsonContentSerializer.cs:85-89` — 非泛型 `JsonSerializer.Serialize(object?, Type, options)` 且接口/实现均无 RUC/RDC → 消费方无告警、AOT 运行时 `NotSupportedException`。实测 IL2026+IL3050 @ (88,16)。
2. **B2** `XmlSerialize.cs:64,79,134,144` — 缺 `[RequiresUnreferencedCode]`，RUC 未传播；AOT 运行时 `PlatformNotSupportedException`。实测 4×IL2026。
3. **B3** `EnhancedHttpClient.cs:1799-1801 / 1818-1820` — 压制 checkId 错（写了 IL2026，实际泄漏 IL2091/IL3050）→ 压制无效 + 真实标注缺失。
4. **B4** `HttpContentSerializerFactory.cs:66-75` — JIT 专属分支 `DefaultJsonTypeInfoResolver` 无双 `[UnconditionalSuppressMessage]`。实测 IL2026+IL3050 @ (73,25)。
5. **B5** `SystemTextJsonContentSerializer.cs:48-51` — 裸 `new JsonSerializerOptions()`（无 `TypeInfoResolver`）默认构造。
6. **B6** `ObjectToInferredTypesConverter.cs:42-45` — 被源生成上下文注册的转换器内走反射式 object 序列化。实测 IL2026+IL3050 @ (44,9)。
7. **B7** `CamelCaseStringEnumConverter.cs:33-36` — `JsonStringEnumConverter(JsonNamingPolicy, bool)` 需动态代码。实测 IL3050 @ (35,27)。（是否为默认注册路径影响严重级别，**【需实测】**）
8. **M13** `Directory.Build.props` — 严格模式未作用于库工程，导致上述告警不会被 `-p:AotStrictMode=true` 拦截（"严格模式可复现"前提不成立）。

### 3.2 建议改进项（Major / Minor）

**Major**
1. **M1** `DefaultHttpRequestExecutor.cs:153,222` — XML 反序列化缺 RUC（实测 2×IL2026）。
2. **M2** `ServiceCollectionExtensions.cs:205,288,372,441,495,549,605,779,806,834` — `Configure<T>`/`Configure<JsonSerializerOptions>` 缺 RUC/RDC/DAM（实测 IL2026/IL3050/IL2091）。
3. **M3** `DefaultSensitiveDataMasker.cs:128` — IL2070 压制挂错方法 + category 不规范。
4. **M4** `ISensitiveDataMasker.MaskObject` 接口缺 RUC/RDC（实测 IL2046+IL3051）。
5. **M5** `IUrlParameterFormatter.Format` 接口缺 RUC/RDC（实测 IL2046+IL3051）。
6. **M6** `IEncryptableHttpClient.EncryptContent(object,...)` 接口缺 RDC；实现缺 RUC（实测 IL3051）。
7. **M7** AOT005 `[Query]` 对字典/泛型集合/`IQueryParameter` 误报。
8. **M8** AOT005 `[Query]` 分支缺 `TypeFullName` → CodeFix 不可用。
9. **M9** AOT007 CodeFix 定位不匹配，实际永不提供；现有测试掩盖缺陷。
10. **M10** `CollectCoveredTypes` 漏检引用程序集 Context（PE 引用）→ AOT004 漏报 / AOT006 误报。**【需实测】**
11. **M11** props 未注册 `Nullable`/`HttpClientOptionsName` → 两项配置静默失效。
12. **M12** 三份 `AotStrictMode` 清单缺 `IL3051;IL2091`。
13. **M14** `test.ps1 -AOT` 未跑严格模式、未覆盖 FullTrim/PackageRef Demo、未覆盖三 RID。
14. **M15** `AotFullTrimVerificationDemo` 无 `AOT_OK`/退出码、未覆盖生成器路径。
15. **M16** `AotVerificationDemo` 的 `AOT_OK` 无条件打印、HTTP 场景吞异常。
16. **M17** 缺 `AotDtoCoverageAnalyzer` 专用测试与 AOT005 分析器级测试。
17. **M18** `Diagnostics.cs` AOT 区间注释过时（AOT001-005 → AOT001-007）。

**Minor**：见 §1.3 表格（6 项，含 `QueryParameterBinder` 深度超限回退反射、`RequestBuilder` 反射回退、`AotXmlCodeFixProvider` 阻塞式 `.Result`、压制 category 写法、文档口径）。

### 3.3 已正确处理的点（确认无误）

1. **A-4 / BuildOptions 运行时分流**：`HttpContentSerializerFactory.BuildOptions` 用 `RuntimeFeature.IsDynamicCodeSupported` 正确区分 AOT（仅源生成 context）与 JIT（额外 Combine `DefaultJsonTypeInfoResolver`）；AOT 且无 resolver 时只挂 `MudHttpJsonContext.Default`，杜绝静默反射回退。**设计正确**，仅需补压制（B4）。
2. **A-5 闭合泛型注册**：`JsonContextGenerator.CollectSerializableTypes` 显式注册闭合泛型（`List<UserDto>`、`FeishuApiResult<T>`），解包 `Nullable<T>`，跳过开放泛型定义，递归注册类型参数；`ScanHttpClientApiTypes` 同时扫描接口继承链方法，并用 `GetMethodSerializationMethod` 排除 FormUrlEncoded/Xml 方法（与 AOT004 误报修正同源）。
3. **A-6 命名策略冲突检测**：AOT001 实现完整（`CheckDuplicateSerializerClassNameConflicts`）。
4. **C-10 AOT007 XML 判定一致性**：`AotXmlRejectionAnalyzer` 严格复用 `MethodAnalyzer.AnalyzeMethod` 的 `SerializationMethod` / `ResponseContentType` / `GetEffectiveContentType()`（`BodyContentType ?? MethodContentType`）+ `ContentTypeHelper.IsXmlContentType`，与 `RequestBuilder`(303-315) / `MethodGenerator`(448) 的判定同源，未另立标准。
5. **C-10 双保险**：`ConstructorGenerator.cs:266-288` 在 `IsAotEnabled` 下把 `XmlSerializer` 静态字段改为"延迟抛 `PlatformNotSupportedException` 的表达式体属性"，既保证 `{safeFieldName}` 引用可编译，又避免静态字段初始化触发 IL3050 —— AOT007 的防御纵深正确。
6. **D-11 AOT 属性注册**：`build/Mud.HttpUtils.Generator.props:16-17` 已注册 `IsAotCompatible`/`PublishAot`；三个 Demo 额外显式声明（应对 `OutputItemType="Analyzer"` 场景），幂等无害。
7. **D-12 分析器健壮性**：两分析器均复用 `SyntaxTrees → SemanticModel.GetDeclaredSymbol` 模式，尊重 `context.CancellationToken`；`AotXmlRejectionAnalyzer` 对单个方法 `try/catch continue`（85-93），不阻断其他诊断。
8. **D-13 诊断级别**：AOT004/005/006 = Warning，AOT007 = Error（`Diagnostics.cs:272-317`），脚手架扫描 AOT004 = Info，符合约定；AOT007 含 `description` 与 `helpLinkUri`。
9. **E-15 FormUrlEncoded 误报修正**：`AotDtoCoverageAnalyzer.cs:324-330` 明确 `continue` 跳过 AOT004，且有 `Aot004FormUrlEncodedTests`（正/反向）回归。
10. **E-17 类型解包**：`ExtractTaskInnerType`（Task/ValueTask）、`IsCovered`（`Nullable<T>`、单类型参数集合）、`IsPrimitiveOrString`（`Nullable<T>` 递归）均已实现。
11. **E-18 AOT006 作用域**：`AnalyzeHttpJsonSerializableCoverage` 只遍历当前 `compilation.SyntaxTrees`，不核验引用程序集声明类型，符合"各程序集各自生成"的约定（引用程序集 Context 收集问题见 M10，属另一维度）。
12. **F-19 Masker 行为对齐**：`AotSafeSensitiveDataMasker.Mask` 与 `DefaultSensitiveDataMasker.Mask` 的掩码常量（`"***"`）、边界（`value.Length <= prefixLength + suffixLength`）、三个模式分支完全一致；`MaskObject` 的差异（未注册类型返回 `[TypeName]`、不做反射）已在 XML 文档中显式声明；Demo 以子类化 + `Register<T>` 注册，未再 shadowing。
13. **F-20 `EncryptContent` 标注**：`EncryptContent<T>` 泛型重载无 `[RequiresDynamicCode]`，内部用 `_contentSerializer.Serialize<T>` + `Utf8JsonWriter` 构建外层 JSON（无 `Dictionary<string,object>` 反射序列化）；`object` 重载保留 RDC + `[Obsolete]`；`AotAttributeAnnotationTests` 断言存在且消息引导"强类型"。
14. **内置上下文条件编译**：`MudHttpJsonContext`/`OAuth2JsonContext` 用 `#if NET8_0_OR_GREATER` 包裹，多目标 `netstandard2.0;net6.0;net8.0;net10.0` 下不会在低版本编译失败。
15. **生成代码 Body 序列化收敛**：`RequestBuilder.cs:353-359` Body 统一委托 `_contentSerializer.ToHttpContent<T>`（泛型），FormUrlEncoded 走编译期属性枚举（`GenerateUrlEncodedBodyParameter`，441-490），`MethodGenerator` 对间接 JSON 路径使用方法级 `[UnconditionalSuppressMessage]` 并写明 justification —— 与"生成代码不直接反射"的设计一致。

---

## 4. 复现指引

```powershell
# 1) 复现库内 IL 告警（本报告 §0.1 全部证据）
dotnet build "d:\Repos\MudHttpUtils\Mud.HttpUtils.Client\Mud.HttpUtils.Client.csproj" `
  -f net8.0 -c Release --no-incremental -v n 2>&1 |
  Select-String -Pattern "warning IL" | ForEach-Object { $_.Line }

# 2) AOT 发布验证（当前 test.ps1 实际行为；注意未传 AotStrictMode）
.\test.ps1 -AOT

# 3) 严格模式（当前仅作用于 Demo 项目，不作用于库）
dotnet publish "d:\Repos\MudHttpUtils\Demos\AotVerificationDemo\AotVerificationDemo.csproj" `
  -c Release -f net8.0 -r win-x64 -p:AotStrictMode=true

# 4) 完整裁剪验证（当前 test.ps1 未驱动，需手工执行）
dotnet publish "d:\Repos\MudHttpUtils\Demos\AotFullTrimVerificationDemo\AotFullTrimVerificationDemo.csproj" `
  -c Release -f net8.0 -r win-x64 -p:AotStrictMode=true

# 5) NuGet 消费方路径（需先 dotnet pack）
dotnet publish "d:\Repos\MudHttpUtils\Demos\AotPackageRefDemo\AotPackageRefDemo.csproj" `
  -c Release -f net8.0 -r win-x64 -p:AotStrictMode=true
```

> 说明：§0.1 的 IL 告警数据由命令 (1) 实测取得，已在本报告中直接引用。凡标注 **【需实测】** 的条目（B7 默认注册路径、M10 引用程序集覆盖、G-21~G-23 发布产物行为）需在命令 (2)~(5) 下进一步确认。
