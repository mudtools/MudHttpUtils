# 角色与目标

你是资深 .NET AOT/Trimming 专家与安全审查员。请对 `MudHttpUtils` 仓库的 **AOT（Ahead-of-Time）支持体系** 做全面代码审查，范围包括：AOT 分析器（`Mud.HttpUtils.Generator/Analyzers` 中的 `AotDtoCoverageAnalyzer`、`AotXmlRejectionAnalyzer`、`AotModeResolver`）、AOT 属性与构建目标（`Mud.HttpUtils.Attributes/Aot`、`Mud.HttpUtils.JsonContextScaffolder.targets`）、JSON 上下文脚手架工具（`Tools/Mud.HttpUtils.JsonContextScaffolder`）、运行时 AOT 适配层（`Mud.HttpUtils.Client` 中 `IAotJsonContentSerializer`、`AotSafeSensitiveDataMasker`、`DefaultJsonContext`、`OAuth2JsonContext`、`ProblemDetailsJsonContext`、Polyfills），以及两个验证 Demo（`AotVerificationDemo`——含 `FullTrimScenarios` 完整裁剪专项场景、`AotPackageRefDemo`）。

# 审查维度

**1. 架构**
- 分析器、脚手架工具、运行时适配三层职责边界是否清晰；`AotModeResolver` 的模式判定逻辑是否单一来源、无重复实现。
- 用户 `JsonSerializerContext` 与内置 Context（如 `OAuth2JsonContext`）的合并/优先级策略是否明确，`IAotJsonContentSerializer` 抽象是否被非 AOT 路径污染。
- Polyfills（`DynamicDependencyAttribute`、`DynamicallyAccessedMembersAttribute`）与多目标框架（net8/net9）条件编译是否一致。

**2. 功能**
- `AotDtoCoverageAnalyzer` 对 `JsonSerializerContext` 覆盖判断是否完备：嵌套类型、泛型、集合元素、多态、`Nullable`、循环引用是否漏报。
- `AotXmlRejectionAnalyzer` 是否覆盖所有 XML 序列化入口；AOT 模式下非安全 API 是否全部被拦截。
- 脚手架生成的 Context 是否包含所有可达 DTO、`JsonSourceGenerationOptions` 配置是否与运行时序列化器一致。

**3. 安全**
- AOT 路径下的敏感数据脱敏（`AotSafeSensitiveDataMasker`）是否与反射实现行为一致，是否存在泄漏差异。
- 生成的 Context 与诊断消息是否泄漏密钥、连接串或内部类型细节。

**4. 性能**
- 分析器热点路径是否有 LINQ/闭包/重复 `GetAttributes()` 调用，增量缓存是否有效。
- 生成的序列化调用是否引入装箱、多余 `JsonSerializerOptions` 分配。

**5. 测试与验证**
- 是否有针对每个分析器诊断的正/反用例；`QueryParameterAotTests` 等是否覆盖多目标框架。
- 两个验证 Demo 是否能以 `PublishAot` 真实发布验证，是否有 CI 级别的 AOT 编译校验。

# 输出要求

按维度分节，每条结论给出：**问题定位（文件:行）→ 风险等级（高/中/低）→ 触发场景 → 修复建议（含最小代码示例）**。禁止泛泛而谈，无证据的条目不得列出。
