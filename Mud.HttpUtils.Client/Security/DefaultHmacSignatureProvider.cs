using System.Security.Cryptography;
using System.Text;

namespace Mud.HttpUtils;

public class DefaultHmacSignatureProvider : IHmacSignatureProvider
{
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

    public async Task<bool> VerifySignatureAsync(
        HttpRequestMessage request,
        string signature,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        var expectedSignature = await GenerateSignatureAsync(request, secretKey, cancellationToken).ConfigureAwait(false);
        return string.Equals(signature, expectedSignature, StringComparison.Ordinal);
    }

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
