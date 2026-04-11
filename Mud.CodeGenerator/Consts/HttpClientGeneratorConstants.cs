// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;


/// <summary>
/// 生成器常量配置
/// </summary>
internal static class HttpClientGeneratorConstants
{
    // 特性名称
    public static readonly string[] HttpClientApiWrapAttributeNames = ["HttpClientApiWrapAttribute", "HttpClientApiWrap"];
    public static readonly string[] TokenAttributeNames = ["TokenAttribute", "Token"];

    /// <summary>
    /// 忽略生成实现的特性名称数组
    /// </summary>
    public static readonly string[] IgnoreImplementAttributeNames = ["IgnoreImplementAttribute", "IgnoreImplement"];

    /// <summary>
    /// 忽略生成包装接口的特性名称数组
    /// </summary>
    public static readonly string[] IgnoreWrapInterfaceAttributeNames = ["IgnoreWrapInterfaceAttribute", "IgnoreWrapInterface"];

    /// <summary>
    /// HttpClientApi特性名称数组
    /// </summary>
    public static string[] HttpClientApiAttributeNames = ["HttpClientApiAttribute", "HttpClientApi"];

    /// <summary>
    /// 支持的HTTP方法名称数组
    /// </summary>
    public static readonly string[] SupportedHttpMethods = ["Get", "GetAttribute", "Post", "PostAttribute", "Put", "PutAttribute", "Delete", "DeleteAttribute", "Patch", "PatchAttribute", "Head", "HeadAttribute", "Options", "OptionsAttribute"];

    public static readonly HashSet<string> PathAttributes = new HashSet<string>(StringComparer.Ordinal) { "PathAttribute", "Path", "RouteAttribute", "Route" };
    public const string QueryAttribute = "QueryAttribute";
    public const string ArrayQueryAttribute = "ArrayQueryAttribute";
    public const string HeaderAttribute = "HeaderAttribute";
    public const string BodyAttribute = "BodyAttribute";
    public const string FormContentAttribute = "FormContentAttribute";
    public const string FilePathAttribute = "FilePathAttribute";

    // Token注入模式
    public const string TokenInjectionModeHeader = "Header";
    public const string TokenInjectionModeQuery = "Query";
    public const string TokenInjectionModePath = "Path";

    // Body加密相关命名参数
    public const string BodyEnableEncryptProperty = "EnableEncrypt";
    public const string BodyEncryptSerializeTypeProperty = "EncryptSerializeType";
    public const string BodyEncryptPropertyNameProperty = "EncryptPropertyName";

    // Token相关命名参数
    public const string TokenInjectionModeProperty = "InjectionMode";
    public const string TokenNameProperty = "Name";
    public const string TokenReplaceProperty = "Replace";

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
    public const string BaseAddressProperty = "BaseAddress";

    // 默认值
    public const string DefaultTokenManageInterface = "ITokenManage";
    public const string DefaultWrapSuffix = "Wrap";
    public const string DefaultContentType = "application/json";
    public const string ImplementationNamespaceSuffix = "Internal";

    // HTTP方法常量(分离特性名和方法名)
    public static readonly string[] HttpMethodAttributeNames =
        ["GetAttribute", "PostAttribute", "PutAttribute", "DeleteAttribute", "PatchAttribute", "HeadAttribute", "OptionsAttribute"];
    public static readonly string[] HttpMethodNames = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
}
