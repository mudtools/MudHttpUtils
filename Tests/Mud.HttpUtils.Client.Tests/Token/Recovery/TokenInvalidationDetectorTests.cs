// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.Json;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// WX-01（Phase A，errcode 令牌失效恢复）专项测试：
/// <see cref="ITokenInvalidationDetector"/> 判定器在令牌恢复链路的两处判定点（首次响应 / 重试响应）
/// 的行为、响应体按需捕获的门控与替换语义、默认语义向后兼容（无判定器 = 仅 HTTP 401）。
/// </summary>
public class TokenInvalidationDetectorTests
{
    private const string ErrcodeInvalidBody = """{"errcode":42001,"errmsg":"access_token expired"}""";
    private const string ErrcodeOkBody = """{"errcode":0,"errmsg":"ok","data":"payload"}""";

    #region 测试判定器（模拟企业微信 errcode 语义）

    /// <summary>
    /// 模拟 WeChat SDK 侧判定器：读取 errcode ∈ {40014, 42001, 42007, 42009, 42011}。
    /// </summary>
    private sealed class WechatErrcodeDetector : ITokenInvalidationDetector
    {
        private static readonly long[] InvalidErrcodes = { 40014, 42001, 42007, 42009, 42011 };

        public bool ShouldInspectResult = true;
        public int ShouldInspectCalls;
        public int IsInvalidCalls;
        public ReadOnlyMemory<byte>? LastBody;
        public bool ThrowInIsInvalid;

        public bool ShouldInspect(HttpRequestMessage request)
        {
            Interlocked.Increment(ref ShouldInspectCalls);
            return ShouldInspectResult;
        }

        public ValueTask<bool> IsTokenInvalidAsync(HttpResponseMessage response, ReadOnlyMemory<byte>? body, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref IsInvalidCalls);
            LastBody = body;
            if (ThrowInIsInvalid)
                throw new InvalidOperationException("detector exploded");
            if (body is not { Length: > 0 } b)
                return ValueTask.FromResult(false);

