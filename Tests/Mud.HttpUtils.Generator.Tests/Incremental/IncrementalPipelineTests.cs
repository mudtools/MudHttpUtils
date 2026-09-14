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

        // 断言：SyntaxProvider 输出与上一次完全相等（指纹未变 → Cached 或 Unchanged，不得为 Modified）
        sourceProviderSteps.All(s => s.Outputs.All(o => o.Reason != IncrementalStepRunReason.Modified))
            .Should().BeTrue("无关文件编辑不得触发接口模型重新变换");

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
    /// </summary>
    private static GeneratorDriver CreateTrackedDriver(string source)
    {
        var generator = new HttpInvokeClassSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
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