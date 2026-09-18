using System.Net;
using Mud.HttpUtils;
using Mud.HttpUtils.Testing;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

public class DiagCaptureTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    [Fact]
    public async Task Diag_CaptureFlow()
    {
        var stub = new StubHttp();
        stub.RespondToAnyRequest(HttpStatusCode.InternalServerError, "err");
        using var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };
        string? redactorSawContent = null;
        string? redactorSawRequestContent = null;
        var client = new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions
        {
            CaptureRequestContent = true,
            ExceptionRedactor = new DelegateExceptionRedactor(ex =>
            {
                redactorSawContent = ex.Content;
                redactorSawRequestContent = ex.RequestContent;
            }),
        });

        var act = () => client.PostAsJsonAsync<object, string>("/api/values", new { name = "test" });
        var ex = await act.Should().ThrowAsync<ApiException>();

        ex.Which.RequestContent.Should().Be("""{"name":"test"}""");
        redactorSawContent.Should().Be("err");
        redactorSawRequestContent.Should().Be("""{"name":"test"}""");
    }

    public void Dispose() => _fixture.RestoreDomains();
}
