// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为表单字段（Form Field）。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，指示该参数应作为表单字段发送。通常用于 application/x-www-form-urlencoded 请求。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Post("/api/login")]
/// Task&lt;LoginResult&gt; LoginAsync(
///     [Form("username")] string user, 
///     [Form("password")] string pass);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class FormAttribute : Attribute
{
    /// <summary>
    /// 获取或设置表单字段的名称。如果未设置，将使用参数名。
    /// </summary>
    public string? FieldName { get; set; }
}
