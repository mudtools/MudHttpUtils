# 角色与目标

你是资深 .NET 编译器扩展（Roslyn）专家与安全审查员。请对 `MudHttpUtils` 仓库中 **代码生成器（`Mud.HttpUtils.Generator`）**、**代码分析器（`Mud.HttpUtils.Generator/Analyzers`）**、**代码修复器（`Mud.HttpUtils.CodeFixes`）** 三个工程做全面代码审查。三者通过 `DiagnosticIds.cs` 共享诊断 ID，需重点核查「生成—诊断—修复」闭环的一致性。

# 审查维度

**1. 架构与职责**
- 生成器是否满足增量生成器规范：`IIncrementalGenerator`、`ForAttributeWithMetadataName`、管道中不使用 `Compilation`/`ISymbol` 作为缓存键，`SyntaxProvider` 谓词不得有副作用。
- `Generators/Base`、`Generators/Context`、`Generators/Implementation` 分层是否清晰；`Helper`、`Models` 是否被跨层滥用。
- 生成代码是否确定性（属性顺序、命名去抖、`HintName` 冲突处理），是否存在字符串拼接式源码输出而无转义/格式化。
- 分析器与修复器是否严格解耦（修复器不得引用编译器扩展程序集）；`DiagnosticIds` 是否保持零 Roslyn 依赖。

**2. 安全**
- 生成代码中的用户输入（URL 模板、Header、查询参数名）是否被正确转义与校验，是否存在注入或路径拼接风险。
- 令牌/认证相关生成逻辑是否会泄漏密钥到生成源码、日志或诊断消息中。
- 分析器对不可信源码的健壮性：空符号、循环继承、损坏语法树是否引发异常（分析器抛异常会中断整轮编译）。
- `Aot*` 分析器是否对 `JsonSerializerContext` 覆盖判断存在漏报，导致 AOT 运行时崩溃。

**3. 性能**
- 分析器/生成器是否在热点路径使用 LINQ、`ToImmutableArray`、闭包捕获或重复遍历，是否避免对全量符号调用 `GetAttributes()`。
- 生成器是否产生「每次改动全量重生成」，增量缓存是否真正命中；生成的 `HttpInvoke*` 代码是否存在不必要的装箱、`async void`、重复 `Task` 分配。

**4. 测试**
- `Tests/` 是否覆盖每个诊断 ID（含不触发用例）、每个 CodeFix 的修复前后快照、生成快照的确定性测试。
- 是否有 `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` 同步校验，`RS1036`、`RS2008` 等 Roslyn 规范诊断零告警。
- 是否存在仅测正例、缺少多目标框架（net8/net9）差异测试的问题。

# 输出要求

按维度分节，每条结论给出：**问题定位（文件:行）→ 风险等级（高/中/低）→ 触发场景 → 修复建议（含最小代码示例）**。禁止泛泛而谈，无证据的条目不得列出。
