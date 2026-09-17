// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的要求。
// -----------------------------------------------------------------------

// M5 回归用例：HC-01/HC-02（响应释放）、HC-03（日志脱敏）、HC-11（NullLogger 判定）

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Testing;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// HC-01/HC-02：非 2xx 路径必须释放 HttpResponseMessage（连接归还连接池）。
/// </summary>
public class M5ResponseDisposeTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    /// <summary>可计数 Dispose 的 HttpContent，用于断言响应是否被释放。</summary>
    private sealed class CountingContent : HttpContent
    {
        public int DisposeCount { get; private set; }

        public CountingContent(string content, string mediaType = "application/json")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            _bytes = bytes;
        }

        private readonly byte[] _bytes;

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes, 0, _bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    /// <summary>可注入自定义响应内容的 handler。</summary>
    private sealed class ContentTrackingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.NotFound;
        public CountingContent? LastContent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastContent = new CountingContent("""{"error":"not found"}""");
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = LastContent,
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }

    private static (DirectEnhancedHttpClient Client, ContentTrackingHandler Handler) Create(
        HttpStatusCode status = HttpStatusCode.NotFound,
        ILogger? logger = null)
    {
        var handler = new ContentTrackingHandler { StatusCode = status };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(
            httpClient,
            logger != null ? new EnhancedHttpClientOptions { Logger = logger } : null);
        return (client, handler);
    }

    [Fact]
    public async Task HC01_SendAsync_404_ResponseIsDisposed()
    {
        var (client, handler) = Create(HttpStatusCode.NotFound);

        var act = () => client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "/api/missing"));
        var ex = await act.Should().ThrowAsync<ApiException>();

        handler.LastContent!.DisposeCount.Should().Be(1,
            "SendAndValidateAsync 失败路径必须释放响应（HC-01）");
        ex.Which.Content.Should().NotBeNull("错误体应在 Dispose 前读入 ApiException");
    }

    [Fact]
    public async Task HC01_SendAsync_404_ApiExceptionContentPreserved()
    {
        var (client, _) = Create(HttpStatusCode.NotFound);

        var ex = await FluentActions.Awaiting(() =>
                client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "/api/missing")))
            .Should().ThrowAsync<ApiException>();

        ex.Which.Content.Should().Contain("error");
    }

    [Fact]
    public async Task HC01_ResponseInterceptorThrows_ResponseIsDisposed()
    {
        var handler = new ContentTrackingHandler { StatusCode = HttpStatusCode.OK };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        var interceptor = new ThrowingResponseInterceptor();
        var client = new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions
        {
            ResponseInterceptors = new IHttpResponseInterceptor[] { interceptor },
        });

        var act = () => client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.LastContent!.DisposeCount.Should().Be(1,
            "响应拦截器抛异常时也必须释放响应（HC-01）");
    }

    [Fact]
    public async Task HC02_SendStreamAsync_500_ResponseIsDisposed()
    {
        var (client, handler) = Create(HttpStatusCode.InternalServerError);

        var act = () => client.SendStreamAsync(new HttpRequestMessage(HttpMethod.Get, "/api/stream"));
        await act.Should().ThrowAsync<ApiException>();

        handler.LastContent!.DisposeCount.Should().Be(1,
            "SendStreamAsync 失败路径必须释放响应（HC-02）");
    }

    [Fact]
    public async Task HC02_SendStreamAsync_Success_ReturnsDisposableStream()
    {
        var (client, handler) = Create(HttpStatusCode.OK);

        using var stream = await client.SendStreamAsync(new HttpRequestMessage(HttpMethod.Get, "/api/stream"));
        stream.Should().NotBeNull();
        // 成功路径：流 Dispose 后底层响应应被释放（DisposableStream 持有 response）
        // 此处不强制断言 DisposeCount（成功路径响应归 DisposableStream 管理，Dispose 流时才释放）
    }

    private sealed class ThrowingResponseInterceptor : IHttpResponseInterceptor
    {
        public int Order => 0;
        public Task OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("interceptor boom");
    }
}

