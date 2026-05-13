namespace Mud.HttpUtils.Generator.Tests;

public class GeneratorCompilationTests
{
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
        var compilation = CreateCompilation(source);
        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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

    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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

        diagnostics.Should().BeEmpty();
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/upload"")]
        [MultipartForm]
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    [InterfaceQuery(Name = ""version"", Value = ""v1"")]
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

    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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

        diagnostics.Should().BeEmpty();
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

    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
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
