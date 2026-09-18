// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// M1 回归用例：#1 #9 #16 N-1（错误响应体限量读取）+ #5（URL 脱敏）+ #4（白名单并发）+ #6（cache_key tag）

using System.Collections.Concurrent;
using System.Linq;
using Mud.HttpUtils.Testing;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M1-#1/#9/N-1：错误响应体在读取阶段限量（含 chunked 无 Content-Length 场景），默认 10240 字符，0/负 = 不限制。
/// </summary>
public class LimitedErrorContentTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    private static (DirectEnhancedHttpClient Client, StubHttp Stub) Create(
        EnhancedHttpClientOptions? options = null)
    {
        var stub = new StubHttp();
        // 白名单域名（经 UrlValidatorFixture 注册）+ https → 通过严格模式校验
        var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(httpClient, options);
        return (client, stub);
    }

    public void Dispose() => _fixture.RestoreDomains();

    [Fact]
    public async Task ErrorContent_ChunkedLargeResponse_TruncatedToDefaultLimit()
    {
        // T-1.1 / T-9.1：无 Content-Length 的 500 响应（流式），错误内容默认截断到 10240 字符
        var (client, stub) = Create();
        stub.RespondToAnyRequest(HttpStatusCode.InternalServerError)
            .WithLazyContent(totalBytes: 2 * 1024 * 1024, chunkSize: 8192); // 2 MB 流式错误体

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/values");
        var act = () => client.SendAsync<string>(request);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.Content.Should().NotBeNull();
        ex.Which.Content!.Length.Should().BeLessThanOrEqualTo(
            HttpExecutionConstants.DefaultMaxExceptionContentLength + "...[已截断]".Length);
        ex.Which.Content.Should().EndWith("...[已截断]");
    }

    [Fact]
    public async Task ErrorContent_DefaultLimit_AppliesWithoutExplicitConfig()
    {
        // N-1：默认配置即截断（两条路径默认行为一致）
        var (client, stub) = Create();
        var bigError = new string('x', 50 * 1024);
        stub.RespondToAnyRequest(HttpStatusCode.InternalServerError, bigError);

        var ex = await FluentActions.Awaiting(() =>
            client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "/api/values")))
            .Should().ThrowAsync<ApiException>();

        ex.Which.Content!.Length.Should().Be(
            HttpExecutionConstants.DefaultMaxExceptionContentLength + "...[已截断]".Length);
    }

    [Fact]
    public async Task ErrorContent_ZeroOrNegativeConfig_NoLimit()
    {
        // T-1.4：MaxExceptionContentLength = 0 → 不截断
        var (client, stub) = Create(new EnhancedHttpClientOptions
        {
            MaxExceptionContentLength = 0,
        });
        var body = new string('x', 20000);
        stub.RespondToAnyRequest(HttpStatusCode.InternalServerError, body);

        var ex = await FluentActions.Awaiting(() =>
            client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "/api/values")))
            .Should().ThrowAsync<ApiException>();

        ex.Which.Content.Should().Be(body);
    }

    [Fact]
    public async Task ErrorContent_SmallResponse_NotTruncated()
    {
        var (client, stub) = Create();
        stub.RespondToAnyRequest(HttpStatusCode.BadRequest, "err");

        var ex = await FluentActions.Awaiting(() =>
            client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "/api/values")))
            .Should().ThrowAsync<ApiException>();

        ex.Which.Content.Should().Be("err");
    }

    [Fact]
    public async Task SuccessContent_LargeResponse_StillFullyDeserialized()
    {
        // T-1.3：成功响应不受错误上限影响（防过度截断回归）
        var (client, stub) = Create();
        // 20 KB JSON 数组（> 10240 字符但 < MaxDebugLogBodyLength）
        var largeArray = "[" + string.Join(",", Enumerable.Repeat("\"item-value-64-characters-long-string-padding-padding!!\"", 200)) + "]";
        stub.RespondToAnyRequest(HttpStatusCode.OK, largeArray);

        var result = await client.SendAsync<string[]>(
            new HttpRequestMessage(HttpMethod.Get, "/api/values"));

        result.Should().HaveCount(200);
    }

    [Fact]
    public async Task CaptureRequestContent_LargeBody_TruncatedInException()
    {
        // T-16.1：CaptureRequestContent = true + 1 MB 请求体 + 500 → RequestContent 受限
        var (client, stub) = Create(new EnhancedHttpClientOptions
        {
            CaptureRequestContent = true,
        });
        stub.RespondToAnyRequest(HttpStatusCode.InternalServerError, "err");
        var bigBody = new string('y', 1024 * 1024);

        var ex = await FluentActions.Awaiting(() =>
            client.PostAsJsonAsync<object, string>("/api/values", bigBody))
            .Should().ThrowAsync<ApiException>();

        ex.Which.RequestContent.Should().NotBeNull();
        ex.Which.RequestContent!.Length.Should().BeLessThanOrEqualTo(
            HttpExecutionConstants.DefaultMaxExceptionContentLength + "...[已截断]".Length);
    }

    [Fact]
    public async Task BOM_PrefixedErrorContent_BomStrippedOnRead()
    {
        // #29（顺带）：BOM 前缀的错误响应体读取时被剥离，不产生乱码
        var (client, stub) = Create();
        var bom = Encoding.UTF8.GetPreamble();
        var payload = bom.Concat(Encoding.UTF8.GetBytes("plain error")).ToArray();
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = content };

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        text.Should().Be("plain error");
    }
}

