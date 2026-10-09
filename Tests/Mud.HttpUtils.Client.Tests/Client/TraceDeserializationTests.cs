// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// M7-HC-01 回归：Trace 日志分支必须读全量响应体做业务反序列化（日志级别不得改变业务行为）

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Testing;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M7-HC-01：Trace 路径反序列化与日志口径 ——
/// 截断仅作用于日志输出，业务反序列化必须消费「截断缓冲 + 原始流剩余」的全量响应体。
/// </summary>
public class M7TraceDeserializationTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    /// <summary>按最小级别收集日志（默认 Trace 全量收集）。</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        private readonly LogLevel _minLevel;

        public RecordingLogger(LogLevel minLevel = LogLevel.Trace) => _minLevel = minLevel;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static (DirectEnhancedHttpClient Client, StubHttp Stub) Create(ILogger logger)
    {
        var stub = new StubHttp();
        var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions
        {
            Logger = logger,
        });
        return (client, stub);
    }

    /// <summary>构造带超长字段的合法 JSON（paddingLength 决定总长，用于跨越 32KB 截断边界）。</summary>
    private static string BuildLargeJson(int paddingLength)
        => $"{{\"value\":42,\"padding\":\"{new string('x', paddingLength)}\"}}";

    private sealed class PayloadDto
    {
        public int Value { get; set; }
        public string? Padding { get; set; }
    }

    /// <summary>T1：Trace 开启 + &gt;32KB 合法 JSON —— 修复前反序列化读到截断体必抛 JsonException（红测转绿）。</summary>
    [Fact]
    public async Task Trace_LargeResponse_DeserializesFullBody()
    {
        var logger = new RecordingLogger();
        var (client, stub) = Create(logger);
        stub.RespondToAnyRequest(HttpStatusCode.OK, BuildLargeJson(40000));

        var result = await client.SendAsync<PayloadDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));

        result.Should().NotBeNull();
        result!.Value.Should().Be(42, "日志级别不得改变业务行为（HC-01）");
        result.Padding!.Length.Should().Be(40000, "反序列化必须读到全量响应体，而非 32KB 截断体");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Trace && e.Message.Contains("原始JSON响应"),
            "Trace 原始体日志照常输出（日志口径不变）");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "全量合法 JSON 在 Trace 开启时不得产生 Error 日志");
    }

    /// <summary>T2a：≤32KB + Trace —— 单缓冲读尽路径回归。</summary>
    [Fact]
    public async Task Trace_SmallResponse_SucceedsAndLogsRawBody()
    {
        var logger = new RecordingLogger();
        var (client, stub) = Create(logger);
        stub.RespondToAnyRequest(HttpStatusCode.OK, """{"value":7,"padding":"tiny"}""");

        var result = await client.SendAsync<PayloadDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));

        result!.Value.Should().Be(7);
        result.Padding.Should().Be("tiny");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Trace && e.Message.Contains("原始JSON响应"));
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    /// <summary>T2b：关闭 Trace —— 走非 Trace 分支，行为回归（成功且无原始体日志）。</summary>
    [Fact]
    public async Task TraceDisabled_Response_SucceedsWithoutRawBodyLog()
    {
        var logger = new RecordingLogger(LogLevel.Warning);
        var (client, stub) = Create(logger);
        stub.RespondToAnyRequest(HttpStatusCode.OK, BuildLargeJson(40000));

        var result = await client.SendAsync<PayloadDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));

        result!.Value.Should().Be(42);
        result.Padding!.Length.Should().Be(40000);
        logger.Entries.Should().NotContain(e => e.Message.Contains("原始JSON响应"),
            "未启用 Trace 时不应输出原始体日志");
    }

    /// <summary>T3：非法 JSON（&gt;32KB）+ Trace —— 异常仍为 JsonException 且带原始内层，
    /// Error 日志保持脱敏限量（LogBodyMaxLength=500）口径，不因本修复改变。</summary>
    [Fact]
    public async Task Trace_InvalidJson_JsonExceptionWithLimitedSanitizedErrorLog()
    {
        var logger = new RecordingLogger();
        var (client, stub) = Create(logger);

        // 超过 32KB 的非法 JSON：触发截断日志 + 反序列化失败双路径
        var broken = "{\"access_token\":\"tok" + new string('x', 40000) + "\",\"broken\":";
        stub.RespondToAnyRequest(HttpStatusCode.OK, broken);

        var act = () => client.SendAsync<PayloadDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));
        var ex = await act.Should().ThrowAsync<JsonException>();

        ex.Which.InnerException.Should().BeOfType<JsonException>("外层异常必须保留原始 JsonException 内层");

        var errors = logger.Entries
            .Where(e => e.Level == LogLevel.Error && e.Message.Contains("原始响应"))
            .ToList();
        errors.Should().NotBeEmpty("反序列化失败应记录带原始体的 Error 日志");
        foreach (var (_, message) in errors)
        {
            message.Length.Should().BeLessThan(2000, "日志侧响应体窗口为 500 字符（HC-03 口径不变）");
        }
    }

    /// <summary>T1 补充：chunked（无 Content-Length）+ Trace + 大响应 —— 组合流对不可 seek 流同样成立。</summary>
    [Fact]
    public async Task Trace_LargeChunkedResponse_DeserializesFullBody()
    {
        var logger = new RecordingLogger();
        var (client, stub) = Create(logger);
        var payload = BuildLargeJson(40000);
        stub.RespondToAnyRequest(HttpStatusCode.OK, content: null)
            .WithStreamContent(() => new MemoryStream(Encoding.UTF8.GetBytes(payload)));

        var result = await client.SendAsync<PayloadDto>(new HttpRequestMessage(HttpMethod.Get, "/api/data"));

        result!.Value.Should().Be(42);
        result.Padding!.Length.Should().Be(40000, "chunked 无 Content-Length 时组合流同样读全量");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }
}
