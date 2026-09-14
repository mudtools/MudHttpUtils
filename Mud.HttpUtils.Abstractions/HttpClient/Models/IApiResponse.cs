// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 响应的抽象接口（非泛型）。
/// 提供响应元信息访问，不携带强类型内容。
/// </summary>
public interface IApiResponse
{
    /// <summary>HTTP 状态码。</summary>
    HttpStatusCode StatusCode { get; }

    /// <summary>是否为成功状态码（2xx）。</summary>
    bool IsSuccessStatusCode { get; }

    /// <summary>响应头（含 content header）。</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    /// <summary>原始 HttpResponseMessage（可能为 null，如缓存命中场景）。</summary>
    HttpResponseMessage? ResponseMessage { get; }
}
