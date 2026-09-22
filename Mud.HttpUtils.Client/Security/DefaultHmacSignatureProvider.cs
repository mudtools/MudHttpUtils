// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;

namespace Mud.HttpUtils;

/// <summary>
/// 默认的 HMAC 签名提供者实现，使用 HMAC-SHA256 算法生成和验证请求签名。
/// </summary>
/// <remarks>
/// <para>
/// 该类实现了 <see cref="IHmacSignatureProvider"/> 接口，用于 HTTP 请求的 HMAC 签名生成和验证。
/// 签名算法使用 HMAC-SHA256，确保请求的完整性和来源可信性。
/// </para>
/// <para>
/// 签名串构建规则：
/// <list type="number">
/// <item><description>HTTP 方法（大写）</description></item>
/// <item><description>请求路径</description></item>
/// <item><description>排序后的查询参数（按字典序）</description></item>
/// <item><description>请求体内容的 Base64 编码（如果有）</description></item>
/// </list>
/// 各部分之间使用换行符（\n）分隔。
/// </para>
/// <para>
/// 该实现包含定时比较（constant-time comparison）功能，防止时序攻击（timing attack）。
/// </para>
/// <para>
/// M6-HC-25（D4-A）防重放签名：默认<b>关闭</b>（<see cref="RequireAntiReplay"/> = <c>false</c>，维持既有签名串口径与确定性）。
/// 开启后签名串首行固定为两行防重放要素，服务端可据此拒绝过期/重复请求：
/// <code>
/// X-Timestamp: {Unix 秒}
/// X-Nonce: {8 字节随机值的十六进制小写}
/// HTTP_METHOD
/// /path
/// sorted=query
/// base64Body
/// </code>
/// 两个值同时写入请求头，使 <see cref="VerifySignatureAsync"/> 能在同一请求上复算一致；
/// 请求已携带这两个头时直接复用（服务端校验入站请求即依赖此路径，按原值复算签名）。
/// </para>
/// <para>
/// <b>nonce 去重与时间窗校验由服务端负责</b>：本提供者不保存任何 nonce 状态（客户端无状态、无额外内存与跨节点一致性问题）。
/// 服务端应自行维护 nonce 缓存（建议 TTL 覆盖可接受的时间窗，如 5 分钟）并拒绝时间戳偏移过大的请求。
/// </para>
/// </remarks>
/// <example>
/// 使用示例：
/// <code>
/// var provider = new DefaultHmacSignatureProvider();
/// 
/// // 生成签名
/// var signature = await provider.GenerateSignatureAsync(request, secretKey);
/// 
/// // 验证签名
/// var isValid = await provider.VerifySignatureAsync(request, signature, secretKey);
/// </code>
/// </example>
public class DefaultHmacSignatureProvider : IHmacSignatureProvider
{
    /// <summary>防重放时间戳头名（M6-HC-25）。</summary>
    internal const string TimestampHeaderName = "X-Timestamp";

    /// <summary>防重放随机数头名（M6-HC-25）。</summary>
    internal const string NonceHeaderName = "X-Nonce";

    /// <summary>
    /// 初始化签名提供者（默认关闭防重放签名，行为与历史版本一致）。
    /// </summary>
    public DefaultHmacSignatureProvider()
        : this(requireAntiReplay: false)
    {
    }

    /// <summary>
    /// 初始化签名提供者，并指定是否启用防重放签名。
    /// </summary>
    /// <param name="requireAntiReplay">
    /// M6-HC-25（D4-A）：为 <c>true</c> 时把 <c>X-Timestamp</c>（Unix 秒）与 <c>X-Nonce</c>（8 字节随机十六进制）
    /// 固定置于签名串首两行，并写入请求头；为 <c>false</c>（默认）时维持原有确定性签名串。
    /// </param>
    /// <remarks>
    /// 注意：开启后签名不再确定（同一请求重复生成会得到不同签名），服务端必须实现 nonce 去重与时间窗校验才能获得防重放收益。
    /// </remarks>
    public DefaultHmacSignatureProvider(bool requireAntiReplay)
    {
        RequireAntiReplay = requireAntiReplay;
    }

    /// <summary>
    /// M6-HC-25：是否启用防重放签名（时间戳 + 随机数入签）。默认 <c>false</c>。
    /// </summary>
    public bool RequireAntiReplay { get; }

