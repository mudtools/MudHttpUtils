namespace Mud.HttpUtils.Resilience;

internal static class HttpRequestMessageCloner
{
    public const long DefaultMaxContentSize = 10 * 1024 * 1024;

    public static Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        return CloneAsync(request, DefaultMaxContentSize);
    }

    public static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, long maxContentSize)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.Content != null)
        {
            var contentLength = request.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxContentSize)
            {
                throw new InvalidOperationException(
                    $"请求体大小 ({contentLength.Value:N0} 字节) 超过最大克隆限制 ({maxContentSize:N0} 字节)。" +
                    "大文件上传场景建议禁用重试策略或增加 MaxCloneContentSize 限制。");
            }
        }

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

    public static async Task<HttpRequestMessage?> TryCloneAsync(HttpRequestMessage request, long maxContentSize = DefaultMaxContentSize)
    {
        try
        {
            return await CloneAsync(request, maxContentSize).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
