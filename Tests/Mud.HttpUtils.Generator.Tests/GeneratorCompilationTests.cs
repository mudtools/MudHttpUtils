namespace Mud.HttpUtils.Generator.Tests;

public class GeneratorCompilationTests
{
    /// <summary>
    /// [G6-A / GEN-10] 模拟 <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> 的标准隐式 using。
    /// 原用例的输入源省略了这些 using（真实消费项目在项目文件中全局启用），
    /// 因此直接编译会因缺 using 而失败；补齐后再做整体编译断言，输入等价于合法消费配置。
    /// </summary>
    private const string ImplicitUsingsPreamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;

        """;

    private Compilation CreateCompilation(string source)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private (ImmutableArray<Diagnostic> diagnostics, Compilation outputCompilation) RunGenerator(string source)
    {
        var compilation = CreateCompilation(ImplicitUsingsPreamble + source);
        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // [G6-A / GEN-10] 真编译断言：既有实现只断言「生成器自身诊断 + 文本 Contains」，
        // 从不检查 outputCompilation.GetDiagnostics() → 生成不可编译代码时测试仍静默通过。
        // 现补齐「输入 + 生成产物」整体无 Error 级编译诊断断言（与 VerifyFixture 口径一致）。
        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        errors.Should().BeEmpty(
            $"生成产物必须可编译（GEN-10）；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        return (diagnostics, outputCompilation);
    }

    private string? GetGeneratedCode(Compilation outputCompilation)
    {
        return outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
    }

    #region Basic GET Interface - Generator Verification

    [Fact]
    public void Generator_SimpleGetInterface_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
    }

    #endregion

    #region POST with Body - Generator Verification

    [Fact]
    public void Generator_PostWithBody_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        // AOT004（CreateUserRequest 未被任何 JsonSerializerContext 覆盖）属预期告警：
        // 本测试关注代码生成，仅要求无错误级诊断。AOT004 的正向行为由
        // AotDtoCoverageAnalyzerTests 覆盖（覆盖集合现可解析引用程序集中的 Context，故会触发该告警）。
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("CreateUserAsync");
    }

    #endregion

    #region Path Parameter - Generator Verification

    [Fact]
    public void Generator_WithPathParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users/{userId}"")]
        Task<string> GetUserAsync([Path] int userId);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUserAsync");
    }

    #endregion

    #region Query Parameter - Generator Verification

    [Fact]
    public void Generator_WithQueryParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([Query] string keyword, [Query] int page);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("SearchAsync");
    }

    #endregion

    #region Header Parameter - Generator Verification

    [Fact]
    public void Generator_WithHeaderParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync([Header(""X-Request-Id"")] string requestId);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetDataAsync");
    }

    #endregion

    #region Token Management - Generator Verification

    [Fact]
    public void Generator_WithTokenManager_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        // HTTPCLIENT018 是预期警告：未显式指定 TokenManagerKey 时生成器使用默认推断值
        diagnostics.Where(d => d.Id != "HTTPCLIENT018").Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetSecureDataAsync");
    }

    #endregion

    #region Form Parameters - Generator Verification

    [Fact]
    public void Generator_WithFormParameters_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/login"")]
        Task<string> LoginAsync([Form] string username, [Form] string password);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("LoginAsync");
    }

    #endregion

    #region Multipart Form with Upload - Generator Verification

    [Fact]
    public void Generator_WithMultipartFormUpload_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.IO;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/upload"")]
        Task<string> UploadAsync([Upload] Stream fileStream, [Form] string description);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("UploadAsync");
    }

    #endregion

    #region Response<T> Return Type - Generator Verification

    [Fact]
    public void Generator_WithResponseType_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users/{id}"")]
        Task<Response<string>> GetUserAsync([Path] int id);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUserAsync");
    }

    #endregion

    #region Interface Properties - Generator Verification

    [Fact]
    public void Generator_WithInterfaceProperties_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    [InterfaceQuery("version", "v1")]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetDataAsync");
    }

    #endregion

    #region Multiple HTTP Methods - Generator Verification

    [Fact]
    public void Generator_WithMultipleMethods_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();

        [Get(""/users/{id}"")]
        Task<string> GetUserAsync([Path] int id);

        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);

        [Put(""/users/{id}"")]
        Task<string> UpdateUserAsync([Path] int id, [Body] CreateUserRequest request);

        [Delete(""/users/{id}"")]
        Task DeleteUserAsync([Path] int id);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        // 同 Generator_PostWithBody：包含未覆盖的 Body DTO 时会得到预期的 AOT004 告警。
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
        generatedCode.Should().Contain("CreateUserAsync");
        generatedCode.Should().Contain("UpdateUserAsync");
        generatedCode.Should().Contain("DeleteUserAsync");
    }

    #endregion

    #region QueryMap Parameter - Generator Verification

    [Fact]
    public void Generator_WithQueryMapParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class SearchParams
    {
        public string Keyword { get; set; }
        public int Page { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([QueryMap] SearchParams parameters);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var generatedCode = GetGeneratedCode(outputCompilation);
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("SearchAsync");
    }

    #endregion

    #region No Diagnostics for Valid Interfaces

    [Fact]
    public void Generator_ValidInterface_NoGeneratorDiagnostics()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
    }

    #endregion
}
