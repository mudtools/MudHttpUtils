# 09 · Generator 审计复核修复与功能完善方案

> **编制依据**：本轮**专项深度审查**（聚焦四主线：A 生成器性能 / B 架构逻辑链条完整性 / C 资源泄漏 /
> D 多应用隔离安全与功能），对 22 条已知高风险锚点逐条核实（15 条不成立 / 6 条部分成立 / 1 条成立但已文档化），
> 基于当前工作区源码（`git status` 干净，master @ c202c41）+ 36 份快照基线 + 运行时消费点（Abstractions / Client）
> 交叉验证 + 两个子审计（运行时执行器契约、测试与快照盘点）。
>
> **编号体系**：本轮 **`G9-01` ~ `G9-12`**，批次 **`G9-A ~ G9-C`**；
> 与 `F*/E*`（05 册）、`GEN-*`（06 册）、`G7-*`（07 册）、`G8-*`（08 册）、`MT-*`（多应用册）、`HC-*`、`CFG-*`
> **相互独立、互不复用**。
>
> **与历史分册关系（防重复立项的关键边界）**：
>
> | 分册 | 已覆盖内容 | 本册处理方式 |
> | --- | --- | --- |
> | `08-Generator四主线专项审查修复与功能完善方案.md`（已实施） | G8-01～G8-21：注入模式口径、继承矩阵（HTTPCLIENT035）、BufferSize 夹取（HTTPCLIENT036）、空管理器解析期消息（G8-09）、`UseAppScope` 避让（G8-15）、令牌头名单一事实源（G8-20/21） | **不重复**。本册核实其全部落地（§7 对账）；仅在 G8-06 未覆盖的 ns2.0 分支（G9-01）与 G8-09 未覆盖的旁路可观测性（G9-09，决策记录）上承接 |
> | `07-Generator专项审查修复与功能完善方案.md`（已实施） | G7-01～G7-19：appManager 透传、下载原子落盘、HTTPCLIENT033、配置值快照、类型解析收敛（G7-07）、所有权文档（G7-09） | **不重复**；G9-05 仅对 G7-07 的**快速路径**做缓存增强与文件级命名空间补齐，不改判定语义 |
> | `多应用与令牌管理-Bug修复与功能完善方案.md` | MT-01～MT-27（默认拒绝、BindTenantGuard、Cookie 转义） | **不重复**；G9-02 仅统一其**异常消息文本化口径**（行为不变） |
> | `Mud.HttpUtils.Generator 生成代码全面审查报告.md`（历史报告） | 2.1.1 multipart 未释放、2.1.2 StreamContent 泄漏、1.2.2 AppContextScope 线程安全等 | **已核实全部失效（已修复）**：`RequestBuilder.cs:693`（using var）、`RequestBuilder.cs:835-853`（G8-05 受保护形态）、`AsyncLocalAppContextSwitcher.cs:53-66`（Interlocked + B8 归属判定）。列入 §7 防复立项 |
>
> **实施前复核结论（2026-09-29，独立审计）**：12 项逐条对照工作区源码核实，**问题全部属实、方案总体可行，
> 无过度设计、与既有功能无重复**（架构能力盘点表逐行验证通过）。三处方案缺口已在本册就地修订：
> ① **G9-01 测试方案**：`Client.Tests` 目标框架为 `net6.0;net8.0;net10.0`（无 netstandard2.0），
>    原方案「ns2.0 TFM 测试」永不执行、会得到假通过——已改为「全 TFM 编译 helper + 源码文本钉子」
>    （仓库对不可执行 `#if` 分支的既有守卫策略，`MultiTfmCompilationGuardTests` 先例）；
> ② **G9-02 前提修正**：`Abstractions/AssemblyInfo.cs:3` 实际存在 `InternalsVisibleTo("Mud.HttpUtils.Client")`，
>    「Client 不依赖 InternalsVisibleTo」表述不成立（选公开门面仍是对的，理由改为降耦合）；
>    `DefaultAppManager` 先行使用处为 **6 处**（39/111/114/129/140/349），非 7 处；
> ③ **G9-07 FormContent 接线**：`FormContentGenerator` 曾因 F-06 **刻意删除** `AnalyzerConfigOptionsProvider`
>    的 Combine（从未消费、纯缓存击穿）——接线必须复用 G7-06 值相等快照模式
>    （`Select(GeneratorConfigSnapshot.Create) + WithComparer(Comparer)`），而非重新引入裸 Combine。
> 另：G9-03 的 `.gitignore` 追加规则需同步改写既有反选规则（`!/Tests/Mud.HttpUtils.Generator.Tests/*.verified.txt`
> 会把孤儿合法化，git 规则后者胜，仅追加不改注释会留下自相矛盾的规则文本）。
>
> **前提**：项目尚未正式发布（`PublicAPI.Shipped.txt` 全空），**不承担兼容义务**；本册**无破坏性变更项**（§5）。
>
> **硬约束**（承接 07/08 册，本册继续遵守）：
> 1. 生成器不得引入新的运行时反射（Native AOT 为一级目标）；
> 2. 每条修复必须配**能拦住它的测试**（快照 / 契约 / 运行时 / 编译断言），不接受「只改代码不加守卫」；
> 3. 生成器**不引用** Attributes/Abstractions 程序集，跨包一致性靠字符串常量 + 契约测试；
> 4. 多目标框架 `netstandard2.0;net6.0;net8.0;net10.0` 条件编译分支必须**行为语义一致**
>    （允许 API 差异，**不允许安全语义差异**）——G9-01 即该约束的整改项；
> 5. 本轮**不新增诊断 ID**（无需同步 `AnalyzerReleases.Unshipped.md` / README 诊断表 / `DiagnosticReachabilityTests`）。

---

## 0. 架构能力盘点（修复必须复用，禁止平行机制）

