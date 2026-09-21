#if !NETSTANDARD2_0
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Mud.HttpUtils.Tests;

[Collection("UrlValidator Collection")]
public class AsyncEnumerableExtensionsTests : IClassFixture<UrlValidatorFixture>
{
    private readonly UrlValidatorFixture _fixture;

    public AsyncEnumerableExtensionsTests(UrlValidatorFixture fixture)
    {
        _fixture = fixture;
        _fixture.RestoreDomains();
    }

    private static Mock<HttpMessageHandler> CreateMockStreamHandler(string ndjsonContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(ndjsonContent, Encoding.UTF8, "application/json")
            });
        return handler;
    }

    private static TestableEnhancedHttpClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        return new TestableEnhancedHttpClient(httpClient);
    }

    [Fact]
    public async Task StreamNdJsonAsync_WithNullClient_ThrowsArgumentNullException()
    {
        // M5-HC-10：扩展方法重命名后不再被实例方法遮蔽，null 守卫可达
        IBaseHttpClient client = null!;
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var act = async () =>
        {
            var enumerator = AsyncEnumerableExtensions.StreamNdJsonAsync<string>(client, request).GetAsyncEnumerator();
            await enumerator.MoveNextAsync();
        };

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("client");
    }

    [Fact]
    public async Task SendAsAsyncEnumerable_WithValidNdJson_ReturnsAllItems()
    {
        var ndjson = "{\"Name\":\"Item1\",\"Value\":1}\n{\"Name\":\"Item2\",\"Value\":2}\n{\"Name\":\"Item3\",\"Value\":3}\n";
        var handler = CreateMockStreamHandler(ndjson);
        var client = CreateClient(handler.Object);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var results = new List<TestItem>();
        await foreach (var item in client.SendAsAsyncEnumerable<TestItem>(request))
        {
            results.Add(item);
        }

        results.Should().HaveCount(3);
        results[0].Name.Should().Be("Item1");
        results[1].Name.Should().Be("Item2");
        results[2].Name.Should().Be("Item3");
    }

    [Fact]
    public async Task SendAsAsyncEnumerable_WithEmptyLines_SkipsEmptyLines()
    {
        var ndjson = "{\"Name\":\"Item1\",\"Value\":1}\n\n\n{\"Name\":\"Item2\",\"Value\":2}\n";
        var handler = CreateMockStreamHandler(ndjson);
        var client = CreateClient(handler.Object);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var results = new List<TestItem>();
        await foreach (var item in client.SendAsAsyncEnumerable<TestItem>(request))
        {
            results.Add(item);
        }

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SendAsAsyncEnumerable_WithCustomJsonSerializerOptions_UsesProvidedOptions()
    {
        var ndjson = "{\"name\":\"Item1\",\"value\":1}\n";
        var handler = CreateMockStreamHandler(ndjson);
        var client = CreateClient(handler.Object);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var results = new List<TestItem>();
        await foreach (var item in client.SendAsAsyncEnumerable<TestItem>(request, jsonSerializerOptions: options))
        {
            results.Add(item);
        }

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Item1");
    }

    [Fact]
    public async Task SendAsAsyncEnumerable_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockStreamHandler("error", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler.Object);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var act = async () =>
        {
            await foreach (var _ in client.SendAsAsyncEnumerable<TestItem>(request))
            {
            }
        };

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendAsAsyncEnumerable_WithCancellation_StopsEnumeration()
    {
        var ndjson = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"{{\"Name\":\"Item{i}\",\"Value\":{i}}}")) + "\n";
        var handler = CreateMockStreamHandler(ndjson);
        var client = CreateClient(handler.Object);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");
        using var cts = new CancellationTokenSource();
        var results = new List<TestItem>();
        var count = 0;

        await foreach (var item in client.SendAsAsyncEnumerable<TestItem>(request, cancellationToken: cts.Token))
        {
            results.Add(item);
            count++;
            if (count >= 3)
            {
                #if NET8_0_OR_GREATER
                await cts.CancelAsync();
#else
                cts.Cancel();
#endif
                break;
            }
        }

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendAsAsyncEnumerable_WithInvalidJsonLine_SkipsNullDeserializationResults()
    {
        var ndjson = "{\"Name\":\"Valid\",\"Value\":1}\nnull\n{\"Name\":\"AlsoValid\",\"Value\":2}\n";
        var handler = CreateMockStreamHandler(ndjson);
        var client = CreateClient(handler.Object);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var results = new List<TestItem>();
        await foreach (var item in client.SendAsAsyncEnumerable<TestItem>(request))
        {
            results.Add(item);
        }

        results.Should().HaveCount(2);
        results[0].Name.Should().Be("Valid");
        results[1].Name.Should().Be("AlsoValid");
    }

    // ─────────────── [G8-07] 枚举器/响应流的释放强断言 ───────────────

    /// <summary>
    /// 可计数的流包装器（G8-07 前置基建）：断言「提前中断 / 取消枚举」时底层响应流确实被释放。
    /// </summary>
    /// <remarks>
    /// 既有取消用例只断言元素计数（<see cref="SendAsAsyncEnumerable_WithCancellation_StopsEnumeration"/>），
    /// 无法发现「流未释放 ⇒ HTTP 响应泄漏」；此处以 <see cref="DisposeAsync"/> 计数补齐该缺口。
    /// </remarks>
    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream _inner;

        public TrackingStream(byte[] data) => _inner = new MemoryStream(data);

        /// <summary>释放次数（含 DisposeAsync 触发的 Dispose(true)）。</summary>
        public int DisposeCount { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // 幂等计数：流可能被 Dispose 与 DisposeAsync 各触发一次（.NET 允许重复释放）。
            if (disposing && DisposeCount == 0)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private static (IBaseHttpClient Client, TrackingStream Stream) CreateTrackingNdJsonClient(int itemCount)
    {
        var ndjson = string.Join("\n", Enumerable.Range(1, itemCount).Select(i => $"{{\"Name\":\"Item{i}\",\"Value\":{i}}}")) + "\n";
        var stream = new TrackingStream(Encoding.UTF8.GetBytes(ndjson));

        var client = new Mock<IBaseHttpClient>();
        client.Setup(c => c.SendStreamAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        return (client.Object, stream);
    }

    /// <summary>
    /// G8-07：提前 <c>break</c> 中断枚举 ⇒ 底层响应流必须被释放（否则 HTTP 响应泄漏）。
    /// </summary>
    [Fact]
    public async Task SendAsAsyncEnumerable_BreakMidEnumeration_DisposesStream()
    {
        var (client, stream) = CreateTrackingNdJsonClient(100);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var count = 0;
        await foreach (var _ in client.StreamNdJsonAsync<TestItem>(request))
        {
            if (++count >= 3)
                break;
        }

        count.Should().Be(3);
        stream.DisposeCount.Should().Be(1,
            "提前中断枚举必须由 await using 释放底层响应流，否则响应（含连接）泄漏");
    }

    /// <summary>
    /// G8-07：取消枚举 ⇒ 底层响应流同样必须被释放（成功/取消/异常三条路径的释放语义必须一致）。
    /// </summary>
    [Fact]
    public async Task SendAsAsyncEnumerable_Cancellation_DisposesStream()
    {
        var (client, stream) = CreateTrackingNdJsonClient(100);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");
        using var cts = new CancellationTokenSource();
        var count = 0;

        try
        {
            await foreach (var _ in client.StreamNdJsonAsync<TestItem>(request, cancellationToken: cts.Token))
            {
                if (++count >= 3)
                {
#if NET8_0_OR_GREATER
                    await cts.CancelAsync();
#else
                    cts.Cancel();
#endif
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 取消在读取阶段抛出属合法路径；关键断言是「流已被释放」。
        }

        stream.DisposeCount.Should().Be(1, "取消路径同样必须释放底层响应流");
    }

    public class TestItem
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    public class TestableEnhancedHttpClient : EnhancedHttpClient
    {
        public TestableEnhancedHttpClient(HttpClient httpClient, EnhancedHttpClientOptions? options = null)
            : base(httpClient, options)
        {
        }
    }
}
#endif
