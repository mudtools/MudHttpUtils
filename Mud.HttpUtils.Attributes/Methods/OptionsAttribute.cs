// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记方法为 HTTP OPTIONS 请求。
/// </summary>
/// <remarks>
/// <para>
/// 用于定义 OPTIONS 请求的 API 方法。OPTIONS 请求通常用于获取目标资源的通信选项或服务器支持的方法。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Options("/api/users")]
/// Task&lt;string[]&gt; GetAllowedMethodsAsync();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OptionsAttribute : HttpMethodAttribute
{
    /// <summary>
    /// 初始化 <see cref="OptionsAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="requestUri">请求 URI。</param>
    public OptionsAttribute(string? requestUri = null)
        : base(HttpMethod.Options, requestUri)
    {
    }
}