| 现有能力 | 位置 | 本册复用于 |
| --- | --- | --- |
| `AppKeyValidator.ToSafeText`（internal，Abstractions 内）与公开门面 `AppKey.ToSafeText`（截断 128 + 控制字符过滤） | `Abstractions/AppContext/AppKeyValidator.cs:47-60`、`Abstractions/AppContext/AppKey.cs:66-79` | **G9-02** 异常消息文本化（`DefaultAppManager` 六处已先行使用，本册对齐两个离群点；Client 侧走公开门面 `AppKey.ToSafeText`） |
| `CopyToWithProgressAsync(stream, dst, bufferSize, progress, offset, ct)`（ns2.0 分支已传 CT） | `Client/HttpClient/DefaultHttpRequestExecutor.cs`（DownloadLargeCoreAsync 内） | **G9-01** 复用同一 CT 语义，仅替换 `progress == null` 快速路径；不重写进度路径 |
| 临时文件 `.mudtmp` + `File.Move` 原子落盘 + catch 清理（G7-03） | `DefaultHttpRequestExecutor.cs:593-678` | G9-01 **不改落盘逻辑**，仅让取消提前发生；`DownloadCleanupTests` 既有断言保持全绿 |
| `PartialThenBlockingStream` / `ThrowingAfterBytesStream` 测试基建 | `Tests/Mud.HttpUtils.Client.Tests/Http/DefaultHttpRequestExecutorTests.cs`、`Tests/Mud.HttpUtils.Client.Tests/Client/DownloadCleanupTests.cs` | **G9-01** 取消时延用例直接复用 |
| `AddMudHttpClient(clientName, configure, setAsDefault)` + `RegisterNamedClient`（TryAddTransient 先者胜 / AddTransient 后者覆盖） | `Client/ServiceCollectionExtensions.cs:100-158, 209-259` | **G9-04** 胜者语义钉子测试的对象（只测不改） |
| `ConditionalWeakTable<Compilation, …>` 静态缓存模式（NEW-GEN-02 先例） | `Generators/Implementation/InterfaceImplementationGenerator.cs:28`、`Helper/SemanticModelCache.cs:30` | **G9-05** 源码命名空间集合缓存（同模式、同失效语义） |
| `compilation.GetTypeByMetadataName` + `SymbolEqualityComparer`（`:372-377` mudAppContextType 先例） | `InterfaceImplementationGenerator.cs:372-386` | **G9-06** `ICurrentUserId` 精确匹配（同口径，不引入字符串前缀新惯例） |
| `GeneratorContext.EmitNullableEnable`（init 只读，构造期注入） | `Generators/Context/GeneratorContext.cs:63/217` | **G9-07** 重构目标形态参照：实现类文件路径（`ClassStructureGenerator.cs:32`）已是参数传递，注册文件路径对齐之 |
| `TestAnalyzerConfigOptionsProvider`（build_property 注入）+ `CreateTrackedDriver` | `Tests/.../Infrastructure/TestAnalyzerConfigOptionsProvider.cs`、`Incremental/IncrementalPipelineTests.cs:448` | **G9-08** force/normal 双 provider 行为钉子 |
| `TestRepoRoot`（仓库根定位） | `Tests/.../Infrastructure/TestRepoRoot.cs` | **G9-03** 基线卫生守卫测试 |
| `SnapshotInputSources.All` + `GeneratorCompileAssert.RunAndAssertNoErrors` | `Tests/.../Generators/GeneratorSnapshotCompileTests.cs`、`Infrastructure/GeneratorCompileAssert.cs` | G9-05/06/07 重构后回归（快照零 diff + 编译零 Error） |
| G8-09 解析期 actionable 异常（空管理器根因 + 修复步骤） | `Generators/HttpInvokeRegistrationGenerator.cs:610-642` | **G9-09** 决策记录的既有缓解证据（不再重复立项运行时通道） |

> **架构原则**：本册全部修复落在既有扩展点上，不新增可写状态、不新增诊断 ID、不新增平行解析机制；
> G9-01 是唯一的行为变更（补齐 ns2.0 取消语义，向 NET6+ 分支对齐），其余为测试钉子 / 重构 / 文档 / 决策记录。

---

## 1. 结论摘要

| 维度 | 结论 |
| --- | --- |
| 总体风险 | **🟢 无阻塞合并项**。22 条锚点核实后，四主线均不构成 🔴；本轮全部为 🟡/🔵 收敛项 |
| 缺陷分类 | ① **跨 TFM 行为不一致**（G9-01：ns2.0 下载取消不响应——硬约束 4 的唯一实锤违例）；② **运行时与生成侧纪律不一致**（G9-02：异常消息插值原文 vs 生成侧静态消息）；③ **测试基线卫生**（G9-03：孤儿快照）；④ **测试钉子缺失**（G9-04/08：已文档化语义无运行期钉子）；⑤ **局部质量**（G9-05/06/07） |
| 修复成本 | G9-A 约 120–180 行 + 测试；G9-B 约 80–140 行 + 快照回归；G9-C 纯测试/文档/决策记录 |
| 黄金指标 | GM1 全解决方案 build + `test.ps1` 全绿；GM2 ns2.0 取消用例通过且 `.mudtmp` 零残留；GM3 基线卫生检查通过；GM4 现有 36 份快照**零 diff**（G9-05/06/07 为无行为变更重构）；GM5 本轮零新增诊断 ID |
| 存疑闭环 | 上一轮存疑 2（GeneratorVersion bump 纪律）**已闭合**：`GeneratorVersion` 取自生成器程序集版本（`GeneratedCodeConsts.cs:17-30`），版本号由 `Directory.Build.props:3 <Version>2.0.9</Version>` 单源驱动，包升级自动翻转 salt（E-3），**无需人工 bump、无需守卫测试**（§6） |

### 1.1 项清单与优先级

| # | 级别 | 类型 | 标题 | 批次 |
| --- | --- | --- | --- | --- |
| G9-01 | 🟡 P1 | 跨 TFM 行为不一致 | ns2.0 大文件下载 `CopyToAsync` 缺 `CancellationToken`，下载期间取消不响应 | G9-A |
| G9-02 | 🟡 P1 | 安全纪律一致性 | 运行时安全异常插值 appKey/userId 原文（`BindTenantGuard` / 用户身份不一致），与生成侧「静态消息」纪律分叉 | G9-A |
| G9-03 | 🟡 P1 | 测试基线卫生 | 项目根存在陈旧孤儿快照（与 `Snapshots\` 同名文件内容不同），并有被误信为基线的风险 | G9-A |
| G9-04 | 🟡 P2 | 测试钉子 | 类型级 `IEnhancedHttpClient` 解析胜者语义（先注册者胜 / `setAsDefault` 覆盖）无运行期钉子；README 快速开始缺醒目警示 | G9-B |
| G9-05 | 🔵 P2 | 性能 | `FindTypeInSourceNamespaces` 每个未解析简单类型名重复全树枚举，且快速路径**漏文件级命名空间**（`FileScopedNamespaceDeclarationSyntax`） | G9-B |
| G9-06 | 🔵 P2 | 功能缺陷（边缘） | `DetectICurrentUserId` 按简单名匹配，消费方同名异源接口会误触发 `CurrentUserId` 发射 | G9-B |
| G9-07 | 🔵 P2 | 质量（重构） | `EmitNullableEnable` 为生成器实例上的可变 protected 属性，注册文件路径依赖「先赋值后使用」顺序纪律 | G9-B |
| G9-08 | 🔵 P3 | 测试钉子 | `ForceHttpGenerator` salt 仅间接覆盖（salt 值翻转），补行为级钉子（`IncrementalCacheForcedInvalidation` 出现性） | G9-C |
| G9-09 | 🔵 P3 | 决策记录 | 空 `DefaultAppManager` 自动注册的 `LogWarning` 在裸容器下静默——维持现状（G8-09 已缓解主路径），补文档 | G9-C |
| G9-10 | 🔵 P3 | 文档 | 受信实例入口（`BeginScope(IMudAppContext)`/`SwitchTo`/`Current` setter）补「实例即凭据」威胁模型说明 | G9-C |
| G9-11 | 🔵 P3 | 决策记录 | TokenManager/HttpClient 模式裸注册缺 DI 依赖不做编译期诊断（生成注释 + README 已覆盖，强报必为噪音） | G9-C |
| G9-12 | — | 存疑闭环 | `GeneratorVersion` 机制自洽（程序集版本单源），不立项 | §6 |

---

## 2. 详细方案

### G9-01（P1）：ns2.0 大文件下载补齐取消响应

- **位置**：`Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs` —— NET6+ 分支 `CopyToAsync(…, cancellationToken)`
  与 ns2.0 分支 `CopyToAsync(fileStream, effectiveBufferSize)`（DownloadLargeCoreAsync 内两段 `#if` / `#else`）。