/// <summary>
/// HC-03：Error/Trace 日志路径必须脱敏 + 限量。
/// </summary>
public class M5LogSanitizationTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public int BeginScopeCount { get; private set; }
        private readonly LogLevel _minLevel;

        public RecordingLogger(LogLevel minLevel = LogLevel.Trace) => _minLevel = minLevel;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            BeginScopeCount++;
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class MaskingLogger : ILogger
    {
        public int MaskCallCount { get; private set; }
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public void CountMask() => MaskCallCount++;
    }

    private sealed class CountingMasker : ISensitiveDataMasker
    {
        public int CallCount { get; private set; }

        public string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2)
        {
            CallCount++;
            return value.Replace("13800138000", "***").Replace("secret-token-value", "***");
        }

        public string MaskObject(object obj)
        {
            CallCount++;
            return obj.ToString() ?? string.Empty;
        }
    }

    private static (DirectEnhancedHttpClient Client, StubHttp Stub) Create(ILogger logger, ISensitiveDataMasker? masker = null)
    {
        var stub = new StubHttp();
        var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions
        {
            Logger = logger,
            SensitiveDataMasker = masker,
        });
        return (client, stub);
    }

    [Fact]
    public async Task HC03_JsonDeserializeFailed_ErrorLogIsSanitizedAndLimited()
    {
        var logger = new RecordingLogger();
        var masker = new CountingMasker();
        var (client, stub) = Create(logger, masker);

        // 200 + 非法 JSON（截断）+ 敏感字段 —— 确保触发 JsonException
        var sensitiveBody = """{"access_token":"secret-token-value","phone":"13800138000","id_card":"110101199001011234","broken":""";
        stub.RespondToAnyRequest(HttpStatusCode.OK, sensitiveBody);

        var act = () => client.SendAsync<ValidDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));
        await act.Should().ThrowAsync<JsonException>();

        var errorEntries = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        errorEntries.Should().NotBeEmpty("反序列化失败应记录 Error 日志");
        masker.CallCount.Should().BeGreaterThan(0, "Error 日志路径应调用脱敏（HC-03）");

        foreach (var (_, message) in errorEntries)
        {
            message.Should().NotContain("secret-token-value", "Error 日志不得含原始 token（经 masker）");
            message.Should().NotContain("13800138000", "Error 日志不得含原始手机号（经 masker）");
        }
    }

    [Fact]
    public async Task HC03_JsonDeserializeFailed_ErrorLogLengthLimited()
    {
        var logger = new RecordingLogger();
        var (client, stub) = Create(logger);

        // 构造非法 JSON + 超长体
        var big = new StringBuilder();
        big.Append("{\"access_token\":\"tok");
        big.Append('x', 2000);
        big.Append("\",\"data\":\"1\""); // 缺右括号 → JsonException
        stub.RespondToAnyRequest(HttpStatusCode.OK, big.ToString());

        var act = () => client.SendAsync<ValidDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));
        await act.Should().ThrowAsync<JsonException>();

        var errorWithRaw = logger.Entries
            .Where(e => e.Level == LogLevel.Error && e.Message.Contains("原始响应"))
            .ToList();
        errorWithRaw.Should().NotBeEmpty();
        // 限量 500 + 截断标记开销（消息模板本身也有固定文案）
        foreach (var (_, message) in errorWithRaw)
        {
            message.Length.Should().BeLessThan(2000, "日志侧响应体窗口为 500 字符");
        }
    }

    [Fact]
    public async Task HC03_TraceDisabled_NoRawBodyLog()
    {
        // 仅启用 Warning —— Trace 原始体不应构造/输出
        var logger = new RecordingLogger(LogLevel.Warning);
        var (client, stub) = Create(logger);

        stub.RespondToAnyRequest(HttpStatusCode.OK, """{"value":42}""");
        var result = await client.SendAsync<ValidDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));

        result!.Value.Should().Be(42);
        logger.Entries.Should().NotContain(e => e.Message.Contains("原始JSON响应"),
            "未启用 Trace 时不应输出原始体日志");
    }

    [Fact]
    public async Task HC03_CustomMasker_IsCalled()
    {
        var masker = new CountingMasker();
        var logger = new RecordingLogger();
        var (client, stub) = Create(logger, masker);

        stub.RespondToAnyRequest(HttpStatusCode.OK, """{"access_token":"secret-token-value","bad":}""");

        var act = () => client.SendAsync<ValidDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));
        await act.Should().ThrowAsync<JsonException>();

        masker.CallCount.Should().BeGreaterThan(0, "自定义 ISensitiveDataMasker 应被调用（HC-03）");
    }

    [Fact]
    public async Task HC03_XmlDeserializeFailed_ErrorLogIsSanitized()
    {
        var logger = new RecordingLogger();
        var masker = new CountingMasker();
        var (client, stub) = Create(logger, masker);

        // 非法 XML（未闭合）触发 InvalidOperationException；自定义 masker 验证脱敏链路被调用
        var xml = """<root><access_token>secret-token-value</access_token><phone>13800138000</phone>""";
        stub.RespondToAnyRequest(HttpStatusCode.OK, xml, "application/xml");

        var act = () => client.SendXmlAsync<ValidDto>(new HttpRequestMessage(HttpMethod.Post, "/api/xml")
        {
            Content = new StringContent("<a/>", Encoding.UTF8, "application/xml"),
        });
        await act.Should().ThrowAsync<InvalidOperationException>();

        var errorEntries = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        errorEntries.Should().NotBeEmpty();
        masker.CallCount.Should().BeGreaterThan(0, "XML Error 日志路径应调用脱敏（HC-03）");
        foreach (var (_, message) in errorEntries)
        {
            message.Should().NotContain("secret-token-value");
            message.Should().NotContain("13800138000");
        }
    }

    private sealed class ValidDto
    {
        public int Value { get; set; }
    }
}