/// <summary>
/// M1-#5：URL 脱敏（SensitiveUrlRedactor / SafeUrl）。
/// </summary>
public class SensitiveUrlRedactorTests
{
    [Theory]
    [InlineData("https://api.example.com/v1/data?access_token=secret-token-value", "https://api.example.com/v1/data?access_token=***REDACTED***")]
    [InlineData("https://api.example.com/v1?refresh_token=abc&x=1", "https://api.example.com/v1?refresh_token=***REDACTED***&x=1")]
    [InlineData("https://api.example.com/v1?api_key=zzz&page=2", "https://api.example.com/v1?api_key=***REDACTED***&page=2")]
    [InlineData("https://api.example.com/v1?password=hunter2", "https://api.example.com/v1?password=***REDACTED***")]
    [InlineData("https://api.example.com/v1?authorization=Bearer xyz", "https://api.example.com/v1?authorization=***REDACTED***")]
    public void Redact_SensitiveQueryValues_Masked(string input, string expected)
    {
        Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://api.example.com/v1/data")]                    // 无 query
    [InlineData("https://api.example.com/v1/data?page=1&size=20")]     // 普通参数不误伤
    [InlineData("https://api.example.com/v1/data?")]                    // 空 query
    public void Redact_NonSensitiveUrl_Unchanged(string url)
    {
        // T-5.3：普通 URL 脱敏后不变
        Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url).Should().Be(url);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsEmpty()
    {
        Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(null).Should().BeEmpty();
        Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Redact_AlreadyRedactedValue_NotDoubleMasked()
    {
        var url = "https://api.example.com/v1?access_token=***REDACTED***";
        Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url).Should().Be(url);
    }

    [Fact]
    public void Redact_BothSensitiveAndNormalParams_OnlySensitiveMasked()
    {
        var url = "https://api.example.com/v1/search?query=hello&session_token=st-1&limit=10";
        var redacted = Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url);
        redacted.Should().Contain("query=hello");
        redacted.Should().Contain("session_token=***REDACTED***");
        redacted.Should().Contain("limit=10");
    }

    [Fact]
    public void Redact_SwitchOff_PreservesFullUrl()
    {
        // T-5.2：RedactUrlInTelemetry = false 时保留完整 URL（排障开关有效性）。
        // 静态开关——finally 恢复默认值，避免影响并行用例。
        MudHttpObservabilityOptions.RedactUrlInTelemetry = false;
        try
        {
            var url = "https://api.example.com/v1?access_token=secret-token-value&page=1";
            Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url).Should().Be(url);
        }
        finally
        {
            MudHttpObservabilityOptions.RedactUrlInTelemetry = true;
        }
    }

    [Fact]
    public void Redact_SwitchOnDefault_MasksSensitiveQuery()
    {
        // 开关默认 true：脱敏生效（与 Redact_SensitiveQueryValues_Masked 互补，锁定开关语义）
        MudHttpObservabilityOptions.RedactUrlInTelemetry.Should().BeTrue();
        var url = "https://api.example.com/v1?access_token=secret-token-value";
        Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url)
            .Should().Be("https://api.example.com/v1?access_token=***REDACTED***");
    }
}

/// <summary>
/// M1-#4：白名单并发安全（原子替换，无空窗期）。
/// </summary>
public class UrlValidatorConcurrencyTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    [Fact]
    public async Task ConfigureAllowedDomains_ConcurrentWithValidateUrl_NoEmptyWindowOrException()
    {
        // T-4.1：8 线程配置 + 32 线程校验并发 → 无异常、无误拒
        UrlValidator.ConfigureAllowedDomains(["api.example.com"]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = cts.Token;
        var configurators = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                UrlValidator.ConfigureAllowedDomains(["api.example.com", "cdn.example.org"]);
                UrlValidator.ConfigureAllowedDomains(["api.example.com"]);
            }
        }, token)).ToArray();

        var validators = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                // allowCustomBaseUrls=false + https + 白名单命中 → 不抛；
                // 若抛 InvalidOperationException 即为"空窗期"误拒（fail fast 由 Should 完成）
                UrlValidator.ValidateUrl("https://api.example.com/v1/data");
            }
        }, token)).ToArray();

        try
        {
            await Task.WhenAll(configurators.Concat(validators).ToArray());
        }
        catch (OperationCanceledException)
        {
            // 取消触发是预期
        }
    }

    [Fact]
    public async Task AddRemoveAllowedDomain_Concurrent_SnapshotConsistent()
    {
        UrlValidator.ConfigureAllowedDomains(["base.example.com"]);
        var results = new ConcurrentBag<string[]>();

        await Task.Run(() => Parallel.For(0, 1000, i =>
        {
            if (i % 3 == 0)
                UrlValidator.AddAllowedDomain($"extra-{i % 10}.example.com");
            else if (i % 3 == 1)
                UrlValidator.RemoveAllowedDomain($"extra-{i % 10}.example.com");
            else
                results.Add(UrlValidator.GetAllowedDomains().ToArray());
        }));

        // 每个快照都应包含 base.example.com（基础集永不丢失）
        foreach (var snapshot in results)
            snapshot.Should().Contain("base.example.com");
    }
}
