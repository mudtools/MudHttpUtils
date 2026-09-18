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
    /// </summary>
    [Fact]
    public async Task Recovery_DisabledBodyCache_ShouldStillSend()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object, new TokenRecoveryOptions { MaxCachedRequestBodyBytes = 0 });

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/items")
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
    /// </summary>
    [Fact]
    public async Task Recovery_SmallBody_ShouldRetryWithOriginalContent()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/items")
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
    /// 无 Content-Length 的 chunked 内容，但体积小于上限：可正常缓冲并携带重试。
    /// </summary>
    [Fact]
    public async Task Recovery_ChunkedSmallBody_ShouldBufferAndRetry()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/items")
        {
            Content = new ChunkedStreamContent(64 * 1024),   // 64KB chunked
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

        response.StatusCode.Should().Be(HttpStatusCode.OK, "未超限 chunked 请求可正常缓冲重试");
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
