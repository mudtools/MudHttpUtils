// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Models.Analysis;

/// <summary>
/// 接口级查询参数信息
/// </summary>
internal class InterfaceQueryParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
/// 接口级路径参数信息
/// </summary>
internal class InterfacePathParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
/// 接口级动态属性信息（标记 [Query]、[Path] 或 [Header] 的接口属性）
/// </summary>
internal class InterfacePropertyInfo
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string AttributeType { get; set; } = string.Empty;

    public string? ParameterName { get; set; }

    public string? Format { get; set; }

    public bool UrlEncode { get; set; } = true;

    public string? DefaultValue { get; set; }

    /// <summary>
    /// GEN-05 修复：指示接口属性是否为只读（仅有 getter）。
    /// 为 true 时生成的实现属性不生成 setter，保持接口契约一致。
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// 是否替换已有的同名请求头。仅对 AttributeType == "Header" 的属性有效。
    /// </summary>
    public bool Replace { get; set; }

    /// <summary>
    /// 请求头的别名，用于映射到不同的请求头名称。仅对 AttributeType == "Header" 的属性有效。
    /// </summary>
    public string? AliasAs { get; set; }
}
