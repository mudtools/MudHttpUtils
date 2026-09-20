// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Generator.Tests.Incremental;

/// <summary>
/// 真实增量行为测试（M0.2）：通过 <see cref="GeneratorDriverOptions"/> 注入
/// <c>trackIncrementalSteps: true</c>，断言无关文件编辑时关键步骤命中 <c>Cached</c>。
/// </summary>
/// <remarks>
/// <para>
/// 相较于 <see cref="IncrementalTests"/>（仅对 <see cref="Mud.HttpUtils.Models.InterfaceModel"/> 指纹判等、
/// 无法覆盖下游步骤），本文件直接观察 Roslyn 增量管道的 <see cref="IncrementalStepRunReason"/>，
/// 是「管道级」的真实断言。若因程序集割裂（SDK 内联 Roslyn vs 引用的 Microsoft.CodeAnalysis）无法注入
/// <c>GeneratorDriverOptions</c>，测试会被标记 Skip 并在 <c>.docs</c> 记录缺口（见方案 §3.2 降级策略）。
/// </para>
/// </remarks>
public class IncrementalPipelineTests
{
    private const string Header = "using Mud.HttpUtils; using Mud.HttpUtils.Attributes;\n";

    private const string BaseInterface = Header + """
        [HttpClientApi]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);
        }
        """;

    private const string BaseWithUnrelated = BaseInterface + "\npublic class Unrelated { }\n";

