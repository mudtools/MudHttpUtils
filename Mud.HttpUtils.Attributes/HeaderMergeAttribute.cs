// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 头部合并模式
/// </summary>
public enum HeaderMergeMode
{
    /// <summary>
    /// 追加模式：接口级和方法级的同名头部都会被添加（默认）
    /// </summary>
    Append,

    /// <summary>
    /// 替换模式：方法级头部替换接口级同名头部
    /// </summary>
    Replace,

    /// <summary>
    /// 忽略模式：方法级头部忽略，只使用接口级头部
    /// </summary>
    Ignore
}

/// <summary>
/// 标记接口或方法，指定 HTTP 头部的合并规则。
/// </summary>
/// <remarks>
/// <para>
/// 当接口级和方法级同时定义了同名 HTTP 头部时，使用此特性控制合并行为。
/// 默认为追加模式（两个头部都会被添加）。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpClientApi]
/// [Header("Accept", "application/json")]
/// [HeaderMerge(HeaderMergeMode.Replace)]
/// public interface IUserApi
/// {
///     [Get("/api/users")]
///     [Header("Accept", "text/plain")]
///     Task&lt;string&gt; GetUsersAsTextAsync();
///     // 方法级 Accept: text/plain 替换接口级 Accept: application/json
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class HeaderMergeAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="HeaderMergeAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="mode">头部合并模式。</param>
    public HeaderMergeAttribute(HeaderMergeMode mode)
    {
        Mode = mode;
    }

    /// <summary>
    /// 获取头部合并模式。
    /// </summary>
    public HeaderMergeMode Mode { get; }
}
