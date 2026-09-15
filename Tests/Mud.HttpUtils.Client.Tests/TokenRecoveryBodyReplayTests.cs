// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// TMR-02：缓冲结果回填请求内容，保证首次与重试同源。
/// TMR-03：恢复失败返回真实 401（响应所有权）。
/// </summary>
public class TokenRecoveryBodyReplayTests
{
    private static Mock<ITokenManager> CreateAlwaysValidManager()
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");
        return mock;
    }

    private static TokenRecoveryExecutor CreateExecutor(ITokenManager manager, TokenRecoveryOptions? options = null)
        => new(manager, options);

    /// <summary>
    /// 不可重放 StreamContent：首次发送应携带完整请求体（缓冲回填后首次发送体不丢失）。
    /// </summary>
    [Fact]
    public async Task Recovery_StreamContent_FirstSend_ShouldCarryFullBody()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var originalBytes = Encoding.UTF8.GetBytes("hello-world-body-data");
        var nonSeekableStream = new NonSeekableStream(originalBytes);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload")
        {
            Content = new StreamContent(nonSeekableStream),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        byte[]? firstSendBody = null;
        var response = await executor.ExecuteAsync(
            request,
            async (req, ct) =>
            {
                if (req.Content != null)
                    firstSendBody = await req.Content.ReadAsByteArrayAsync(ct);
                var auth = req.Headers.Authorization?.Parameter;
                return auth == "refreshed-token"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "恢复成功");
        firstSendBody.Should().NotBeNull("首次发送应读出请求体");
        firstSendBody.Should().Equal(originalBytes, "首次发送体应与原始字节一致（TMR-02：缓冲回填）");
        response.Dispose();
    }

    /// <summary>
    /// 可重放内容（ByteArrayContent）：首次发送与重试发送体应一致。
    /// </summary>
    [Fact]
    public async Task Recovery_ByteArrayContent_FirstSendAndRetry_ShouldCarrySameBody()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var originalBytes = Encoding.UTF8.GetBytes("replay-test-payload");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/items")
        {
            Content = new ByteArrayContent(originalBytes),
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var sendBodies = new List<byte[]>();
        var response = await executor.ExecuteAsync(
            request,
            async (req, ct) =>
            {
                if (req.Content != null)
                {
                    var body = await req.Content.ReadAsByteArrayAsync(ct);
                    sendBodies.Add(body);
                }
                var auth = req.Headers.Authorization?.Parameter;
                return auth == "refreshed-token"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sendBodies.Should().HaveCount(2, "应发送两次（首次 + 重试）");
        sendBodies[0].Should().Equal(originalBytes, "首次发送体完整");
        sendBodies[1].Should().Equal(originalBytes, "重试发送体与首次一致");
        response.Dispose();
    }

    /// <summary>
    /// 恢复耗尽时返回的响应应包含服务端真实 401 头（WWW-Authenticate）。
    /// </summary>
    [Fact]
    public async Task Recovery_Exhausted_ShouldReturnReal401WithServerHeaders()
    {
        var manager = new Mock<ITokenManager>();
        manager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");

        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/secure")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                resp.Headers.Add("WWW-Authenticate", @"Bearer error=""invalid_token"", error_description=""The token expired""");
                resp.Content = new StringContent("{\"error\":\"invalid_token\"}");
                return Task.FromResult(resp);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("WWW-Authenticate").Should().BeTrue("应保留服务端真实 WWW-Authenticate 头（D3：响应保真）");
        var wwwAuth = response.Headers.GetValues("WWW-Authenticate").First();
        wwwAuth.Should().Contain("invalid_token", "服务端错误描述应保留");
        response.Dispose();
    }

    /// <summary>
    /// 重试成功时应释放原始 401 响应。
    /// </summary>
    [Fact]
    public async Task Recovery_RetrySucceeded_ShouldDisposeOriginalResponse()
    {
        var manager = CreateAlwaysValidManager();
        var executor = CreateExecutor(manager.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data")
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale") }
        };

        HttpResponseMessage? originalResponse = null;
        HttpResponseMessage? retryResponse = null;
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                var auth = req.Headers.Authorization?.Parameter;
                if (auth == "refreshed-token")
                {
                    retryResponse = new HttpResponseMessage(HttpStatusCode.OK);
                    return Task.FromResult(retryResponse);
                }

                originalResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                return Task.FromResult(originalResponse);
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "重试成功");
        response.Should().BeSameAs(retryResponse, "应返回重试响应而非原始 401（D3：重试成功时返回重试响应）");
        response.Should().NotBeSameAs(originalResponse, "不应返回原始 401 响应");
        response.Dispose();
    }

    /// <summary>
    /// 不可寻址流，用于测试不可重放内容场景。
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public NonSeekableStream(byte[] data) => _data = data;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _data.Length - _position;
            if (remaining <= 0) return 0;
            var toRead = Math.Min(count, remaining);
            Buffer.BlockCopy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
