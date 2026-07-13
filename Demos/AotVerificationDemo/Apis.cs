using AotVerificationDemo;
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
/// 对比：[QueryMap] 使用 FlattenObjectToQueryParams() 展平 POCO 对象，
/// 该方法依赖运行时反射（GetProperties/GetValue），AOT 裁剪后属性元数据丢失，
/// 已标注 [RequiresUnreferencedCode]，不适用于 Native AOT。
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
}

// ─────────────────────────────────────────────────────────────
// 非 AOT 安全路径 — 仅文档说明，不在 AOT 构建中使用
// ─────────────────────────────────────────────────────────────

/*
 * ⚠️ QueryMap 路径在 Native AOT 下不安全：
 *
 * [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
 * public interface IComplexSearchApi
 * {
 *     // QueryMap 使用 FlattenObjectToQueryParams() 展平 POCO 对象，
 *     // 该方法依赖运行时反射 (GetProperties/GetValue)，
 *     // AOT 裁剪后属性元数据丢失，查询参数会静默为空。
 *     // 已标注 [RequiresUnreferencedCode]，AOT 分析器会产生 IL2026 警告。
 *     //
 *     // AOT 替代方案（已在上方 ISearchApi 中验证）：
 *     // 使用 [Query] 逐个参数声明，源生成器在编译期发射内联代码。
 *     [Get("/api/search")]
 *     Task<List<UserDto>?> SearchAsync([QueryMap] SearchCriteria criteria);
 * }
 *
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
 * // 已标注 [RequiresUnreferencedCode]/[RequiresDynamicCode]。
 * // AOT 替代方案：使用编译期安全的字典式 ISensitiveDataMasker 实现（见 AotSafeMasker.cs）。
 */
