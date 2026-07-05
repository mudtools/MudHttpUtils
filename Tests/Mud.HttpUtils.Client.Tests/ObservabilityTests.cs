// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 可观测性单元测试：验证 ActivitySource / Meter / CircuitBreakerStateObserver /
/// MudHttpObservability / TracingDelegatingHandler 在订阅与未订阅场景下的行为。
/// </summary>
public class ObservabilityTests
{
    // ============ MudHttpActivitySource ============

    [Fact]
    public void ActivitySource_Name_And_Version_Should_Be_Const()
    {
        MudHttpActivitySource.Name.Should().Be("Mud.HttpUtils.HttpClient");
        MudHttpActivitySource.Version.Should().Be("2.0.0");
        MudHttpActivitySource.ActivityNameRequest.Should().Be("Mud.HttpUtils.HttpClient.Request");
        MudHttpActivitySource.ActivityNameTokenRecovery.Should().Be("Mud.HttpUtils.Token.Recovery");
    }

    [Fact]
    public void ActivitySource_Instance_NotNull()
    {
        MudHttpActivitySource.Instance.Should().NotBeNull();
        MudHttpActivitySource.Instance.Name.Should().Be(MudHttpActivitySource.Name);
        MudHttpActivitySource.Instance.Version.Should().Be(MudHttpActivitySource.Version);
    }

    [Fact]
    public void ActivitySource_HasListeners_Reflects_Listener_Presence()
    {
        MudHttpActivitySource.Instance.HasListeners().Should().BeFalse();

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        MudHttpActivitySource.Instance.HasListeners().Should().BeTrue();

        listener.Dispose();
        MudHttpActivitySource.Instance.HasListeners().Should().BeFalse();
    }

    [Fact]
    public void ActivitySource_StartActivity_WithoutListener_ReturnsNull()
    {
        // 确保没有监听器
        MudHttpActivitySource.Instance.HasListeners().Should().BeFalse();

        var activity = MudHttpActivitySource.Instance.StartActivity("test", ActivityKind.Client);
        activity.Should().BeNull();
    }

