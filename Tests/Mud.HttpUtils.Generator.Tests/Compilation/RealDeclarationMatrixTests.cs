// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F14（P0-1 测试盲区的结构性修复，按方案关键路径提前至 Phase 1 落地）：
/// 「真实声明式编译矩阵」守卫测试族 —— 每组样本一次完整 <see cref="CSharpCompilation"/> +
/// 双生成器（实现类 + 注册）<see cref="GeneratorDriver"/> 运行，断言「输入 + 全部生成产物」零 Error。
/// <para>
/// <b>为什么必须双生成器</b>：P0-1（默认模式 + 继承 ⇒ CS1739）的出事文件是<b>注册产物</b>
/// （<c>HttpClientApiExtensions.g.cs</c> 的工厂 lambda 命名实参 vs 实现类构造形参），
/// 既有 <see cref="GeneratorCompileAssert"/> / <see cref="GeneratorSnapshotCompileTests"/>
/// 只运行 <see cref="HttpInvokeClassSourceGenerator"/>，结构上抓不到该类跨生成器契约断裂
/// （原审查报告「测试全绿」的根因）。范型为 <c>MultiTfmCompilationGuardTests</c> 的双生成器驱动。
/// </para>
/// </summary>
public class RealDeclarationMatrixTests
{
    /// <summary>
    /// 宿主自维护基类契约样本（F10 文档化的最小形态）：
    /// 构造函数形参与 <c>base(...)</c> 发射对齐，protected 字段名与生成方法体引用对齐。
    /// </summary>
    private const string HostBaseClass = """
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

    /// <summary>默认模式 + 4 种继承形态 × 缓存三态：注册工厂命名实参与构造形参必须互锁（P0-1）。</summary>
    public static TheoryData<string, string, string> InheritanceMatrix()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (shapeName, ownCache, baseCache) in new[]
        {
            //               形态名                自身方法带 [Cache]   基接口方法带 [Cache]
            ("无继承", false, false),
            ("自动检测抽象基", false, false),
            ("自动检测抽象基", false, true),
            ("自动检测抽象基", true, false),
            ("显式 InheritedFrom 生成基", false, false),
            ("显式 InheritedFrom 生成基", false, true),
            ("显式 InheritedFrom 生成基", true, false),
            ("InheritedFrom 宿主基类", false, false),
            ("InheritedFrom 宿主基类", true, false),
        })
        {
            data.Add(shapeName, ownCache ? "自身缓存" : "无缓存", baseCache ? "生成基缓存" : "无缓存");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InheritanceMatrix))]
    public void DefaultMode_InheritanceShapes_CompileWithZeroErrors(string shape, string ownCache, string baseCache)
    {
        var ownCacheAttr = ownCache == "自身缓存" ? "        [Cache(60)]\n" : string.Empty;
        var baseCacheAttr = baseCache == "生成基缓存" ? "        [Cache(60)]\n" : string.Empty;

        var interfaces = shape switch
        {
            "无继承" => $$"""
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/own")]
                {{ownCacheAttr}}    Task<string> GetOwnAsync();
                }
                """,
            "自动检测抽象基" => $$"""
                [HttpClientApi(IsAbstract = true)]
                public interface IBaseApi
                {
                    [Get("/base")]
                {{baseCacheAttr}}    Task<string> GetBaseAsync();
                }

                [HttpClientApi]
                public interface ITestApi : IBaseApi
                {
                    [Get("/derived")]
                {{ownCacheAttr}}    Task<string> GetDerivedAsync();
                }
                """,
            "显式 InheritedFrom 生成基" => $$"""
                [HttpClientApi(IsAbstract = true)]
                public interface IBaseApi
                {
                    [Get("/base")]
                {{baseCacheAttr}}    Task<string> GetBaseAsync();
                }

                [HttpClientApi(InheritedFrom = "BaseApi")]
                public interface ITestApi : IBaseApi
                {
                    [Get("/derived")]
                {{ownCacheAttr}}    Task<string> GetDerivedAsync();
                }
                """,
            _ => $$"""
                [HttpClientApi(InheritedFrom = "HostBase")]
                public interface ITestApi
                {
                    [Get("/own")]
                {{ownCacheAttr}}    Task<string> GetOwnAsync();
                }
                """,
        };

        var source = Usings + $$"""
            namespace TestNamespace
            {
                {{HostBaseClass}}

                {{interfaces}}
            }
            """;

        var (output, _) = RunBothGenerators(source);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.Should().BeEmpty(
            $"默认模式 × {shape} × 自身[{ownCache}] × 基[{baseCache}] 的输入 + 双生成器产物必须可编译（P0-1 回归）；" +
            $"错误：{string.Join("\n", errors.Select(e => e.ToString()))}");
    }

    /// <summary>P0-1 行 6：宿主基类 + 普通基接口（无 [HttpClientApi]）带 [Cache] —— 注册侧必须向派生可选形参传 null。</summary>
    [Fact]
    public void HostBase_WithPlainBaseInterfaceCache_CompilesAndRegistrationPassesNull()
    {
        var source = Usings + $$"""
            namespace TestNamespace
            {
                {{HostBaseClass}}

                public interface IPlainCacheCarrier
                {
                    [Get("/plain")]
                    [Cache(60)]
                    Task<string> GetPlainCachedAsync();
                }

                // 宿主基类必须自行实现普通基接口的成员（派生侧 AllMethods 仅含自身方法，
                // 不补位）——这正是 F10 文档化的宿主基类契约的一部分。
                public abstract class HostCacheBase : HostBase, IPlainCacheCarrier
                {
                    // 构造函数形参须与 base(...) 发射一致：无缓存/弹性的宿主基类只收到
                    // (appContext, appContextHolder, executor) + 两个命名实参 logger/contentSerializer。
                    protected HostCacheBase(IMudAppContext appContext, IAppContextHolder appContextHolder, IHttpRequestExecutor executor,
                        ILogger? logger = null, IHttpContentSerializer? contentSerializer = null)
                        : base(appContext, appContextHolder, executor, logger, contentSerializer)
                    {
                    }

                    public virtual Task<string> GetPlainCachedAsync() => throw new NotSupportedException();
                }

                [HttpClientApi(InheritedFrom = "HostCacheBase")]
                public interface ITestApi : IPlainCacheCarrier
                {
                    [Get("/own")]
                    Task<string> GetOwnAsync();
                }
            }
            """;

        var (output, _) = RunBothGenerators(source);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.Should().BeEmpty(
            $"宿主基类 + 普通基接口 [Cache] 必须可编译（P0-1 行 6 回归）；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        var registration = GetRegistrationText(output);
        registration.Should().Contain("cacheProvider: null",
            "宿主基类模式下普通基接口的 [Cache] 由宿主基类自管，注册侧必须传 null（对齐构造侧可选形参）");
    }

    /// <summary>评审修订 R1 反向守护：生成基模式 + 普通基接口 [Cache] —— 派生类会生成该缓存方法，注册侧必须 GetRequiredService。</summary>
    [Fact]
    public void GeneratedBase_WithPlainBaseInterfaceCache_RegistrationResolvesCache()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                public interface IPlainCacheCarrier
                {
                    [Get("/plain")]
                    [Cache(60)]
                    Task<string> GetPlainCachedAsync();
                }

                [HttpClientApi(IsAbstract = true)]
                public interface IBaseApi
                {
                    [Get("/base")]
                    Task<string> GetBaseAsync();
                }

                [HttpClientApi]
                public interface ITestApi : IBaseApi, IPlainCacheCarrier
                {
                    [Get("/derived")]
                    Task<string> GetDerivedAsync();
                }
            }
            """;

        var (output, _) = RunBothGenerators(source);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.Should().BeEmpty(
            $"生成基 + 普通基接口 [Cache] 必须可编译（评审修订 R1 回归）；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        var registration = GetRegistrationText(output);
        registration.Should().Contain("sp.GetRequiredService<global::Mud.HttpUtils.IHttpResponseCache>()",
            "生成基模式下普通基接口的缓存方法由派生类生成，注册侧必须解析缓存服务（不得收窄为 null）");
    }

    /// <summary>
    /// F3 补充（发布前复核）：接口名本身是保留字（<c>interface @class</c>）时，
    /// 注册产物里的类型引用必须成对补 <c>@</c>（<c>ISymbol.Name</c> 不含 <c>@</c>）。
    /// </summary>
    [Fact]
    public void KeywordNamedInterface_RegistrationEmitsEscapedTypeReference()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface @class
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        var (output, _) = RunBothGenerators(source);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.Should().BeEmpty(
            $"保留字命名的接口必须可编译（注册产物类型引用须补 @）；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        var registration = GetRegistrationText(output);
        registration.Should().Contain("global::TestNamespace.@class",
            "注册产物引用保留字接口名时必须补 @ 前缀（否则 global::TestNamespace.class 为 CS1001）");
    }

    /// <summary>
    /// 泛型接口：实现类可生成，但注册产物必须<b>跳过</b>——开放泛型无法绑定
    /// <c>AddTransient&lt;TService, TImplementation&gt;</c>（生成物必然 CS0305）。
    /// </summary>
    [Fact]
    public void GenericInterface_GeneratesImplementationWithoutRegistration()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IGenericApi<T>
                {
                    [Get("/items")]
                    Task<T> GetAsync();
                }
            }
            """;

        var (output, diagnostics) = RunBothGenerators(source);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.Should().BeEmpty(
            $"泛型接口必须跳过 DI 注册生成（否则开放泛型类型引用 CS0305）；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        output.SyntaxTrees.Should().NotContain(
            t => t.FilePath.Contains("HttpClientApiExtensions"),
            "泛型接口不应产出 DI 注册扩展方法");

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT012",
            "应提示泛型接口仅生成实现类、需按封闭泛型手动注册");
    }

    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        """;

    private static string GetRegistrationText(Compilation output)
        => output.SyntaxTrees.FirstOrDefault(t => t.FilePath.Contains("HttpClientApiExtensions"))?.ToString()
           ?? throw new InvalidOperationException("应生成注册产物 HttpClientApiExtensions.g.cs");

    /// <summary>
    /// 双生成器 harness：实现类生成器 + 注册生成器一并运行，返回更新后的编译单元。
    /// 注册产物依赖 DI 抽象（<c>BasicReferenceAssemblies</c> 未覆盖），按程序集名补引用。
    /// </summary>
    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunBothGenerators(string source)
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
            "RealDeclarationMatrix",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [
                new HttpInvokeClassSourceGenerator().AsSourceGenerator(),
                new HttpInvokeRegistrationGenerator().AsSourceGenerator(),
            ]);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }
}
