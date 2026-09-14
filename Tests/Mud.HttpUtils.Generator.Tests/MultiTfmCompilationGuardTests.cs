using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [Phase1 修复 4.4] 多 TFM 编译守卫：同一份生成产物在消费端不同预处理符号集下均须无错编译。
/// </summary>
/// <remarks>
/// <para>
/// 生成器在产物中发射了条件编译区块：
/// <list type="bullet">
///   <item><c>RequestBuilder.cs</c>：<c>#if NETSTANDARD2_0</c> ⇒ PATCH 走 <c>new HttpMethod("PATCH")</c>、
///   <c>Retry(AllowNonIdempotent)</c> 走 <c>HttpRequestMessage.Properties</c>；<c>#else</c> ⇒ <c>HttpMethod.Patch</c> /
///   <c>HttpRequestMessage.Options</c>；</item>
///   <item><c>HttpInvokeRegistrationGenerator.cs</c>：<c>#if NET5_0_OR_GREATER</c> ⇒ <c>[ModuleInitializer]</c>；
///   <c>#else</c> ⇒ <c>RegisterAllFactories()</c>；<c>#if NET6_0_OR_GREATER</c> ⇒ 附加 HttpVersion 参数。</item>
/// </list>
/// 这些分支在仓库自身的 net8.0 构建中永远走不到 <c>#if NETSTANDARD2_0</c> 侧，历史缺陷（分支内代码不可编译）
/// 因此无法被发现。本守卫用 <see cref="CSharpParseOptions.WithPreprocessorSymbols"/> 显式模拟两种消费端符号集。
/// </para>
/// <para>覆盖 TFM：netstandard2.0（含 PATCH + 幂等放行 + 无 ModuleInitializer）与 net5.0+/net8.0（现代分支）。</para>
/// </remarks>
public class MultiTfmCompilationGuardTests
{
    /// <summary>同时触发 PATCH/Retry(AllowNonIdempotent) 条件块与 DI 注册/工厂注册条件块的输入。</summary>
    private const string Source = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface ITfmApi
            {
                [Get("/users/{id}")]
                Task<string> GetAsync([Path] int id);

                [Patch("/users/{id}")]
                [Retry(AllowNonIdempotent = true)]
                Task<string> PatchAsync([Path] int id, [Body] Payload payload);
            }

            public class Payload { public string Name { get; set; } }
        }
        """;

    /// <summary>netstandard2.0 消费端：NUGET 侧最保守的符号集（无 NET5_0_OR_GREATER / NET6_0_OR_GREATER）。</summary>
    [Fact]
    public void GeneratedCode_CompilesUnder_NetStandard20()
        => AssertCompilesUnderTfm(["NETSTANDARD2_0"]);

    /// <summary>net5.0 消费端：有 ModuleInitializer 但无 HttpVersion 重载。</summary>
    [Fact]
    public void GeneratedCode_CompilesUnder_Net50()
        => AssertCompilesUnderTfm(["NET5_0_OR_GREATER", "NET6_0_OR_GREATER", "NET8_0_OR_GREATER"]);

    /// <summary>net8.0 消费端：仓库自身目标 TFM（条件块另一侧）。</summary>
    [Fact]
    public void GeneratedCode_CompilesUnder_Net80()
        => AssertCompilesUnderTfm(["NET8_0_OR_GREATER", "NET6_0_OR_GREATER", "NET5_0_OR_GREATER", "NET7_0_OR_GREATER"]);

    /// <summary>无任何 TFM 符号（消费端未定义任何条件符号）——验证 <c>#else</c> 兜底分支可编译。</summary>
    [Fact]
    public void GeneratedCode_CompilesUnder_NoTfmSymbols()
        => AssertCompilesUnderTfm([]);

    private static void AssertCompilesUnderTfm(string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: preprocessorSymbols);

        var syntaxTree = CSharpSyntaxTree.ParseText(Source, parseOptions);

        // DI 注册产物（HttpClientApiExtensions.g.cs / GeneratedFactoryRegistration.g.cs）依赖
        // Microsoft.Extensions.DependencyInjection[.Extensions] 与 Microsoft.Extensions.Http
        // （IServiceCollection.AddHttpClient），BasicReferenceAssemblies 未覆盖，此处按程序集名补引用
        // （测试输出目录随 ProjectReference 携带这些 DLL）。
        var references = BasicReferenceAssemblies.GetReferences();
        foreach (var assemblyName in new[]
                 {
                     "Microsoft.Extensions.DependencyInjection.Abstractions",
                     "Microsoft.Extensions.Http",
                 })
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load(assemblyName).Location));
        }

        var compilation = CSharpCompilation.Create(
            "MultiTfmGuard",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 同时运行两个生成器：实现类生成器（RequestBuilder 的条件块）与注册生成器（ModuleInitializer 条件块）。
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [
                new HttpInvokeClassSourceGenerator().AsSourceGenerator(),
                new HttpInvokeRegistrationGenerator().AsSourceGenerator(),
            ],
            parseOptions: parseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        errors.Should().BeEmpty(
            $"预处理符号 [{string.Join(";", preprocessorSymbols)}] 下生成产物必须无错编译；" +
            $"错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        // 断言条件块确实被激活（防止符号拼写错误导致守卫静默退化为「只测 #else 分支」）。
        var generated = string.Join("\n", outputCompilation.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        if (preprocessorSymbols.Contains("NETSTANDARD2_0"))
        {
            generated.Should().Contain("RegisterAllFactories",
                "netstandard2.0 分支必须走 RegisterAllFactories（无 ModuleInitializer）");
        }
        else
        {
            generated.Should().Contain("ModuleInitializer",
                "net5.0+ 分支必须走 [ModuleInitializer]");
        }
    }
}
