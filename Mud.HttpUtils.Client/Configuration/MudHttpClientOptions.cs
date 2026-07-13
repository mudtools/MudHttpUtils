// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 单个 HttpClient 实例的配置选项
/// </summary>
/// <remarks>
/// <para>配置单个命名 HttpClient 的详细选项，包括基地址、超时、默认请求头等。</para>
/// <para>配置示例：</para>
/// <code>
/// "Clients": {
///   "MyApi": {
///     "BaseAddress": "https://api.example.com",
///     "TimeoutSeconds": 30,
///     "DefaultHeaders": {
///       "X-Api-Key": "my-api-key",
///       "X-Client-Version": "1.0.0"
///     },
///     "AllowCustomBaseUrls": false
///   }
/// }
/// </code>
/// </remarks>
public class MudHttpClientOptions
{
    /// <summary>
    /// 基础地址
    /// </summary>
    /// <remarks>
    /// HttpClient 请求的基础 URL，例如：https://api.example.com
    /// </remarks>
    public string? BaseAddress { get; set; }

    private int? _timeoutSeconds;

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    /// <remarks>
    /// <para>请求的超时时间，单位为秒。如果未设置，使用系统默认值（通常为 100 秒）。</para>
    /// <para>设置时必须大于 0。</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int? TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = value.HasValue && value.Value <= 0
            ? throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "超时时间必须大于 0 秒。")
            : value;
    }

    /// <summary>
    /// 默认请求头
    /// </summary>
    /// <remarks>
    /// 每个请求都会自动添加的默认 HTTP 头。
    /// 常用于设置 API 密钥、客户端版本等信息。
    /// </remarks>
    public Dictionary<string, string>? DefaultHeaders { get; set; }

    /// <summary>
    /// 是否允许自定义基础 URL
    /// </summary>
    /// <remarks>
    /// 如果设置为 true，允许请求白名单域名之外的 URL（仍会检查私有 IP 和内网域名以防范 SSRF）。
    /// 如果设置为 false（默认），则只允许访问白名单中的域名。
    /// <para>注意：启用此选项会放宽 URL 验证策略，请确保在受信任的环境中使用。</para>
    /// </remarks>
    public bool AllowCustomBaseUrls { get; set; }
}
