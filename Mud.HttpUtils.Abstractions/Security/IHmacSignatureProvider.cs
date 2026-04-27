// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HMAC 签名提供者接口，用于生成和验证 HTTP 请求的 HMAC 签名。
/// </summary>
/// <remarks>
/// HMAC（Hash-based Message Authentication Code）是一种基于哈希函数和密钥的消息认证码，
/// 用于验证消息的完整性和真实性。该接口提供了异步的签名生成和验证功能。
/// <para>
/// 适用场景：
/// <list type="bullet">
///   <item>API 请求的身份验证和防篡改</item>
///   <item>Webhook 回调的签名验证</item>
///   <item>敏感操作的安全认证</item>
/// </list>
/// </para>
/// <para>
/// 安全注意事项：
/// <list type="bullet">
///   <item>密钥应妥善保管，不应硬编码在代码中</item>
///   <item>建议使用 HTTPS 传输，防止签名被截获</item>
///   <item>定期轮换密钥以提高安全性</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 实现 HMAC 签名提供者
/// public class HmacSha256SignatureProvider : IHmacSignatureProvider
/// {
///     public async Task&lt;string&gt; GenerateSignatureAsync(HttpRequestMessage request, string secretKey, CancellationToken cancellationToken = default)
///     {
///         // 构建待签名字符串
///         var stringToSign = $"{request.Method}\n{request.RequestUri}\n{await request.Content.ReadAsStringAsync(cancellationToken)}";
///         
///         // 使用 HMAC-SHA256 生成签名
///         using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
///         var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
///         return Convert.ToBase64String(hash);
///     }
///     
///     public async Task&lt;bool&gt; VerifySignatureAsync(HttpRequestMessage request, string signature, string secretKey, CancellationToken cancellationToken = default)
///     {
///         var expectedSignature = await GenerateSignatureAsync(request, secretKey, cancellationToken);
///         return expectedSignature == signature;
///     }
/// }
/// 
/// // 使用示例
/// var provider = new HmacSha256SignatureProvider();
/// var signature = await provider.GenerateSignatureAsync(request, "my-secret-key");
/// request.Headers.Add("X-Signature", signature);
/// </code>
/// </example>
/// <seealso cref="ISecretProvider"/>
/// <seealso cref="IApiKeyProvider"/>
public interface IHmacSignatureProvider
{
    /// <summary>
    /// 异步生成 HTTP 请求的 HMAC 签名。
    /// </summary>
    /// <param name="request">要签名的 HTTP 请求消息。</param>
    /// <param name="secretKey">用于生成签名的密钥。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>生成的 HMAC 签名（通常为 Base64 编码的字符串）。</returns>
    /// <exception cref="System.ArgumentNullException">当 <paramref name="request"/> 或 <paramref name="secretKey"/> 为 <c>null</c> 时抛出。</exception>
    /// <exception cref="System.InvalidOperationException">当请求格式不正确或无法生成签名时抛出。</exception>
    /// <remarks>
    /// 签名通常基于请求的多个部分组成，包括：
    /// <list type="bullet">
    ///   <item>HTTP 方法（GET、POST 等）</item>
    ///   <item>请求 URI</item>
    ///   <item>请求体内容</item>
    ///   <item>时间戳（防止重放攻击）</item>
    /// </list>
    /// <para>
    /// 具体的签名算法和组成规则应由实现方定义，并在客户端和服务端保持一致。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 生成请求签名
    /// var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");
    /// request.Content = new StringContent("{\"key\":\"value\"}", Encoding.UTF8, "application/json");
    /// 
    /// var signature = await signatureProvider.GenerateSignatureAsync(request, "my-secret-key");
    /// Console.WriteLine($"签名: {signature}");
    /// </code>
    /// </example>
    Task<string> GenerateSignatureAsync(HttpRequestMessage request, string secretKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步验证 HTTP 请求的 HMAC 签名。
    /// </summary>
    /// <param name="request">要验证的 HTTP 请求消息。</param>
    /// <param name="signature">要验证的签名值。</param>
    /// <param name="secretKey">用于验证签名的密钥。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>如果签名验证成功则为 <c>true</c>；否则为 <c>false</c>。</returns>
    /// <exception cref="System.ArgumentNullException">当 <paramref name="request"/>、<paramref name="signature"/> 或 <paramref name="secretKey"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 此方法用于验证请求在传输过程中是否被篡改，以及请求是否来自合法的发送方。
    /// <para>
    /// 验证过程：
    /// <list type="number">
    ///   <item>使用相同的算法和密钥重新计算签名</item>
    ///   <item>将计算出的签名与提供的签名进行比较</item>
    ///   <item>如果匹配则验证成功，否则验证失败</item>
    /// </list>
    /// </para>
    /// <para>
    /// 为防止时序攻击（timing attack），应使用恒定时间比较算法来比较签名。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 验证请求签名
    /// var signature = Request.Headers["X-Signature"];
    /// var isValid = await signatureProvider.VerifySignatureAsync(request, signature, "my-secret-key");
    /// 
    /// if (isValid)
    /// {
    ///     Console.WriteLine("签名验证成功");
    /// }
    /// else
    /// {
    ///     Console.WriteLine("签名验证失败，请求可能被篡改");
    /// }
    /// </code>
    /// </example>
    Task<bool> VerifySignatureAsync(HttpRequestMessage request, string signature, string secretKey, CancellationToken cancellationToken = default);
}
