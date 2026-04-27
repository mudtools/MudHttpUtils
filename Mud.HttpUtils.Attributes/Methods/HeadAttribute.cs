// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记方法为 HTTP HEAD 请求。
/// </summary>
/// <remarks>
/// <para>
/// 用于定义 HEAD 请求的 API 方法。HEAD 请求与 GET 类似，但服务器只返回响应头，不返回响应体。
/// 通常用于检查资源是否存在或获取元数据。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Head("/api/users/{id}")]
/// Task CheckUserExistsAsync(int id);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HeadAttribute : HttpMethodAttribute
{
    /// <summary>
    /// 初始化 <see cref="HeadAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="requestUri">请求 URI。</param>
    public HeadAttribute(string? requestUri = null)
        : base(HttpMethod.Head, requestUri)
    {
    }
}
