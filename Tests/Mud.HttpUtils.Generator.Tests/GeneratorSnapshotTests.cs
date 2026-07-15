// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// Verify 快照测试：验证源生成器对各种特性的代码生成正确性。
/// 快照基线文件存放在 _snapshots/ 目录下。
/// 使用 `dotnet test --filter Snapshot` 运行快照测试，
/// 首次运行生成 .received.txt，审核后重命名为 .verified.cs 建立基线。
/// </summary>
public class GeneratorSnapshotTests
{
    #region 基础 HTTP 方法（GET/POST/PUT/DELETE）— 场景 1-4

    /// <summary>
    /// 场景 1: 基础 GET 方法生成。
    /// </summary>
    [Fact]
    public Task Snapshot_BasicGet_ShouldEmitImplementation()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/users")]
        Task<string> GetUsersAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 2: 基础 POST 方法 + Body 参数生成。
    /// </summary>
    [Fact]
    public Task Snapshot_BasicPostWithBody_ShouldEmitImplementation()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string? Email { get; set; }
    }

    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Post("/users")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 3: PUT 方法 + Path + Body 参数。
    /// </summary>
    [Fact]
    public Task Snapshot_BasicPutWithPathAndBody_ShouldEmitImplementation()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class UpdateUserRequest
    {
        public string Name { get; set; }
    }

    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Put("/users/{id}")]
        Task<string> UpdateUserAsync([Path] int id, [Body] UpdateUserRequest request);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 4: DELETE 方法 + Path 参数。
    /// </summary>
    [Fact]
    public Task Snapshot_BasicDeleteWithPath_ShouldEmitImplementation()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Delete("/users/{id}")]
        Task DeleteUserAsync([Path] int id);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 参数绑定（Query/Path/Header/Body）— 场景 5-8

    /// <summary>
    /// 场景 5: Query 参数绑定（含可选参数和默认值）。
    /// </summary>
    [Fact]
    public Task Snapshot_QueryParameters_ShouldEmitCorrectQueryBinding()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/search")]
        Task<string> SearchAsync([Query] string keyword, [Query] int page = 1, [Query] int pageSize = 10);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 6: Path 参数绑定（多路径参数）。
    /// </summary>
    [Fact]
    public Task Snapshot_MultiplePathParameters_ShouldEmitCorrectPathBinding()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/users/{userId}/posts/{postId}")]
        Task<string> GetPostAsync([Path] int userId, [Path] int postId);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 7: Header 参数绑定。
    /// </summary>
    [Fact]
    public Task Snapshot_HeaderParameters_ShouldEmitCorrectHeaderBinding()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/data")]
        Task<string> GetDataAsync([Header("X-Request-Id")] string requestId);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 8: Body 参数绑定 + CancellationToken。
    /// </summary>
    [Fact]
    public Task Snapshot_BodyParameterWithCancellationToken_ShouldEmitCorrectBodyBinding()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class RequestModel
    {
        public string Value { get; set; }
    }

    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Post("/submit")]
        Task<string> SubmitAsync([Body] RequestModel request, CancellationToken cancellationToken = default);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 返回类型 — 场景 9-10

    /// <summary>
    /// 场景 9: Response<T> 返回类型。
    /// </summary>
    [Fact]
    public Task Snapshot_ResponseReturnType_ShouldEmitResponseHandling()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/users/{id}")]
        Task<Response<string>> GetUserWithResponseAsync([Path] int id);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 10: void 返回类型（Task）+ 多方法接口。
    /// </summary>
    [Fact]
    public Task Snapshot_VoidReturnWithMultipleMethods_ShouldEmitAllMethods()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/users")]
        Task<List<string>> GetUsersAsync();

        [Post("/users")]
        Task CreateUserAsync([Body] string name);

        [Delete("/users/{id}")]
        Task DeleteUserAsync([Path] int id);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 客户端模式（HttpClient / TokenManager）— 场景 11-12

    /// <summary>
    /// 场景 11: HttpClient 模式。
    /// </summary>
    [Fact]
    public Task Snapshot_HttpClientMode_ShouldEmitHttpClientConstructor()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
    public interface ITestApi
    {
        [Get("/data")]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 12: TokenManager 模式。
    /// </summary>
    [Fact]
    public Task Snapshot_TokenManagerMode_ShouldEmitTokenManagerConstructor()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = "ITestTokenManager")]
    public interface ITestApi
    {
        [Get("/data")]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 弹性策略 — 场景 13-15

    /// <summary>
    /// 场景 13: [Retry] 弹性策略。
    /// </summary>
    [Fact]
    public Task Snapshot_RetryAttribute_ShouldEmitRetryLogic()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/data")]
        [Retry(3, 1000)]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 14: [Timeout] 弹性策略。
    /// </summary>
    [Fact]
    public Task Snapshot_TimeoutAttribute_ShouldEmitTimeoutLogic()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/data")]
        [Timeout(5000)]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 15: [CircuitBreaker] 弹性策略。
    /// </summary>
    [Fact]
    public Task Snapshot_CircuitBreakerAttribute_ShouldEmitCircuitBreakerLogic()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/data")]
        [CircuitBreaker(5, 30000)]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 缓存与令牌 — 场景 16-18

    /// <summary>
    /// 场景 16: [Cache] 缓存特性。
    /// </summary>
    [Fact]
    public Task Snapshot_CacheAttribute_ShouldEmitCacheLogic()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/data")]
        [Cache(60)]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 17: [Token] 令牌注入（Header 模式）。
    /// </summary>
    [Fact]
    public Task Snapshot_TokenHeaderMode_ShouldEmitTokenInjection()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = "ITestTokenManager")]
    public interface ITestApi
    {
        [Get("/secure-data")]
        [Token(TokenInjectionMode.Header, "Authorization", "Bearer {0}")]
        Task<string> GetSecureDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 18: [Token] 令牌注入（Query 模式）。
    /// </summary>
    [Fact]
    public Task Snapshot_TokenQueryMode_ShouldEmitTokenAsQueryParameter()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = "ITestTokenManager")]
    public interface ITestApi
    {
        [Get("/data")]
        [Token(TokenInjectionMode.Query, "access_token")]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 接口级配置 — 场景 19-20

    /// <summary>
    /// 场景 19: [BasePath] + [Header] 接口级配置。
    /// </summary>
    [Fact]
    public Task Snapshot_BasePathAndInterfaceHeaders_ShouldEmitCorrectConfiguration()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    [BasePath("/api/v1")]
    [Header("X-API-Version", "2.0")]
    public interface ITestApi
    {
        [Get("/users")]
        Task<string> GetUsersAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 20: 接口属性（Path 属性）。
    /// </summary>
    [Fact]
    public Task Snapshot_InterfaceProperties_ShouldEmitPropertyImplementation()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Path]
        string TenantId { get; set; }

        [Get("/tenants/{TenantId}/data")]
        Task<string> GetDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 高级场景 — 场景 21-22

    /// <summary>
    /// 场景 21: [FilePath] 下载场景。
    /// </summary>
    [Fact]
    public Task Snapshot_FilePathDownload_ShouldEmitDownloadLogic()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = "ITestTokenManager")]
    public interface ITestApi
    {
        [Get("/files/{fileId}/download")]
        Task DownloadFileAsync([Path] string fileId, [FilePath] string filePath);
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>
    /// 场景 22: [InheritedFrom] 继承模式。
    /// </summary>
    [Fact]
    public Task Snapshot_InheritedFromMode_ShouldEmitDerivedClass()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = "ITestTokenManager")]
    public interface IBaseApi
    {
        [Get("/base")]
        Task<string> GetBaseDataAsync();
    }

    [HttpClientApi(TokenManage = "ITestTokenManager", InheritedFrom = "IBaseApi")]
    public interface IDerivedApi : IBaseApi
    {
        [Get("/derived")]
        Task<string> GetDerivedDataAsync();
    }
}
""";
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion
}