/// <summary>
/// HC-11：NullLogger&lt;T&gt; 注入时不应创建日志作用域。
/// </summary>
public class M5NullLoggerTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    private sealed class ScopeCountingLogger : ILogger
    {
        public int BeginScopeCount { get; private set; }
        public bool IsEnabled(LogLevel logLevel) => false; // 模拟 NullLogger 行为

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            BeginScopeCount++;
            return null;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    [Fact]
    public async Task HC11_NullLoggerGeneric_NoScopeCreated()
    {
        var stub = new StubHttp();
        stub.RespondToAnyRequest(HttpStatusCode.OK, """{"value":1}""");
        var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };

        // NullLogger<T> 与 NullLogger.Instance 是不同实例 —— 原引用比较会误判为「已启用」
        var client = new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions
        {
            Logger = NullLogger<DirectEnhancedHttpClient>.Instance,
        });

        var result = await client.SendAsync<Dto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));
        result!.Value.Should().Be(1);
        // 修复后：IsEnabled(Critical)==false → _enableLogging=false → 不创建 scope
        // 此处通过「请求成功 + 无异常」间接锁定；BeginScope 计数需可注入 logger，见下一用例
    }

    [Fact]
    public async Task HC11_LoggerWithNoLevelsEnabled_NoScopeCreated()
    {
        var counting = new ScopeCountingLogger();
        var stub = new StubHttp();
        stub.RespondToAnyRequest(HttpStatusCode.OK, """{"value":2}""");
        var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions
        {
            Logger = counting,
        });

        var result = await client.SendAsync<Dto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));
        result!.Value.Should().Be(2);
        counting.BeginScopeCount.Should().Be(0,
            "无任何级别启用的 logger 不应创建 scope（HC-11）");
    }

    private sealed class Dto
    {
        public int Value { get; set; }
    }
}
