// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// CFG-13：超时层级契约的集成用例 —— <c>HttpClient.Timeout</c> 为外层硬上限，
/// 短于内层等待时先触发，并归一化为 <see cref="ApiRequestException"/>(<c>IsTimeout=true</c>)。
/// </summary>
/// <remarks>
/// 契约：<c>HttpClient.Timeout</c> ⊃ <c>Timeout(options)</c>（全局 Polly）⊃ <c>[Timeout]</c>（方法级 Polly）。
/// 外层硬上限会封顶内层超时（编译期由 HTTPCLIENT021 提示，见 CFG-07）。
/// </remarks>
public class TimeoutHierarchyTests
{
    public TimeoutHierarchyTests()
    {
        // 白名单命中即跳过私有 IP / DNS 预检，避免测试依赖网络。
        UrlValidator.AddAllowedDomain("api.example.com");
    }

    [Fact]
    public async Task HttpClientTimeout_ShorterThanResponse_TriggersOuterTimeout_AsApiRequestException()
    {
        // 外层 HttpClient.Timeout 短于服务端响应耗时 → 外层先触发。
        using var httpClient = new HttpClient(new SlowHandler(TimeSpan.FromSeconds(5)))
        {
            BaseAddress = new Uri("https://api.example.com"),
            Timeout = TimeSpan.FromMilliseconds(250),
        };
        var client = new DirectEnhancedHttpClient(
            httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });

        var act = async () => await client.GetAsync<string>("/slow");

        var assertion = await act.Should().ThrowAsync<ApiRequestException>();
        assertion.Which.IsTimeout.Should().BeTrue();
    }

    [Fact]
    public async Task HttpClientTimeout_LongerThanResponse_Succeeds_NoTimeout()
    {
        // 外层超时充裕 → 不触发任何超时，请求成功（锁定「外层不误杀」）。
        using var httpClient = new HttpClient(new SlowHandler(TimeSpan.FromMilliseconds(50)))
        {
            BaseAddress = new Uri("https://api.example.com"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        var client = new DirectEnhancedHttpClient(
            httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });

        var result = await client.GetAsync<string>("/fast");

        result.Should().Be("ok");
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public SlowHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                // GetAsync<string> 反序列化 JSON 字符串
                Content = new StringContent("\"ok\"", Encoding.UTF8, "application/json"),
                RequestMessage = request,
                Version = request.Version,
            };
        }
    }
}
