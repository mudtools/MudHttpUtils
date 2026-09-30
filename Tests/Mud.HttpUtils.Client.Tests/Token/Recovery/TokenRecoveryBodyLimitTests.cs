// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// SR-H2/H3（P1.4，D4）请求体读取阶段限量 + 无体不重试测试。
/// </summary>
public class TokenRecoveryBodyLimitTests
{
    private static TokenRecoveryExecutor CreateExecutor(
        ITokenManager manager, TokenRecoveryOptions? options = null)
        => new(manager, options);

    private static Mock<ITokenManager> CreateAlwaysValidManager()
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");
        return mock;
    }

    /// <summary>
    /// 无 Content-Length 的 16MB chunked 流：超限不缓冲但仍发送，返回真实 401，不进行重试。
    /// </summary>
    [Fact]
    public async Task Recovery_ChunkedBody_ShouldSendButNotRetry()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload")
        {
            Content = new ChunkedStreamContent(16 * 1024 * 1024),   // 16MB，无 Content-Length
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var sendCount = 0;
        HttpResponseMessage? sentResponse = null;
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                sentResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                return Task.FromResult(sentResponse);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "超限 chunked 带体请求返回真实 401");
        sendCount.Should().Be(1, "超限请求仍正常发送一次（TMR-01：禁止重试 ≠ 禁止发送）");
        response.Should().BeSameAs(sentResponse, "返回的是服务端真实响应实例（D3：响应保真）");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "超限带体请求不进入恢复链路");
        response.Dispose();
    }

    /// <summary>
    /// Content-Length = 100MB 声明：不缓冲但仍发送一次，返回真实 401（不读取流缓冲）。
    /// </summary>
    [Fact]
    public async Task Recovery_DeclaredOversizeBody_ShouldSendButNotBuffer()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var oversize = new OversizeDeclaredContent(100 * 1024 * 1024);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload")
        {
            Content = oversize,
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sendCount.Should().Be(1, "超限请求仍正常发送一次（TMR-01）");
        oversize.StreamRequestedCount.Should().Be(0, "声明超限：零缓冲（不预读流），但发送时流仍会被序列化");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "超限带体请求不进入恢复链路");
        response.Dispose();
    }

    /// <summary>
    /// 带体请求但 MaxCachedRequestBodyBytes = 0（流式优先模式）：仍正常发送，返回真实 401，不重试。
    /// 注：用 PUT（幂等）以隔离 R-P1-04 幂等门控，确保本条验证的是"缓冲关闭"这一逃生门。
    /// </summary>
    [Fact]
    public async Task Recovery_DisabledBodyCache_ShouldStillSend()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object, new TokenRecoveryOptions { MaxCachedRequestBodyBytes = 0 });

        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("payload")),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sendCount.Should().Be(1, "流式优先模式下带体请求仍正常发送一次（TMR-01）");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        response.Dispose();
    }

    /// <summary>
    /// GET / 无体请求：401 恢复链路不变（行为不回归，缓冲逻辑不拦截无体请求）。
    /// </summary>
    [Fact]
    public async Task Recovery_NoBodyRequest_ShouldStillRecover()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                var auth = req.Headers.Authorization?.Parameter;
                return Task.FromResult(auth == "refreshed-token"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "无体 GET 请求 401 恢复链路保持可用");
        response.Dispose();
    }

    /// <summary>
    /// 小体积带体请求（未超限）：401 后携带原始请求体重试成功（正常恢复路径不回归）。
    /// 注：用 PUT（幂等）以隔离 R-P1-04 幂等门控；POST 的默认拒绝与放行由
    /// <c>TokenRecoveryIdempotencyGateTests</c> 单独覆盖。
    /// </summary>
    [Fact]
    public async Task Recovery_SmallBody_ShouldRetryWithOriginalContent()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items")
        {
            Content = new StringContent("small-payload"),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var response = await executor.ExecuteAsync(
            request,
            async (req, ct) =>
            {
                var auth = req.Headers.Authorization?.Parameter;
                if (auth == "refreshed-token" && req.Content != null)
                {
                    var body = await req.Content.ReadAsStringAsync(ct);
                    if (body == "small-payload")
                        return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "未超限带体请求 401 恢复应携带原始请求体");
        response.Dispose();
    }

    /// <summary>
    /// R-P1-02（破坏性变更）：<b>未声明长度</b>（chunked）的流式请求体不再预读 ——
    /// 首次发送完整（修复 B8 首发送截断），但放弃 401 重放能力。
    /// </summary>
    [Fact]
    public async Task Recovery_ChunkedUndeclaredBody_ShouldSendIntactButNotRetry()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items")
        {
            Content = new ChunkedStreamContent(64 * 1024),   // 64KB，无 Content-Length（chunked）
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var sendCount = 0;
        long firstSendBodyBytes = -1;
        var response = await executor.ExecuteAsync(
            request,
            async (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                firstSendBodyBytes = req.Content is null
                    ? 0
                    : (await req.Content.ReadAsByteArrayAsync(ct)).LongLength;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "未声明长度的流式体不具备无损重放能力（R-P1-02 取舍）");
        sendCount.Should().Be(1, "禁止重试 ≠ 禁止发送（TMR-01）");
        firstSendBodyBytes.Should().Be(64 * 1024,
            "首次发送体必须完整 —— 修复前预读失败（超限弃置）会让流被部分消费，首发送被截断（B8）");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "不具备重放能力 ⇒ 不进入恢复链路");
        response.Dispose();
    }

    /// <summary>
    /// R-P1-02 回归保护：<b>已声明长度</b>的流式请求体（可寻址流）仍按上限缓冲并重放 ——
    /// 只有"未声明长度"这一种情形被收窄。
    /// </summary>
    [Fact]
    public async Task Recovery_DeclaredLengthStreamBody_ShouldBufferAndRetry()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var payload = new byte[64 * 1024];
        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items")
        {
            // 可寻址流 ⇒ StreamContent 能计算出 Content-Length
            Content = new StreamContent(new MemoryStream(payload)),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var response = await executor.ExecuteAsync(
            request,
            async (req, ct) =>
            {
                var auth = req.Headers.Authorization?.Parameter;
                if (auth == "refreshed-token" && req.Content != null)
                {
                    var body = await req.Content.ReadAsByteArrayAsync(ct);
                    if (body.Length == payload.Length)
                        return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "已声明长度的流式体仍可缓冲重放（Auto 模式语义不变）");
        response.Dispose();
    }

    /// <summary>
    /// R-P1-03：<see cref="RequestBodyBufferingMode.MemoryOnly"/> 下不触碰任何流式内容。
    /// </summary>
    [Fact]
    public async Task Recovery_MemoryOnlyMode_ShouldNotBufferStreamBody()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object, new TokenRecoveryOptions
        {
            BufferingMode = RequestBodyBufferingMode.MemoryOnly,
        });

        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items")
        {
            Content = new StreamContent(new MemoryStream(new byte[1024])),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var sendCount = 0;
        var contentTypeObserved = new List<string>();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                contentTypeObserved.Add(req.Content?.GetType().Name ?? "(null)");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sendCount.Should().Be(1);
        contentTypeObserved.Should().ContainSingle("MemoryOnly 不得把流式内容替换为缓冲体（避免流读取与拷贝）")
            .Which.Should().Be("StreamContent");
        response.Dispose();
    }

    /// <summary>
    /// R-P1-03 回归保护：内存型内容不被替换（既不释放调用方内容，也不做额外拷贝）。
    /// </summary>
    [Fact]
    public async Task Recovery_MemoryBackedContent_ShouldNotBeReplaced()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var payload = new byte[256 * 1024];
        var original = new ByteArrayContent(payload);
        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items")
        {
            Content = original,
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        request.Content.Should().BeSameAs(original, "内存型内容无需回填缓冲体（原实例可重读）");
        response.Dispose();
    }

    /// <summary>
    /// 声明 Content-Length 且超限的内容源：用于验证"零缓冲"路径（流从未被请求）。
    /// </summary>
    private sealed class OversizeDeclaredContent : HttpContent
    {
        private readonly long _declaredLength;
        public int StreamRequestedCount;

        public OversizeDeclaredContent(long declaredLength)
        {
            _declaredLength = declaredLength;
            Headers.ContentLength = declaredLength;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Interlocked.Increment(ref StreamRequestedCount);
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _declaredLength;
            return true;
        }
    }

    /// <summary>
    /// 无 Content-Length 的流式内容（模拟 chunked）：按需生成指定总字节数。
    /// </summary>
    private sealed class ChunkedStreamContent : HttpContent
    {
        private readonly int _totalBytes;

        public ChunkedStreamContent(int totalBytes) => _totalBytes = totalBytes;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[8192];
            var remaining = _totalBytes;
            while (remaining > 0)
            {
                var chunk = Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, chunk)).ConfigureAwait(false);
                remaining -= chunk;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;   // 不声明长度（chunked）
        }
    }
}