- **现状与证据**：

```csharp
#if NET6_0_OR_GREATER
    if (progress == null)
        await contentStream.CopyToAsync(fileStream, effectiveBufferSize, cancellationToken).ConfigureAwait(false);   // ✅ 传 CT
#else
    if (progress == null)
        await contentStream.CopyToAsync(fileStream, effectiveBufferSize).ConfigureAwait(false);                     // ❌ 无 CT
    else
        await CopyToWithProgressAsync(contentStream, fileStream, effectiveBufferSize, progress, 0, cancellationToken)... // ✅ 传 CT
#endif
```

  即 ns2.0 下**仅** `progress == null` 快速路径不响应取消：客户端取消令牌在大文件 body 拷贝主耗时段不被观察，
  取消要等下载完成才生效。异常/取消后 `.mudtmp` 仍由 catch（`:663-678`）清理，**正确性不受影响，属响应性缺口**。
  附注：ns2.0 的 `HttpContent.ReadAsStreamAsync()` 本就无 CT 重载（.NET 5+ 才有），故只能在拷贝层补救。
- **方案**：ns2.0 快速路径改为**分块拷贝 + CT 检查**，并让 `bytesWritten` 语义与现实现一致（`fileStream.Position`）。
  **复核修订**：helper **不包在 `#if NETSTANDARD2_0` 内**，编译为全 TFM——`ReadAsync(buffer, 0, len, ct)` /
  `WriteAsync(buffer, 0, len, ct)` 在 ns2.0 与 NET6+ 的 `Stream` 上语义一致，全 TFM 编译使 Client.Tests（NET6+ 宿主）
  可以直接单测该 helper 的行为（同一份源码，ns2.0 编译即为目标修复代码），绕开「ns2.0 分支不可执行」的测试盲区：

```csharp
#else
    using (var fileStream = new FileStream(tmpPath, tmpMode, FileAccess.Write, FileShare.None, effectiveBufferSize))
    using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
    {
        bytesWritten = progress == null
            ? await CopyToWithCancellationAsync(contentStream, fileStream, effectiveBufferSize, cancellationToken).ConfigureAwait(false)
            : await CopyToWithProgressAsync(contentStream, fileStream, effectiveBufferSize, progress, 0, cancellationToken).ConfigureAwait(false);
    }
    // （File.Delete/File.Move 落盘段不变）
#endif

    /// <summary>G9-01：ns2.0 快速路径的分块拷贝——Read/Write 均携带 CT，逐块前置 ThrowIfCancellationRequested，
    /// 使取消时延上限为一个缓冲区周期（与 NET6+ CopyToAsync(ct) 语义对齐）。
    /// 全 TFM 编译（不加 #if）：NET6+ 宿主单测本 helper 行为，ns2.0 分支接线同一实现。</summary>
    private static async Task<long> CopyToWithCancellationAsync(
        Stream source, Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        var buffer = new byte[bufferSize];   // 每次下载一个缓冲区，受 4 MiB 夹取上界约束；如工程已引用 System.Memory 可改 ArrayPool
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            total += read;
        }
        return total;
    }
```

  要点：
  1. **不改** tmp/原子落盘/catch 清理（G7-03 资产保持）；取消提前只会让既有 catch 清理更早触发；
  2. `CopyToWithProgressAsync`（ns2.0 进度路径已传 CT）不动，两路径 CT 语义就此对齐；
  3. 缓冲区一次分配、受 `MaxSupportedBufferSize = 4 MiB` 夹取约束，不引入 LOH 风险（默认 80 KiB）；
  4. NET6+ 分支零改动（硬约束 4：允许 API 差异，不允许语义差异——本项即消除语义差异）；
  5. **（复核修订）** helper 全 TFM 编译而非 `#if NETSTANDARD2_0` 内联——ns2.0 分支在本仓库**无任何可执行测试**
     （见下「测试与守卫」），行为测试必须落在可执行的同源 helper 上。
- **测试与守卫**（`Tests/Mud.HttpUtils.Client.Tests/Http/DefaultHttpRequestExecutorTests.cs`）：
  **（复核修订）** 原方案「ns2.0 TFM 测试」不成立：`Client.Tests.csproj` 目标框架为 `net6.0;net8.0;net10.0`
  （不含 netstandard2.0），`DefaultHttpRequestExecutor` 的 ns2.0 `#else` 分支在全部既有测试中**从未被执行**，
  运行时用例只可能走 NET6+ 分支并「假通过」。改为三层守卫：
  - **L1（helper 行为测试，NET6+ 宿主直接执行）**：`CopyToWithCancellationAsync_FastPath_RespondsToCancellation`
    ——internal 可见（`InternalsVisibleTo("Mud.HttpUtils.Client.Tests")` 已有），复用 `PartialThenBlockingStream`
    （第一块成功后阻塞）+ `cts.Cancel()`，断言 `OperationCanceledException`（或其子类）在 5s watchdog 内抛出；
  - **L2（ns2.0 分支接线文本钉子）**：`NetStandard20Branch_FastPath_MustCarryCancellationToken`
    ——经 `TestRepoRoot` 读取 `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs` 源文本，断言
    **不存在**无 CT 的 `CopyToAsync(fileStream, effectiveBufferSize)` 调用（回归即原文缺陷复现）。
    策略依据：仓库对「测试不可执行的 `#if` 分支」的既有守卫方式是编译级/文本级断言（`MultiTfmCompilationGuardTests`
    专门为此而建，其 remarks 明言「历史缺陷因此无法被发现」）；文本钉子是该策略在库源码上的最小延伸；
  - **L3（运行时清理回归，NET6+）**：由既有 `DownloadLarge_Cancel_Midway_LeavesNoPartialFinalFile` 承担
    （其断言骨架已含「中途取消 + 最终路径与 `.mudtmp` 均不存在」；端到端及时性在 NET6+ 由 `CopyToAsync(ct)` 保证），
    不新增重复用例。
  - 既有 `DownloadCleanupTests` 全套（MidStreamIOException / OverwriteFalse / HugeBufferSize / BufferSizeOutOfRange / Success）
    必须保持全绿（落盘语义未动的回归证据）。
