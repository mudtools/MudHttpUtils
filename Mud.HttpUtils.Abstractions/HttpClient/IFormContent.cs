namespace Mud.HttpUtils;

public interface IFormContent
{
    HttpContent ToHttpContent();

    Task<HttpContent> ToHttpContentAsync(IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
