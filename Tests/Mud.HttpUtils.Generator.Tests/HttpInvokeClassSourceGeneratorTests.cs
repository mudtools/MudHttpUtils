namespace Mud.HttpUtils.Generator.Tests;

public class HttpInvokeClassSourceGeneratorTests
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

    [Fact]
    public void Generator_WithNoAttributes_ShouldNotGenerateCode()
    {
        var source = @"
namespace TestNamespace
{
    public interface ITestInterface
    {
        Task<string> GetDataAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        outputCompilation.SyntaxTrees.Count().Should().Be(1);
    }

    [Fact]
    public void Generator_WithHttpClientApiAttribute_ShouldGenerateImplementation()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty("带有 [HttpClientApi] 特性的接口应生成实现代码");
        generatedCode.Should().Contain("ITestApi", "生成的代码应包含原始接口名");
        generatedCode.Should().Contain("GetUsersAsync", "生成的代码应包含接口方法实现");
    }

    [Fact]
    public void Generator_WithMultipleMethods_ShouldGenerateAllMethods()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();

        [Post(""/users"")]
        Task<string> CreateUserAsync(string name);

        [Put(""/users/{id}"")]
        Task<string> UpdateUserAsync(int id, string name);

        [Delete(""/users/{id}"")]
        Task DeleteUserAsync(int id);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
        generatedCode.Should().Contain("CreateUserAsync");
        generatedCode.Should().Contain("UpdateUserAsync");
        generatedCode.Should().Contain("DeleteUserAsync");
    }

    [Fact]
    public void Generator_WithPathParameters_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users/{userId}/posts/{postId}"")]
        Task<string> GetUserPostAsync(int userId, int postId);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("userId");
        generatedCode.Should().Contain("postId");
    }

    [Fact]
    public void Generator_WithQueryParameters_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([Query] string keyword, [Query] int page, [Query] int size);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("keyword");
        generatedCode.Should().Contain("page");
        generatedCode.Should().Contain("size");
    }

    [Fact]
    public void Generator_WithBodyParameter_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class UserData
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] UserData user);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("CreateUserAsync");
        generatedCode.Should().Contain("user");
    }

    [Fact]
    public void Generator_WithHeaderParameter_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync([Header(""Authorization"")] string token);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("Authorization");
        generatedCode.Should().Contain("token");
    }

    [Fact]
    public void Generator_WithTokenManager_ShouldGenerateTokenInjectionCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("ITestTokenManager");
        generatedCode.Should().Contain("GetTokenAsync");
    }

    [Fact]
    public void Generator_WithBasicAuthTokenInjection_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    [Token(InjectionMode = TokenInjectionMode.BasicAuth)]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("__basicCredentials");
    }

    [Fact]
    public void Generator_WithBasicAuthTokenInjectionOnMethod_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Token(InjectionMode = TokenInjectionMode.BasicAuth)]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
    }

    [Fact]
    public void Generator_WithFormParameters_ShouldGenerateFormContent()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/login"")]
        Task<string> LoginAsync([Form] string username, [Form] string password);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("username");
        generatedCode.Should().Contain("password");
    }

    [Fact]
    public void Generator_WithUploadParameter_ShouldGenerateUploadCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;
using System.IO;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/upload"")]
        [MultipartForm]
        Task<string> UploadAsync([Upload] Stream fileStream);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("fileStream");
        generatedCode.Should().Contain("MultipartFormDataContent");
    }

    [Fact]
    public void Generator_WithResponseReturnType_ShouldGenerateResponseHandling()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users/{id}"")]
        Task<Response<string>> GetUserAsync([Path] int id);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUserAsync");
        generatedCode.Should().Contain("Response");
    }

    [Fact]
    public void Generator_WithCacheAttribute_ShouldGenerateCacheHandling()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Cache(60)]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
    }

    [Fact]
    public void Generator_WithInterfaceQueryProperty_ShouldGeneratePropertyImplementation()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    [InterfaceQuery(""version"", ""v1"")]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("version");
    }

    [Fact]
    public void Generator_WithQueryMapParameter_ShouldGenerateFlattenCall()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class SearchParams
    {
        public string Keyword { get; set; }
        public int Page { get; set; }
    }

    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([QueryMap] SearchParams parameters);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("parameters");
    }

    [Fact]
    public void Generator_GeneratesConstructorWithDependencies()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("IAppContextSwitcher");
    }

    [Fact]
    public void Generator_GeneratesClassWithCorrectNamespace()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace MyApp.Apis
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("MyApp.Apis");
    }

    [Fact]
    public void Generator_WithAsyncEnumerableReturn_ShouldGenerateStreamCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ChatMessage
    {
        public string Content { get; set; }
    }

    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/chat/stream"")]
        IAsyncEnumerable<ChatMessage> StreamChatAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("StreamChatAsync");
    }

    [Fact]
    public void Generator_WithBodyEnableEncrypt_ShouldGenerateEncryptionCode()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class SecretData
    {
        public string Value { get; set; }
    }

    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/secret"")]
        Task<string> PostSecretAsync([Body(EnableEncrypt = true)] SecretData data);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("PostSecretAsync");
        generatedCode.Should().Contain("EncryptContent", "EnableEncrypt = true 时应生成加密调用");
    }

    [Fact]
    public void Generator_WithBodyEnableEncryptAndResponse_ShouldGenerateDecryptionCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/secret"", ResponseEnableDecrypt = true)]
        Task<Response<string>> PostSecretAsync([Body(EnableEncrypt = true)] string data);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("DecryptContent", "ResponseEnableDecrypt = true 时应生成解密调用");
    }

    [Fact]
    public void Generator_WithAllowAnyStatusCode_ShouldGenerateWithoutEnsureSuccess()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    [AllowAnyStatusCode]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<Response<string>> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
    }

    [Fact]
    public void Generator_WithBasePath_ShouldIncludeBasePathInUrl()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    [BasePath(""api/v1"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetUsersAsync");
    }

    [Fact]
    public void Generator_WithCancellationTokenParameter_ShouldPassTokenToCall()
    {
        var source = @"
using Mud.HttpUtils.Attributes;
using System.Threading;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync(CancellationToken cancellationToken = default);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("cancellationToken");
    }
}
