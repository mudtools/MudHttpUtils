// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// 统一诊断描述符集合，用于集中管理所有代码生成过程中的诊断信息
/// <para>错误码命名规范：前缀(3-6字符) + 三位数字 (如 DTO001, HTTPCLIENT001)</para>
/// <para>所有生成器必须使用此统一类中的诊断描述符，确保错误码唯一且格式一致</para>
/// </summary>
internal static class Diagnostics
{
    #region 错误码前缀规范说明
    /*
     * 错误码命名规范：
     * - 前缀使用大写字母，长度3-6字符
     * - 后缀使用3位数字 (001-999)
     * - 前缀分配：
     *   - HTTPCLIENT*: HttpClient API生成器 (HTTPCLIENT001-005)
     *   - HTTPCLIENTREG*: HttpClient注册生成器 (HTTPCLIENTREG001-002)
     *   - MCG*: 通用代码生成器 (MCG001-002)
     *   - EHSG*: 事件处理器生成器 (EHSG001)
     *   - COMWRAP*: COM包装生成器 (COMWRAP001)
     *   - SG*: 源代码生成器通用 (SG001)
     *   - EG*: 实体生成器通用 (EG001-002)
     */
    #endregion 

    #region HttpClient API生成器诊断信息 (HTTPCLIENT001-005)
    public static readonly DiagnosticDescriptor HttpClientApiGenerationError = new(
        id: "HTTPCLIENT001",
        title: "HttpClient API生成错误",
        messageFormat: "生成接口 {0} 的实现时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientApiSyntaxError = new(
        id: "HTTPCLIENT003",
        title: "HttpClient API语法错误",
        messageFormat: "接口 {0} 的语法分析失败: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientApiParameterError = new(
        id: "HTTPCLIENT004",
        title: "HttpClient API参数错误",
        messageFormat: "接口 {0} 的参数配置错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientInvalidUrlTemplate = new(
        id: "HTTPCLIENT005",
        title: "Invalid URL Template",
        messageFormat: "接口 {0} 的URL模板 '{1}' 格式无效: {2}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientAotCompatibilityWarning = new(
        id: "HTTPCLIENT006",
        title: "AOT兼容性警告",
        messageFormat: "接口 {0} 的方法 {1} 中复杂查询参数类型 '{2}' 未实现 IQueryParameter 接口，在 AOT/Trim 模式下可能失败。建议实现 IQueryParameter 接口或使用简单参数替代。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
    #endregion

    #region HttpClient注册生成器诊断信息 (HTTPCLIENTREG001-002)
    public static readonly DiagnosticDescriptor HttpClientRegistrationGenerationError = new(
        id: "HTTPCLIENTREG001",
        title: "HttpClient API注册生成错误",
        messageFormat: "为接口 {0} 生成HttpClient API注册时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientInvalidRegistryGroupName = new(
        id: "HTTPCLIENTREG002",
        title: "Invalid RegistryGroupName",
        messageFormat: "RegistryGroupName '{0}' 不是有效的C#标识符。RegistryGroupName必须以字母或下划线开头，只能包含字母、数字和下划线。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region 事件处理器生成器诊断信息 (EHSG001)
    public static readonly DiagnosticDescriptor EventHandlerGenerationError = new(
        id: "EHSG001",
        title: "事件处理器生成器错误",
        messageFormat: "为类 {0} 生成事件处理器代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion  

    #region FormContent生成器诊断信息 (FORM001-003)
    public static readonly DiagnosticDescriptor FormContentGenerationError = new(
        id: "FORM001",
        title: "FormContent代码生成错误",
        messageFormat: "为类 {0} 生成FormContent代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FormContentNoFilePathAttribute = new(
        id: "FORM002",
        title: "FormContent缺少FilePath属性",
        messageFormat: "类 {0} 标记了 [FormContent] 特性，但没有找到任何标记了 [FilePath] 特性的属性。必须且只能有一个属性标记 [FilePath] 特性。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FormContentMultipleFilePathAttributes = new(
        id: "FORM003",
        title: "FormContent存在多个FilePath属性",
        messageFormat: "类 {0} 标记了 [FormContent] 特性，但发现了多个标记了 [FilePath] 特性的属性: {1}。必须且只能有一个属性标记 [FilePath] 特性。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion
}
