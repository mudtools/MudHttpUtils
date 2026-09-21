// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <summary>
/// 生成器常量配置
/// </summary>
internal static class HttpClientGeneratorConstants
{
    public static readonly string[] TokenAttributeNames = ["TokenAttribute", "Token"];

    public static readonly string[] IgnoreGeneratorAttributeNames = ["IgnoreGeneratorAttribute", "IgnoreGenerator"];

    /// <summary>
    /// HttpClientApi特性名称数组
    /// </summary>
    public static string[] HttpClientApiAttributeNames = ["HttpClientApiAttribute", "HttpClientApi"];

    /// <summary>
    /// 支持的HTTP方法名称数组
    /// </summary>
    public static readonly string[] SupportedHttpMethods = ["Get", "GetAttribute", "Post", "PostAttribute", "Put", "PutAttribute", "Delete", "DeleteAttribute", "Patch", "PatchAttribute", "Head", "HeadAttribute", "Options", "OptionsAttribute"];

    /// <summary>
    /// [Phase5 优化 3.3] 支持的HTTP方法名称 HashSet，用于 O(1) 查找。
    /// </summary>
    public static readonly HashSet<string> SupportedHttpMethodsSet = new(SupportedHttpMethods, StringComparer.Ordinal);

    /// <summary>
    /// 路径参数特性名（长名 + 短名）。
    /// </summary>
    /// <remarks>
    /// G8-12：原集合含 <c>"RouteAttribute"</c>/<c>"Route"</c> 两个**幻影条目** ——
    /// <c>Mud.HttpUtils.Attributes</c> 中不存在 <c>RouteAttribute</c> 类型（全仓无 <c>class RouteAttribute</c>），
    /// 「早期文档的别名」从来不可能命中，属纯负债。已删除（`Generator/README.md` 已自认不存在 <c>[Route]</c>）。
    /// </remarks>
    public static readonly HashSet<string> PathAttributes = new HashSet<string>(StringComparer.Ordinal) { "PathAttribute", "Path" };
    public const string QueryAttribute = "QueryAttribute";
    public const string ArrayQueryAttribute = "ArrayQueryAttribute";
    public const string HeaderAttribute = "HeaderAttribute";
    public const string HeaderCollectionAttribute = "HeaderCollectionAttribute";
    public const string BodyAttribute = "BodyAttribute";
    public const string FormContentAttribute = "FormContentAttribute";
    public const string FilePathAttribute = "FilePathAttribute";
    public const string UploadAttribute = "UploadAttribute";
    public const string MultipartFormAttribute = "MultipartFormAttribute";
    public const string FormAttribute = "FormAttribute";
    public const string QueryMapAttribute = "QueryMapAttribute";
    public const string RawQueryStringAttribute = "RawQueryStringAttribute";

    // Token注入模式
    public const string TokenInjectionModeHeader = "Header";
    public const string TokenInjectionModeQuery = "Query";
    public const string TokenInjectionModePath = "Path";
    public const string TokenInjectionModeApiKey = "ApiKey";
    public const string TokenInjectionModeHmacSignature = "HmacSignature";
    public const string TokenInjectionModeBasicAuth = "BasicAuth";
    public const string TokenInjectionModeCookie = "Cookie";

    // Body加密相关命名参数
    public const string BodyEnableEncryptProperty = "EnableEncrypt";
    public const string BodyEncryptSerializeTypeProperty = "EncryptSerializeType";
    public const string BodyEncryptPropertyNameProperty = "EncryptPropertyName";

    // Token相关命名参数
    public const string TokenInjectionModeProperty = "InjectionMode";
    public const string TokenNameProperty = "Name";

    // HttpMethod响应相关命名参数
    public const string HttpMethodContentTypeProperty = "ContentType";
    public const string HttpMethodResponseContentTypeProperty = "ResponseContentType";
    public const string HttpMethodResponseEnableDecryptProperty = "ResponseEnableDecrypt";

    public const string TimeoutProperty = "Timeout";
    public const string RegistryGroupNameProperty = "RegistryGroupName";
    public const string TokenManageProperty = "TokenManage";
    public const string HttpClientProperty = "HttpClient";
    public const string IsAbstractProperty = "IsAbstract";
    public const string InheritedFromProperty = "InheritedFrom";
    // CFG-22：BaseAddressProperty 为死常量（全仓仅定义、无读取），已删除。
    // [HttpClientApi(BaseAddress = …)] 使用处会直接产生编译错误 CS0619（属性标注 [Obsolete(error: true)]）。

    // G8-12：原 BasePathAttributeNames 常量已删除（全仓零引用；真实判定为硬编码，
    // 见 InterfaceImplementationGenerator.ExtractBasePath：「BasePathAttribute」/「BasePath」）。

    public static readonly string[] AllowAnyStatusCodeAttributeNames = ["AllowAnyStatusCodeAttribute", "AllowAnyStatusCode"];

    public static readonly string[] CacheAttributeNames = ["CacheAttribute", "Cache"];

    public static readonly string[] InterfaceQueryAttributeNames = ["InterfaceQueryAttribute", "InterfaceQuery"];
    public static readonly string[] InterfacePathAttributeNames = ["InterfacePathAttribute", "InterfacePath"];
    public static readonly string[] HeaderMergeAttributeNames = ["HeaderMergeAttribute", "HeaderMerge"];
    public static readonly string[] SerializationMethodAttributeNames = ["SerializationMethodAttribute", "SerializationMethod"];

    /// <summary>CFG-18：接口级「允许未匹配路由占位符」标记特性名。</summary>
    public static readonly string[] AllowUnmatchedRouteParametersAttributeNames = ["AllowUnmatchedRouteParametersAttribute", "AllowUnmatchedRouteParameters"];