    /// <summary>
    /// 异步生成 HTTP 请求的 HMAC 签名。
    /// </summary>
    /// <param name="request">要签名的 HTTP 请求消息。</param>
    /// <param name="secretKey">用于签名的密钥。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>Base64 编码的 HMAC-SHA256 签名。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="request"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="secretKey"/> 为空时抛出。</exception>
    /// <remarks>
    /// <para>
    /// 签名生成过程：
    /// <list type="number">
    /// <item><description>构建签名串（包含方法、路径、查询参数、请求体）</description></item>
    /// <item><description>使用 HMAC-SHA256 和密钥对签名串进行哈希计算</description></item>
    /// <item><description>将哈希结果转换为 Base64 字符串</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task<string> GenerateSignatureAsync(
        HttpRequestMessage request,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(secretKey))
            throw new ArgumentException("密钥不能为空", nameof(secretKey));

        var signatureString = await BuildSignatureStringAsync(request, cancellationToken).ConfigureAwait(false);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureString));

        return Convert.ToBase64String(signatureBytes);
    }

    /// <summary>
    /// 异步验证 HTTP 请求的签名是否有效。
    /// </summary>
    /// <param name="request">要验证的 HTTP 请求消息。</param>
    /// <param name="signature">要验证的签名字符串（Base64 编码）。</param>
    /// <param name="secretKey">用于验证的密钥。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>如果签名有效则返回 true，否则返回 false。</returns>
    /// <remarks>
    /// <para>
    /// 验证过程：
    /// <list type="number">
    /// <item><description>使用相同的算法重新生成预期签名</description></item>
    /// <item><description>使用定时比较（constant-time comparison）对比提供的签名和预期签名</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 使用定时比较可以防止时序攻击，确保比较操作的时间不依赖于签名的匹配程度。
    /// </para>
    /// </remarks>
    public async Task<bool> VerifySignatureAsync(
        HttpRequestMessage request,
        string signature,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        var expectedSignature = await GenerateSignatureAsync(request, secretKey, cancellationToken).ConfigureAwait(false);

        byte[] signatureBytes;
        byte[] expectedBytes;

        try
        {
            signatureBytes = Convert.FromBase64String(signature);
            expectedBytes = Convert.FromBase64String(expectedSignature);
        }
        catch (FormatException)
        {
            return false;
        }

        // B-6：常量时间比较收敛到 SecurityHelper（与 DefaultAesEncryptionProvider 共用唯一实现）
        return SecurityHelper.FixedTimeEquals(signatureBytes, expectedBytes);
    }

    /// <summary>
    /// 构建用于 HMAC 签名的签名字符串。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>构建完成的签名字符串。</returns>
    /// <remarks>
    /// <para>
    /// 签名字符串格式（每部分以换行符分隔）：
    /// <code>
    /// HTTP_METHOD
    /// /path/to/resource
    /// sorted=query&amp;parameters=here
    /// base64EncodedRequestBody (optional)
    /// </code>
    /// </para>
    /// <para>
    /// 查询参数按字典序排序以确保一致性，请求体使用 Base64 编码。
    /// </para>
    /// </remarks>
    private async Task<string> BuildSignatureStringAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // M6-HC-25：防重放要素固定置于签名串首两行（服务端重建签名串时按同一顺序解析）。
        if (RequireAntiReplay)
        {
            sb.Append(TimestampHeaderName);
            sb.Append(": ");
            sb.Append(GetOrAddHeaderValue(request, TimestampHeaderName, CreateTimestamp));
            sb.Append('\n');
            sb.Append(NonceHeaderName);
            sb.Append(": ");
            sb.Append(GetOrAddHeaderValue(request, NonceHeaderName, CreateNonce));
            sb.Append('\n');
        }

        sb.Append(request.Method.Method.ToUpperInvariant());
        sb.Append('\n');

        // F-02 延迟签名发射点使相对 URI 请求首次可运行：生成代码在 BaseAddress 解析前以相对 URI
        // 构造 HttpRequestMessage，而 AbsolutePath/Query 对相对 URI 会抛 InvalidOperationException，
        // 故对相对 URI 手动拆分路径与查询串（客户端签名与服务端重建须使用同一口径）。
        var uri = request.RequestUri;
        string path;
        string query;
        if (uri is null)
        {
            path = "/";
            query = string.Empty;
        }
        else if (uri.IsAbsoluteUri)
        {
            path = uri.AbsolutePath;
            query = uri.Query;
        }
        else
        {
            var raw = uri.ToString();
            var queryIndex = raw.IndexOf('?');
            // FIX-01：netstandard2.0 不支持 range/index 运算符（CS0518）。
            // 使用 Substring 替代，零 polyfill 依赖，行为等价。
            path = queryIndex >= 0 ? raw.Substring(0, queryIndex) : raw;
            query = queryIndex >= 0 ? raw.Substring(queryIndex) : string.Empty;
        }

        sb.Append(path);
        sb.Append('\n');

        if (query is { Length: > 1 })
        {
            var queryString = query.StartsWith("?") ? query.Substring(1) : query;
            var sortedParams = queryString
                .Split('&')
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => p, StringComparer.Ordinal);
            sb.Append(string.Join("&", sortedParams));
        }
        sb.Append('\n');

        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (contentBytes.Length > 0)
            {
                sb.Append(Convert.ToBase64String(contentBytes));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// M6-HC-25：读取请求上已有的防重放头值；缺失时按 <paramref name="factory"/> 生成并写回请求头。
    /// </summary>
    /// <remarks>
    /// 复用已有值是<b>验证</b>路径成立的前提：<see cref="VerifySignatureAsync"/> 在同一请求对象上复算签名，
    /// 服务端则读取入站请求已携带的头值。若每次都生成新值，签名将永远无法自校验通过。
    /// </remarks>
    private static string GetOrAddHeaderValue(HttpRequestMessage request, string headerName, Func<string> factory)
    {
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        var created = factory();
        request.Headers.Remove(headerName);
        request.Headers.Add(headerName, created);
        return created;
    }

    /// <summary>M6-HC-25：当前 Unix 时间戳（秒，InvariantCulture，跨区域一致）。</summary>
    private static string CreateTimestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>M6-HC-25：8 字节加密安全随机数的十六进制小写表示（16 个字符）。</summary>
    private static string CreateNonce()
    {
        var bytes = new byte[8];
#if NET6_0_OR_GREATER
        RandomNumberGenerator.Fill(bytes);
#else
        // netstandard2.0 / net6 以下：无静态 Fill，改用实例 API。
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
#endif

        var chars = new char[bytes.Length * 2];
        const string hex = "0123456789abcdef";
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = hex[bytes[i] >> 4];
            chars[i * 2 + 1] = hex[bytes[i] & 0x0F];
        }

        return new string(chars);
    }
}