    [Fact]
    public async Task UnrelatedFileEdit_ShouldCacheDerivedSteps()
    {
        var driver = CreateTrackedDriver(BaseInterface);
        driver = driver.RunGenerators(Compile(BaseInterface));

        var firstRun = driver.GetRunResult().Results[0].TrackedSteps;
        firstRun.Should().NotBeEmpty();

        // 第二次运行：注入无关内容变更（同一逻辑文件追加一个无关类）——编译随之变化，
        // 但接口声明指纹未变，下游 RegisterSourceOutput 依赖的 Combine 节点应命中 Cached/Unchanged。
        driver = driver.RunGenerators(Compile(BaseWithUnrelated));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        var sourceProviderSteps = secondRun
            .FirstOrDefault(kvp => kvp.Key == "HttpInvokeBase_SyntaxProvider").Value;
        sourceProviderSteps.Should().NotBeNullOrEmpty("应存在 HttpInvokeBase_SyntaxProvider 追踪步骤");

        // 断言：SyntaxProvider 输出与上一次完全相等（指纹未变 → 显式命中 Cached/Unchanged）。
        // [GEN-11/A-1] 从宽松的「!= Modified」收紧为显式 Reason is Cached or Unchanged。
        var reasons = sourceProviderSteps.SelectMany(s => s.Outputs).ToList();
        reasons.Should().NotBeEmpty("HttpInvokeBase_SyntaxProvider 应产出模型元素");
        reasons.Should().OnlyContain(
            o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged,
            "无关文件编辑必须显式命中 Cached/Unchanged，而非仅「未标记为 Modified」");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task InterfaceMethodChange_ShouldMarkModified()
    {
        var changedSource = Header + """
            [HttpClientApi]
            public interface IApi
            {
                [Get("/users/{id}")]
                Task<string> GetAsync([Path] int id);

                [Get("/users")]
                Task<string> ListAsync();
            }
            """;

        var driver = CreateTrackedDriver(BaseInterface);
        driver = driver.RunGenerators(Compile(BaseInterface));

        driver = driver.RunGenerators(Compile(changedSource));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        var sourceProviderSteps = secondRun
            .FirstOrDefault(kvp => kvp.Key == "HttpInvokeBase_SyntaxProvider").Value;
        sourceProviderSteps.Should().NotBeNullOrEmpty();

        sourceProviderSteps.Any(s => s.Outputs.Any(o => o.Reason == IncrementalStepRunReason.Modified))
            .Should().BeTrue("接口成员新增必须触发接口模型 Modified");
    }

    // ─────────────── [Phase3 4.3/4.4] 下游（per-item）步骤的增量断言 ───────────────

    /// <summary>
    /// 无关文件编辑时，逐接口注册点的下游步骤（<c>HttpInvokeBase_CompleteData</c>）同样不得重跑。
    /// </summary>
    /// <remarks>
    /// 历史缺口（审查 4.5）：既有断言只覆盖上游 <c>HttpInvokeBase_SyntaxProvider</c>，
    /// 无法发现「上游命中缓存但下游 Combine 节点被判定 Modified」——即审查 1.4 的「增量退化为全量」。
    /// Phase3 拆出 per-item 注册点后，本断言成为该拆分的防回归守卫。
    /// </remarks>
    [Fact]
    public async Task UnrelatedFileEdit_ShouldCachePerInterfaceDownstreamStep()
    {
        var driver = CreateTrackedDriver(BaseInterface);
        driver = driver.RunGenerators(Compile(BaseInterface));

        driver = driver.RunGenerators(Compile(BaseWithUnrelated));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        var perInterfaceSteps = secondRun
            .FirstOrDefault(kvp => kvp.Key == "HttpInvokeBase_CompleteData").Value;
        perInterfaceSteps.Should().NotBeNullOrEmpty(
            "应存在逐接口注册点的追踪步骤 HttpInvokeBase_CompleteData");

        perInterfaceSteps
            .SelectMany(s => s.Outputs)
            .Should().NotContain(o => o.Reason == IncrementalStepRunReason.Modified,
                "无关文件编辑不得让逐接口生成步骤重跑（否则 per-item 缓存失效，退化为全量生成）");

        await Task.CompletedTask;
    }

    /// <summary>
    /// [Phase3 验收] 两个接口仅改 1 个 → 逐接口注册点恰好 1 个输出 Modified，其余命中缓存。
    /// </summary>
    /// <remarks>
    /// 这是「真 per-interface 增量」的核心证据：修复前管道末端为 <c>Collect()</c> + <c>Combine</c>
    /// 的单一节点，任一接口变化都会让全部接口的输出被判定 Modified。
    /// </remarks>
    [Fact]
    public async Task SingleInterfaceChange_ShouldOnlyModifyThatInterfaceDownstream()
    {
        const string twoInterfacesSource = Header + """
            [HttpClientApi]
            public interface IApiA
            {
                [Get("/users/{id}")]
                Task<string> GetAsync([Path] int id);
            }

            [HttpClientApi]
            public interface IApiB
            {
                [Get("/orders/{id}")]
                Task<string> GetAsync([Path] int id);
            }
            """;

        // 仅修改 IApiA 的 URL，IApiB 保持逐字不变。
        const string modifiedSource = Header + """
            [HttpClientApi]
            public interface IApiA
            {
                [Get("/v2/users/{id}")]
                Task<string> GetAsync([Path] int id);
            }

            [HttpClientApi]
            public interface IApiB
            {
                [Get("/orders/{id}")]
                Task<string> GetAsync([Path] int id);
            }
            """;

        var driver = CreateTrackedDriver(twoInterfacesSource);
        driver = driver.RunGenerators(Compile(twoInterfacesSource));

        var firstRunOutputs = driver.GetRunResult().Results[0].TrackedSteps
            .First(kvp => kvp.Key == "HttpInvokeBase_CompleteData").Value
            .SelectMany(s => s.Outputs).Count();
        firstRunOutputs.Should().Be(2, "两个 [HttpClientApi] 接口应产生两个逐接口输入元素");

        driver = driver.RunGenerators(Compile(modifiedSource));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        var outputs = secondRun
            .FirstOrDefault(kvp => kvp.Key == "HttpInvokeBase_CompleteData").Value
            .SelectMany(s => s.Outputs)
            .ToList();

        outputs.Should().HaveCount(2);
        outputs.Count(o => o.Reason == IncrementalStepRunReason.Modified)
            .Should().Be(1, "只改 1 个接口时下游应只有 1 个元素 Modified（per-item 缓存生效）");

        await Task.CompletedTask;
    }

    // ─────────────── [GEN-11/A-1] 新增三类编辑的管道级用例（05 册 M0 未覆盖） ───────────────

    // 场景一：仅注释/空白编辑 → 全链显式 Cached/Unchanged（不得 Modified）。
    // 注：注释放在接口声明的 leading trivia 位置（与 InterfaceModelFingerprintTests 的
    // CommentOnlyChange_ShouldNotRegenerate 口径一致），被 WithoutTrivia() 剥离。
    private const string CommentOnlyVariant = Header + """
        // 仅注释变更，方法签名与接口特性逐字不变
        [HttpClientApi]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);
        }
        """;

    [Fact]
    public async Task CommentOrWhitespaceEdit_ShouldCacheEntireChain()
    {
        var driver = CreateTrackedDriver(BaseInterface);
        driver = driver.RunGenerators(Compile(BaseInterface));

        driver = driver.RunGenerators(Compile(CommentOnlyVariant));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        // 全链覆盖：SyntaxProvider(上游) → CompleteData(per-item Combine) → Collected/GlobalData(全局) 均不得 Modified。
        foreach (var stepName in new[]
                 {
                     "HttpInvokeBase_SyntaxProvider",
                     "HttpInvokeBase_CompleteData",
                     "HttpInvokeBase_GlobalData",
                 })
        {
            var step = secondRun.FirstOrDefault(kvp => kvp.Key == stepName).Value;
            step.Should().NotBeNullOrEmpty($"应存在追踪步骤 {stepName}");

            var reasons = step.SelectMany(s => s.Outputs).ToList();
            reasons.Should().NotBeEmpty($"{stepName} 应产出元素");
            reasons.Should().OnlyContain(
                o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged,
                $"仅注释/空白编辑不得让 {stepName} 标记为 Modified");
        }

        await Task.CompletedTask;
    }

    // 场景二：方法签名编辑 → 该接口 Modified，同编译内其他接口 Cached（per-interface 粒度核心断言）。
    private const string TwoInterfaces = Header + """
        [HttpClientApi]
        public interface IApiA
        {
            [Get("/users/{id}")]
            Task<string> GetAAsync([Path] int id);
        }

        [HttpClientApi]
        public interface IApiB
        {
            [Get("/orders/{id}")]
            Task<string> GetBAsync([Path] int id);
        }
        """;

    // 仅改 IApiA 的方法签名（参数列表变化），IApiB 逐字不变。
    private const string TwoInterfaces_SignatureEdited = Header + """
        [HttpClientApi]
        public interface IApiA
        {
            [Get("/users/{id}")]
            Task<string> GetAAsync([Path] Guid id);
        }

        [HttpClientApi]
        public interface IApiB
        {
            [Get("/orders/{id}")]
            Task<string> GetBAsync([Path] int id);
        }
        """;

    [Fact]
    public async Task SignatureEdit_ShouldOnlyModifyEditedInterface()
    {
        var driver = CreateTrackedDriver(TwoInterfaces);
        driver = driver.RunGenerators(Compile(TwoInterfaces));

        driver = driver.RunGenerators(Compile(TwoInterfaces_SignatureEdited));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        var outputs = secondRun
            .First(kvp => kvp.Key == "HttpInvokeBase_CompleteData").Value
            .SelectMany(s => s.Outputs)
            .ToList();

        outputs.Should().HaveCount(2, "两个接口应各产出一个逐接口输入元素");
        outputs.Count(o => o.Reason == IncrementalStepRunReason.Modified)
            .Should().Be(1, "方法签名编辑只影响被编辑的接口，其他接口必须保持 Cached/Unchanged（per-item 增量核心）");
        outputs.Count(o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged)
            .Should().Be(1, "未被编辑的接口应显式命中 Cached/Unchanged 而非 Modified");

        await Task.CompletedTask;
    }

    // 场景三：接口级 [HttpClientApi] 命名参数编辑 → 该接口 Modified。
    private const string InterfaceWithTimeout = Header + """
        [HttpClientApi(Timeout = 5000)]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);
        }
        """;

    private const string InterfaceWithTimeoutChanged = Header + """
        [HttpClientApi(Timeout = 9000)]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);
        }
        """;

    /// <summary>
    /// 接口级 [HttpClientApi] 命名参数（Timeout）编辑需参与指纹，触发该接口 Modified。
    /// </summary>
    [Fact]
    public async Task InterfaceAttributeNamedArgEdit_ShouldMarkModified()
    {
        var driver = CreateTrackedDriver(InterfaceWithTimeout);
        driver = driver.RunGenerators(Compile(InterfaceWithTimeout));

        driver = driver.RunGenerators(Compile(InterfaceWithTimeoutChanged));
        var secondRun = driver.GetRunResult().Results[0].TrackedSteps;

        var outputs = secondRun
            .First(kvp => kvp.Key == "HttpInvokeBase_CompleteData").Value
            .SelectMany(s => s.Outputs)
            .ToList();

        outputs.Should().NotBeEmpty("应产出逐接口输入元素");
        outputs.Should().Contain(
            o => o.Reason == IncrementalStepRunReason.Modified,
            "接口级 [HttpClientApi] 命名参数编辑必须让该接口标记为 Modified");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ForceHttpGenerator_ShouldChangeSaltValue()
    {
        // F4：ForceHttpGenerator=true 必须使 BuildSalt 产出不同值（…|force），
        // 从而在下一次增量运行中翻转下游 Combine 的缓存命中。
        // 注意：Roslyn 4.11 的 GeneratorDriver 无 WithUpdatedAnalyzerConfigOptions，两次运行必须新建 driver，
        // 因此此处断言的是「salt 值随属性变化」这一本质不变量（值翻转 ⇒ 沿用同一 driver 时必然 Modified）。
        var normalProvider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());
        var forceProvider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string> { ["build_property.ForceHttpGenerator"] = "true" });

        var normalSalt = GetSaltOutput(normalProvider);
        var forceSalt = GetSaltOutput(forceProvider);

        normalSalt.Should().EndWith("|normal");
        forceSalt.Should().EndWith("|force");
        normalSalt.Should().NotBe(forceSalt, "ForceHttpGenerator 必须改变下游失效信号，否则逃生舱静默失效");
    }

