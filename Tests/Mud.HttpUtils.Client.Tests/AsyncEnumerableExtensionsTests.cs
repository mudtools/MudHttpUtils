#if !NETSTANDARD2_0
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Mud.HttpUtils.Tests;

public class AsyncEnumerableExtensionsTests : IClassFixture<UrlValidatorFixture>
{
    private readonly UrlValidatorFixture _fixture;

    public AsyncEnumerableExtensionsTests(UrlValidatorFixture fixture)
    {
        _fixture = fixture;
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
    public async Task SendAsAsyncEnumerable_WithNullClient_ThrowsNullReferenceException()
    {
        IBaseHttpClient client = null!;
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var act = async () =>
        {
            var enumerator = client.SendAsAsyncEnumerable<string>(request).GetAsyncEnumerator();
            await enumerator.MoveNextAsync();
        };

        await act.Should().ThrowAsync<NullReferenceException>();
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
                await cts.CancelAsync();
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

    public class TestItem
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    public class TestableEnhancedHttpClient : EnhancedHttpClient
    {
        public TestableEnhancedHttpClient(HttpClient httpClient, ILogger? logger = null)
            : base(httpClient, logger)
        {
        }

        public override string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
            => throw new NotImplementedException();

        public override string DecryptContent(string encryptedContent)
            => throw new NotImplementedException();

        public override byte[] EncryptBytes(byte[] data)
            => throw new NotImplementedException();

        public override byte[] DecryptBytes(byte[] encryptedData)
            => throw new NotImplementedException();
    }
}
#endif
