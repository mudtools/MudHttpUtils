// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 快照输入源的批量编译断言（GEN-10 / M0 A-0）：
/// 遍历 <see cref="GeneratorSnapshotTests"/> 的每一份输入源，统一走
/// <see cref="GeneratorCompileAssert.RunAndAssertNoErrors"/>，断言「输入 + 生成产物」可编译。
/// <para>分工：快照只负责「形状」，本类负责「可编译性」，避免快照「看着对、实际编译不过」。</para>
/// </summary>
public class GeneratorSnapshotCompileTests
{
    /// <summary>
    /// 模拟 <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> 的标准隐式 using 头。
    /// 快照输入源与真实消费配置一致（依赖 ImplicitUsings），而 <see cref="GeneratorCompileAssert"/> 刻意不注入，
    /// 故此处补齐，使输入源等价于合法消费方。
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

    /// <summary>
    /// 与 <see cref="GeneratorSnapshotTests"/> 一一对应的输入源（名称对齐快照测试方法）。
    /// 快照基线文件不影响——本类只做编译断言，不产出快照。
    /// </summary>
    public static IEnumerable<object[]> Scenarios()
    {
        foreach (var (name, source) in SnapshotInputSources.All)
        {
            yield return new object[] { name, source };
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void SnapshotInput_Compiles(string name, string source)
    {
        GeneratorCompileAssert.RunAndAssertNoErrors(
            ImplicitUsingsPreamble + source,
            description: $"快照输入「{name}」的输入 + 生成产物必须可编译");
    }
}

/// <summary>
/// 快照输入源的唯一权威集合，供快照测试与编译断言测试共用（消除源字符串重复、防止漂移）。
/// </summary>
public static class SnapshotInputSources
{
    public record Scenario(string Name, string Source);

    public static readonly Scenario[] All =
    {
        new("Snapshot_BasicGet_ShouldEmitImplementation", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/users")]
        Task<string> GetUsersAsync();
    }
}
"""),
        new("Snapshot_BasicPostWithBody_ShouldEmitImplementation", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string? Email { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Post("/users")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);
    }
}
"""),
        new("Snapshot_BasicPutWithPathAndBody_ShouldEmitImplementation", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class UpdateUserRequest
    {
        public string Name { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Put("/users/{id}")]
        Task<string> UpdateUserAsync([Path] int id, [Body] UpdateUserRequest request);
    }
}
"""),
        new("Snapshot_BasicDeleteWithPath_ShouldEmitImplementation", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Delete("/users/{id}")]
        Task DeleteUserAsync([Path] int id);
    }
}
"""),
        new("Snapshot_QueryParameters_ShouldEmitCorrectQueryBinding", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/search")]
        Task<string> SearchAsync([Query] string keyword, [Query] int page = 1, [Query] int pageSize = 10);
    }
}
"""),
        new("Snapshot_MultiplePathParameters_ShouldEmitCorrectPathBinding", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/users/{userId}/posts/{postId}")]
        Task<string> GetPostAsync([Path] int userId, [Path] int postId);
    }
}
"""),
        new("Snapshot_HeaderParameters_ShouldEmitCorrectHeaderBinding", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/data")]
        Task<string> GetDataAsync([Header("X-Request-Id")] string requestId);
    }
}
"""),
        new("Snapshot_BodyParameterWithCancellationToken_ShouldEmitCorrectBodyBinding", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class RequestModel
    {
        public string Value { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Post("/submit")]
        Task<string> SubmitAsync([Body] RequestModel request, CancellationToken cancellationToken = default);
    }
}
"""),
        new("Snapshot_ResponseReturnType_ShouldEmitResponseHandling", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/users/{id}")]
        Task<Response<string>> GetUserWithResponseAsync([Path] int id);
    }
}
"""),
        new("Snapshot_VoidReturnWithMultipleMethods_ShouldEmitAllMethods", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
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
"""),
        new("Snapshot_HttpClientMode_ShouldEmitHttpClientConstructor", """
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
"""),
        new("Snapshot_TokenManagerMode_ShouldEmitTokenManagerConstructor", """
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
"""),
        new("Snapshot_RetryAttribute_ShouldEmitRetryLogic", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/data")]
        [Retry(3, 1000)]
        Task<string> GetDataAsync();
    }
}
"""),
        new("Snapshot_RetryAllowNonIdempotent_ShouldEmitRetryFlag", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post("/orders")]
        [Retry(3, 1000, AllowNonIdempotent = true)]
        Task<string> CreateOrderAsync();
    }
}
"""),
        new("Snapshot_TimeoutAttribute_ShouldEmitTimeoutLogic", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/data")]
        [Timeout(5000)]
        Task<string> GetDataAsync();
    }
}
"""),
        new("Snapshot_CircuitBreakerAttribute_ShouldEmitCircuitBreakerLogic", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/data")]
        [CircuitBreaker(5, BreakDurationSeconds = 30)]
        Task<string> GetDataAsync();
    }
}
"""),
        new("Snapshot_CacheAttribute_ShouldEmitCacheLogic", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/data")]
        [Cache(60)]
        Task<string> GetDataAsync();
    }
}
"""),
        new("Snapshot_TokenHeaderMode_ShouldEmitTokenInjection", """
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
        [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header, Name = "Authorization", Scheme = "Bearer")]
        Task<string> GetSecureDataAsync();
    }
}
"""),
        new("Snapshot_TokenQueryMode_ShouldEmitTokenAsQueryParameter", """
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
        [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Query, Name = "access_token")]
        Task<string> GetDataAsync();
    }
}
"""),
        new("Snapshot_BasePathAndInterfaceHeaders_ShouldEmitCorrectConfiguration", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    [BasePath("/api/v1")]
    [Header("X-API-Version", "2.0")]
    public interface ITestApi
    {
        [Get("/users")]
        Task<string> GetUsersAsync();
    }
}
"""),
        new("Snapshot_InterfaceProperties_ShouldEmitPropertyImplementation", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Path]
        string TenantId { get; set; }

        [Get("/tenants/{TenantId}/data")]
        Task<string> GetDataAsync();
    }
}
"""),
        new("Snapshot_FilePathDownload_ShouldEmitDownloadLogic", """
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
"""),
        new("Snapshot_FilePathDownload_WithRetry_ShouldEmitOrchestratedDownload", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/files/{fileId}/download")]
        [Retry(3, 100)]
        Task DownloadFileAsync([Path] string fileId, [FilePath] string filePath);
    }
}
"""),
        new("Snapshot_InheritedFromMode_ShouldEmitDerivedClass", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = "ITestTokenManager", IsAbstract = true)]
    public interface IBaseApi
    {
        [Get("/base")]
        Task<string> GetBaseDataAsync();
    }

    [HttpClientApi(TokenManage = "ITestTokenManager", InheritedFrom = "BaseApi")]
    public interface IDerivedApi : IBaseApi
    {
        [Get("/derived")]
        Task<string> GetDerivedDataAsync();
    }
}
"""),
        // ── T-01（评审改版）：缺陷回归样本，入 SnapshotInputSources 后自动获得快照级 + 编译断言双覆盖 ──

        // F-02 回归：HmacSignature 全接口样本。修复前 __httpRequest 先使用后声明（CS0841），
        // 修复后签名调用延迟至请求组装完成后（using 声明之后、Send 之前）。含 [Body] 验证签名覆盖请求体。
        new("Snapshot_HmacSignatureMode_ShouldEmitDeferredSignature", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    public class CreateOrderRequest
    {
        public string Sku { get; set; } = string.Empty;
    }

    [HttpClientApi(TokenManage = "ITestTokenManager")]
    [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.HmacSignature)]
    public interface ITestApi
    {
        [Post("/orders")]
        Task<string> CreateOrderAsync([Body] CreateOrderRequest request);
    }
}
"""),
        // F-01 层A 回归：两接口同名同参方法各标 [Cache]，默认键首段须含接口全名（跨接口隔离）。
        new("Snapshot_CacheMultiInterfaceSameName_ShouldIsolateCacheKeys", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface IFooApi
    {
        [Get("/foo/users/{id}")]
        [Cache(60)]
        Task<string> GetUserAsync([Path] int id);
    }

    [HttpClientApi]
    public interface IBarApi
    {
        [Get("/bar/users/{id}")]
        [Cache(60)]
        Task<string> GetUserAsync([Path] int id);
    }
}
"""),
        // F-04 生成文本侧回归：默认模式 + [Cache] + [Retry]，实现类构造（含必需 cacheProvider/resilienceResolver）
        // 与注册产物工厂 lambda 一并被编译断言覆盖（Registration 文件被 VerifyGenerator 快照跳过，但参与编译）。
        new("Snapshot_DefaultModeWithCacheAndRetry_ShouldCompileRegistration", """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get("/data")]
        [Cache(60)]
        [Retry(3, 1000)]
        Task<string> GetDataAsync();
    }
}
"""),
    };
}