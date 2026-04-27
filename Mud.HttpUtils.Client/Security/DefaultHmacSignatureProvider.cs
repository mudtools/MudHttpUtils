using System.Security.Cryptography;
using System.Text;

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
/// 在 .NET Standard 2.0 环境下使用自定义实现，在更高版本中使用 
/// <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>。
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

        var signatureBytes = Convert.FromBase64String(signature);
        var expectedBytes = Convert.FromBase64String(expectedSignature);

#if NETSTANDARD2_0
        return FixedTimeEquals(signatureBytes, expectedBytes);
#else
        return CryptographicOperations.FixedTimeEquals(signatureBytes, expectedBytes);
#endif
    }

#if NETSTANDARD2_0
    /// <summary>
    /// 在 .NET Standard 2.0 环境下实现的定时字节数组比较方法，防止时序攻击。
    /// </summary>
    /// <param name="left">第一个字节数组。</param>
    /// <param name="right">第二个字节数组。</param>
    /// <returns>如果两个字节数组完全相同则返回 true，否则返回 false。</returns>
    /// <remarks>
    /// <para>
    /// 该方法使用异或（XOR）操作累积比较结果，确保比较时间与数据内容无关。
    /// 这是防止时序攻击的关键安全措施。
    /// </para>
    /// </remarks>
    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        var length = left.Length;
        var result = 0;

        for (var i = 0; i < length; i++)
        {
            result |= left[i] ^ right[i];
        }

        return result == 0;
    }
#endif

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
    /// sorted=query&parameters=here
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

        sb.Append(request.Method.Method.ToUpperInvariant());
        sb.Append('\n');

        sb.Append(request.RequestUri?.AbsolutePath ?? "/");
        sb.Append('\n');

        var query = request.RequestUri?.Query;
        if (!string.IsNullOrEmpty(query) && query.Length > 1)
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
}
