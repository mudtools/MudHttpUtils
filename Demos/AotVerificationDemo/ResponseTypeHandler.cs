using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AotVerificationDemo;

/// <summary>
/// 假 <see cref="HttpMessageHandler"/>，返回固定的 <c>UserDto</c> JSON 响应，
/// 用于验证 <see cref="Response{T}"/> 包装路径在 Native AOT 下正确反序列化。
/// </summary>
/// <remarks>
/// 该 handler 不依赖真实服务器，使冒烟测试可在离线/CI 环境稳定运行。
/// 返回的 JSON 仅含 <c>UserDto</c> 字段（Id/Name/Email），
/// 与 <c>AppJsonContext</c> 中声明的类型元数据一致。
/// </remarks>
internal sealed class ResponseTypeHandler : HttpMessageHandler
{
    private const string Json = "{\"id\":1,\"name\":\"AOT Resp\",\"email\":\"resp@example.com\"}";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Json, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
