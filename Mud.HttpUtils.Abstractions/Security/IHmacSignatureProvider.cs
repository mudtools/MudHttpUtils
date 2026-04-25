namespace Mud.HttpUtils;

public interface IHmacSignatureProvider
{
    Task<string> GenerateSignatureAsync(HttpRequestMessage request, string secretKey, CancellationToken cancellationToken = default);

    Task<bool> VerifySignatureAsync(HttpRequestMessage request, string signature, string secretKey, CancellationToken cancellationToken = default);
}
