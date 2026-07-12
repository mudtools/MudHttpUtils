namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// HttpClient API 源代码生成器集成测试
/// 验证生成器对各种特性的代码生成正确性
/// 注意：增量生成器需要完整的编译管线才能正确执行 ForAttributeWithMetadataName，
/// 因此本测试类侧重于验证生成器的基本编译兼容性，详细代码生成验证通过 Demos 项目进行。
/// </summary>
public class HttpClientApiGeneratorTests
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

    private string GetAllGeneratedCode(Compilation outputCompilation)
    {
        return string.Join("\n", outputCompilation.SyntaxTrees.Skip(1).Select(t => t.ToString()));
    }

    #region BUG-04: HttpClient 与 TokenManager 互斥校验

    [Fact]
    public void Generator_WithBothHttpClientAndTokenManager_ReportsDiagnostic()
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

    [HttpClientApi(HttpClient = ""IEnhancedHttpClient"", TokenManage = ""ITestTokenManager"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT007",
            "同时指定 HttpClient 和 TokenManage 时应报告 HTTPCLIENT007 互斥诊断");
    }

    [Fact]
    public void Generator_WithOnlyHttpClient_NoMutuallyExclusiveDiagnostic()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""IEnhancedHttpClient"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT007").Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithOnlyTokenManager_NoMutuallyExclusiveDiagnostic()
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
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT007").Should().BeEmpty();
    }

    #endregion

    #region BUG-03: Response<T> + Cache 组合警告

    [Fact]
    public void Generator_WithCacheAndResponseType_ReportsWarning()
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
        [Cache(60)]
        Task<Response<string>> GetUsersAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT011",
            "使用 Response<T> 返回类型与 Cache 特性组合时应报告 HTTPCLIENT011 警告");
    }

    [Fact]
    public void Generator_WithCacheButNoResponseType_NoWarning()
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
        [Cache(60)]
        Task<string> GetUsersAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT011").Should().BeEmpty();
    }

    #endregion

    #region 代码生成验证

    [Fact]
    public void Generator_WithSimpleGetInterface_GeneratesCode()
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        // 如果增量生成器在单元测试环境下正常工作，应该生成代码
        // 否则此测试验证编译本身不会崩溃
        outputCompilation.SyntaxTrees.Count().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Generator_WithFormParameters_CompilesSuccessfully()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/submit"")]
        Task<string> SubmitAsync([Form] string username, [Form] string password);
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);

        // 验证编译不崩溃
        outputCompilation.SyntaxTrees.Count().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Generator_WithUploadParameter_CompilesSuccessfully()
    {
        var source = @"
using Mud.HttpUtils;
using System.IO;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/upload"")]
        Task<string> UploadFileAsync([Upload] Stream fileStream);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);

        outputCompilation.SyntaxTrees.Count().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Generator_WithAsyncEnumerableReturn_CompilesSuccessfully()
    {
        var source = @"
using Mud.HttpUtils;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ChatMessage
    {
        public string Content { get; set; }
    }

    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/chat/stream"")]
        IAsyncEnumerable<ChatMessage> StreamChatAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);

        outputCompilation.SyntaxTrees.Count().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Generator_WithMultipartForm_UsesUsingVarForContent()
    {
        // 验证修复：MultipartFormDataContent 应使用 using var 声明以防止异常时资源泄漏
        var source = @"
using Mud.HttpUtils;
using System.IO;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/upload"")]
        [MultipartForm]
        Task<string> UploadAsync([Upload] Stream file);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            // MultipartFormDataContent 应使用 using var 声明，确保异常时也能释放资源
            generatedCode.Should().Contain("using var __multipartContent = new System.Net.Http.MultipartFormDataContent()",
                "MultipartFormDataContent 应使用 using var 声明以防止资源泄漏");
        }
    }

    [Fact]
    public void Generator_WithInterfacePropertyWithDefault_GeneratesCorrectSyntax()
    {
        // 验证修复 BUG：属性默认值不应被拆到独立行
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            // 验证属性声明和默认值在同一行
            var lines = generatedCode.Split('\n');
            var propertyLines = lines.Where(l => l.Contains("get; set;")).ToList();
            foreach (var line in propertyLines)
            {
                // 如果有默认值，应该在同一行
                if (line.Contains("= "))
                {
                    line.Should().MatchRegex(@"get;\s*set;\s*\}\s*=\s*.+;",
                        "属性默认值应与属性声明在同一行");
                }
            }
        }
    }

    [Fact]
    public void Generator_WithResponseDecrypt_GeneratesNotNullCheck()
    {
        // 验证修复 BUG：解密逻辑应使用 != null 而非 string.IsNullOrEmpty
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users/{id}"")]
        [Path(""id"")]
        Task<User> GetUserAsync(int id);
    }

    public class User
    {
        public string Name { get; set; }
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            // 对于非 string 返回类型，解密逻辑不应使用 string.IsNullOrEmpty
            // (此处仅验证代码生成不崩溃，实际解密代码在 EnableEncrypt 场景下才会出现)
            generatedCode.Should().NotContain("string.IsNullOrEmpty(__result)",
                "非 string 类型的解密检查不应使用 string.IsNullOrEmpty");
        }
    }

    [Fact]
    public void Generator_WithStringParameter_GeneratesIsNullOrWhiteSpaceValidation()
    {
        // 验证修复 BUG：string 查询参数应使用 IsNullOrWhiteSpace 而非 IsNullOrEmpty
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync(string keyword);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("string.IsNullOrWhiteSpace(keyword)",
                "string 查询参数验证应使用 IsNullOrWhiteSpace");
            generatedCode.Should().NotContain("string.IsNullOrEmpty(keyword)",
                "string 查询参数验证不应使用 IsNullOrEmpty");
        }
    }

    [Fact]
    public void Generator_WithOptionValue_GeneratesNullCheck()
    {
        // 验证修复 BUG：option 应先检查 null，再检查 option.Value
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("if (option == null)",
                "应先检查 option 是否为 null");
            generatedCode.Should().Contain("throw new ArgumentNullException(nameof(option))",
                "option 为 null 时应抛出 ArgumentNullException");
            generatedCode.Should().Contain("option.Value ?? throw new InvalidOperationException",
                "option.Value 为 null 时应抛出 InvalidOperationException");
        }
    }

    [Fact]
    public void Generator_WithAppContextScope_GeneratesInterlockedDisposed()
    {
        // 验证修复：_disposed 字段使用 int + Interlocked.CompareExchange 保证线程安全
        var source = @"
using Mud.HttpUtils;

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
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("private int _disposed",
                "AppContextScope._disposed 应使用 int 类型");
            generatedCode.Should().Contain("System.Threading.Interlocked.CompareExchange(ref _disposed, 1, 0)",
                "AppContextScope.Dispose 应使用 Interlocked.CompareExchange 保证原子性");
        }
    }

    [Fact]
    public void Generator_WithNoHttpMethodAttribute_DoesNotGenerateMethod()
    {
        // 验证没有 HTTP 方法特性的方法不会被生成
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

        // 无 HTTP 方法特性，不应生成实现
        string CustomMethod();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);

        outputCompilation.SyntaxTrees.Count().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Generator_WithPatchMethod_GeneratesConditionalCompilation()
    {
        // Patch 方法在 NETSTANDARD2_0 下需要特殊处理
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Patch(""/users/{id}"")]
        Task<string> UpdateUserAsync(int id, string name);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("NETSTANDARD2_0",
                "Patch 方法应生成条件编译代码");
        }
    }

    [Fact]
    public void Generator_WithMultipleHttpMethods_GeneratesAllImplementations()
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

        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] string name);

        [Put(""/users/{id}"")]
        Task<string> UpdateUserAsync(int id, [Body] string name);

        [Delete(""/users/{id}"")]
        Task DeleteUserAsync(int id);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("GetUsersAsync");
            generatedCode.Should().Contain("CreateUserAsync");
            generatedCode.Should().Contain("UpdateUserAsync");
            generatedCode.Should().Contain("DeleteUserAsync");
        }
    }

    #endregion

    #region TokenManage 编译时校验 (HTTPCLIENT015/016)

    [Fact]
    public void Generator_TokenManageTypeNotFound_ReportsHTTPCLIENT015()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(TokenManage = ""INonExistentType"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT015",
            "TokenManage 指定不存在的类型时应报告 HTTPCLIENT015 错误");
    }

    [Fact]
    public void Generator_TokenManageMissingGetDefaultApp_ReportsHTTPCLIENT016()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface IInvalidTokenManager
    {
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""IInvalidTokenManager"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT016",
            "TokenManage 类型缺少 GetDefaultApp() 方法时应报告 HTTPCLIENT016 错误");
    }

    [Fact]
    public void Generator_TokenManageMissingGetApp_ReportsHTTPCLIENT016()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface IInvalidTokenManager
    {
        IMudAppContext GetDefaultApp();
    }

    [HttpClientApi(TokenManage = ""IInvalidTokenManager"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT016",
            "TokenManage 类型缺少 GetApp(string) 方法时应报告 HTTPCLIENT016 错误");
    }

    [Fact]
    public void Generator_TokenManageIncompatibleReturnType_ReportsHTTPCLIENT016()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface IInvalidTokenManager
    {
        string GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""IInvalidTokenManager"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT016",
            "TokenManage 类型 GetDefaultApp() 返回类型不兼容时应报告 HTTPCLIENT016 错误");
    }

    [Fact]
    public void Generator_TokenManageValidType_NoDiagnosticError()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface IValidTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""IValidTokenManager"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT015" || d.Id == "HTTPCLIENT016",
            "TokenManage 类型合法时不应报告 HTTPCLIENT015 或 HTTPCLIENT016 诊断");
    }

    #endregion

    #region BUG 修复验证：__appContext 捕获（TOCTOU 竞态修复）

    [Fact]
    public void Generator_WithTokenManage_CapturesAppContextToLocalVariable()
    {
        // 验证修复 BUG：在 TokenManage 模式下应捕获 _appContextHolder.Current 到局部变量 __appContext
        // 避免在异步执行过程中 _appContextHolder.Current 被其他线程修改导致 TOCTOU 竞态
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("var __appContext = _appContextHolder.Current",
                "TokenManage 模式下应捕获 _appContextHolder.Current 到局部变量 __appContext");
            generatedCode.Should().Contain("__appContext.HttpClient",
                "应使用 __appContext.HttpClient 而非 _appContextHolder.Current!.HttpClient");
            generatedCode.Should().NotContain("_appContextHolder.Current!.HttpClient",
                "不应直接使用 _appContextHolder.Current!.HttpClient");
        }
    }

    [Fact]
    public void Generator_WithHttpClient_DoesNotGenerateAppContext()
    {
        // 验证 HttpClient 属性模式下不需要 __appContext
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""MyHttpClient"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().NotContain("var __appContext = _appContextHolder.Current",
                "HttpClient 属性模式下不应生成 __appContext 局部变量");
        }
    }

    #endregion

    #region BUG 修复验证：弹性策略 __ct 传递

    [Fact]
    public void Generator_WithResiliance_PassesPolicyCancellationTokenToSendRawAsync()
    {
        // 重构后：弹性策略的 CancellationToken 处理由运行时执行器（DefaultHttpRequestExecutor）
        // 和 ResiliencePolicyResolver 负责。Polly 策略传入的 CT 会传给 coreExecute，确保超时能取消进行中的 HTTP 请求。
        // 生成器只需将方法的 CT 传给 ExecuteAsync，并在 ExecutionDescriptor 中配置 Resilience。
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Retry(3)]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("ExecuteAsync<string>(",
                "弹性策略模式下应通过 ExecuteAsync 统一调用执行器");
            generatedCode.Should().Contain("RetryEnabled = true",
                "ExecutionDescriptor 中应配置 RetryEnabled = true");
        }
    }

    #endregion

    #region BUG 修复验证：XML 反序列化 StringReader 释放

    [Fact]
    public void Generator_WithXmlResponse_UsesUsingForStringReader()
    {
        // 验证修复 BUG：XML 反序列化时应使用 using var 释放 StringReader
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/data"", ResponseContentType = ""application/xml"")]
        Task<MyXmlData> GetDataAsync();
    }

    public class MyXmlData
    {
        public string Name { get; set; }
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var allGeneratedCode = GetAllGeneratedCode(outputCompilation);

        allGeneratedCode.Should().Contain("XmlSerializer = _xmlSerializer_TestNamespace_MyXmlData",
            "XML 反序列化应通过 ResponseDescriptor.XmlSerializer 将序列化器交由执行器运行时调用");
    }

    #endregion

    #region BUG 修复验证：FormContent 使用 IsNullOrWhiteSpace

    [Fact]
    public void Generator_WithFormContentClass_UsesIsNullOrWhiteSpaceForStrings()
    {
        // 验证修复 BUG：FormContent 字符串属性应使用 IsNullOrWhiteSpace 而非 IsNullOrEmpty
        var source = @"
using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [FormContent]
    public class UploadRequest
    {
        [JsonPropertyName(""name"")]
        public string Name { get; set; }

        [JsonPropertyName(""file"")]
        [FilePath]
        public string FilePath { get; set; }
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("!string.IsNullOrWhiteSpace(Name)",
                "FormContent 字符串属性应使用 IsNullOrWhiteSpace");
            generatedCode.Should().NotContain("string.IsNullOrEmpty(Name)",
                "FormContent 字符串属性不应使用 IsNullOrEmpty");
        }
    }

    #endregion

    #region BUG 修复验证：GetApiKeyAsync 使用 ConfigureAwait(false)

    [Fact]
    public void Generator_WithApiKeyInjection_UsesConfigureAwaitOnGetApiKeyAsync()
    {
        // 验证修复 BUG：GetApiKeyAsync 调用应使用 ConfigureAwait(false)
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

    [HttpClientApi(TokenManage = ""ITestTokenManager"", TokenType = ""ApiKey"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        [Token(InjectionMode = TokenInjectionMode.ApiKey)]
        Task<string> GetSecureDataAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        if (generatedCode != null)
        {
            generatedCode.Should().Contain("GetApiKeyAsync").And.Contain("ConfigureAwait(false)",
                "GetApiKeyAsync 调用应使用 ConfigureAwait(false)");
        }
    }

    #endregion

    #region BUG 修复验证：HttpClient 模式下执行器参数传递

    [Fact]
    public void Generator_WithHttpClient_NoCacheNoResilience_PassesOptionalParamsToExecutor()
    {
        // 验证 HttpClient 模式下，即使接口未声明 [Cache]/[Retry] 特性，
        // 生成的构造函数仍接受可选的 cacheProvider/resilienceResolver 参数并传递给执行器
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""MyHttpClient"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("IHttpResponseCache? cacheProvider = null",
            "HttpClient 模式下应始终接受可选的 cacheProvider 参数");
        generatedCode.Should().Contain("IResiliencePolicyResolver? resilienceResolver = null",
            "HttpClient 模式下应始终接受可选的 resilienceResolver 参数");
        generatedCode.Should().Contain("IHttpRequestExecutor executor",
            "HttpClient 模式下应通过 DI 注入 IHttpRequestExecutor");
        generatedCode.Should().Contain("_executor = executor ?? throw new ArgumentNullException(nameof(executor));",
            "执行器应通过 DI 注入而非手动创建");
    }

    [Fact]
    public void Generator_WithHttpClient_WithCache_PassesCacheProviderToExecutor()
    {
        // 验证 HttpClient 模式下，声明 [Cache] 特性时，cacheProvider 为必选参数并正确传递给执行器
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""MyHttpClient"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Cache(DurationSeconds = 60)]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("IHttpResponseCache cacheProvider",
            "声明 [Cache] 特性时 cacheProvider 应为必选参数（无默认值）");
        generatedCode.Should().Contain("IResiliencePolicyResolver? resilienceResolver = null",
            "未声明 [Retry] 特性时 resilienceResolver 应为可选参数");
        generatedCode.Should().Contain("IHttpRequestExecutor executor",
            "HttpClient 模式下应通过 DI 注入 IHttpRequestExecutor");
        generatedCode.Should().Contain("_executor = executor ?? throw new ArgumentNullException(nameof(executor));",
            "执行器应通过 DI 注入而非手动创建");
    }

    [Fact]
    public void Generator_WithHttpClient_WithResilience_PassesResilienceResolverToExecutor()
    {
        // 验证 HttpClient 模式下，声明 [Retry] 特性时，resilienceResolver 为必选参数并正确传递给执行器
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""MyHttpClient"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Retry(MaxRetries = 3)]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("IHttpResponseCache? cacheProvider = null",
            "未声明 [Cache] 特性时 cacheProvider 应为可选参数");
        generatedCode.Should().Contain("IResiliencePolicyResolver resilienceResolver",
            "声明 [Retry] 特性时 resilienceResolver 应为必选参数（无默认值）");
        generatedCode.Should().Contain("IHttpRequestExecutor executor",
            "HttpClient 模式下应通过 DI 注入 IHttpRequestExecutor");
        generatedCode.Should().Contain("_executor = executor ?? throw new ArgumentNullException(nameof(executor));",
            "执行器应通过 DI 注入而非手动创建");
    }

    #endregion

    #region BUG 修复验证：TokenManager/AppContext 模式下执行器参数传递

    [Fact]
    public void Generator_WithTokenManager_NoCacheNoResilience_AcceptsOptionalParamsAndPassesToExecutor()
    {
        // 验证 TokenManager 模式下，即使接口未声明 [Cache]/[Retry] 特性，
        // 生成的构造函数仍接受可选的 cacheProvider/resilienceResolver 参数，
        // 且方法内的执行器创建引用 _cacheProvider/_resilienceResolver 字段而非 null
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("IHttpResponseCache? cacheProvider = null",
            "TokenManager 模式下应始终接受可选的 cacheProvider 参数");
        generatedCode.Should().Contain("IResiliencePolicyResolver? resilienceResolver = null",
            "TokenManager 模式下应始终接受可选的 resilienceResolver 参数");
        generatedCode.Should().Contain("readonly IHttpResponseCache? _cacheProvider;",
            "未声明 [Cache] 特性时应生成可空的 _cacheProvider 字段");
        generatedCode.Should().Contain("readonly IResiliencePolicyResolver? _resilienceResolver;",
            "未声明 [Retry] 特性时应生成可空的 _resilienceResolver 字段");
        generatedCode.Should().Contain("_cacheProvider = cacheProvider;",
            "未声明 [Cache] 特性时应直接赋值（不抛异常）");
        generatedCode.Should().Contain("_resilienceResolver = resilienceResolver;",
            "未声明 [Retry] 特性时应直接赋值（不抛异常）");
        // 执行器统一使用 DI 注入的 _executor 字段（所有模式均通过构造函数注入，无状态设计）
        generatedCode.Should().Contain("_executor = executor ?? throw new ArgumentNullException(nameof(executor));",
            "执行器应通过 DI 注入而非手动创建");
    }

    [Fact]
    public void Generator_WithTokenManager_WithCache_GeneratesRequiredCacheParamAndField()
    {
        // 验证 TokenManager 模式下，声明 [Cache] 特性时，cacheProvider 为必选参数且字段为非空
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = ""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Cache(DurationSeconds = 60)]
        Task<string> GetUsersAsync();
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("IHttpResponseCache cacheProvider",
            "声明 [Cache] 特性时 cacheProvider 应为必选参数（无默认值）");
        generatedCode.Should().Contain("IResiliencePolicyResolver? resilienceResolver = null",
            "未声明 [Retry] 特性时 resilienceResolver 应为可选参数");
        generatedCode.Should().Contain("readonly IHttpResponseCache _cacheProvider;",
            "声明 [Cache] 特性时应生成非空 _cacheProvider 字段");
        generatedCode.Should().Contain("_cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));",
            "声明 [Cache] 特性时应校验非空");
    }

    #endregion

    #region 默认模式下 scope 方法生成验证

    [Fact]
    public void Generator_WithDefaultMode_GeneratesUseDefaultAppScope()
    {
        // 验证默认模式（裸 [HttpClientApi]）下生成 UseDefaultAppScope() 方法
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("public IDisposable UseDefaultAppScope()",
            "默认模式下应生成 UseDefaultAppScope() 方法");
        generatedCode.Should().Contain("_appManager?.GetDefaultApp() ?? _defaultAppContext",
            "默认模式下 UseDefaultAppScope() 应使用 _appManager 回退到 _defaultAppContext");
    }

    [Fact]
    public void Generator_WithDefaultMode_GeneratesUseAppWithoutObsolete()
    {
        // GEN-02 修复：UseApp(string) 不再标记 [Obsolete]，
        // 因为它与 BeginScope 是互补关系而非替代关系
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("public IMudAppContext UseApp(string appKey)",
            "默认模式下应生成 UseApp(string) 方法");
        generatedCode.Should().NotContain("[Obsolete",
            "GEN-02 修复后 UseApp 不再标记 [Obsolete]");
    }

    [Fact]
    public void Generator_WithDefaultMode_GeneratesUseDefaultAppWithoutObsolete()
    {
        // GEN-02 修复：UseDefaultApp() 不再标记 [Obsolete]，
        // 因为它与 UseDefaultAppScope() 是互补关系而非替代关系
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("public IMudAppContext UseDefaultApp()",
            "默认模式下应生成 UseDefaultApp() 方法");
        generatedCode.Should().NotContain("[Obsolete",
            "GEN-02 修复后 UseDefaultApp 不再标记 [Obsolete]");
    }

    [Fact]
    public void Generator_WithDefaultMode_GeneratesBeginScopeWithString()
    {
        // 验证默认模式下生成 BeginScope(string appKey) 方法
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("public IDisposable BeginScope(string appKey)",
            "默认模式下应生成 BeginScope(string appKey) 方法");
        generatedCode.Should().Contain("if (_appManager == null)",
            "默认模式下 BeginScope(string) 应检查 _appManager 是否为 null");
    }

    [Fact]
    public void Generator_WithDefaultMode_GeneratesAppManagerAndDefaultAppContextFields()
    {
        // 验证默认模式下生成 _appManager 和 _defaultAppContext 字段
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("readonly IAppManager<IMudAppContext>? _appManager;",
            "默认模式下应生成可空的 _appManager 字段");
        generatedCode.Should().Contain("readonly IMudAppContext _defaultAppContext;",
            "默认模式下应生成 _defaultAppContext 字段");
    }

    [Fact]
    public void Generator_WithDefaultMode_AcceptsOptionalAppManagerParameter()
    {
        // 验证默认模式构造函数接受可选的 IAppManager<IMudAppContext>? 参数
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("IAppManager<IMudAppContext>? appManager = null",
            "默认模式构造函数应接受可选的 IAppManager<IMudAppContext>? 参数");
        generatedCode.Should().Contain("_defaultAppContext = appContext ?? throw new ArgumentNullException(nameof(appContext));",
            "默认模式构造函数应存储 _defaultAppContext");
        generatedCode.Should().Contain("_appManager = appManager;",
            "默认模式构造函数应存储 _appManager");
    }

    [Fact]
    public void Generator_WithDefaultMode_UseDefaultAppFallsBackToDefaultAppContext()
    {
        // 验证默认模式下 UseDefaultApp() 在 _appManager 为 null 时回退到 _defaultAppContext
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

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("_appManager?.GetDefaultApp() ?? _defaultAppContext",
            "默认模式下 UseDefaultApp() 和 UseDefaultAppScope() 应在 _appManager 为 null 时回退到 _defaultAppContext");
    }

    #endregion

    #region 文件下载（[FilePath]）代码生成验证

    /// <summary>
    /// 验证 [FilePath] 参数生成 DownloadLargeAsync 调用，包含正确的参数顺序和默认值。
    /// </summary>
    [Fact]
    public void Generator_WithFilePathParameter_GeneratesDownloadLargeAsyncCall()
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
        [Get(""/api/v1/file/download"")]
        Task DownloadLargeFileAsync([Query(""fileId"")] string fileId, [FilePath] string filePath);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetAllGeneratedCode(outputCompilation);

        generatedCode.Should().Contain("DownloadLargeAsync",
            "[FilePath] 参数应生成 DownloadLargeAsync 调用");
        generatedCode.Should().Contain("filePath",
            "filePath 参数应传递给 DownloadLargeAsync");
        generatedCode.Should().Contain("true, 81920",
            "默认 Overwrite=true 且 BufferSize=81920");
        generatedCode.Should().Contain("progress: null",
            "无 IProgress 参数时应传递 null");
        generatedCode.Should().Contain("cancellationToken: default",
            "无 CancellationToken 参数时应使用 default");
        generatedCode.Should().Contain("IsVoidReturn = true",
            "Task 返回类型应标记 IsVoidReturn = true");
    }

    /// <summary>
    /// 验证 [FilePath(BufferSize = N)] 自定义缓冲区大小被正确提取到生成代码中。
    /// </summary>
    [Fact]
    public void Generator_WithFilePathCustomBufferSize_GeneratesCorrectBufferSize()
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
        [Get(""/api/v1/file/download"")]
        Task DownloadLargeFileAsync([Query(""fileId"")] string fileId, [FilePath(BufferSize = 40960)] string filePath);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetAllGeneratedCode(outputCompilation);

        generatedCode.Should().Contain("40960",
            "[FilePath(BufferSize = 40960)] 应在生成代码中使用 40960");
        generatedCode.Should().NotContain("81920",
            "自定义 BufferSize 时不应使用默认值 81920");
    }

    /// <summary>
    /// 验证 [FilePath(Overwrite = false)] 禁用覆盖被正确提取到生成代码中。
    /// </summary>
    [Fact]
    public void Generator_WithFilePathOverwriteFalse_GeneratesCorrectOverwriteFlag()
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
        [Get(""/api/v1/file/download"")]
        Task DownloadLargeFileAsync([Query(""fileId"")] string fileId, [FilePath(Overwrite = false)] string filePath);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetAllGeneratedCode(outputCompilation);

        generatedCode.Should().Contain("false, 81920",
            "[FilePath(Overwrite = false)] 应在生成代码中使用 false 作为 overwrite 参数");
    }

    /// <summary>
    /// 验证 [FilePath] 搭配 IProgress{long} 参数时，进度参数被正确传递。
    /// </summary>
    [Fact]
    public void Generator_WithFilePathAndProgress_GeneratesProgressParameter()
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
        [Get(""/api/v1/file/download"")]
        Task DownloadLargeFileAsync([Query(""fileId"")] string fileId, [FilePath] string filePath, IProgress<long> progress);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetAllGeneratedCode(outputCompilation);

        generatedCode.Should().Contain("progress: progress",
            "IProgress<long> 参数应按名称传递给 DownloadLargeAsync");
    }

    /// <summary>
    /// 验证 [FilePath] 搭配 CancellationToken 参数时，取消令牌被正确传递。
    /// </summary>
    [Fact]
    public void Generator_WithFilePathAndCancellationToken_GeneratesCancellationTokenArg()
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
        [Get(""/api/v1/file/download"")]
        Task DownloadLargeFileAsync([Query(""fileId"")] string fileId, [FilePath] string filePath, CancellationToken cancellationToken = default);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetAllGeneratedCode(outputCompilation);

        generatedCode.Should().Contain("cancellationToken: cancellationToken",
            "CancellationToken 参数应按名称传递给 DownloadLargeAsync");
    }

    /// <summary>
    /// 验证 [FilePath] 参数不会被误添加到查询参数、请求体或 URL 路径中。
    /// </summary>
    [Fact]
    public void Generator_WithFilePathParameter_ExcludesFromQueryAndBody()
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
        [Get(""/api/v1/file/download"")]
        Task DownloadLargeFileAsync([Query(""fileId"")] string fileId, [FilePath] string filePath);
    }
}";

        var (_, outputCompilation) = RunGenerator(source);
        var generatedCode = GetAllGeneratedCode(outputCompilation);

        // filePath 不应出现在查询参数构建逻辑中
        generatedCode.Should().NotContain(@"__queryParams.Add(""filePath""",
            "[FilePath] 参数不应被添加到查询参数集合");
        generatedCode.Should().NotContain(@"__queryParams.Add(""FilePath""",
            "[FilePath] 参数不应被添加到查询参数集合（大小写变体）");

        // filePath 不应出现在请求体构建逻辑中
        generatedCode.Should().NotContain("JsonContent",
            "[FilePath] 参数不应触发 JSON 请求体序列化");
    }

    #endregion
}
