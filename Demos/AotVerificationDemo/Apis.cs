using AotVerificationDemo;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotVerificationDemo;

// ─────────────────────────────────────────────────────────────
// 生成 API 客户端 — JSON 路径
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 用户 API（JSON 请求/响应）
/// 使用 HttpClient 模式，依赖 IEnhancedHttpClient
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/api/users/{id}")]
    Task<UserDto?> GetUserAsync([Path] int id);

    [Post("/api/users")]
    Task<UserDto?> CreateUserAsync([Body] CreateUserRequest request);

    [Get("/api/users")]
    Task<List<UserDto>?> ListUsersAsync([Query] string? keyword = null, [Query] int page = 1);
}

// ─────────────────────────────────────────────────────────────
// 生成 API 客户端 — FormUrlEncoded 路径
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 认证 API（FormUrlEncoded Body）
/// 使用 [SerializationMethod(FormUrlEncoded)] 使 [Body] 参数以表单方式序列化
/// 源生成器在编译期枚举 LoginForm 属性，发射静态属性访问代码（AOT 安全）
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IAuthApi
{
    [Post("/api/auth/login")]
    [SerializationMethod(SerializationMethod.FormUrlEncoded)]
    Task<LoginResult?> LoginAsync([Body] LoginForm form);
}

// ─────────────────────────────────────────────────────────────
// 生成 API 客户端 — 查询参数路径（AOT 安全的 [Query] 方式）
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 搜索 API（[Query] 逐参数声明，AOT 安全）
/// </summary>
/// <remarks>
/// 验证查询参数路径的 AOT 安全性：
/// <para>
/// [Query] 参数逐个声明，源生成器在编译期为每个参数发射内联查询构建代码。
/// 简单类型（string/int/bool 等）使用 ToString() 序列化，不涉及反射。
/// </para>
/// <para>
/// 对比：对象型 [QueryMap] 的一级属性已由源生成器在编译期发射内联展平代码（AOT 安全）。
/// 仅嵌套复杂属性仍回退到 FlattenObjectToQueryParams() 反射路径（已知限制）。
/// 简单 [Query] 参数（string/int/bool 等）使用 ToString() 序列化，不涉及反射。
/// </para>
/// </remarks>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface ISearchApi
{
    [Get("/api/search")]
    Task<List<UserDto>?> SearchAsync(
        [Query] string? keyword = null,
        [Query] int? minAge = null,
        [Query] int? maxAge = null,
        [Query] bool? activeOnly = null);

    [Get("/api/search/advanced")]
    Task<List<UserDto>?> AdvancedSearchAsync([Query] SearchCriteria? criteria = null);
}

// ─────────────────────────────────────────────────────────────
// 生成 API 客户端 — Response<T> 包装路径（AOT 安全反序列化验证）
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 响应包装 API（返回 <see cref="Response{T}"/>）。
/// 源生成器对 <c>Response&lt;UserDto&gt;</c> 返回类型发射
/// <c>ExecuteAsResponseAsync&lt;UserDto&gt;</c> 调用，
/// 响应体经 DI 注入的 JsonSerializerOptions（含 AppJsonContext resolver）
/// 反序列化为 UserDto，再包装为 Response&lt;UserDto&gt; 返回（不抛异常）。
/// 此路径在 Native AOT 下由源生成 resolver 保证类型元数据可用。
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IResponseApi
{
    [Get("/api/users/{id}")]
    [AllowAnyStatusCode]
    Task<Response<UserDto>> GetUserResponseAsync([Path] int id);
}

// ─────────────────────────────────────────────────────────────
// [QueryMap] 特性维度 Demo 场景（AOT 安全，一级属性已由生成器内联展平）
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 复杂搜索 API（[QueryMap] 对象展平路径）
/// </summary>
/// <remarks>
/// 验证 [QueryMap] 特性的 AOT 安全性：
/// <para>
/// 对象型 [QueryMap] 参数的一级属性已由源生成器在编译期发射内联展平代码
/// （TryGenerateInlineQueryFlattening），不涉及运行时反射。
/// 仅嵌套复杂属性（如 SearchCriteria.Nested）仍回退到 FlattenObjectToQueryParams()
/// 反射路径（已知限制，需权衡递归代码膨胀与 AOT 收益）。
/// </para>
/// </remarks>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IComplexSearchApi
{
    [Get("/api/search")]
    Task<List<UserDto>?> SearchAsync([QueryMap] SearchCriteria? criteria = null);
}

// ─────────────────────────────────────────────────────────────
// 生成 API 客户端 — 默认模式（无 DI 入口 + ModuleInitializer 自动注册验证）
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 默认模式 API（无 HttpClient/TokenManager 包装类型）。
/// 源生成器为此接口生成 ModuleInitializer 工厂注册代码，
/// 可通过 <c>RestService.ForGenerated&lt;INonDiApi&gt;(httpClient, options)</c> 无 DI 调用。
/// </summary>
/// <remarks>
/// v3.3 Phase 0 T0.4：验证无 DI AOT 入口与 ModuleInitializer 自动注册。
/// </remarks>
[HttpClientApi]
public interface INonDiApi
{
    [Get("/api/users/{id}")]
    Task<UserDto?> GetUserAsync([Path] int id);
}

// ─────────────────────────────────────────────────────────────
// 以下路径在 Native AOT 下不安全 — 仅文档说明，不在 AOT 构建中使用
// ─────────────────────────────────────────────────────────────

/*
 * ⚠️ XML 响应路径在 Native AOT 下不安全：
 *
 * [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
 * [SerializationMethod(SerializationMethod.Xml)]
 * public interface IXmlApi
 * {
 *     // XmlSerializer 运行时生成动态程序集，Native AOT 不支持。
 *     // 已标注 [RequiresDynamicCode]，AOT 分析器会产生 IL3050 警告。
 *     // AOT 替代方案：使用 JSON 序列化（JsonSerializerContext 已源生成）。
 *     [Get("/api/xml/data")]
 *     Task<MyXmlData?> GetDataAsync();
 * }
 *
 * ⚠️ DefaultSensitiveDataMasker 在 Native AOT 下不安全：
 *
 * // DefaultSensitiveDataMasker 使用反射遍历对象属性读取 [SensitiveData] 特性，
 * // AOT 裁剪后属性/特性元数据丢失，敏感字段可能漏脱敏。
 * // 已标注 [Obsolete]/[RequiresUnreferencedCode]/[RequiresDynamicCode]。
 * // AOT 替代方案：使用 AotSafeSensitiveDataMasker（已标 virtual 可子类化，见 DemoSensitiveDataMasker.cs）。
 */
