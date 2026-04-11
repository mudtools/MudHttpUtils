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
     *   - DTO*: DTO生成器 (DTO001-003)
     *   - BO*: BO生成器 (BO001-002)
     *   - VO*: VO生成器 (VO001-002)
     *   - EM*: 实体映射方法生成器 (EM001-002)
     *   - BE*: 实体建造者模式生成器 (BE001)
     *   - QI*: 查询输入类生成器 (QI001-002)
     *   - AR*: AutoRegister生成器 (AR001-006)
     *   - HTTPCLIENT*: HttpClient API生成器 (HTTPCLIENT001-005)
     *   - HTTPCLIENTREG*: HttpClient注册生成器 (HTTPCLIENTREG001-002)
     *   - MCG*: 通用代码生成器 (MCG001-002)
     *   - EHSG*: 事件处理器生成器 (EHSG001)
     *   - COMWRAP*: COM包装生成器 (COMWRAP001)
     *   - SG*: 源代码生成器通用 (SG001)
     *   - EG*: 实体生成器通用 (EG001-002)
     */
    #endregion

    #region DTO生成器诊断信息 (DTO001-003)
    public static readonly DiagnosticDescriptor DtoGenerationError = new(
        id: "DTO001",
        title: "DTO代码生成错误",
        messageFormat: "生成类 {0} 时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DtoInitializationError = new(
        id: "DTO002",
        title: "DTO代码生成初始化错误",
        messageFormat: "初始化DTO代码生成器时发生错误: {0}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DtoGenerationFailure = new(
        id: "DTO003",
        title: "DTO类生成失败",
        messageFormat: "无法为类 {0} 生成DTO类",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
    #endregion

    #region BO生成器诊断信息 (BO001-002)
    public static readonly DiagnosticDescriptor BoGenerationFailure = new(
        id: "BO001",
        title: "BO类生成失败",
        messageFormat: "无法为类 {0} 生成BO类",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BoGenerationError = new(
        id: "BO002",
        title: "BO类生成错误",
        messageFormat: "生成类 {0} 的BO类时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region VO生成器诊断信息 (VO001-002)
    public static readonly DiagnosticDescriptor VoGenerationFailure = new(
        id: "VO001",
        title: "VO类生成失败",
        messageFormat: "无法为类 {0} 生成VO类",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VoGenerationError = new(
        id: "VO002",
        title: "VO类生成错误",
        messageFormat: "生成类 {0} 的VO类时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region 实体映射方法生成器诊断信息 (EM001-002)
    public static readonly DiagnosticDescriptor EntityMethodGenerationFailure = new(
        id: "EM001",
        title: "实体映射方法生成失败",
        messageFormat: "无法为类 {0} 生成实体映射方法",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntityMethodGenerationError = new(
        id: "EM002",
        title: "实体映射方法生成错误",
        messageFormat: "生成类 {0} 的实体映射方法时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region 实体建造者模式代码生成器诊断信息 (BE001)
    public static readonly DiagnosticDescriptor EntityBuilderGenerationError = new(
        id: "BE001",
        title: "实体建造者模式代码生成错误",
        messageFormat: "生成类 {0} 的建造者模式代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region 查询输入类生成器诊断信息 (QI001-002)
    public static readonly DiagnosticDescriptor QueryInputGenerationFailure = new(
        id: "QI001",
        title: "查询输入类生成失败",
        messageFormat: "无法为类 {0} 生成查询输入类",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryInputGenerationError = new(
        id: "QI002",
        title: "查询输入类生成错误",
        messageFormat: "生成类 {0} 的查询输入类时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region AutoRegister代码生成器诊断信息 (AR001-006)
    public static readonly DiagnosticDescriptor AutoRegisterGenerationError = new(
        id: "AR001",
        title: "AutoRegister代码生成错误",
        messageFormat: "生成类 {0} 的AutoRegister代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AutoRegisterMetadataExtractionFailed = new(
        id: "AR002",
        title: "AutoRegister元数据提取失败",
        messageFormat: "无法为类 {0} 提取AutoRegister元数据。找到的特性: {1}。特性详情: {2}",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AutoRegisterGenerationSkipped = new(
        id: "AR003",
        title: "AutoRegister代码生成跳过",
        messageFormat: "在程序集 {0} 中未找到AutoRegister服务。已处理的类: {1}。正在检查类是否有AutoRegister特性...",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AutoRegisterMetadataExtracted = new(
        id: "AR004",
        title: "AutoRegister元数据已提取",
        messageFormat: "已提取 {0} 个AutoRegister服务: {1}",
        category: "代码生成",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AutoRegisterMetadataDetails = new(
        id: "AR005",
        title: "AutoRegister元数据详情",
        messageFormat: "为类 {0} 提取的元数据: ImplType={1}, BaseType={2}, LifeTime={3}",
        category: "代码生成",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AutoRegisterAttributesFound = new(
        id: "AR006",
        title: "AutoRegister特性已找到",
        messageFormat: "类 {0} 有AutoRegister特性: {1}。完整特性详情: {2}",
        category: "代码生成",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);
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

    #region 通用代码生成器诊断信息 (MCG001-002)
    public static readonly DiagnosticDescriptor GeneratorError = new(
        id: "MCG001",
        title: "代码生成器错误",
        messageFormat: "代码生成失败: {0}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GeneratorWarning = new(
        id: "MCG002",
        title: "代码生成器警告",
        messageFormat: "代码生成警告: {0}",
        category: "代码生成",
        DiagnosticSeverity.Warning,
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

    #region COM包装生成器诊断信息 (COMWRAP001)
    public static readonly DiagnosticDescriptor ComWrapGenerationError = new(
        id: "COMWRAP001",
        title: "COM包装生成器错误",
        messageFormat: "为类 {0} 生成COM包装代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region 源代码生成器通用诊断信息 (SG001)
    public static readonly DiagnosticDescriptor SourceGeneratorError = new(
        id: "SG001",
        title: "源代码生成器错误",
        messageFormat: "代码生成失败: {0}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region 实体生成器通用诊断信息 (EG001-002)
    public static readonly DiagnosticDescriptor EntityGenerationFailure = new(
        id: "EG001",
        title: "实体类生成失败",
        messageFormat: "无法为类 {0} 生成实体类",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntityGenerationError = new(
        id: "EG002",
        title: "实体类生成错误",
        messageFormat: "生成类 {0} 的实体类时发生错误: {1}",
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
