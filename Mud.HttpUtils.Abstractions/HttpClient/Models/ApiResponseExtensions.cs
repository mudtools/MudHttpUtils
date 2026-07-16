// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// 响应扩展方法。
/// </summary>
/// <remarks>
/// <para>
/// 使用传统 <c>static</c> 扩展方法（非 C# 14 <c>extension</c> 块），兼容 netstandard2.0。
/// </para>
/// </remarks>
public static class ApiResponseExtensions
{
    /// <summary>
    /// 若状态码非 2xx，抛出 <see cref="ApiException"/>（保留 <see cref="Response{T}"/> 包装）。
    /// </summary>
    /// <typeparam name="T">响应内容类型。</typeparam>
    /// <param name="task">异步响应任务。</param>
    /// <returns>若成功则返回 <see cref="Response{T}"/>（保留元数据）。</returns>
    public static Task<Response<T>> EnsureSuccessStatusCodeAsync<T>(this Task<Response<T>> task)
        => task.ContinueWith(t => EnsureSuccessStatusCode(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);

    /// <summary>
    /// 若状态码非 2xx，抛出 <see cref="ApiException"/>（同步版本，保留 <see cref="Response{T}"/> 包装）。
    /// </summary>
    /// <typeparam name="T">响应内容类型。</typeparam>
    /// <param name="response">响应实例。</param>
    /// <returns>若成功则返回原 <paramref name="response"/>（可继续访问元数据）。</returns>
    public static Response<T> EnsureSuccessStatusCode<T>(this Response<T> response)
    {
        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            throw new ApiException(response.StatusCode, response.ErrorContent);
        return response;
    }

    /// <summary>
    /// 读取响应头指定值。
    /// </summary>
    /// <typeparam name="T">响应内容类型。</typeparam>
    /// <param name="response">响应实例。</param>
    /// <param name="name">头名称。</param>
    /// <returns>头值；若不存在则返回 <c>null</c>。</returns>
    public static string? GetHeader<T>(this IApiResponse<T> response, string name)
    {
        // IApiResponse.Headers 为 IReadOnlyDictionary<string, IReadOnlyList<string>>
        // 通过接口直接访问（IApiResponse<T> 继承 IApiResponse，无需强制转换）
        var headers = response.Headers;
        if (headers == null || headers.Count == 0) return null;
        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;
    }
}
