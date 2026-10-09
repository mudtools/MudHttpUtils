// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net.Http.Headers;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// B5 回归护栏：<see cref="TokenRecoveryOptions.AdditionalTokenInvalidationDetectors"/> 的<b>并集</b>语义，
/// 以及库内组合器 <see cref="CompositeTokenInvalidationDetector"/> 的短路与异常降级契约。
/// </summary>
/// <remarks>
/// 动因：多产品线各自在 <c>PostConfigure</c> 写单槽 <see cref="TokenRecoveryOptions.TokenInvalidationDetector"/>
/// 会互相覆盖（后者胜）⇒ 某条产品线的恢复静默失效。追加式集合保证互不覆盖。
/// </remarks>
public class TokenRecoveryAdditionalDetectorTests
{
    private static Mock<ITokenManager> CreateTokenManagerMock()
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");
        return mock;
    }

    private static HttpRequestMessage CreateAuthRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "old-token");
        return request;
    }

    private static async Task<int> ExecuteReturningOkAsync(
        TokenRecoveryExecutor executor, HttpRequestMessage request, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            request,
            (_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(statusCode));
            },
            CancellationToken.None);

        response.Dispose();
        return sendCount;
    }

    // ── ① 追加式判定器：HTTP 200 + 命中 ⇒ 进入恢复链路 ───────────────────

    [Fact]
    public async Task AdditionalDetector_HitOnHttp200_ShouldTriggerRecovery()
    {
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 1, RefreshDedupWindowSeconds = 0 };
        options.AdditionalTokenInvalidationDetectors.Add(new FakeDetector("errcode", hit: true));
        var executor = new TokenRecoveryExecutor(CreateTokenManagerMock().Object, options);

        var sendCount = await ExecuteReturningOkAsync(executor, CreateAuthRequest());

        sendCount.Should().Be(2, "HTTP 200 + 追加判定器命中 ⇒ 失效 → 刷新 → 重试（1 次）");
    }

    // ── ② 回归等价：两类来源皆空 ⇒ 仅认 401 ────────────────────────────

    [Fact]
    public async Task NoDetector_Http200_ShouldNotTriggerRecovery()
    {
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 1, RefreshDedupWindowSeconds = 0 };
        var executor = new TokenRecoveryExecutor(CreateTokenManagerMock().Object, options);

        var sendCount = await ExecuteReturningOkAsync(executor, CreateAuthRequest());

        sendCount.Should().Be(1, "无任何判定器时 HTTP 200 不得触发恢复（逐字节等价旧行为）");
    }

    // ── ③ 并集与短路顺序：单槽优先，再按追加顺序 ────────────────────────

    [Fact]
    public async Task Union_ShouldEvaluatePrimaryFirstThenAdditional_AndShortCircuit()
    {
        var log = new List<string>();
        var primary = new FakeDetector("primary", hit: false, log: log);
        var first = new FakeDetector("first", hit: true, log: log);
        var second = new FakeDetector("second", hit: true, log: log);

        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 1, RefreshDedupWindowSeconds = 0 };
        options.TokenInvalidationDetector = primary;
        options.AdditionalTokenInvalidationDetectors.Add(first);
        options.AdditionalTokenInvalidationDetectors.Add(second);

        var executor = new TokenRecoveryExecutor(CreateTokenManagerMock().Object, options);

        var sendCount = await ExecuteReturningOkAsync(executor, CreateAuthRequest());

        sendCount.Should().Be(2);
        log.Take(2).Should().Equal(
            new[] { "primary", "first" },
            "求值顺序固定：单槽属性优先，其后按追加顺序；任一命中即短路");
        second.IsTokenInvalidCalls.Should().Be(0, "短路后不得再调用后续判定器");
    }

    // ── ④ 判定器异常 ⇒ 降级"未失效"，不放大为调用失败 ────────────────────

    [Fact]
    public async Task DetectorThrows_ShouldDegradeToNotInvalid()
    {
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 1, RefreshDedupWindowSeconds = 0 };
        options.AdditionalTokenInvalidationDetectors.Add(
            new FakeDetector("boom", toThrow: new InvalidOperationException("detector failure")));
        var executor = new TokenRecoveryExecutor(CreateTokenManagerMock().Object, options);

        var sendCount = await ExecuteReturningOkAsync(executor, CreateAuthRequest());

        sendCount.Should().Be(1, "判定器故障降级为未失效（检测故障不得放大为调用失败）");
    }

    // ── ⑤ ShouldInspect=false ⇒ 不参与判定 ─────────────────────────────

    [Fact]
    public async Task DetectorNotInspecting_ShouldBeSkipped()
    {
        var skipped = new FakeDetector("skipped", hit: true, shouldInspect: false);
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 1, RefreshDedupWindowSeconds = 0 };
        options.AdditionalTokenInvalidationDetectors.Add(skipped);
        var executor = new TokenRecoveryExecutor(CreateTokenManagerMock().Object, options);

        var sendCount = await ExecuteReturningOkAsync(executor, CreateAuthRequest());

        sendCount.Should().Be(1);
        skipped.IsTokenInvalidCalls.Should().Be(0, "ShouldInspect=false 者必须在读取响应体之前被跳过");
    }

    // ── ⑥ 组合器：并集 / 短路 / 单点故障隔离 ───────────────────────────

    [Fact]
    public void Composite_ShouldInspect_ShouldBeUnion()
    {
        var request = CreateAuthRequest();
        var noneInspecting = new CompositeTokenInvalidationDetector(
            new FakeDetector("a", shouldInspect: false),
            new FakeDetector("b", shouldInspect: false));
        var oneInspecting = new CompositeTokenInvalidationDetector(
            new FakeDetector("a", shouldInspect: false),
            new FakeDetector("b", shouldInspect: true));

        noneInspecting.ShouldInspect(request).Should().BeFalse();
        oneInspecting.ShouldInspect(request).Should().BeTrue();
        new CompositeTokenInvalidationDetector().ShouldInspect(request).Should().BeFalse("空组合器等价于无判定器");
    }

    [Fact]
    public async Task Composite_OneThrower_ShouldNotDisableOtherDetectors()
    {
        var composite = new CompositeTokenInvalidationDetector(
            new FakeDetector("boom", toThrow: new InvalidOperationException("a bad detector")),
            new FakeDetector("hit", hit: true));

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var invalid = await composite.IsTokenInvalidAsync(response, null, CancellationToken.None);

        invalid.Should().BeTrue("单个判定器故障不得使其它产品线的判定失效");
    }

    [Fact]
    public async Task Composite_Empty_ShouldBeNotInvalid()
    {
        var composite = new CompositeTokenInvalidationDetector();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        (await composite.IsTokenInvalidAsync(response, null, CancellationToken.None)).Should().BeFalse();
    }

    // ── 测试替身 ───────────────────────────────────────────────────────

    private sealed class FakeDetector : ITokenInvalidationDetector
    {
        private readonly bool _hit;
        private readonly bool _shouldInspect;
        private readonly Exception? _toThrow;
        private readonly List<string>? _log;

        public FakeDetector(
            string name,
            bool hit = false,
            bool shouldInspect = true,
            Exception? toThrow = null,
            List<string>? log = null)
        {
            Name = name;
            _hit = hit;
            _shouldInspect = shouldInspect;
            _toThrow = toThrow;
            _log = log;
        }

        public string Name { get; }

        public int IsTokenInvalidCalls { get; private set; }

        public bool ShouldInspect(HttpRequestMessage request) => _shouldInspect;

        public ValueTask<bool> IsTokenInvalidAsync(
            HttpResponseMessage response,
            ReadOnlyMemory<byte>? body,
            CancellationToken cancellationToken)
        {
            IsTokenInvalidCalls++;
            _log?.Add(Name);

            if (_toThrow is not null)
                throw _toThrow;

            return new ValueTask<bool>(_hit);
        }
    }
}
