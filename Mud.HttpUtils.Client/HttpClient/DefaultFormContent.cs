namespace Mud.HttpUtils;

public class DefaultFormContent : IFormContent
{
    private readonly Dictionary<string, string> _formData;

    public DefaultFormContent(Dictionary<string, string> formData)
    {
        _formData = formData ?? throw new ArgumentNullException(nameof(formData));
    }

    public HttpContent ToHttpContent()
    {
        return new FormUrlEncodedContent(_formData);
    }

    public Task<HttpContent> ToHttpContentAsync(IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ToHttpContent());
    }
}