- **验收标准**：GM1 + GM2；L1/L2/L3 入 CI（L2 为源码文本断言，跨 TFM 有效）。

### G9-02（P1）：运行时安全异常消息统一文本化（appKey / userId）

- **位置**：
  - `Mud.HttpUtils.Abstractions/TokenManager/TokenManagerBase.cs:428-430`（`BindTenantGuard`）；
  - `Mud.HttpUtils.Client/TokenManager/DefaultTokenProvider.cs:90-92`（用户身份不一致：结构化日志 + 异常）、`:110-112`（获取用户令牌失败：日志 + 异常）。
- **现状与证据**：

```csharp
// TokenManagerBase.cs:428-430 —— appKey 原文插值
throw new InvalidOperationException(
    $"令牌管理器（{MetricsKey}）已绑定租户 '{existing}'，不能用于租户 '{tenantKey}' 的请求。" + …);
// DefaultTokenProvider.cs:92 —— userId 原文插值（:90 结构化日志同值）
throw new InvalidOperationException(
    $"用户身份不一致：上下文主体用户 '{principalUserId}' 与请求用户 '{request.UserId}' 不匹配，已拒绝获取用户令牌。");
```

  对照生成侧纪律（`ConstructorGenerator.cs:864`「所有异常消息均不插值原始 appKey」，
  由 `ApplicationSwitchGuardContractTests.GeneratedCode_ShouldNotInterpolateRawAppKeyIntoExceptionMessages` 钉死）
  与 Abstractions 既有惯例（`DefaultAppManager.cs:39/111/114/129/140/349` 六处全部经 `AppKeyValidator.ToSafeText`），
  以上两处是**离群点**。注入向量分析：`appContext.AppKey` 来自用户实现的 `IMudAppContext`（接口属性，框架不强制
  再校验），恶意/异常实现可返回含 CRLF/控制字符的字符串 → 进异常消息/日志构成注入面（appKey 经
  `UseApp` 守卫路径时已被 `AppKey.IsValid` 拦截，但 `DefaultTokenProvider` 直取 `appContext.AppKey`，无此前置）。
  **（复核修正）** `Abstractions/AssemblyInfo.cs:3` 实际有 `[assembly: InternalsVisibleTo("Mud.HttpUtils.Client")]`，
  Client 编译期可以直用 internal `AppKeyValidator.ToSafeText`——原方案「不依赖 InternalsVisibleTo」的理由不成立；
  仍选**公开门面** `AppKey.ToSafeText`，理由改为「降低对 Internals 的耦合、与公开 XML 注释契约（IAppManager 已声明
  「异常消息回显必须经 `AppKey.ToSafeText`」）同源」。
- **方案**（行为不变，仅消息文本化）：
  1. `TokenManagerBase.BindTenantGuard`（Abstractions 内，直接用 internal 门面）：

```csharp
     throw new InvalidOperationException(
         $"令牌管理器（{MetricsKey}）已绑定租户 '{AppKeyValidator.ToSafeText(existing)}'，" +
         $"不能用于租户 '{AppKeyValidator.ToSafeText(tenantKey)}' 的请求。" + …);
```

  2. `DefaultTokenProvider`（Client 程序集，用**公开**门面 `AppKey.ToSafeText`——复核修正：并非因
     Internals 不可用，而是降低耦合并与 IAppManager 的公开 XML 契约同源）：
     - `:90-92`：结构化日志（EventId 模板参数）**保留原值**（遥测可由日志管道统一脱敏）；异常消息两处 userId
       包 `AppKey.ToSafeText(...)`（截断 128 + 控制字符过滤，语义即「安全文本化」，方法名虽为 AppKey 但实现通用）；
     - `:110-112`：`request.UserId` 同样包 `AppKey.ToSafeText`；`TokenManagerKey` 为开发者配置型标识符，保持原样。
  3. 可选一致性增强（不阻塞）：`AppKey.ToSafeText` 的 XML 注释补充一句「亦适用于 userId 等外部标识的安全回显」，
     避免后来者误以为仅限 appKey。
