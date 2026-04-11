// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Models.Analysis;

/// <summary>
/// 参数信息
/// </summary>
/// <remarks>
/// 存储方法参数的详细信息，包括参数名、类型、特性和默认值。
/// </remarks>
internal class ParameterInfo
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数类型显示字符串
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 参数特性列表
    /// </summary>
    public IReadOnlyList<ParameterAttributeInfo> Attributes { get; set; } = [];

    /// <summary>
    /// Token 类型（如果是 Token 参数）
    /// </summary>
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// 是否具有默认值
    /// </summary>
    public bool HasDefaultValue { get; set; }

    /// <summary>
    /// 默认值
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// 默认值的字面量表示
    /// </summary>
    public string? DefaultValueLiteral { get; set; }
}
