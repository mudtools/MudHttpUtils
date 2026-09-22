// -----------------------------------------------------------------------
//  M6-HC-09 回归：SendAsResponseAsync 非 2xx 错误体读取受 MaxExceptionContentLength 约束
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M6-HC-09：<c>DefaultHttpRequestExecutor.SendAsResponseAsync</c> 原对成功/失败分支<b>无条件</b>
/// 调 <c>ReadContentAsync</c>，在 <c>maxSuccessResponseBytes = 0</c>（默认不限长）时错误响应体可被
/// 无限读取（OOM 风险），且与 <c>SendAndDeserializeAsync</c> / <c>SendAsync</c> 的限量口径不一致。
/// 修复后非 2xx 走 <c>ReadErrorContentLimitedAsync</c>。
/// </summary>
public class ResponseErrorContentLimitTests
{
    private const int MaxExceptionContentLength = 1024;
    private const string TruncatedSuffix = "...[已截断]";

    private static (DefaultHttpRequestExecutor Executor, Mock<IBaseHttpClient> Client) Create(
        HttpStatusCode statusCode, string body, int maxExceptionContentLength = MaxExceptionContentLength)
    {
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            maxExceptionContentLength: maxExceptionContentLength);
        var client = new Mock<IBaseHttpClient>();
        client.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        return (executor, client);
    }

    private static HttpRequestMessage Request() =>
        new(HttpMethod.Get, new Uri("https://api.example.com/test"));

    private static ResponseDescriptor JsonDescriptor() =>
        new() { ResponseContentType = "application/json" };

    [Fact]
    public async Task NonSuccess_OversizedErrorBody_ErrorContentIsLimitedAndTruncated()
    {
        // maxSuccessResponseBytes 保持默认 0（不限长）；错误体 10 KB 必须受 1024 字符上限约束
        var (executor, client) = Create(HttpStatusCode.BadRequest, new string('E', 10 * 1024));

        var response = await executor.SendAsResponseAsync<string>(
            Request(), client.Object, JsonDescriptor(), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.ErrorContent.Should().NotBeNull();
        response.ErrorContent!.Should().EndWith(TruncatedSuffix,
            "HC-09：超限错误体应在读取阶段限量并追加截断标记");
        response.ErrorContent.Length.Should().Be(MaxExceptionContentLength + TruncatedSuffix.Length,
            "HC-09：错误体只读入上限字符 + 截断后缀，其余内容不得进入内存");
    }

    [Fact]
    public async Task NonSuccess_ErrorBodyWithinLimit_NotTruncated()
    {
        // 未超限：原样返回，不追加截断标记
        var body = new string('E', 100);
        var (executor, client) = Create(HttpStatusCode.InternalServerError, body);

        var response = await executor.SendAsResponseAsync<string>(
            Request(), client.Object, JsonDescriptor(), null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.ErrorContent.Should().Be(body);
        response.ErrorContent.Should().NotContain(TruncatedSuffix);
    }

    [Fact]
    public async Task NonSuccess_ZeroLimit_ReadsFullErrorBody()
    {
        // maxExceptionContentLength <= 0 = 不限制（显式关闭三态语义），错误体全量读取
        var body = new string('E', 10 * 1024);
        var (executor, client) = Create(HttpStatusCode.BadGateway, body, maxExceptionContentLength: 0);

        var response = await executor.SendAsResponseAsync<string>(
            Request(), client.Object, JsonDescriptor(), null);

        response.ErrorContent.Should().Be(body);
    }

    [Fact]
    public async Task Success_OversizedBody_NotConstrainedByExceptionLimit()
    {
        // 成功路径不受 MaxExceptionContentLength 约束（无 MaxSuccessResponseBytes 守卫时全量读取）
        var payload = new string('a', 5 * 1024);
        var json = "{\"Name\":\"" + payload + "\",\"Id\":1}";
        var (executor, client) = Create(HttpStatusCode.OK, json);

        var response = await executor.SendAsResponseAsync<SuccessUser>(
            Request(), client.Object, JsonDescriptor(), null);

        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Should().NotBeNull();
        response.Content!.Name.Should().Be(payload);
        response.Content.Name.Length.Should().Be(5 * 1024,
            "成功路径不得受错误体上限影响（截断会破坏反序列化）");
    }

    public sealed class SuccessUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}