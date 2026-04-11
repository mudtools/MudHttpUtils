// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Models.Analysis;

/// <summary>
/// 参数特性信息
/// </summary>
/// <remarks>
/// 存储参数特性的详细信息，包括特性名称、构造函数参数和命名参数。
/// </remarks>
internal class ParameterAttributeInfo
{
    /// <summary>
    /// 特性名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 构造函数参数数组
    /// </summary>
    public object?[] Arguments { get; set; } = [];

    /// <summary>
    /// 命名参数字典
    /// </summary>
    public IReadOnlyDictionary<string, object?> NamedArguments { get; set; } = new Dictionary<string, object?>();
}