            using var doc = JsonDocument.Parse(b);
            var errcode = doc.RootElement.TryGetProperty("errcode", out var e) ? e.GetInt64() : -1;
            return ValueTask.FromResult(Array.IndexOf(InvalidErrcodes, errcode) >= 0);
        }
    }

    #endregion

    #region 测试基建

    private static Mock<ITokenManager> CreateAlwaysValidManager()
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("fresh-token");
        return mock;
    }

    /// <summary>构造企业微信形态的请求：Query 注入 access_token + TokenRecoveryContext（生成代码等价物）。</summary>
    private static HttpRequestMessage CreateWechatRequest(string accessToken = "stale-token")
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://qyapi.weixin.qq.com/cgi-bin/user/list?access_token={accessToken}&department_id=1");
        request.Options.Set(new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey), new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.Query,
            QueryParameterName = "access_token",
            TokenManagerKey = "Wechat.AccessToken",
        });
        return request;
    }

    private static HttpResponseMessage JsonOk(string body) => Json(HttpStatusCode.OK, body);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        return new HttpResponseMessage(statusCode) { Content = content };
    }

    /// <summary>声明 Content-Length 但从不提供流的响应内容：用于验证「不读流」门控。</summary>
    private sealed class ProbeResponseContent : HttpContent
    {
        private readonly long _declaredLength;
        public int StreamRequestedCount;

        public ProbeResponseContent(long declaredLength)
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

    /// <summary>不声明长度（chunked 形态）的响应内容：用于验证「未知长度不捕获」门控。</summary>
    private sealed class ChunkedResponseContent : HttpContent
    {
        public int StreamRequestedCount;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Interlocked.Increment(ref StreamRequestedCount);
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    #endregion

    #region T1 默认语义（向后兼容）

    [Fact]
    public async Task Detector_Absent_ErrcodeResponse_ShouldNotRecover()
    {
        var manager = CreateAlwaysValidManager();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions { RecoveryMaxRetries = 1 });

        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(JsonOk(ErrcodeInvalidBody));
            },
            CancellationToken.None);

        sendCount.Should().Be(1, "无判定器时保持既有默认语义：非 401 一律视为成功");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        (await response.Content.ReadAsStringAsync(CancellationToken.None)).Should().Contain("42001");
        response.Dispose();
    }

    #endregion

    #region T2/T3 触发恢复与成功判定

    [Fact]
    public async Task Detector_ErrcodeInvalid_ShouldRefreshAndReinjectQueryToken()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var sentRequests = new List<HttpRequestMessage>();
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                sentRequests.Add(req);
                return Task.FromResult(JsonOk(ErrcodeInvalidBody));
            },
            CancellationToken.None);

        sentRequests.Count.Should().Be(2, "1 次原始发送 + 1 次重试");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once,
            "errcode 失效应触发与 401 一致的单次刷新");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sentRequests[^1].RequestUri!.Query.Should().Contain("access_token=fresh-token",
            "Query 注入模式下刷新后的令牌应重新注入同名参数");
        sentRequests[^1].RequestUri!.Query.Should().Contain("department_id=1",
            "重试请求应保留其余查询参数");
    }

    [Fact]
    public async Task Detector_ErrcodeZero_ShouldNotRecoverAndKeepBodyReadable()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(JsonOk(ErrcodeOkBody));
            },
            CancellationToken.None);

        sendCount.Should().Be(1);
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        detector.IsInvalidCalls.Should().BeGreaterThanOrEqualTo(1);
        detector.LastBody.Should().NotBeNull("声明长度的 200 响应应被捕获并交给判定器");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        body.Should().Contain("errcode", "捕获后响应内容必须对调用方保持可读");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json",
            "内容替换时必须保留 Content-Type 等内容头");
        response.Dispose();
    }

    #endregion

    #region T4/T5 捕获门控（超限 / 未知长度不读流）

    [Fact]
    public async Task Detector_DeclaredOversizeBody_ShouldNotReadStream()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
            MaxCapturedResponseBodyBytes = 4096,
        });

        var oversize = new ProbeResponseContent(100 * 1024);
        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = oversize });
            },
            CancellationToken.None);

        sendCount.Should().Be(1, "body = null → 判定器回退状态码判定（非 401 = 未失效）");
        oversize.StreamRequestedCount.Should().Be(0, "声明超限：零读流，调用方响应流不被消费污染");
        detector.LastBody.Should().BeNull("超限响应应向判定器传递 null body");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        response.Dispose();
    }

    [Fact]
    public async Task Detector_ChunkedBody_ShouldNotReadStream()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var chunked = new ChunkedResponseContent();
        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = chunked });
            },
            CancellationToken.None);

        sendCount.Should().Be(1);
        chunked.StreamRequestedCount.Should().Be(0, "未知长度（chunked）：不读流");
        detector.LastBody.Should().BeNull();
        response.Dispose();
    }

    [Fact]
    public async Task Detector_CaptureDisabled_ShouldNotReadStream()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
            MaxCapturedResponseBodyBytes = 0,
        });

        var content = new ProbeResponseContent(64);
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }),
            CancellationToken.None);

        content.StreamRequestedCount.Should().Be(0, "捕获上限为 0 表示禁用捕获");
        detector.LastBody.Should().BeNull();
        response.Dispose();
    }

    #endregion

    #region T6/T7 重试判定点一致性

    [Fact]
    public async Task Detector_RetryStillInvalid_ShouldExhaustAndReturnOriginalResponse()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 2,
            TokenInvalidationDetector = detector,
        });

        HttpResponseMessage? firstResponse = null;
        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                if (Interlocked.Increment(ref sendCount) == 1)
                {
                    firstResponse = JsonOk(ErrcodeInvalidBody);
                    return Task.FromResult(firstResponse);
                }
                return Task.FromResult(JsonOk(ErrcodeInvalidBody));
            },
            CancellationToken.None);

        sendCount.Should().Be(3, "1 次原始发送 + RecoveryMaxRetries(2) 次重试");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Exactly(2),
            "每轮重试判定失效后都应再次刷新（forceRefresh 语义）");
        response.Should().BeSameAs(firstResponse, "恢复耗尽返回服务端真实响应（D3 响应保真）");
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("42001", "返回的原始响应内容仍可读");
        response.Dispose();
    }

    [Fact]
    public async Task Detector_RetrySucceeds_ShouldReturnRetryResponseWithReadableBody()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                var isFirst = Interlocked.Increment(ref sendCount) == 1;
                return Task.FromResult(JsonOk(isFirst ? ErrcodeInvalidBody : ErrcodeOkBody));
            },
            CancellationToken.None);

        sendCount.Should().Be(2);
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("\"errcode\":0", "重试成功响应内容对调用方可读（捕获替换不破坏语义）");
        response.Dispose();
    }

    #endregion

    #region T8/T9 预过滤与故障降级

    [Fact]
    public async Task Detector_ShouldInspectFalse_ShouldSkipCaptureAndJudgement()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector { ShouldInspectResult = false };
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var content = new ProbeResponseContent(64);
        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            },
            CancellationToken.None);

        detector.ShouldInspectCalls.Should().BeGreaterThanOrEqualTo(1, "非 401 响应应先经预过滤");
        detector.IsInvalidCalls.Should().Be(0, "预过滤拒绝后不得调用判定方法");
        content.StreamRequestedCount.Should().Be(0, "预过滤拒绝后不得读响应流");
        sendCount.Should().Be(1);
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        response.Dispose();
    }

    [Fact]
    public async Task Detector_Throws_ShouldDegradeToNotInvalid()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector { ThrowInIsInvalid = true };
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var sendCount = 0;
        var request = CreateWechatRequest();
        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(JsonOk(ErrcodeInvalidBody));
            },
            CancellationToken.None);

        sendCount.Should().Be(1, "判定器故障降级为「未失效」，不进入恢复链路");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
        response.Dispose();
    }

    #endregion

    #region T10 401 语义不回归（不读 401 响应体）

    [Fact]
    public async Task Detector_With401Response_ShouldShortCircuitWithoutBodyRead()
    {
        var manager = CreateAlwaysValidManager();
        var detector = new WechatErrcodeDetector();
        var executor = new TokenRecoveryExecutor(manager.Object, new TokenRecoveryOptions
        {
            RecoveryMaxRetries = 1,
            TokenInvalidationDetector = detector,
        });

        var body401 = new ProbeResponseContent(256);
        var sendCount = 0;
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale");

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = body401 });
            },
            CancellationToken.None);

        body401.StreamRequestedCount.Should().Be(0, "401 判定短路在前，不读响应体");
        detector.ShouldInspectCalls.Should().Be(0, "401 为默认语义，不经判定器");
        sendCount.Should().Be(2, "401 仍走既有恢复链路（1 原始 + 1 重试）");
        manager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        response.Dispose();
    }

    #endregion

    #region T11 选项校验

    [Fact]
    public void Options_NegativeCaptureLimit_ShouldThrow()
    {
        var act = () => new TokenRecoveryOptions { MaxCapturedResponseBodyBytes = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validator_NegativeCaptureLimit_ShouldFail()
    {
        // setter 已在入口防御（见 Options_NegativeCaptureLimit_ShouldThrow）；此处经反射模拟
        // 绕过 setter 的负值状态（配置绑定等路径），验证校验器作为第二道防线。
        var options = new TokenRecoveryOptions();
        typeof(TokenRecoveryOptions)
            .GetField("_maxCapturedResponseBodyBytes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(options, -1);

        var result = new TokenRecoveryOptionsValidator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxCapturedResponseBodyBytes");
    }

    [Fact]
    public void Validator_DefaultOptions_ShouldPass()
    {
        var result = new TokenRecoveryOptionsValidator().Validate(null, new TokenRecoveryOptions());
        result.Succeeded.Should().BeTrue();
    }

    #endregion
}