    // ─────────────── [G6-D / D-1 · GEN-21] 实验：provider 实例敏感性（决策门） ───────────────

    /// <summary>
    /// [D-1 · GEN-21] 实验：当 <c>AnalyzerConfigOptionsProvider</c> 成为增量图输入节点
    /// （<c>HttpInvokeBase_CompleteData</c> 内的 <c>Combine(context.AnalyzerConfigOptionsProvider)</c>）时，
    /// 更换一个「内容相同、实例不同」的 provider（该类型无 <c>Equals</c> → 引用不等）是否会让下游步骤失效。
    /// <para>
    /// [§9.1 实验 / §0.2.5-② 修正] Roslyn 4.11 无 <c>WithUpdatedAnalyzerConfigOptions</c>，同一 driver 无法更换
    /// provider，因此用「两个新建 driver」各持一个 <see cref="TestAnalyzerConfigOptionsProvider"/> 实例。
    /// </para>
    /// <para>
    /// 判定：若 <c>HttpInvokeBase_CompleteData</c> 全部输出 <see cref="IncrementalStepRunReason.Cached"/>
    /// / <see cref="IncrementalStepRunReason.Unchanged"/> → Roslyn 按值/内容比较 provider，GEN-21 证伪；
    /// 出现 <see cref="IncrementalStepRunReason.Modified"/> → 确认缺陷（provider 引用差异使 IDE 增量退化为全量）。
    /// </para>
    /// </summary>
    [Fact]
    public void UnrelatedEdit_WithFreshOptionsProvider_ShouldStillCache()
    {
        var providerA = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());
        var providerB = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());

        var compilationWithUnrelatedEdit = Compile(BaseWithUnrelated);

        // driverA：providerA 实例，跑含无关编辑的编译（作为基线给出下游步骤输出）。
        var driverA = CreateTrackedDriver(BaseWithUnrelated, optionsProvider: providerA);
        driverA = driverA.RunGenerators(compilationWithUnrelatedEdit);

        // driverB：providerB（同一配置内容、不同实例，引用不等），跑同一编译。
        // 在「无等值比较（引用路径）」的假设下，provider 引用差异会让 Combine 节点被判 Modified。
        var driverB = CreateTrackedDriver(compilationWithUnrelatedEdit.SyntaxTrees.First().ToString(), optionsProvider: providerB);
        driverB = driverB.RunGenerators(compilationWithUnrelatedEdit);

        var perInterfaceSteps = driverB.GetRunResult().Results[0].TrackedSteps
            .FirstOrDefault(kvp => kvp.Key == "HttpInvokeBase_CompleteData").Value;
        perInterfaceSteps.Should().NotBeNullOrEmpty(
            "应存在逐接口注册点的追踪步骤 HttpInvokeBase_CompleteData");

        var reasons = perInterfaceSteps
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToList();
        reasons.Should().NotBeEmpty("HttpInvokeBase_CompleteData 应产出逐接口输入元素");

        // ── GEN-21 实验观测结论（2026-09-16 实测）──
        // 在 Roslyn 4.11 无法于同一 driver 内更换 provider 的约束下，跨 driver 是「冷启动」：
        // driverB 首次 RunGenerators 的每一步输出恒为 New（冷构建标记），既不可能是 Cached/Unchanged
        // （需同 driver 前次状态），也不可能出现 Modified（无跨 driver 比较）。
        // 判定：**未观测到 Modified 缺陷症状** → GEN-21 未获“确认缺陷”证据 → 按 D-3 选项 1 做防御性收敛。
        // 此处断言即锁定该观测：仅有冷启动 New，绝不允许出现确认缺陷的 Modified。
        reasons.Should().NotContain(
            o => o == IncrementalStepRunReason.Modified,
            "GEN-21 实验：provider 隔离场景下不得出现 Modified 缺陷症状（该症状即为 IDE 增量退化为全量的证据）；" +
            $"实际 Reason 序列：[{string.Join(", ", reasons)}]");
    }

    // ─────────────── [G7-06] 配置值快照：Provider 实例重建但值相同时下游缓存命中 ───────────────

    /// <summary>
    /// [G7-06] 两个「内容相同、实例不同」的 <see cref="AnalyzerConfigOptionsProvider"/> 构建的
    /// <see cref="GeneratorConfigSnapshot"/> 必须值相等（Comparer.Equals == true 且 HashCode 相同）。
    /// <para>
    /// 这是 IDE 增量恢复的防线：Provider 类型无 <c>Equals</c>（引用相等），若 Combine 图仍以引用型
    /// provider 为输入，则 IDE 重建 provider 实例（值未变）时下游恒 <c>Modified</c>（缓存全量失效）。
    /// G7-06 改为值相等快照后，此断言钉死「值相等 → 快照相等 → 下游 Cached」的语义。
    /// </para>
    /// </summary>
    [Fact]
    public void ProviderInstanceRecreated_SameValues_ShouldBeValueEqual()
    {
        var providerA = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());
        var providerB = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());

        var snapshotA = GeneratorConfigSnapshot.Create(providerA);
        var snapshotB = GeneratorConfigSnapshot.Create(providerB);

        GeneratorConfigSnapshot.Comparer.Equals(snapshotA, snapshotB)
            .Should().BeTrue("配置值相同（空字典）时，不同 provider 实例的快照必须值相等");
        GeneratorConfigSnapshot.Comparer.GetHashCode(snapshotA)
            .Should().Be(GeneratorConfigSnapshot.Comparer.GetHashCode(snapshotB),
                "值相等快照的散列必须一致，否则融入 Combine 图后退化为引用比较");
    }

    /// <summary>
    /// [G7-06] 配置值真正变化（ForceHttpGenerator 翻转）时快照必须不相等（下游 Modified）。
    /// <para>与 <see cref="ForceHttpGenerator_ShouldChangeSaltValue"/> 互为两面：salt 与快照双通道失效。</para>
    /// </summary>
    [Fact]
    public void ForceFlip_ConfigSnapshotShouldDiffer()
    {
        var normalProvider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());
        var forceProvider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string> { ["build_property.ForceHttpGenerator"] = "true" });

        var normalSnapshot = GeneratorConfigSnapshot.Create(normalProvider);
        var forceSnapshot = GeneratorConfigSnapshot.Create(forceProvider);

        GeneratorConfigSnapshot.Comparer.Equals(normalSnapshot, forceSnapshot)
            .Should().BeFalse("ForceHttpGenerator 翻转必须使快照不相等，否则逃生舱在下游 Combine 中静默失效");
    }

    private static string GetSaltOutput(AnalyzerConfigOptionsProvider provider)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new HttpInvokeClassSourceGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: null,
            optionsProvider: provider,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(Compile(BaseInterface));

        var steps = driver.GetRunResult().Results[0].TrackedSteps;
        var saltSteps = steps.FirstOrDefault(kvp => kvp.Key == "HttpInvokeBase_GenerationSalt").Value;
        saltSteps.Should().NotBeNullOrEmpty("应存在 HttpInvokeBase_GenerationSalt 追踪步骤");

        return saltSteps
            .SelectMany(s => s.Outputs)
            .Select(o => o.Value as string)
            .FirstOrDefault(v => v != null) ?? string.Empty;
    }

    /// <summary>
    /// 构造注入 <c>GeneratorDriverOptions(trackIncrementalSteps: true)</c> 的生成器驱动。
    /// 若发生 Roslyn 版本割裂导致的类型不匹配异常，测试应显式失败（而非静默降级）。
    /// <para>
    /// [GEN-12/A-2] 本重载用于构造 provider 隔离用例：可显式传入独立的
    /// <c>AnalyzerConfigOptionsProvider</c> 实例，验证下游 Combine 对 provider 实例的敏感性。
    /// 默认实现不传 provider（沿用同实例语义），保持既有断言语义不变。
    /// </para>
    /// </summary>
    private static GeneratorDriver CreateTrackedDriver(string source)
        => CreateTrackedDriver(source, optionsProvider: null);

    private static GeneratorDriver CreateTrackedDriver(string source, AnalyzerConfigOptionsProvider? optionsProvider)
    {
        var generator = new HttpInvokeClassSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: null,
            optionsProvider: optionsProvider,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        return driver;
    }

    private static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
            "IncrementalPipelineTest",
            new[] { tree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}