namespace Mud.HttpUtils.Resilience;

internal static class HttpRequestMessageCloner
{
    public const long DefaultMaxContentSize = 10 * 1024 * 1024;

    public static Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        return CloneAsync(request, DefaultMaxContentSize);
    }

    public static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        long maxContentSize,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        cancellationToken.ThrowIfCancellationRequested();

        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            byte[] contentBytes;
            if (maxContentSize < 0)
            {
                // -1 = 不限制（ResilienceOptions.MaxCloneContentSize 的既有语义）
                contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            else
            {
                // M1-#2：按 maxContentSize + 1 限量缓冲，Content-Length 缺失（chunked）或被伪造时同样不会超读；
                // 读取阶段透传取消令牌。
                var (bytes, exceeded) = await BufferUpToAsync(
                    request.Content, maxContentSize, cancellationToken).ConfigureAwait(false);
                if (exceeded)
                {
                    throw new InvalidOperationException(
                        $"请求体大小超过最大克隆限制 ({maxContentSize:N0} 字节)。" +
                        "大文件上传场景建议禁用重试策略或调整 MaxCloneContentSize 限制。");
                }
                contentBytes = bytes;
            }

            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        CopyMetadata(request, clone);

        return clone;
    }

    /// <summary>
    /// 把源请求的元数据（Version / VersionPolicy / Properties / Options / 请求头）复制到目标请求。
    /// 供 <see cref="CloneAsync"/> 与 <see cref="ResilientHttpClient"/> 的下载路径克隆共用，避免多处漂移。
    /// </summary>
    internal static void CopyMetadata(HttpRequestMessage source, HttpRequestMessage target)
    {
        target.Version = source.Version;
#if NET5_0_OR_GREATER
        // M1-#3：VersionPolicy 是与 Version 独立的属性，漏拷会使 HTTP/2-only / HTTP/3 重试静默降级到 1.1
        target.VersionPolicy = source.VersionPolicy;
#endif

        foreach (var header in source.Headers)
        {
            target.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

#if NETSTANDARD2_0
        // ns2.0 仅有 Properties
        foreach (var kvp in source.Properties)
            target.Properties[kvp.Key] = kvp.Value;
#else
        // net5+：Options 与 Properties 都拷（仓库自身两者都在用，
        // 如 EnhancedHttpClient 写 __mud_captured_request_content、MudHttpObservability.TryGetProperty 双查）
        foreach (var option in source.Options)
        {
            target.Options.TryAdd(option.Key, option.Value);
        }
        foreach (var kvp in source.Properties)
        {
            target.Properties[kvp.Key] = kvp.Value;
        }
#endif
    }

    /// <summary>
    /// 以字节数上限缓冲请求内容：读到 maxBytes + 1 字节即可断定超限，避免无谓多读。
    /// </summary>
    /// <remarks>
    /// 已缓冲内容（StringContent/ByteArrayContent 等）的内部流支持重复读取；
    /// 为保持"同一请求可多次克隆"的既有语义，本方法不消耗内容的可重放性
    /// （不 dispose 内容自有流，探测读取依赖 BCL 缓冲内容的 seek 能力）。
    /// </remarks>
    private static async Task<(byte[] Bytes, bool Exceeded)> BufferUpToAsync(
        HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        // 快路径：声明长度明确且在限制内 → 直接缓冲（BCL 自带重放支持）
        var declared = content.Headers.ContentLength;
        if (declared.HasValue && declared.Value <= maxBytes)
        {
            var all = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return (all, false);
        }

        // 声明超限 → 直接判定（不多读一个字节）
        if (declared.HasValue && declared.Value > maxBytes)
            return (Array.Empty<byte>(), true);

        // 无声明长度（chunked / 伪造）：经流限量探测 maxBytes + 1 字节
        var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        var canSeek = stream.CanSeek;
        if (canSeek)
            stream.Position = 0;   // 已缓冲内容从 0 读取，保持可重放

        var limit = maxBytes == long.MaxValue ? maxBytes : maxBytes + 1;
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (total < limit)
        {
            var toRead = (int)Math.Min(buffer.Length, limit - total);
            var read = await stream.ReadAsync(buffer, 0, toRead, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await ms.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            total += read;
        }

        if (canSeek)
            stream.Position = 0;   // 复位，保持内容可再次读取（重放语义）

        return total > maxBytes
            ? (ms.ToArray(), true)
            : (ms.ToArray(), false);
    }

    public static async Task<HttpRequestMessage?> TryCloneAsync(
        HttpRequestMessage request,
        long maxContentSize = DefaultMaxContentSize,
        CancellationToken cancellationToken = default)
    {
        // H-7：透传取消令牌。克隆阶段被取消时 OperationCanceledException 即时上抛
        // （不被下方 InvalidOperationException 捕获），且读取阶段同步感知取消。
        try
        {
            return await CloneAsync(request, maxContentSize, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
