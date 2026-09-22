#if !NETSTANDARD2_0
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
#endif

namespace Mud.HttpUtils;

/// <summary>
/// IBaseHttpClient 的 NDJSON 流式扩展方法。
/// </summary>
/// <remarks>
/// M5-HC-10：原名 <c>SendAsAsyncEnumerable</c> 与 <see cref="IBaseHttpClient"/> 实例方法同名同形参，
/// 被实例方法遮蔽（扩展方法不可达）。重命名为语义化名称后 null 守卫恢复可达。
/// </remarks>
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
    /// <remarks>
    /// <b>Native AOT 注意</b>：此重载使用开放泛型反序列化，
    /// AOT 场景下须确保 <typeparamref name="T"/> 已在 <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> 对应的 Context 中声明。
    /// 推荐使用接收 <c>JsonTypeInfo&lt;T&gt;</c> 的 <c>StreamNdJsonAsync</c> 重载。
    /// </remarks>
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode("NDJSON 反序列化使用开放泛型 JsonSerializer.Deserialize<T>，AOT 场景须确保 T 已在 JsonSerializerContext 中声明。推荐使用 JsonTypeInfo<T> 重载。")]
#endif
    public static async IAsyncEnumerable<T> StreamNdJsonAsync<T>(
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
            var options = jsonSerializerOptions as System.Text.Json.JsonSerializerOptions;
            // M6-HC-31：优先复用客户端注入的序列化器（其 options 承载消费方的 JsonSerializerContext resolver 与自定义设置），
            // 仅当客户端非 EnhancedHttpClient（如第三方实现 / 测试替身）时才按入参另建默认序列化器。
            var contentSerializer = client is EnhancedHttpClient enhancedClient
                ? enhancedClient.ContentSerializer
                : new SystemTextJsonContentSerializer(options);

            await foreach (var item in EnhancedHttpClient.ParseNdJsonStreamAsync<T>(stream, options, contentSerializer, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// 发送 HTTP 请求并以 IAsyncEnumerable 流式返回 NDJSON 响应中的每一行数据（AOT 安全重载）。
    /// </summary>
    /// <typeparam name="T">每行数据的类型。</typeparam>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="jsonTypeInfo">来自 <c>JsonSerializerContext</c> 的类型信息（AOT 安全）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式返回的异步枚举。</returns>
    public static async IAsyncEnumerable<T> StreamNdJsonAsync<T>(
        this IBaseHttpClient client,
        HttpRequestMessage request,
        JsonTypeInfo<T> jsonTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (jsonTypeInfo == null)
            throw new ArgumentNullException(nameof(jsonTypeInfo));

        var stream = await client.SendStreamAsync(request, cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            // M6-HC-31：同开放泛型重载 —— 复用客户端注入的序列化器，避免另建默认实例导致
            // 与客户端配置（含 JsonSerializerContext resolver、命名策略）不一致。
            var contentSerializer = client is EnhancedHttpClient enhancedClient
                ? enhancedClient.ContentSerializer
                : HttpContentSerializerFactory.CreateDefault();

            await foreach (var item in EnhancedHttpClient.ParseNdJsonStreamAsync(stream, jsonTypeInfo, contentSerializer, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }
#endif
}
#endif
