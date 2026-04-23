namespace Mud.HttpUtils.Resilience;

internal static class HttpRequestMessageCloner
{
    public static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        clone.Version = request.Version;

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

#if !NETSTANDARD2_0
        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }
#endif

        return clone;
    }
}
