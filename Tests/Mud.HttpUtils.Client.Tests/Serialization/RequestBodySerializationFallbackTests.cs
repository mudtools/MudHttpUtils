// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// T18（CFG-39 / 不变量 I-15）：<c>RequestBodySerialization</c> fast-path 条件未满足时的可观测性。
/// </summary>
/// <remarks>
/// <para>
/// 修复前：<see cref="RequestBodySerializationMode.Buffered"/>/<c>Streamed</c> 已配置，但注入的
/// <see cref="IHttpContentSerializer"/> 未实现 <see cref="ISynchronousContentSerializer"/> 时，
/// 代码<b>静默</b>回退默认路径（无任何日志），用户无法得知 fast-path 未生效。
/// </para>
/// <para>
/// 修复后：回退分支记录一次 <c>Debug</c>（EventId 166）。之所以「只记一次」，
/// 是因为回退条件（配置 + 序列化器类型）在整个客户端实例生命周期内恒定，每请求记录会刷屏。
/// </para>
/// </remarks>
[Collection("UrlValidator Collection")]
public class RequestBodySerializationFallbackTests : IDisposable
{
    private sealed record RequestDto(int Value);

    private sealed record ResponseDto(int Value);

    /// <summary>固定响应的 <see cref="HttpMessageHandler"/>（不发起真实网络请求）。</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    /// <summary>测试用 <see cref="EnhancedHttpClient"/> 派生类，用于注入自定义序列化器。</summary>
    private sealed class TestEnhancedClient(
        HttpClient httpClient,
        EnhancedHttpClientOptions options,
        IHttpContentSerializer serializer)
        : EnhancedHttpClient(httpClient, options, jsonOptions: null, contentSerializer: serializer);

    public RequestBodySerializationFallbackTests()
    {
        UrlValidator.ConfigureAllowedDomains(["api.example.com"]);
    }

    public void Dispose()
    {
        UrlValidator.ConfigureAllowedDomains(Array.Empty<string>());
    }

    private static (TestEnhancedClient Client, Mock<IHttpContentSerializer> Serializer) CreateClient(
        CollectingLoggerProvider loggerProvider,
        RequestBodySerializationMode mode)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder
            .AddProvider(loggerProvider)
            .SetMinimumLevel(LogLevel.Trace));

        var serializerMock = new Mock<IHttpContentSerializer>();
        serializerMock
            .Setup(s => s.ToHttpContent(It.IsAny<RequestDto>(), It.IsAny<object?>()))
            .Returns(new StringContent("{\"value\":1}", Encoding.UTF8, "application/json"));
        serializerMock
            .Setup(s => s.FromHttpContentAsync<ResponseDto>(
                It.IsAny<HttpContent>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponseDto(1));

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":1}", Encoding.UTF8, "application/json"),
        });

        var options = new EnhancedHttpClientOptions
        {
            Logger = loggerFactory.CreateLogger<RequestBodySerializationFallbackTests>(),
            RequestBodySerialization = mode,
        };

        var client = new TestEnhancedClient(new HttpClient(handler), options, serializerMock.Object);
        return (client, serializerMock);
    }

    [Fact]
    public async Task T18_FallbackToDefaultPath_LogsDebugOnce()
    {
        var loggerProvider = new CollectingLoggerProvider();
        var (client, _) = CreateClient(loggerProvider, RequestBodySerializationMode.Buffered);

        // Act — 连续两次请求；回退是恒定事实，只应记录一次
        await client.PatchAsJsonAsync<RequestDto, ResponseDto>("https://api.example.com/x", new RequestDto(1));
        await client.PatchAsJsonAsync<RequestDto, ResponseDto>("https://api.example.com/x", new RequestDto(2));

        // Assert — 恰好一条包含回退说明的 Debug 日志（单次门控生效，未刷屏）
        var fallbackLogs = loggerProvider.GetLogRecords(LogLevel.Debug)
            .Where(r => r.Message.Contains("RequestBodySerialization", StringComparison.Ordinal))
            .ToList();

        fallbackLogs.Should().HaveCount(1, "回退日志必须记录且仅记录一次（I-15 单次门控）");
        fallbackLogs[0].Message.Should().Contain("Buffered");
        fallbackLogs[0].Message.Should().Contain("ISynchronousContentSerializer");
    }

    [Fact]
    public async Task FastPathSerializer_DoesNotLogFallback()
    {
        // 反例：默认序列化器（SystemTextJsonContentSerializer）实现了 ISynchronousContentSerializer ⇒ 不回退、不记日志
        var loggerProvider = new CollectingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder => builder
            .AddProvider(loggerProvider)
            .SetMinimumLevel(LogLevel.Trace));

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":1}", Encoding.UTF8, "application/json"),
        });

        var options = new EnhancedHttpClientOptions
        {
            Logger = loggerFactory.CreateLogger<RequestBodySerializationFallbackTests>(),
            RequestBodySerialization = RequestBodySerializationMode.Buffered,
        };

        var client = new TestEnhancedClient(new HttpClient(handler), options, new SystemTextJsonContentSerializer());

        await client.PatchAsJsonAsync<RequestDto, ResponseDto>("https://api.example.com/x", new RequestDto(1));

        loggerProvider.GetLogRecords(LogLevel.Debug)
            .Where(r => r.Message.Contains("RequestBodySerialization", StringComparison.Ordinal))
            .Should().BeEmpty("fast-path 已生效时不得记录回退日志");
    }

    [Fact]
    public async Task DefaultMode_DoesNotLogFallback()
    {
        // 反例：未配置 Buffered/Streamed（默认模式）时走既有路径，与 fast-path 无关，不应记日志
        var loggerProvider = new CollectingLoggerProvider();
        var (client, _) = CreateClient(loggerProvider, RequestBodySerializationMode.Default);

        await client.PatchAsJsonAsync<RequestDto, ResponseDto>("https://api.example.com/x", new RequestDto(1));

        loggerProvider.GetLogRecords(LogLevel.Debug)
            .Where(r => r.Message.Contains("RequestBodySerialization", StringComparison.Ordinal))
            .Should().BeEmpty();
    }
}
