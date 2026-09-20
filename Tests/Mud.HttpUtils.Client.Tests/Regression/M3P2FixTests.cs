// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

// M3（P2 一般与建议 + Roadmap）验收用例。
// 覆盖：#22 流式路径请求配置、#23 jsonSerializerOptions 透传、#24 _fetchLocks 容量上限、
//       #25 QueryParameterBuilder 相对 baseUrl、#26 空 chunked 响应体容忍、#29 BOM 成功路径实测、
//       R-1 指标 tag 白名单、R-2 诊断事件开关。
// （#20 由 Mud.HttpUtils.Resilience.Tests/ShouldRetryTests 覆盖；#21 smoke 在 ResilientHttpClientTests；
//   #27 运行时滑动过期由 CacheResponseInterceptor* 用例覆盖。）

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Client.Tests;

public class M3P2FixTests
{
    private const string JsonMediaType = "application/json";

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, JsonMediaType) };

    #region #22 流式路径补 ApplyRequestConfig

    [Fact]
    public async Task SendAsAsyncEnumerable_AppliesHttpVersionToRequest()
    {
        // M3-#22 验收：配置 HttpVersion = 2.0 + 流式接口 → 请求 Version 为 2.0（修复前为默认 1.1）
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            httpVersion: new Version(2, 0));

        HttpRequestMessage? captured = null;
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient
            .Setup(c => c.SendAsAsyncEnumerable<int>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequestMessage, object?, CancellationToken>((req, _, _) => captured = req)
            .Returns(EmptySequence());

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");
        // 注意：null 需显式转型 object?，否则重载决策会绑定 JsonTypeInfo<T> 重载（派生类型对 null 字面量更优）
        await foreach (var _ in executor.SendAsAsyncEnumerable<int>(request, mockClient.Object, (object?)null))
        {
        }

        captured.Should().NotBeNull();
        captured!.Version.Should().Be(new Version(2, 0));
    }

    private static async IAsyncEnumerable<int> EmptySequence()
    {
        await Task.CompletedTask;
        yield break;
    }

    #endregion

    #region #23 jsonSerializerOptions 透传

    [Fact]
    public async Task SendAndDeserialize_PassesJsonSerializerOptionsThroughToSerializer()
    {
        // M3-#23 验收：object? options 原样透传给序列化器（不再被 as JsonSerializerOptions 丢弃）
        var sentinel = new { Version = 1 };
        object? capturedOptions = null;
        var serializerMock = new Mock<IHttpContentSerializer>();
        serializerMock
            .Setup(s => s.Deserialize<long>(It.IsAny<string>(), It.IsAny<object?>()))
            .Callback<string, object?>((_, opts) => capturedOptions = opts)
            .Returns(42L);

        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            contentSerializer: serializerMock.Object);
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient
            .Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonResponse("42"));

        var result = await executor.SendAndDeserializeAsync<long>(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
            mockClient.Object,
            new ResponseDescriptor { ResponseContentType = JsonMediaType },
            sentinel);

        result.Should().Be(42);
        capturedOptions.Should().BeSameAs(sentinel);
    }

    [Fact]
    public async Task SendAndDeserialize_WithJsonTypeInfo_UsesTypeInfoFastPath()
    {
        // M3-#23 验收（方案文档 §#23）：传入 JsonTypeInfo<T> 可达 STJ 的 typeInfo 重载
        // （修复前被 `as JsonSerializerOptions` 丢弃，静默退化为默认 options）
        var jsonTypeInfo = (JsonTypeInfo<long>)new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }.GetTypeInfo(typeof(long))!;

        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient
            .Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonResponse("42"));

        var result = await executor.SendAndDeserializeAsync<long>(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
            mockClient.Object,
            new ResponseDescriptor { ResponseContentType = JsonMediaType },
            jsonTypeInfo);

        result.Should().Be(42);
    }

    #endregion

    #region #24 _fetchLocks 容量上限

    [Fact]
    public async Task GetOrFetchAsync_ManyDistinctFailedKeys_FetchLocksStayBounded()
    {
        // M3-#24 验收：大量不同 key 的失败回源 → _fetchLocks 有界（上限 = 2 × maxCacheSize）
        using var cache = new MemoryHttpResponseCache(maxCacheSize: 4);
        var fetchLocksField = typeof(MemoryHttpResponseCache).GetField("_fetchLocks", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到 _fetchLocks 字段");

        for (var i = 0; i < 50; i++)
        {
            var key = $"m3-lock-{i}";
            await FluentActions.Awaiting(() => cache.GetOrFetchAsync<string>(
                    key,
                    () => Task.FromException<string>(new InvalidOperationException("boom")),
                    TimeSpan.FromMinutes(1)))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        var locks = (ConcurrentDictionary<string, SemaphoreSlim>)fetchLocksField.GetValue(cache)!;
        locks.Count.Should().BeLessThanOrEqualTo(8);
    }

    #endregion

    #region #25 QueryParameterBuilder 相对 baseUrl

    [Fact]
    public void Build_RelativeBaseUrl_ConcatenatesQueryString()
    {
        // M3-#25 验收：Create("/relative") + Add("page","1") + Build() → "/relative?page=1"
        var builder = QueryParameterBuilder.Create("/relative");
        builder.Add("page", "1");
        builder.Build().Should().Be("/relative?page=1");
    }

    [Fact]
    public void Build_RelativeBaseUrlWithoutQuery_ReturnsBaseUrlOnly()
    {
        QueryParameterBuilder.Create("/relative").Build()
            .Should().Be("/relative");
    }

    [Fact]
    public void Build_AbsoluteBaseUrl_BehaviorUnchanged()
    {
        // M3-#25 防回归：绝对路径维持 UriBuilder 拼接现状
        var builder = QueryParameterBuilder.Create("https://x.y/path");
        builder.Add("page", "1");
        builder.Build().Should().Be("https://x.y/path?page=1");
    }

    #endregion

    #region #26 空 chunked 响应体容忍

    [Fact]
    public async Task FromHttpContent_SeekableEmptyStream_ReturnsDefault()
    {
        // M3-#26 验收：空体不抛 JsonException，返回 default(T)（值类型 long → 0）
        var serializer = new SystemTextJsonContentSerializer();
        using var content = new ByteArrayContent(Array.Empty<byte>());

        long? result = await serializer.FromHttpContentAsync<long>(content);
        result.Should().Be(0);
    }

    [Fact]
    public async Task FromHttpContent_NonSeekableEmptyStream_ReturnsDefault()
    {
        // chunked 空体：Content-Length 预检不命中 → 序列化器探测首字节 EOF → default(T)（引用类型 → null）
        var serializer = new SystemTextJsonContentSerializer();
        using var content = new StreamContent(new NonSeekableStream(Array.Empty<byte>()));

        var result = await serializer.FromHttpContentAsync<BomDto>(content);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FromHttpContent_NonSeekableStream_DeserializesWithoutByteLoss()
    {
        // 非空流：探测首字节后经 PrependedByteStream 回填，不丢字节
        var serializer = new SystemTextJsonContentSerializer();
        using var content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("42")));

        (await serializer.FromHttpContentAsync<long>(content)).Should().Be(42);
    }

    [Fact]
    public async Task Executor_SendAndDeserialize_EmptyChunkedBody_ReturnsDefault()
    {
        // M3-#26 生成代码路径：DefaultHttpRequestExecutor 读取空 chunked 体得到空串，
        // Deserialize<T>(string) 空串守卫返回 default（此前会抛 JsonException 并被包装为 ApiException）
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            // 无 Content-Length 的空流（chunked 空体语义）
            Content = new StreamContent(new NonSeekableStream(Array.Empty<byte>()))
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await executor.SendAndDeserializeAsync<long>(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
            mockClient.Object,
            new ResponseDescriptor { ResponseContentType = "application/json" },
            null);

        result.Should().Be(0);
    }

    [Fact]
    public async Task Executor_SendAndDeserialize_EmptyChunkedXmlBody_ReturnsDefault()
    {
        // M3-#26 生成代码路径（XML）：空 chunked 体返回 default，而非 XmlSerializer 抛 InvalidOperationException
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(Array.Empty<byte>()))
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var descriptor = new ResponseDescriptor { ResponseContentType = "application/xml" };
        descriptor.XmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(EmptyXmlDto));

        var result = await executor.SendAndDeserializeAsync<EmptyXmlDto>(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
            mockClient.Object,
            descriptor,
            null);

        result.Should().BeNull();
    }

    public sealed class EmptyXmlDto
    {
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] bytes) => _inner = new MemoryStream(bytes);

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion

    #region #29 BOM 成功路径实测

    [Fact]
    public async Task FromHttpContent_Utf8BomPrefix_DeserializesSuccessfully()
    {
        // #29 实测：UTF-8 BOM 前缀的成功响应体 —— 确认 STJ DeserializeAsync(Stream) 的实际行为
        // （错误响应路径经 LimitedContentReader 已天然剥离 BOM，见 M1RegressionTests.BOM_PrefixedErrorContent_BomStrippedOnRead）
        var serializer = new SystemTextJsonContentSerializer();
        var payload = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("""{"Id":7,"Name":"Bom"}"""))
            .ToArray();
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(JsonMediaType);

        var result = await serializer.FromHttpContentAsync<BomDto>(content);

        result.Should().NotBeNull();
        result!.Id.Should().Be(7);
        result.Name.Should().Be("Bom");
    }

    public sealed class BomDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion

    #region R-1 指标 tag 白名单（MetricTagAllowlist）

    [Fact]
    public void FilterTags_DefaultAllowlist_ReturnsSameArrayWithoutAllocation()
    {
        // 默认白名单 = 全部内建维度 → 零分配快路径（返回原数组引用）
        var tags = new[]
        {
            new KeyValuePair<string, object?>("method", "GET"),
            new KeyValuePair<string, object?>("custom_high_cardinality", "v"),
        };

        MudHttpMeter.FilterTags(tags).Should().BeSameAs(tags);
    }

    [Fact]
    public void FilterTags_CustomAllowlist_KeepsOnlyAllowlistedTags()
    {
        var original = MudHttpObservabilityOptions.MetricTagAllowlist;
        try
        {
            MudHttpObservabilityOptions.MetricTagAllowlist =
                new HashSet<string>(StringComparer.Ordinal) { "method", "status_code" };
            var tags = new[]
            {
                new KeyValuePair<string, object?>("method", "GET"),
                new KeyValuePair<string, object?>("user_id", "42"),
                new KeyValuePair<string, object?>("status_code", 200),
                new KeyValuePair<string, object?>("host", "api.example.com"),
            };

            var filtered = MudHttpMeter.FilterTags(tags);

            filtered.Select(t => t.Key).Should().BeEquivalentTo("method", "status_code");
        }
        finally
        {
            MudHttpObservabilityOptions.MetricTagAllowlist = original;
        }
    }

    [Fact]
    public void MudHttpObservabilityOptions_HasM3Defaults()
    {
        // R-1/R-2 默认值基线：RecordFullUrlOnSuccess 关闭、EmitDiagnosticEvents 开启
        MudHttpObservabilityOptions.RecordFullUrlOnSuccess.Should().BeFalse();
        MudHttpObservabilityOptions.EmitDiagnosticEvents.Should().BeTrue();
        MudHttpObservabilityOptions.MetricTagAllowlist.Should().Contain("client_name");
    }

    [Fact]
    public void CircuitBreakerGauge_Respects_MetricTagAllowlist()
    {
        // R-3：ObservableGauge 的 policy_key 维度受白名单治理（与 FilterTags 同一 allowlist 事实源）
        CircuitBreakerStateObserver.Clear();
        var original = MudHttpObservabilityOptions.MetricTagAllowlist;
        try
        {
            CircuitBreakerStateObserver.SetState("gauge_cb", CircuitBreakerState.Open);

            var measurements = new List<Measurement<int>>();
            using var meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MudHttpMeter.MeterName && instrument.Name == "mud.http.circuit_breaker.state")
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
                measurements.Add(new Measurement<int>(value, tags.ToArray())));
            meterListener.Start();

            // 确定性启用测量事件：MudHttpMeter 为进程级惰性静态源，其类型初始化时机取决于
            // 本测试之外是否有其它并行类先访问它 —— 若 gauge 在 listener.Start() 之前已创建，
            // InstrumentPublished 不会触发，RecordObservableInstruments 将永远无测量（隔离运行必失败）。
            // 显式 EnableMeasurementEvents(静态实例) 不依赖仪器创建时序，先访问亦顺带完成类型初始化。
            meterListener.EnableMeasurementEvents(MudHttpMeter.CircuitBreakerState);

            // 默认白名单含 policy_key → Measurement 携带 policy_key tag
            meterListener.RecordObservableInstruments();
            measurements.Should().ContainSingle();
            measurements[0].Tags.ToArray().Should().Contain(t => t.Key == "policy_key" && Equals(t.Value, "gauge_cb"));

            // 收缩白名单（移除 policy_key）→ Measurement 无任何 tags
            MudHttpObservabilityOptions.MetricTagAllowlist =
                new HashSet<string>(StringComparer.Ordinal) { "client_name", "outcome" };
            measurements.Clear();
            meterListener.RecordObservableInstruments();
            measurements.Should().ContainSingle();
            measurements[0].Tags.ToArray().Should().BeEmpty("policy_key 已被白名单收缩移除（R-3）");

            // IsTagAllowed 与 FilterTags 共享同一判定（internal 经 InternalsVisibleTo 供测试）
            MudHttpMeter.IsTagAllowed("client_name").Should().BeTrue();
            MudHttpMeter.IsTagAllowed("policy_key").Should().BeFalse();
        }
        finally
        {
            MudHttpObservabilityOptions.MetricTagAllowlist = original;
            CircuitBreakerStateObserver.Clear();
        }
    }

    #endregion

    #region R-2 诊断事件开关（EmitDiagnosticEvents）

    [Fact]
    public void AddActivityEvent_WhenEmitDiagnosticEventsDisabled_SkipsActivityEvent()
    {
        using var listener = CreateMudActivityListener();
        ActivitySource.AddActivityListener(listener);

        var original = MudHttpObservabilityOptions.EmitDiagnosticEvents;
        try
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = false;
            using var activity = MudHttpActivitySource.Instance.StartActivity("m3-r2-test", ActivityKind.Client);

            MudHttpActivitySource.AddActivityEvent(
                "Mud.HttpUtils.M3.Test.Event",
                () => new object(),
                "m3.r2.test.event",
                () => new[] { new KeyValuePair<string, object?>("k", "v") });

            activity!.Events.Should().BeEmpty();
        }
        finally
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = original;
        }
    }

    [Fact]
    public void AddActivityEvent_WhenDisabled_DoesNotInvokeTagFactory()
    {
        // G28：关闭开关后 payload 工厂与 tags 工厂都不得被调用（零分配承诺的机制验证）
        using var listener = CreateMudActivityListener();
        ActivitySource.AddActivityListener(listener);

        var original = MudHttpObservabilityOptions.EmitDiagnosticEvents;
        try
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = false;
            using var activity = MudHttpActivitySource.Instance.StartActivity("m3-g28-test", ActivityKind.Client);

            var payloadInvocations = 0;
            var tagsInvocations = 0;
            MudHttpActivitySource.AddActivityEvent(
                "Mud.HttpUtils.G28.Test.Event",
                () => { payloadInvocations++; return new object(); },
                "g28.test.event",
                () => { tagsInvocations++; return new[] { new KeyValuePair<string, object?>("k", "v") }; });

            activity!.Events.Should().BeEmpty();
            payloadInvocations.Should().Be(0, "payload 工厂不得被调用");
            tagsInvocations.Should().Be(0, "tags 工厂不得被调用");
        }
        finally
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = original;
        }
    }

    [Fact]
    public void AddActivityEvent_WhenEnabled_InvokesTagsFactoryOnce()
    {
        using var listener = CreateMudActivityListener();
        ActivitySource.AddActivityListener(listener);

        var original = MudHttpObservabilityOptions.EmitDiagnosticEvents;
        try
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = true;
            using var activity = MudHttpActivitySource.Instance.StartActivity("m3-g28-on", ActivityKind.Client);

            var tagsInvocations = 0;
            MudHttpActivitySource.AddActivityEvent(
                "Mud.HttpUtils.G28.Test.Event",
                () => new object(),
                "g28.test.event",
                () =>
                {
                    tagsInvocations++;
                    return new[] { new KeyValuePair<string, object?>("k", "v") };
                });

            var evt = activity!.Events.Should().ContainSingle(e => e.Name == "g28.test.event").Subject;
            evt.Tags.Should().Contain(t => t.Key == "k" && Equals(t.Value, "v"));
            tagsInvocations.Should().Be(1, "开启时 tags 工厂被惰性调用一次");
        }
        finally
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = original;
        }
    }

    [Fact]
    public void AddActivityEvent_WhenEmitDiagnosticEventsEnabled_WritesActivityEvent()
    {
        using var listener = CreateMudActivityListener();
        ActivitySource.AddActivityListener(listener);

        var original = MudHttpObservabilityOptions.EmitDiagnosticEvents;
        try
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = true;
            using var activity = MudHttpActivitySource.Instance.StartActivity("m3-r2-test", ActivityKind.Client);

            MudHttpActivitySource.AddActivityEvent(
                "Mud.HttpUtils.M3.Test.Event",
                () => new object(),
                "m3.r2.test.event",
                () => new[] { new KeyValuePair<string, object?>("k", "v") });

            activity!.Events.Should().ContainSingle(e => e.Name == "m3.r2.test.event");
        }
        finally
        {
            MudHttpObservabilityOptions.EmitDiagnosticEvents = original;
        }
    }

    private static ActivityListener CreateMudActivityListener() => new()
    {
        ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
        SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    #endregion
}