    [Fact]
    public void ActivitySource_StartActivity_WithListener_ReturnsActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = MudHttpActivitySource.Instance.StartActivity("test-activity", ActivityKind.Client);
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("test-activity");
        activity.Kind.Should().Be(ActivityKind.Client);
    }

    [Fact]
    public void ActivitySource_Tags_Constants_Match_Otel_Semantic_Conventions()
    {
        MudHttpActivitySource.Tags.HttpMethod.Should().Be("http.method");
        MudHttpActivitySource.Tags.HttpUrl.Should().Be("http.url");
        MudHttpActivitySource.Tags.HttpScheme.Should().Be("http.scheme");
        MudHttpActivitySource.Tags.HttpHost.Should().Be("http.host");
        MudHttpActivitySource.Tags.HttpStatusCode.Should().Be("http.status_code");
        MudHttpActivitySource.Tags.MudClientName.Should().Be("mud.http.client_name");
    }

    // ============ MudHttpMeter ============

    [Fact]
    public void Meter_Name_And_Version_Should_Be_Const()
    {
        MudHttpMeter.MeterName.Should().Be("Mud.HttpUtils.HttpClient");
        MudHttpMeter.Version.Should().Be("2.0.0");
    }

    [Fact]
    public void Meter_Instance_NotNull()
    {
        MudHttpMeter.Instance.Should().NotBeNull();
        MudHttpMeter.Instance.Name.Should().Be(MudHttpMeter.MeterName);
        MudHttpMeter.Instance.Version.Should().Be(MudHttpMeter.Version);
    }

    [Fact]
    public void Meter_Instruments_Are_Created_With_Expected_Names()
    {
        MudHttpMeter.RequestCounter.Name.Should().Be("mud.http.requests");
        MudHttpMeter.RequestCounter.Unit.Should().Be("{request}");
        MudHttpMeter.RequestDuration.Name.Should().Be("mud.http.request.duration");
        MudHttpMeter.RequestDuration.Unit.Should().Be("ms");
        MudHttpMeter.CacheCounter.Name.Should().Be("mud.http.cache");
        MudHttpMeter.TokenRefreshCounter.Name.Should().Be("mud.token.refresh");
        MudHttpMeter.TokenRefreshDuration.Name.Should().Be("mud.token.refresh.duration");
        MudHttpMeter.RetryCounter.Name.Should().Be("mud.http.retry");
        MudHttpMeter.CircuitBreakerState.Name.Should().Be("mud.http.circuit_breaker.state");
    }

    [Fact]
    public void Meter_RequestCounter_Add_Captured_By_MeterListener()
    {
        var capturedInstruments = new List<Instrument>();
        var capturedValues = new List<long>();
        var capturedTags = new List<KeyValuePair<string, object?>[]>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            capturedInstruments.Add(instrument);
            capturedValues.Add(value);
            capturedTags.Add(tags.ToArray());
        });
        meterListener.Start();

        MudHttpMeter.RequestCounter.Add(1,
            new KeyValuePair<string, object?>("client_name", "test"),
            new KeyValuePair<string, object?>("outcome", "success"));

        // MeterListener 的事件回调是同步调用的
        capturedInstruments.Should().ContainSingle();
        capturedInstruments[0].Name.Should().Be("mud.http.requests");
        capturedValues.Should().ContainSingle().Which.Should().Be(1);
        var tagList = capturedTags[0];
        tagList.Should().Contain(t => t.Key == "client_name" && (string?)t.Value == "test");
        tagList.Should().Contain(t => t.Key == "outcome" && (string?)t.Value == "success");
    }

    [Fact]
    public void Meter_RequestDuration_Record_Captured_By_MeterListener()
    {
        var measurements = new List<(Instrument Instrument, double Value)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            measurements.Add((instrument, value));
        });
        meterListener.Start();

        MudHttpMeter.RequestDuration.Record(123.45);

        measurements.Should().ContainSingle();
        measurements[0].Instrument.Name.Should().Be("mud.http.request.duration");
        measurements[0].Value.Should().Be(123.45);
    }

    [Fact]
    public void Meter_RetryCounter_Add_Captured_By_MeterListener()
    {
        var values = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.retry")
                values.Add(value);
        });
        meterListener.Start();

        MudHttpMeter.RetryCounter.Add(1,
            new KeyValuePair<string, object?>("policy_key", "global_retry"),
            new KeyValuePair<string, object?>("outcome", "retry"));
        MudHttpMeter.RetryCounter.Add(1,
            new KeyValuePair<string, object?>("policy_key", "global_retry"),
            new KeyValuePair<string, object?>("outcome", "timeout"));

        values.Should().HaveCount(2);
        values.Should().AllBeEquivalentTo(1);
    }

    [Fact]
    public void Meter_CacheCounter_Add_Captured_By_MeterListener()
    {
        var values = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.cache")
                values.Add(value);
        });
        meterListener.Start();

        MudHttpMeter.CacheCounter.Add(1,
            new KeyValuePair<string, object?>("outcome", "hit"));
        MudHttpMeter.CacheCounter.Add(1,
            new KeyValuePair<string, object?>("outcome", "miss"));

        values.Should().HaveCount(2);
    }

    [Fact]
    public void Meter_TokenRefreshCounter_Add_Captured_By_MeterListener()
    {
        var values = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.token.refresh")
                values.Add(value);
        });
        meterListener.Start();

        MudHttpMeter.TokenRefreshCounter.Add(1,
            new KeyValuePair<string, object?>("token_manager_key", "oauth"),
            new KeyValuePair<string, object?>("outcome", "success"));

        values.Should().ContainSingle().Which.Should().Be(1);
    }

    // ============ CircuitBreakerStateObserver ============

    [Fact]
    public void CircuitBreakerObserver_SetState_Adds_State()
    {
        CircuitBreakerStateObserver.Clear();

        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);
        CircuitBreakerStateObserver.SetState("policy_b", CircuitBreakerState.HalfOpen);

        var states = CircuitBreakerStateObserver.CurrentStates.ToList();
        states.Should().HaveCount(2);
        states.Should().Contain(m => m.MatchesTag("policy_key", "policy_a") && m.Value == (int)CircuitBreakerState.Open);
        states.Should().Contain(m => m.MatchesTag("policy_key", "policy_b") && m.Value == (int)CircuitBreakerState.HalfOpen);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public void CircuitBreakerObserver_SetState_Updates_Existing_State()
    {
        CircuitBreakerStateObserver.Clear();

        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);
        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Closed);

        var states = CircuitBreakerStateObserver.CurrentStates.ToList();
        states.Should().ContainSingle();
        states[0].Value.Should().Be((int)CircuitBreakerState.Closed);

        CircuitBreakerStateObserver.Clear();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CircuitBreakerObserver_SetState_With_NullOrEmpty_Key_Does_Nothing(string? key)
    {
        CircuitBreakerStateObserver.Clear();

        CircuitBreakerStateObserver.SetState(key!, CircuitBreakerState.Open);

        CircuitBreakerStateObserver.CurrentStates.Should().BeEmpty();

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public void CircuitBreakerObserver_RemoveState_Removes_State()
    {
        CircuitBreakerStateObserver.Clear();

        CircuitBreakerStateObserver.SetState("policy_a", CircuitBreakerState.Open);
        CircuitBreakerStateObserver.SetState("policy_b", CircuitBreakerState.Closed);

        var removed = CircuitBreakerStateObserver.RemoveState("policy_a");
        removed.Should().BeTrue();

        var states = CircuitBreakerStateObserver.CurrentStates.ToList();
        states.Should().ContainSingle();
        states.Should().Contain(m => m.MatchesTag("policy_key", "policy_b"));

        CircuitBreakerStateObserver.Clear();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CircuitBreakerObserver_RemoveState_With_NullOrEmpty_Key_Returns_False(string? key)
    {
        CircuitBreakerStateObserver.RemoveState(key!).Should().BeFalse();
    }

    [Fact]
    public void CircuitBreakerObserver_RemoveState_With_Unknown_Key_Returns_False()
    {
        CircuitBreakerStateObserver.Clear();

        CircuitBreakerStateObserver.RemoveState("not_exists").Should().BeFalse();

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public void CircuitBreakerObserver_Clear_Removes_All_States()
    {
        CircuitBreakerStateObserver.SetState("a", CircuitBreakerState.Open);
        CircuitBreakerStateObserver.SetState("b", CircuitBreakerState.HalfOpen);
        CircuitBreakerStateObserver.SetState("c", CircuitBreakerState.Closed);

        CircuitBreakerStateObserver.CurrentStates.Should().HaveCount(3);

        CircuitBreakerStateObserver.Clear();

        CircuitBreakerStateObserver.CurrentStates.Should().BeEmpty();
    }

    [Fact]
    public void CircuitBreakerObserver_CurrentStates_Empty_When_No_State_Set()
    {
        CircuitBreakerStateObserver.Clear();
        CircuitBreakerStateObserver.CurrentStates.Should().BeEmpty();
    }

    [Fact]
    public void CircuitBreakerState_Enum_Has_Correct_Values()
    {
        ((int)CircuitBreakerState.Closed).Should().Be(0);
        ((int)CircuitBreakerState.HalfOpen).Should().Be(1);
        ((int)CircuitBreakerState.Open).Should().Be(2);
    }

    // ============ MudHttpObservability ============

    [Fact]
    public void Observability_StartRequestActivity_WithoutListener_ReturnsNull()
    {
        MudHttpActivitySource.Instance.HasListeners().Should().BeFalse();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var activity = MudHttpObservability.StartRequestActivity(request, "client_a");
        activity.Should().BeNull();
    }

    [Fact]
    public void Observability_StartRequestActivity_WithListener_Sets_Tags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/path?query=1");
        using var activity = MudHttpObservability.StartRequestActivity(request, "client_a");

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpMethod).Should().Be("POST");
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpUrl).Should().Be("https://api.example.com/path?query=1");
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpScheme).Should().Be("https");
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpHost).Should().Be("api.example.com");
        activity.GetTagItem(MudHttpActivitySource.Tags.MudClientName).Should().Be("client_a");
    }

    [Fact]
    public void Observability_StartRequestActivity_WithNullClientName_OmitsClientNameTag()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        using var activity = MudHttpObservability.StartRequestActivity(request, null);

        activity.Should().NotBeNull();
        activity!.GetTagItem(MudHttpActivitySource.Tags.MudClientName).Should().BeNull();
    }

    [Fact]
    public void Observability_MarkObserved_IsObserved_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.IsObserved(request).Should().BeFalse();

        MudHttpObservability.MarkObserved(request);

        MudHttpObservability.IsObserved(request).Should().BeTrue();
    }

    [Fact]
    public void Observability_SetClientName_GetClientName_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.GetClientName(request).Should().BeNull();

        MudHttpObservability.SetClientName(request, "client_a");

        MudHttpObservability.GetClientName(request).Should().Be("client_a");
    }

    [Fact]
    public void Observability_SetClientName_WithNullOrEmpty_DoesNotSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.SetClientName(request, null);
        MudHttpObservability.SetClientName(request, "");
        MudHttpObservability.GetClientName(request).Should().BeNull();
    }

    [Fact]
    public void Observability_SetStatusCode_CanBeRead_Back()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.SetStatusCode(request, 200);

        MudHttpObservability.TryGetProperty(request, "__mud_status_code", out var value).Should().BeTrue();
        value.Should().Be(200);
    }

    [Fact]
    public void Observability_SetContentLength_CanBeRead_Back()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.SetContentLength(request, 1024L);

        MudHttpObservability.TryGetProperty(request, "__mud_content_length", out var value).Should().BeTrue();
        value.Should().Be(1024L);
    }

    [Fact]
    public void Observability_SetContentLength_WithNull_DoesNotSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.SetContentLength(request, null);

        MudHttpObservability.TryGetProperty(request, "__mud_content_length", out _).Should().BeFalse();
    }

    [Fact]
    public void Observability_TrySetProperty_TryGetProperty_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        MudHttpObservability.TrySetProperty(request, "custom_key", "custom_value");

        MudHttpObservability.TryGetProperty(request, "custom_key", out var value).Should().BeTrue();
        value.Should().Be("custom_value");
    }

    [Fact]
    public void Observability_TryGetProperty_WithMissingKey_ReturnsFalse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");

        MudHttpObservability.TryGetProperty(request, "missing_key", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Observability_RecordResponse_RecordsMetrics_And_SetsActivityStatus_Ok()
    {
        // 准备 ActivityListener
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // 准备 MeterListener
        var counterValues = new List<long>();
        var histogramValues = new List<double>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.request.duration")
                histogramValues.Add(value);
        });
        meterListener.Start();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users");
        using var activity = MudHttpObservability.StartRequestActivity(request, "client_a");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello")
        };

        MudHttpObservability.RecordResponse(activity, response, elapsedMs: 42.5, clientName: "client_a");

        // 验证 Activity 状态
        activity!.Status.Should().Be(ActivityStatusCode.Ok);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCode).Should().Be(200);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCodeClass).Should().Be("2xx");

        // 验证指标
        counterValues.Should().ContainSingle().Which.Should().Be(1);
        histogramValues.Should().ContainSingle().Which.Should().Be(42.5);
    }

    [Fact]
    public void Observability_RecordResponse_With4xx_SetsActivityStatus_Error()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users");
        using var activity = MudHttpObservability.StartRequestActivity(request, "client_a");
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        MudHttpObservability.RecordResponse(activity, response, elapsedMs: 10.0, clientName: "client_a");

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCode).Should().Be(404);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCodeClass).Should().Be("4xx");
    }

    [Fact]
    public void Observability_RecordResponse_With5xx_SetsActivityStatus_Error()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users");
        using var activity = MudHttpObservability.StartRequestActivity(request, "client_a");
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        MudHttpObservability.RecordResponse(activity, response, elapsedMs: 10.0, clientName: "client_a");

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCode).Should().Be(500);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCodeClass).Should().Be("5xx");
    }

    [Fact]
    public void Observability_RecordError_SetsActivityStatus_Error_And_Exception_Tags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var counterValues = new List<long>();
        var histogramValues = new List<double>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.request.duration")
                histogramValues.Add(value);
        });
        meterListener.Start();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users");
        using var activity = MudHttpObservability.StartRequestActivity(request, "client_a");

        var exception = new InvalidOperationException("connection refused");
        MudHttpObservability.RecordError(activity, exception, elapsedMs: 5.0, clientName: "client_a", request: request);

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("connection refused");
        activity.GetTagItem("exception.type").Should().Be(typeof(InvalidOperationException).FullName);
        activity.GetTagItem("exception.message").Should().Be("connection refused");

        counterValues.Should().ContainSingle().Which.Should().Be(1);
        histogramValues.Should().ContainSingle().Which.Should().Be(5.0);
    }

    [Fact]
    public void Observability_RecordSuccessFromRequest_Reads_StatusCode_From_Property()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var counterValues = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.Start();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users");
        MudHttpObservability.SetStatusCode(request, 204);
        using var activity = MudHttpObservability.StartRequestActivity(request, "client_a");

        MudHttpObservability.RecordSuccessFromRequest(activity, request, elapsedMs: 8.0, clientName: "client_a");

        activity!.Status.Should().Be(ActivityStatusCode.Ok);
        activity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCode).Should().Be(204);
        counterValues.Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public void Observability_CreateLoggerScope_Returns_NonNull_When_Logger_Provided()
    {
        var scopeDisposable = new Mock<IDisposable>().Object;
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.BeginScope(It.IsAny<object>())).Returns(scopeDisposable);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        using var scope = MudHttpObservability.CreateLoggerScope(loggerMock.Object, request, "client_a");
        scope.Should().NotBeNull();
        scope.Should().BeSameAs(scopeDisposable);
    }

    [Fact]
    public void Observability_CreateLoggerScope_WithNullLogger_ReturnsNull()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var scope = MudHttpObservability.CreateLoggerScope(null!, request, "client_a");
        scope.Should().BeNull();
    }

    // ============ TracingDelegatingHandler ============

    [Fact]
    public async Task TracingHandler_WithoutListener_Still_Records_Metrics()
    {
        // 没有监听器，但 MeterListener 仍可捕获
        MudHttpActivitySource.Instance.HasListeners().Should().BeFalse();

        var counterValues = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.Start();

        var innerHandler = new FakeDelegatingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracingDelegatingHandler { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // 即使没有 ActivityListener，TracingHandler 仍会记录指标
        counterValues.Should().ContainSingle().Which.Should().Be(1);
        // 已标记 observed
        MudHttpObservability.IsObserved(request).Should().BeTrue();
    }

    [Fact]
    public async Task TracingHandler_WithListener_CreatesActivity_And_RecordsMetrics()
    {
        var startedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => startedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var counterValues = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.Start();

        var innerHandler = new FakeDelegatingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracingDelegatingHandler { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users");
        MudHttpObservability.SetClientName(request, "client_a");

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        startedActivities.Should().ContainSingle();
        startedActivities[0].OperationName.Should().Be(MudHttpActivitySource.ActivityNameRequest);
        startedActivities[0].Kind.Should().Be(ActivityKind.Client);
        startedActivities[0].GetTagItem(MudHttpActivitySource.Tags.HttpMethod).Should().Be("GET");
        startedActivities[0].GetTagItem(MudHttpActivitySource.Tags.HttpStatusCode).Should().Be(200);
        startedActivities[0].Status.Should().Be(ActivityStatusCode.Ok);

        counterValues.Should().ContainSingle().Which.Should().Be(1);
        MudHttpObservability.IsObserved(request).Should().BeTrue();
    }

    [Fact]
    public async Task TracingHandler_WhenAlreadyObserved_DoesNotRecordAgain()
    {
        var counterValues = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.Start();

        var innerHandler = new FakeDelegatingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracingDelegatingHandler { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");
        // 预先标记为已观察
        MudHttpObservability.MarkObserved(request);

        await invoker.SendAsync(request, CancellationToken.None);

        // 不应该再次记录指标
        counterValues.Should().BeEmpty();
    }

    [Fact]
    public async Task TracingHandler_WhenInnerHandlerThrows_RecordsErrorAndRethrows()
    {
        var counterValues = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.Start();

        var expectedException = new HttpRequestException("network error");
        var innerHandler = new FakeDelegatingHandler(_ => throw expectedException);
        var handler = new TracingDelegatingHandler { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/");

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("network error");

        // 应该记录 error 路径的指标
        counterValues.Should().ContainSingle().Which.Should().Be(1);
        MudHttpObservability.IsObserved(request).Should().BeTrue();
    }

    [Fact]
    public void TracingHandler_ObservedPropertyKey_Matches_Const()
    {
        TracingDelegatingHandler.ObservedPropertyKey.Should().Be("__mud_observed");
    }

    /// <summary>
    /// 用于测试的简单 DelegatingHandler，根据传入的委托返回响应或抛出异常。
    /// </summary>
    private sealed class FakeDelegatingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}

/// <summary>
/// 测试辅助扩展方法，用于检查 Measurement&lt;T&gt; 的 tag。
/// </summary>
internal static class MeasurementExtensions
{
    public static bool MatchesTag<T>(this Measurement<T> measurement, string key, object? expectedValue) where T : struct
    {
        foreach (var tag in measurement.Tags)
        {
            if (tag.Key == key && Equals(tag.Value, expectedValue))
                return true;
        }
        return false;
    }
}