- **测试与守卫**（`Tests/Mud.HttpUtils.Client.Tests/`，新增 `Token/SafeTextExceptionMessageTests.cs`
  ——复核修正：既有租户守卫测试目录为 `Token\`（`TokenIsolationAndGuardTests.cs` 已有 `BindTenantGuard`
  经 InternalsVisibleTo 直调的脚手架，新用例与之同目录），原文所写 `TokenManager/` 目录不存在）：
  - `BindTenantGuard_MalformedTenantKey_NoControlCharsInMessage`：自定义 `IMudAppContext`（AppKey 返回
    `"t\u0001A\r\nB"` 类恶意值，绕开 RegisterApp 校验直构上下文）触发 `BindTenantGuard` 拒绝，
    断言异常消息中不出现 `\r` / `\n` / 控制字符（被替换为 `_`）；
  - `UserTokenIdentityMismatch_ExceptionMessage_NoControlCharsInUserId`：`ICurrentUserContext.UserId` 返回含控制字符值，
    断言异常消息已文本化。
- **验收标准**：GM1；新增两用例入 CI；`MT-*` 既有测试（BindTenantGuard 拒绝路径、EventId 162 告警）零回归。

### G9-03（P1）：快照基线卫生——清除孤儿快照 + 防复发守卫

- **位置**：`Tests/Mud.HttpUtils.Generator.Tests/GeneratorSnapshotTests.Snapshot_InheritedFromTokenIdentity_ShouldEmitIdentitySeams.verified.txt`（**项目根**）。
- **现状与证据**：Verify 的默认落盘是「测试类所在目录」，`GeneratorSnapshotTests.cs` 位于 `Snapshots\`（基线目录正确），
  `HeaderCollectionBindingTests.cs` 位于 `Generators\`（其 3 份快照为**默认行为、非散置错误**）。
  项目根的同名 `.verified.txt` 是**测试类早先位于项目根时的接收文件残留被误提交**，内容为旧版（506 行 vs 现行 547 行），
  不参与任何测试执行，但对基线阅读者构成「双基线」误信风险（本次审查即被其误导过一次）。
- **方案**：
  1. 删除项目根孤儿文件（git 历史可溯，无信息丢失）；
  2. `.gitignore` 规则改写（**复核修订**：现行规则 `!/Tests/Mud.HttpUtils.Generator.Tests/*.verified.txt`
     反选规则会把根目录孤儿「合法化」且其注释描述的历史场景（测试类曾位于项目根）已不存在——
     测试类现居 `Snapshots\` / `Generators\` 子目录，子目录基线不受任何忽略规则影响。因此改写而非仅追加，
     避免留下自相矛盾的规则文本）：

```gitignore
     # Verify 快照：.received.txt（失败产物）与项目根层级的 .verified.txt（孤儿残留）不入库；
     # 基线只允许存在于测试类旁的子目录（Snapshots\ / Generators\），须显式 accept 后提交。
     /Tests/Mud.HttpUtils.Generator.Tests/*.received.txt
     /Tests/Mud.HttpUtils.Generator.Tests/*.verified.txt
     ```
  3. **可选项（不阻塞）**：统一基线目录——在 `VerifyTestAssemblyConfiguration.cs` 增加
     `[module: VerifyTests.DerivePathInfo(…)]` 把全部快照路由到 `Snapshots\`，并把 `HeaderCollectionBindingTests`
     的 3 份基线迁入（一次性 `accept`）。收益是「单一基线目录」；成本是破坏 3 份基线的路径惯性。
     建议**本轮只做 1+2**，可选项随下次基线大批量变更时顺带执行。
- **测试与守卫**：新增 `Tests/Mud.HttpUtils.Generator.Tests/Guards/SnapshotBaselineHygieneTests.cs`：
  - `NoVerifiedSnapshotAtProjectRoot`：经 `TestRepoRoot` 定位仓库根，断言
    `Tests/Mud.HttpUtils.Generator.Tests/*.verified.txt` 不存在（防孤儿复活）。
- **验收标准**：GM3；孤儿文件从 git 索引移除。

### G9-04（P2）：命名客户端解析胜者语义的运行期钉子 + README 醒目警示

- **位置**：`Client/ServiceCollectionExtensions.cs:252-259`（被测对象，只测不改）；
  `Generator/README.md`「快速开始」章节（补警示）。
- **现状与证据**：实现类注入**类型级** `IEnhancedHttpClient`，多个命名客户端共存时 `TryAddTransient` **先注册者胜**，
  仅 `setAsDefault: true`（`AddTransient`）可后注册覆盖；因此 `[HttpClientApi(Timeout)]` 写入的
  `{接口名}_HttpClient` 命名客户端配置，仅在该名称成为默认时对实现类生效（HTTPCLIENT033 Info + README
  「生成客户端命名（G7-04a）」三条结论已文档化）。缺口：该**胜者语义**只有文档与生成器侧 Info，
  没有运行期测试钉子——运行库重构（如改 keyed）或文档漂移时无回归报警。
- **方案**：
  1. 新增运行期钉子测试（`Tests/Mud.HttpUtils.Client.Tests/Client/NamedClientResolutionPinTests.cs`）。
     **（复核确认）** 方案 A 可行：`AddMudHttpClient` 返回 `IHttpClientBuilder`，测试在其返回值上
     `ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler("A_Client"))` 即可——handler 工厂动作按注册
     顺序执行、后注册者胜出为 PrimaryHandler（NET6+ 非严格模式下 `AddMudHttpClient` 内部未注册 primary handler，
     记录型 handler 即唯一 primary），无真实网络（记录型 handler 回 200 空响应）：

```csharp
     [Fact]
     public async Task TypeLevelEnhancedHttpClient_ResolvesToFirstRegisteredNamedClient()
     {
         var services = new ServiceCollection();
         var hits = new List<string>();
         services.AddMudHttpClient("A_Client", c => c.BaseAddress = new Uri("https://a.invalid/"));
         services.AddMudHttpClient("B_Client", c => c.BaseAddress = new Uri("https://b.invalid/"));
         // 观测手段（二选一，优先 A）：
         // A. 各命名客户端 ConfigurePrimaryHttpMessageHandler 注入记录型 handler（记录 clientName），
         //    解析 services.BuildServiceProvider().GetRequiredService<IEnhancedHttpClient>() 并发一次请求，
         //    断言 hits.Single() == "A_Client"（先注册者胜）；
         // B. 若 HttpClientFactoryEnhancedClient 暴露可观测的 clientName（internal + InternalsVisibleTo），直接断言。
     }

     [Fact]
     public async Task SetAsDefault_LaterRegistration_OverridesFirstWins()
     {
         // 同上但第二个客户端 setAsDefault: true → 断言命中 "B_Client"
     }
     ```
  2. README「快速开始」在「2. 注册服务」处加一句加粗警示（已落地于 Generator README）：
     **「多接口工程必读：`[HttpClientApi(Timeout)]` 仅在对应命名客户端被注册为默认 `IEnhancedHttpClient` 时生效，
     详见『生成客户端命名（G7-04a）』。」**
     **（实施验证修正）** Client README 无「快速开始」章节（结构为概述 → 目标框架 → 包含内容…），
     其 Client 侧警示由既有 `GeneratedClientOptions` 章节的 G7-04a 完整注记（含 `TryAdd` 先者胜语义与
     `HTTPCLIENT033` 指引）承担，不重复添加。
  3. **不做**（本轮明确排除）：keyed 逐接口解析属破坏性架构变更（每个接口一份 `IEnhancedHttpClient` 语义），
     若未来立项须独立方案册评估 handler 管道/缓存/热更新连带影响——本册仅立测试钉子。
- **验收标准**：GM1；两个钉子测试入 CI；README 两处分册警示就位。

### G9-05（P2）：类型解析快速路径——命名空间集合按 Compilation 缓存 + 补文件级命名空间

- **位置**：`Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs:467-486`
  （`FindTypeInSourceNamespaces`）。
- **现状与证据**：

```csharp
foreach (var tree in _compilation.SyntaxTrees)
{
    var root = tree.GetRoot(cancellationToken: default);
    foreach (var nsDecl in root.DescendantNodes().OfType<NamespaceDeclarationSyntax>())   // ❌ 漏 FileScoped
    { … _compilation.GetTypeByMetadataName($"{nsName}.{typeName}") … }
}
```

  两个问题：① 该全树枚举对**每个**未命中快速路径的简单类型名各执行一次（结果虽经
  `:411-412` 的 `(Compilation, 类型名)` 缓存，但 N 个未知名 = N 次全 SyntaxTrees 扫描，冷启动一次性成本）；
  ② 只识别块级 `NamespaceDeclarationSyntax`，**`namespace X;`（文件级，C# 10 起主流写法）不在快速路径内**，
  全部落到更贵的全树保底扫描（`FindTypeInNamespace`）——正确性由保底兜住，纯性能/命中率问题。
- **方案**（判定语义不变，仅提命中率和复用枚举）：

```csharp
// 同类静态 CWT（NEW-GEN-02 先例：InterfaceImplementationGenerator._typeResolveCache / SemanticModelCache）
private static readonly ConditionalWeakTable<Compilation, IReadOnlyList<string>> _sourceNamespacesCache = new();

private static IReadOnlyList<string> GetSourceNamespaceNames(Compilation compilation) =>
    _sourceNamespacesCache.GetValue(compilation, static c =>
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in c.SyntaxTrees)
            foreach (var ns in tree.GetRoot(cancellationToken: default)
                                   .DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
                names.Add(ns.Name.ToString());          // Base 基类同时覆盖块级与文件级
        return names.ToArray();
    });

private INamedTypeSymbol? FindTypeInSourceNamespaces(string typeName)
{
    foreach (var nsName in GetSourceNamespaceNames(_compilation))
    {
        var type = _compilation.GetTypeByMetadataName($"{nsName}.{typeName}");
        if (type != null) return type;
    }
    return null;
}
```

  要点：① `BaseNamespaceDeclarationSyntax`（Roslyn 4.x）同时覆盖块级与文件级——补齐 `namespace X;` 快速路径；
  ② 命名空间名单每 Compilation 构建一次并弱引用缓存（Compilation 回收即释放，与既有两缓存同失效语义）；
  ③ 全树保底扫描（`:448-455`）与判定顺序完全不变。
- **测试与守卫**：
  - 既有 `TokenHelperTests` / 生成器快照与编译测试全部零 diff（GM4）；
  - 新增快照输入（`Tests/.../Generators/`）：**文件级命名空间**下 `[HttpClientApi(TokenManage = "ITestTokenManager")]`
    简单名引用的用例 `TokenManagerType_ResolvedInFileScopedNamespace_EmitsTypedField`——断言生成的
    `_tokenManager` 字段类型为该类型（命中快速路径），并断言产物编译零 Error
    （`GeneratorCompileAssert.RunAndAssertNoErrors`）。该用例同时是「文件级命名空间补齐」的行为钉子。
- **验收标准**：GM1 + GM4；新用例入 CI。

### G9-06（P2）：`DetectICurrentUserId` 收紧为「元数据名精确匹配」

- **位置**：`Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs:891-895`。
- **现状与证据**：

```csharp
context.ImplementsICurrentUserId = _interfaceSymbol.AllInterfaces
    .Any(i => i.Name == "ICurrentUserId");     // ❌ 简单名匹配
```

  库契约实为 `Mud.HttpUtils.ICurrentUserId`（`Abstractions/AppContext/ICurrentUserId.cs`，成员
  `string? CurrentUserId { get; }`）。消费方自定义同名异源接口（不同命名空间、不同成员）会被误判为实现，
  触发 `CurrentUserId` 成员发射（`ConstructorGenerator.cs:102-120/264-283`），在成员形态不一致时产生
  CS0737/可空性告警等下游噪音。同文件 `:372-377` 校验 `IMudAppContext` 已使用
  `GetTypeByMetadataName + SymbolEqualityComparer` 的规范口径，此处对齐即可。
- **方案**：

```csharp
private void DetectICurrentUserId(GeneratorContext context)
{
    var currentUserIdType = _compilation.GetTypeByMetadataName("Mud.HttpUtils.ICurrentUserId");
    context.ImplementsICurrentUserId = currentUserIdType != null
        && _interfaceSymbol.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, currentUserIdType));
}
```

  （全名常量建议落 `HttpClientGeneratorConstants`，与既有特性名常量同源管理。）
- **测试与守卫**（`Tests/.../Generators/CurrentUserIdDetectionPrecisionTests.cs`，实施验证修正）：
  - `UserDefinedSameNameICurrentUserId_DoesNotEmitCurrentUserIdMember`：接口继承自定义
    `MyCompany.Lib.ICurrentUserId { string Id { get; } }`（基列表全限定避免 CS0104 二义）+ `[Token(RequiresUserId = true)]`。
    **实施验证发现原文断言不可行**：`AnyMethodRequiresUserId` 场景下 `CurrentUserId` 成员总会发射——
    差异在**形态**：误判为库契约实现时发射 get/set 双向穿透 `ICurrentUserContext` 的「实现形态」
    （对同名异源接口 `set` 会穿透写入宿主上下文）；正确判定为非库契约时走表达式体只读回退形态。
    故断言改为：产物**不含**「（ICurrentUserId 实现）」注释与 `set => _currentUserContext.SetUserId(value);` 穿透 setter；
  - `LibraryICurrentUserId_StillEmitsCurrentUserIdMember`：继承库接口的正例保持现状，
    并精确断言 get/set 穿透形态存在（防矫枉过正）。
- **验收标准**：GM1 + GM4（`Snapshot_InterfaceProperties_*` 零 diff）。

### G9-07（P2）：`EmitNullableEnable` 去实例可变状态（参数化重构）

- **位置**：`Mud.HttpUtils.Generator/TransitiveCodeGenerator.cs:69`（可变属性）、`:104`（实例 `GenerateFileHeader`
  读该属性）；写入点 `Generators/HttpInvokeClassSourceGenerator.cs:67`、`Generators/HttpInvokeRegistrationGenerator.cs:38`；
  另有 `Generators/FormContentGenerator.cs:208`（依赖属性默认 `true`，从未赋值）。
- **现状与证据**：增量生成器实例跨编译代复用，注册文件路径依赖「每次 `ExecuteGenerator` 先赋值后生成」的顺序纪律；
  实现类文件路径已是参数传递（`ClassStructureGenerator.cs:32` 用 `context.EmitNullableEnable`），
  同一配置存在**两种传递机制**。`FormContentGenerator` 从不赋值 → 恒用默认 `true`，
  与「消费项目 `Nullable=disable` 不发射」的 D-03 契约存在隐性不一致。
- **方案**：
  1. 删除 `TransitiveCodeGenerator.EmitNullableEnable` 可变属性；
  2. 实例 `GenerateFileHeader(StringBuilder)` 改为 `GenerateFileHeader(StringBuilder sb, bool emitNullableEnable)`；
  3. `HttpInvokeRegistrationGenerator`：`ExecuteGenerator` 把 `configSnapshot.NullableEnable` 作为参数下传至
     `GenerateExtensionClass` / `GenerateFactoryRegistrationCode`（`:267/:306` 两处调用点）；
  4. `HttpInvokeClassSourceGenerator:67/84`：删除属性赋值，直接以 `configSnapshot.NullableEnable` 实参传递
     （`ProcessInterface` 已有对应形参）；
  5. `FormContentGenerator:208`：**对齐 D-03 契约**——接线 `NullableEnable`。
     **（复核修订）** 该生成器曾因 F-06 **刻意删除** `AnalyzerConfigOptionsProvider` 的 Combine
     （原 Combine 的 Right 从未被消费，只会让 Provider 实例重建无谓击穿逐节点缓存）——因此**不得**重新引入
     裸 Combine，必须复用主生成器的 G7-06 值相等快照模式：`AnalyzerConfigOptionsProvider
     .Select(static (p, _) => GeneratorConfigSnapshot.Create(p)).WithComparer(GeneratorConfigSnapshot.Comparer)`
     后与 `formContentModels` Combine。消费快照值 + 值相等比较器 = 真实依赖且增量语义正确
     （配置值不变 ⇒ Cached；`Nullable` 变化 ⇒ 重生成是**正确**行为），与 F-06 反模式有本质区别。
     此为唯一行为变化点，效果是 `Nullable=disable` 项目下 FormContent 产物也不再发射 `#nullable enable`
     （与实现类/注册产物一致）。
- **测试与守卫**：
  - `TestAnalyzerConfigOptionsProvider` 注入 `Nullable=disable` 的快照用例（新增 1 份，覆盖 FormContent 与注册产物
    均无 `#nullable enable`）；
  - 其余 36 份快照零 diff（GM4，默认 `Nullable=enable` 路径行为不变）；
  - `MultiTfmCompilationGuardTests` 全绿。
- **验收标准**：GM1 + GM4；全仓 `EmitNullableEnable` 残留引用为零（grep 守卫）。

### G9-08（P3）：`ForceHttpGenerator` 行为级钉子（salt 间接覆盖的补强）

- **位置**：`Tests/Mud.HttpUtils.Generator.Tests/Incremental/IncrementalPipelineTests.cs`
  （既有 `ForceHttpGenerator_ShouldChangeSaltValue` / `ForceFlip_ConfigSnapshotShouldDiffer`）。
- **现状与证据**：受 Roslyn 4.11 缺少 `WithUpdatedAnalyzerConfigOptions` 限制，现有断言为「salt 值翻转 + 快照值不等」，
  未断言「同 driver 下下游步骤 Modified」。Roslyn 等价性语义保证值不等 ⇒ Modified，风险低但值得补一个
  **行为级**钉子：`IncrementalCacheForcedInvalidation` 诊断（`HttpInvokeClassSourceGenerator.cs:113-118`）仅在
  `generationSalt` 以 `|force` 结尾时报告——该诊断的出现性本身就是「force 生效」的端到端可观测出口。
- **方案**：新增测试 `ForcedInvalidationDiagnostic_ReportedOnlyWhenForce`：
  1. normal provider（默认配置）跑 `CreateTrackedDriver` → 断言产物**不含** `IncrementalCacheForcedInvalidation`；
  2. force provider（`TestAnalyzerConfigOptionsProvider` 注入 `build_property.ForceHttpGenerator=true`）新建 driver
     跑同一 source → 断言**恰含一条**该诊断（Location 为 None，Info 级）。
  Roslyn 升级到提供 `WithUpdatedAnalyzerConfigOptions` 的版本后，再补「同 driver 二跑 + 换 provider ⇒
  下游 Modified」的直接断言（在测试文件内留 TODO 锚点）。
- **验收标准**：GM1；无行为代码变更。

### G9-09（P3，决策记录）：空 `DefaultAppManager` 自动注册的可观测性通道

- **结论**：**维持现状，不新增通道**。理由：
  1. G8-09 已把失败根因前移到**解析期**：生成的 DI 工厂在 `GetDefaultApp()` 失败时抛出带根因
     （「已自动注册空的 IAppManager」）与两步修复指引的 `InvalidOperationException`（`HttpInvokeRegistrationGenerator.cs:610-642`，
     `EmptyAppManagerMessageTests` 固化），裸容器用户在首次解析即获明确指引；
  2. `LogWarning`（`:577-578`）的 `loggerFactory?.` 两级空条件导致的静默只影响「注册了应用管理器但旁路场景失败」，
     主路径已被 1 覆盖；
  3. 编译期 Info 诊断无法区分「宿主已正确注册」与「裸容器」（生成器看不见 DI），必为全员噪音。
- **动作**：`Generator/README.md`「多应用切换与信任边界」章节补一句：「自动注册的空管理器在未注册日志基础设施时
  不产生运行时告警；缺省应用的失败统一由 DI 解析期异常给出根因与修复步骤（G8-09）。」

### G9-10（P3，文档）：受信实例入口的威胁模型说明

- **结论**：**不改代码**。`BeginScope(IMudAppContext)` / `SwitchTo(IMudAppContext)` / `Current` setter 无授权校验为
  G7-11/G8-10 定论的信任边界（生成物 XML 注释 + README 表格 + `ContextBasedSwitch_DocumentsTrustBoundary` 三重落地）。
- **动作**：README「多应用切换与信任边界（G7-11）」表格下方补一段：
  「**威胁模型：实例即凭据**。`IMudAppContext` 实例本身就是应用身份的载体——任何持有该实例的代码等价于已通过授权。
  宿主不得向不可信代码暴露 `IAppManager.GetApp` 的返回值或已解析的应用上下文；不可信 appKey 输入一律走
  `UseApp`/`UseAppScope`/`BeginScope(string)`（默认拒绝守卫）。」

### G9-11（P3，决策记录）：TokenManager/HttpClient 模式裸注册缺依赖不做编译期诊断

- **结论**：**不立项**。生成器无法观测宿主 DI 注册，`AddTransient<IFoo, Impl>`（TokenManager/HttpClient 模式，
  `HttpInvokeRegistrationGenerator.cs:646`）的依赖缺失只能在运行期暴露；且所需依赖（自定义类型、`ITokenProvider`、
  `IAppContextHolder`、基础设施）在任何真实工程中都会注册（否则该模式不可用），编译期 Info 必为全员噪音。
  现有防线已足：生成注释明示必需依赖（`:552-566`）+ `GetRequiredService` 的 DI 报错语义清晰 +
  README「三种运行模式」章节列明构造函数依赖。
- **后续观察点**：若社区反馈「裸注册激活异常难定位」再评估 opt-in 分析器（识别 `[HttpClientApi]` 接口的注册方法
  调用图）——独立立项。

---

## 3. 实施批次与依赖

| 批次 | 项 | 前置依赖 | 预估规模 |
| --- | --- | --- | --- |
| **G9-A（P1，先行）** | G9-01、G9-02、G9-03 | 无（三件互相独立，可并行） | 运行库 ~60 行 + 生成库 0 行 + 测试 ~150 行 + 1 文件删除 + .gitignore |
| **G9-B（P2，次批）** | G9-04、G9-05、G9-06、G9-07 | G9-A 合并后执行（G9-07 触发快照基线变更需干净基线） | 生成器 ~80 行 + Client 0 行 + 测试 ~120 行 + 1 份新快照 |
| **G9-C（P3，排期）** | G9-08、G9-09、G9-10、G9-11 | 无强依赖；G9-08 建议随 G9-B 同 PR（同测试工程） | 测试 ~60 行 + 文档 3 处 |

**执行顺序约束**：G9-07（快照零 diff 重构）必须在 G9-03（基线卫生）之后，避免孤儿快照干扰「零 diff」判定；
G9-05/G9-06 不改变命中场景的产物形态，但合并前必须跑全量快照对比。

---

## 4. 黄金指标与验收清单

| 指标 | 内容 | 验证方式 |
| --- | --- | --- |
| GM1 | `dotnet build Mud.HttpUtils.slnx` + `test.ps1` 全绿 | CI |
| GM2 | 下载取消及时性：`CopyToWithCancellationAsync_FastPath_RespondsToCancellation`（helper 行为，5s watchdog 内 OCE）+ ns2.0 接线文本钉子 + `.mudtmp` 零残留 | L1/L2/L3 三层守卫（见 G9-01 测试与守卫） |
| GM3 | `Tests/Mud.HttpUtils.Generator.Tests/` 项目根无 `.verified.txt` | `NoVerifiedSnapshotAtProjectRoot` + git 索引 |
| GM4 | 36 份快照零 diff（G9-05/06/07 为无行为变更重构，G9-07 新增 1 份 `Nullable=disable` 快照） | CI 快照对比 |
| GM5 | 本轮零新增诊断 ID；`DiagnosticReachabilityTests` / `AnalyzerReleases.Unshipped.md` 不变 | CI |
| GM6 | 既有守卫全绿：`ApplicationSwitchGuardContractTests`、`AppAuthorizationTests`、`DownloadCleanupTests`、`AsyncEnumerableExtensionsTests`、`InheritedFromModeMatrixTests` | CI |

---

## 5. 破坏性变更清单

**无**。G9-01 为错误路径行为补齐（取消时延，成功路径字节级不变）；G9-02 仅异常消息文本化；
G9-07 的 FormContent `Nullable=disable` 对齐属**缺陷收敛**（D-03 契约的一致化），仅在 `Nullable=disable`
消费项目下改变 FormContent 产物头部一行，且方向与主生成器既有行为一致——不构成 API/行为契约破坏。

---

## 6. 存疑闭环

| 上一轮存疑 | 结论 | 证据 |
| --- | --- | --- |
| GeneratorVersion bump 纪律（E-3 salt 是否需人工维护） | **机制自洽，不立项**。`GeneratorVersion` 取自生成器程序集版本（`Assembly.GetExecutingAssembly().GetName().Version`，`GeneratedCodeConsts.cs:17-30`），版本号由 `Directory.Build.props:3` 的 `<Version>2.0.9</Version>` 单源驱动；NuGet 包升级 ⇒ 程序集版本变化 ⇒ salt 翻转 ⇒ 旧产物强制刷新 | `Consts/GeneratedCodeConsts.cs`、`Directory.Build.props:3` |

---

## 7. 明确不立项项（锚点核实对账，防后续重复立项）

以下为本轮 22 条锚点中经核实**不成立**（或属已文档化权衡且已有测试固化）的条目，后续审查不得再次立项；
如需推翻，必须给出「导致静默生成错误代码 / 跨租户泄漏」的最小复现：

| 锚点 | 不成立理由（一句话） | 固化证据 |
| --- | --- | --- |
| 2 逐接口 `Combine(Provider)` 缓存整体失效 | G7-06 已改值相等 `GeneratorConfigSnapshot` | `IncrementalPipelineTests.ProviderInstanceRecreated_SameValues_ShouldBeValueEqual`、`UnrelatedEdit_WithFreshOptionsProvider_ShouldStillCache` |
| 3 `Collect()` 全量重跑（作为缺陷） | 产物轻量、行为被测试**固化**为预期 | `AnyInterfaceChange_ShouldModifyGlobalDataRegistrationPoint` |
| 5 CWT 缓存跨代过期/内存放大 | 键亡即释、键不变值恒有效 | `SemanticModelCache.cs:30`、`InterfaceImplementationGenerator.cs:28` |
| 6 生成代码内容释放遗漏 | 全分支 `using var` 或创建点移交 + multipart 受保护形态 | `RequestBuilder.cs:478-811`；`GeneratedCodeEscapeTests`/编译断言 |
| 8 流式/直达返回所有权与取消释放 | G7-09 remarks + finally 双层释放 | `AsyncEnumerableExtensionsTests` 三路径 |
| 9 继承模式 `_appAuthorizer` 透传 | G8-04 HTTPCLIENT035 拦截 + 快照透传 | `InheritedFromModeMatrixTests`（9 组合） |
| 12 令牌键缺 appKey（作为缺陷） | DP-6 设计：每 App 实例 + `BindTenantGuard` fail-closed | `DefaultTokenProvider.cs:43-44`、README G7-12 章节 |
| 15 无 DI 工厂参数错位 | 与 `DefaultHttpRequestExecutor` 构造契约逐位一致 | 逐参数核对（本轮）；`GeneratorSnapshotCompileTests` |
| 16 appKey 校验口径不一致 | 三处一致、大小写敏感为有意契约 | `AppKey.cs`/`AppKeyValidator.cs`/`DefaultAppManager.cs` + `AppManagerCaseSensitivityTests` |
| 19 `build_property.*` 未注册 | 9/9 全部注册 | `build/Mud.HttpUtils.Generator.props:15-38` |
| 20 hintName 冲突 | GEN-17（`+` 分隔 + 元数 + 命名空间） | `GeneratedCodeEscapeTests`/编译断言 |
| 21 契约占位避让缺失 | 登记完整（含 G8-15） | `UnconditionalMemberAvoidanceTests`/`ContractCompletionTests` |
| 22 死诊断/占位符错配 | 48/48 有触发点、抽样 12 项计数一致 | `DiagnosticReachabilityTests`/`DiagnosticTagPolicyTests` |
| 1/4/10/11/13/14/17/18 | 部分成立项分别处置：1（权衡）、4（G9-05 性能增强）、10（G9-10 文档）、11（存量权衡）、13（G9-09 决策记录）、14（G9-04 钉子）、17（G9-02 收敛）、18（Warning 已达警示目的） | 见对应 G9 项 |

---

## 8. 附：本轮核实方法论备忘（供后续审查复用）

1. **锚点核实「三证据」**：生成器源码行号 + 快照文本 + 运行时实现/测试；缺任一不得下「成立」结论；
2. **跨 TFM 分支必须成对核对**（G9-01 即漏网示例：NET6+ 分支修过 CT、ns2.0 分支漏改——两分支由同一功能需求驱动，改一处必查另一处）；
3. **生成期与运行期的安全纪律要成对核对**（G9-02：生成侧静态消息 vs 运行时插值）；
4. **测试基建的默认行为要先弄清再定性**（G9-03：Verify 默认落盘在测试类旁，`Generators\` 下 3 份快照是默认行为而非散置错误；真正的缺陷只有 1 份孤儿）；
5. **快照基线本身要审查**：孤儿/陈旧基线的误导性不亚于基线固化错误行为。
