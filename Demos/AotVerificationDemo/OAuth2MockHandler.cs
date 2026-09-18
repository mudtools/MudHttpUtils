using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AotVerificationDemo;

/// <summary>
/// 模拟 OAuth2 令牌端点和自省端点的 <see cref="HttpMessageHandler"/>，
/// 用于 AOT 端到端验证 <see cref="StandardOAuth2TokenManager"/> 的完整路径。
/// </summary>
/// <remarks>
/// <para>
/// TMR-14：此前 <c>DemoOAuth2Serialization</c> 仅直接调 <c>JsonSerializer.Deserialize</c>
/// 验证 <c>OAuth2JsonContext</c> 元数据可用，未走 <see cref="StandardOAuth2TokenManager"/>
/// 的真实 HTTP + 反序列化 + 缓存链路。本 handler 使该链路在离线/CI 环境下可执行。
/// </para>
/// <para>
/// 令牌端点返回标准 <c>OAuth2TokenResponse</c> JSON（<c>access_token</c>/<c>refresh_token</c>/<c>expires_in</c>）；
/// 自省端点返回 <c>TokenIntrospectionResult</c> JSON（<c>active</c>/<c>client_id</c>/<c>scopes</c>）。
/// 序列化格式与 <see cref="Mud.HttpUtils.OAuth2JsonContext"/> 的 <c>SnakeCaseLower</c> 命名策略一致。
/// </para>
/// </remarks>
internal sealed class OAuth2MockHandler : HttpMessageHandler
{
    private const string TokenResponseJson =
        "{\"access_token\":\"mock-access-token-abc123\",\"refresh_token\":\"mock-refresh-token-xyz789\"," +
        "\"expires_in\":3600,\"token_type\":\"Bearer\",\"scope\":\"read write\"}";

    private const string IntrospectionResponseJson =
        // [场景17修复] TokenIntrospectionResult.Scopes 经 OAuth2JsonContext 的 SnakeCaseLower
        // 策略映射为 JSON 字段 "scopes"（string[]）。RFC 7662 的标准字段是单数 "scope"
        // （空格分隔字符串），与库现行契约不一致——真实 RFC 服务器返回 "scope" 时库将解析不到
        // 作用域（已知兼容性缺口，待库侧决策）。此处按库契约提供数组形式。
        "{\"active\":true,\"client_id\":\"test-client\",\"username\":\"testuser\"," +
        "\"scopes\":[\"read\",\"write\"],\"token_type\":\"Bearer\",\"exp\":9999999999}";

    /// <summary>
    /// 记录收到的令牌端点请求数（供断言验证请求确实到达）。
    /// </summary>
    public int TokenRequestCount { get; private set; }

    /// <summary>
    /// 记录收到的自省端点请求数。
    /// </summary>
    public int IntrospectionRequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/token", System.StringComparison.OrdinalIgnoreCase))
        {
            TokenRequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TokenResponseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        if (path.EndsWith("/introspect", System.StringComparison.OrdinalIgnoreCase))
        {
            IntrospectionRequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(IntrospectionResponseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"{{\"error\":\"unknown_endpoint\",\"path\":\"{path}\"}}", Encoding.UTF8, "application/json")
        });
    }
}