    public static readonly string[] RetryAttributeNames = ["RetryAttribute", "Retry"];
    public static readonly string[] CircuitBreakerAttributeNames = ["CircuitBreakerAttribute", "CircuitBreaker"];
    public static readonly string[] TimeoutAttributeNames = ["TimeoutAttribute", "Timeout"];

    /// <summary>
    /// 所有已知的「HTTP 参数特性」名称集合（长名 + 短名）。
    /// 参数若未标注其中任何一个，且不属于特殊类型，则按类型自动推断默认特性：简单类型 → <c>[Query]</c>、复杂类型 → <c>[Body]</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>G8-12 单一事实源</b>：原集合私有于 <c>ParameterAnalyzer</c>，与 <see cref="PathAttributes"/>、
    /// <see cref="QueryAttribute"/> 等常量形成**第三份副本**（三处独立维护同一批特性名 ⇒ 改名/新增时静默失配）。
    /// 现收敛到本类，由 <c>ConstantUsageGuardTests</c> 与 <c>ParameterAttributeNamesTests</c> 双向钉死成员集合。
    /// </para>
    /// <para>
    /// <b>刻意不含</b>非参数级特性（<c>[Cache]</c>/<c>[Retry]</c>/<c>[CircuitBreaker]</c>/<c>[Timeout]</c>/
    /// <c>[HeaderMerge]</c>/<c>[SerializationMethod]</c>/<c>[AllowUnmatchedRouteParameters]</c>）——
    /// 它们不是「参数的 HTTP 语义标注」，纳入会改变自动推断行为（如把 <c>[Cache]</c> 参数当作已标注而漏推断）。
    /// </para>
    /// </remarks>
    public static readonly HashSet<string> HttpParameterAttributeNames = new(StringComparer.Ordinal)
    {
        // Path（与 PathAttributes 保持同源；G8-12 已移除 Route* 幻影条目）
        "PathAttribute", "Path",
        // Query 系列
        "QueryAttribute", "Query",
        "ArrayQueryAttribute", "ArrayQuery",
        "QueryMapAttribute", "QueryMap",
        "RawQueryStringAttribute", "RawQueryString",
        // Header
        "HeaderAttribute", "Header",
        // Body 系列
        "BodyAttribute", "Body",
        "FormContentAttribute", "FormContent",
        "MultipartFormAttribute", "MultipartForm",
        "UploadAttribute", "Upload",
        "FormAttribute", "Form",
        "FilePathAttribute", "FilePath",
        // Token
        "TokenAttribute", "Token"
    };

    // Resilience相关命名参数
    public const string RetryMaxRetriesProperty = "MaxRetries";
    public const string RetryDelayMillisecondsProperty = "DelayMilliseconds";
    public const string RetryUseExponentialBackoffProperty = "UseExponentialBackoff";
    public const string CircuitBreakerFailureThresholdProperty = "FailureThreshold";
    public const string CircuitBreakerBreakDurationSecondsProperty = "BreakDurationSeconds";
    public const string CircuitBreakerSamplingDurationSecondsProperty = "SamplingDurationSeconds";
    public const string CircuitBreakerMinimumThroughputProperty = "MinimumThroughput";
    public const string TimeoutMillisecondsProperty = "TimeoutMilliseconds";

    // Cache相关命名参数
    public const string CacheDurationSecondsProperty = "DurationSeconds";
    public const string CacheKeyTemplateProperty = "CacheKeyTemplate";
    public const string CacheVaryByUserProperty = "VaryByUser";
    public const string CacheUseSlidingExpirationProperty = "UseSlidingExpiration";

    // 默认值
    /// <summary>
    /// 未显式配置 <c>[HttpClientApi(Timeout=…)]</c> 时的默认超时秒数。
    /// 与 <c>Mud.HttpUtils.Attributes.HttpClientApiAttribute.DefaultTimeoutSeconds</c> 保持一致（CFG-03），
    /// 由测试 <c>HttpClientApiAttributeDefaultTimeoutTests</c> 守护一致性。
    /// </summary>
    public const int DefaultHttpClientTimeoutSeconds = 50;

    // G8-12：以下 3 个死常量已删除（全仓词边界检索确认「仅定义处命中」，属纯负债）：
    //   · BasePathAttributeNames —— 真实判定为硬编码（InterfaceImplementationGenerator.ExtractBasePath）
    //   · DefaultTokenManageInterface = "ITokenManage" —— 无消费且**值错误**（真实约定见 BaseClassValidator：「ITokenManager」）
    //   · DefaultWrapSuffix = "Wrap" —— 真实判定为硬编码（BaseClassValidator.IsGeneratedClass）
    public const string DefaultContentType = "application/json";
    public const string ImplementationNamespaceSuffix = "Internal";

    /// <summary>
    /// G8-06：文件下载默认缓冲区大小（字节），与 <c>DefaultHttpRequestExecutor.DownloadLargeAsync</c> 的默认值一致。
    /// </summary>
    public const int DefaultDownloadBufferSize = 81920;

    /// <summary>
    /// G8-06：<c>[FilePath(BufferSize = …)]</c> 的支持上界（4 MiB）。
    /// </summary>
    /// <remarks>
    /// BufferSize 最终用于 <c>new byte[bufferSize]</c> 与
    /// <c>new FileStream(…, bufferSize, …)</c>；特性 setter 不会被 Roslyn 实例化，
    /// 故超大值（如 <c>int.MaxValue</c>）只能在生成器侧夹取（HTTPCLIENT036）+ 运行库侧二次夹取。
    /// 4 MiB 已远超任何合理的文件下载缓冲（默认 80 KiB），并且小于大对象堆压力阈值。
    /// </remarks>
    public const int MaxSupportedDownloadBufferSize = 4 * 1024 * 1024;
}
