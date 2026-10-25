// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F16（防回归自检）：注册生成器的工厂命名实参 ↔ 实现类构造函数形参的**契约机器守护**。
/// </summary>
/// <remarks>
/// <para>
/// P0-1（CS1739）的成因是两侧判定口径漂移，而「注释即契约」在编译期没有任何强制力。
/// 本类把两侧契约提为可执行断言，覆盖两个维度：
/// </para>
/// <list type="number">
/// <item><b>名集合</b>：工厂发射的每个命名实参都必须对应构造形参（否则 CS1739）；
/// 构造函数的每个<b>必需</b>形参都必须被工厂赋值（否则 CS7036）。</item>
/// <item><b>值形态</b>：形参为<b>可选</b>时，工厂不得传 <c>sp.GetRequiredService&lt;T&gt;()</c>
/// ——<c>IResiliencePolicyResolver</c>/<c>IHttpResponseCache</c> 未必有注册，
/// 向可选形参塞必需解析会把编译期缺陷降级为运行期 DI 失败。</item>
/// </list>
/// <para>
/// 与 <see cref="RealDeclarationMatrixTests"/> 互补：后者用真实编译兜底「能不能编译」，
/// 本类锁定「为什么能编译」的口径，避免有人用「把形参改成可选」的方式把缺陷压成静默。
/// </para>
/// </remarks>
public class RegistrationConstructorContractGuardTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        """;

    private const string HostBase = """
        public abstract class HostBase
        {
            protected readonly IAppContextHolder _appContextHolder;
            protected readonly IHttpRequestExecutor _executor;
            protected readonly IMudAppContext _defaultAppContext;
            protected readonly IHttpContentSerializer _contentSerializer;

            protected HostBase(IMudAppContext appContext, IAppContextHolder appContextHolder, IHttpRequestExecutor executor,
                ILogger? logger = null, IHttpContentSerializer? contentSerializer = null)
            {
                _appContextHolder = appContextHolder;
                _executor = executor;
                _defaultAppContext = appContext;
                _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
            }
        }
        """;

    /// <summary>场景 × 缓存/弹性组合：每格都要求「名集合互锁 + 值形态正确」。</summary>
    public static TheoryData<string, bool, bool> Matrix()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var shape in new[] { "无继承", "自动检测抽象基", "宿主基类" })
        {
            foreach (var cache in new[] { false, true })
            {
                foreach (var resilience in new[] { false, true })
                {
                    data.Add(shape, cache, resilience);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void FactoryNamedArguments_MustMatchConstructorParameters(string shape, bool cache, bool resilience)
    {
        var attributes = (cache ? "        [Cache(60)]\n" : string.Empty)
                         + (resilience ? "        [Retry(3)]\n" : string.Empty);

        var interfaces = shape switch
        {
            "自动检测抽象基" => $$"""
                [HttpClientApi(IsAbstract = true)]
                public interface IBaseApi
                {
                    [Get("/base")]
                    Task<string> GetBaseAsync();
                }

                [HttpClientApi]
                public interface ITestApi : IBaseApi
                {
                    [Get("/own")]
                {{attributes}}    Task<string> GetOwnAsync();
                }
                """,
            "宿主基类" => $$"""
                [HttpClientApi(InheritedFrom = "HostBase")]
                public interface ITestApi
                {
                    [Get("/own")]
                {{attributes}}    Task<string> GetOwnAsync();
                }
                """,
            _ => $$"""
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/own")]
                {{attributes}}    Task<string> GetOwnAsync();
                }
                """,
        };

        var source = Usings + $$"""
            namespace TestNamespace
            {
                {{HostBase}}

                {{interfaces}}
            }
            """;

        var (implText, registrationText) = RunBothGenerators(source);

        var ctorParameters = ParseConstructorParameters(implText);
        ctorParameters.Should().NotBeEmpty("实现类必须有公共构造函数");

        var factoryArguments = ParseFactoryArguments(registrationText);

        // ── 维度 1a：名集合子集（否则 CS1739「最佳重载没有名为 X 的参数」） ──
        foreach (var argumentName in factoryArguments.Keys)
        {
            ctorParameters.Should().ContainKey(argumentName,
                $"场景「{shape}／缓存={cache}／弹性={resilience}」：注册工厂传了 {argumentName}:，" +
                "但实现类构造函数没有该形参（P0-1 类 CS1739）");
        }

        // ── 维度 1b：必需形参全覆盖（否则 CS7036「未提供必需形参」） ──
        foreach (var (parameterName, isOptional) in ctorParameters)
        {
            if (!isOptional)
            {
                factoryArguments.Should().ContainKey(parameterName,
                    $"场景「{shape}／缓存={cache}／弹性={resilience}」：构造函数必需形参 {parameterName} 未被注册工厂赋值（CS7036）");
            }
        }

        // ── 维度 2：值形态（可选形参不得被塞 GetRequiredService） ──
        AssertValueShape(ctorParameters, factoryArguments, "cacheProvider", cache, shape);
        AssertValueShape(ctorParameters, factoryArguments, "resilienceResolver", cache || resilience, shape);
    }

    private static void AssertValueShape(
        Dictionary<string, bool> ctorParameters,
        Dictionary<string, string> factoryArguments,
        string parameterName,
        bool expectedRequired,
        string shape)
    {
        if (!ctorParameters.TryGetValue(parameterName, out var isOptional))
            return; // 该模式下未发射该形参（如宿主基类模式的可选收窄）

        if (!factoryArguments.TryGetValue(parameterName, out var value))
            return; // 可选形参允许省略

        if (isOptional)
        {
            value.Should().Be("null",
                $"场景「{shape}」：{parameterName} 是可选形参，工厂必须传 null —— " +
                "塞 GetRequiredService 会把「服务未注册」从编译期问题变成运行期 DI 失败（F16 值形态断言）");
        }
        else
        {
            value.Should().StartWith("sp.GetRequiredService<",
                $"场景「{shape}」：{parameterName} 是必需形参，工厂必须解析真实服务（不得传 null）");
        }
    }

    /// <summary>
    /// 解析实现类公共构造函数的形参（名称 + 是否可选）。生成器把签名发射为单行，
    /// 但泛型实参含逗号，故按 &lt;&gt;/() 深度切分。
    /// </summary>
    private static Dictionary<string, bool> ParseConstructorParameters(string implText)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var match = Regex.Match(implText, @"public\s+\w+\((?<params>[^)]*)\)");
        match.Success.Should().BeTrue("实现类构造函数签名应可与正则匹配（生成器单行发射）");

        foreach (var part in SplitTopLevel(match.Groups["params"].Value))
        {
            var declaration = part.Trim();
            if (declaration.Length == 0)
                continue;

            var isOptional = declaration.Contains('=');
            // 形参名 = 最后一个空白分隔的标识符（类型在前，可能带 global:: 与泛型）
            var nameMatch = Regex.Match(declaration, @"(?<name>[A-Za-z_@][A-Za-z0-9_]*)\s*(=\s*[^=]*)?$");
            nameMatch.Success.Should().BeTrue($"形参声明应可解析出名称：{declaration}");
            result[nameMatch.Groups["name"].Value.TrimStart('@')] = isOptional;
        }

        return result;
    }

    /// <summary>
    /// 截取指定接口工厂块中<b>实现类构造调用</b>的实参文本。
    /// </summary>
    /// <remarks>
    /// 不能按「到首个 <c>});</c> 为止」截断：同一工厂块里还有
    /// <c>new DefaultHttpRequestExecutor(...)</c> 的一批命名实参（appResilienceResolver 等），
    /// 它们是执行器形参、不属于实现类构造契约，混入会产生假阳性。
    /// </remarks>
    private static string ExtractFactoryBlock(string registrationText, string interfaceName)
    {
        var marker = $"services.AddTransient<global::TestNamespace.{interfaceName}>(sp =>";
        var start = registrationText.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"注册产物必须包含 {interfaceName} 的工厂注册块");

        var block = registrationText.Substring(start);
        var returnNew = block.IndexOf("return new ", StringComparison.Ordinal);
        returnNew.Should().BeGreaterThanOrEqualTo(0, "工厂块内必须以 return new 实现类(...) 结束");

        var openParen = block.IndexOf('(', returnNew);
        openParen.Should().BeGreaterThan(returnNew);

        var depth = 0;
        for (var i = openParen; i < block.Length; i++)
        {
            if (block[i] == '(')
                depth++;
            else if (block[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return block.Substring(openParen + 1, i - openParen - 1);
            }
        }

        return block.Substring(openParen + 1);
    }

    /// <summary>解析实现类构造调用的命名实参（生成器每行一个）。</summary>
    private static Dictionary<string, string> ParseFactoryArguments(string constructorArguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     constructorArguments,
                     @"(?<name>[A-Za-z_][A-Za-z0-9_]*):\s*(?<value>[^,\r\n]+)",
                     RegexOptions.Multiline))
        {
            result[match.Groups["name"].Value] = match.Groups["value"].Value.Trim();
        }

        result.Should().NotBeEmpty("默认模式注册工厂必须发射命名实参（否则本守卫失去意义）");
        return result;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<' or '(' or '[':
                    depth++;
                    break;
                case '>' or ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text.Substring(start));
        return parts;
    }

    /// <summary>双生成器运行：返回（实现类文本, 注册文本）。</summary>
    private static (string ImplText, string RegistrationText) RunBothGenerators(string source)
    {
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
            "RegistrationConstructorContract",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [
                new HttpInvokeClassSourceGenerator().AsSourceGenerator(),
                new HttpInvokeRegistrationGenerator().AsSourceGenerator(),
            ]);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        // 只取被测派生接口的实现类与工厂块：同一编译里基接口也会生成各自的实现类与工厂，
        // 混在一起解析会拿到错误的契约（守卫必须逐接口精确，不能跨接口取首个匹配）。
        var implText = string.Join("\n", output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith("TestApi.g.cs", StringComparison.Ordinal))
            .Select(t => t.ToString()));
        // 只取 DI 注册扩展文件：无 DI 工厂（GeneratedFactoryRegistration）走
        // options?.Xxx 取值，形参可空性与 DI 路径不同，不在本守卫口径内。
        var registrationText = string.Join("\n", output.SyntaxTrees
            .Where(t => t.FilePath.Contains("HttpClientApiExtensions"))
            .Select(t => t.ToString()));

        implText.Should().NotBeNullOrEmpty("应生成派生接口的实现类");
        registrationText.Should().NotBeNullOrEmpty("应生成注册扩展");

        return (implText, ExtractFactoryBlock(registrationText, "ITestApi"));
    }
}
