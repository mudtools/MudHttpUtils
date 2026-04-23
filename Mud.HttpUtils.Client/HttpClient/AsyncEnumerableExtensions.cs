#if !NETSTANDARD2_0
using System.Runtime.CompilerServices;

namespace Mud.HttpUtils;

/// <summary>
/// IBaseHttpClient 的 IAsyncEnumerable 扩展方法
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// 发送 HTTP 请求并以 IAsyncEnumerable 流式返回 NDJSON 响应中的每一行数据。
    /// </summary>
    /// <typeparam name="T">每行数据的类型。</typeparam>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="jsonSerializerOptions">JSON 序列化选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式返回的异步枚举。</returns>
    public static async IAsyncEnumerable<T> SendAsAsyncEnumerable<T>(
        this IBaseHttpClient client,
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        var stream = await client.SendStreamAsync(request, cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
#if NET7_0_OR_GREATER
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#else
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
#endif
                if (string.IsNullOrEmpty(line))
                    continue;

                T? item;
                if (jsonSerializerOptions is System.Text.Json.JsonSerializerOptions options)
                {
                    item = System.Text.Json.JsonSerializer.Deserialize<T>(line, options);
                }
                else
                {
                    item = System.Text.Json.JsonSerializer.Deserialize<T>(line);
                }

                if (item != null)
                    yield return item;
            }
        }
    }
}
#endif
