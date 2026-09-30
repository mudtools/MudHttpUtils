// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P2-05（C5）：默认异常擦除器 —— 按敏感字段词表掩码 form / JSON 正文，且不截断、不改写非结构化文本。
/// </summary>
public class DefaultSensitiveFieldExceptionRedactorTests
{
    private static ApiException CreateException(string? requestContent, string? content)
        => new(HttpStatusCode.BadRequest, content, "https://api.example.com/x") { RequestContent = requestContent };

    [Fact]
    public void Redact_ShouldMaskJsonSensitiveFields_AndKeepOthers()
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var exception = CreateException(
            "{\"access_token\":\"secret-a\",\"page\":2}",
            "{\"error\":\"invalid_grant\",\"refresh_token\":\"secret-r\"}");

        redactor.Redact(exception);

        exception.RequestContent.Should().NotContain("secret-a");
        exception.RequestContent.Should().Contain("page");
        exception.Content.Should().NotContain("secret-r");
        exception.Content.Should().Contain("invalid_grant", "非敏感字段必须保留（排障可用性）");
    }

    [Fact]
    public void Redact_ShouldMaskNestedJsonSensitiveFields()
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var exception = CreateException(
            "{\"data\":{\"items\":[{\"password\":\"p1\"},{\"name\":\"ok\"}]}}",
            null);

        redactor.Redact(exception);

        exception.RequestContent.Should().NotContain("p1");
        exception.RequestContent.Should().Contain("ok");
    }

    [Fact]
    public void Redact_ShouldMaskFormSensitiveFields()
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var exception = CreateException("grant_type=password&client_secret=top-secret&page=2", null);

        redactor.Redact(exception);

        exception.RequestContent.Should().NotContain("top-secret");
        exception.RequestContent.Should().Contain("grant_type=password");
        exception.RequestContent.Should().Contain("page=2");
    }

    [Theory]
    [InlineData("unauthorized")]                      // 纯文本，无键值边界
    [InlineData("{\"error\":\"invalid_token\"}")]     // JSON 但无敏感字段
    [InlineData("{\"page\":2}")]
    public void Redact_ShouldLeaveNonSensitiveBodyUnchanged(string body)
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var exception = CreateException(body, body);

        redactor.Redact(exception);

        exception.RequestContent.Should().Be(body);
        exception.Content.Should().Be(body);
    }

    [Fact]
    public void Redact_ShouldNotTruncateLongBodies()
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var filler = new string('x', 5000);
        var body = $"{{\"error\":\"{filler}\"}}";
        var exception = CreateException(body, null);

        redactor.Redact(exception);

        // 与 MessageSanitizer.Sanitize 的"按 maxLength 截断"语义刻意不同：异常正文是排障依据。
        exception.RequestContent.Should().Be(body);
        exception.RequestContent!.Length.Should().BeGreaterThan(4000);
    }

    [Fact]
    public void Redact_ShouldTolerateMalformedJson()
    {
        const string malformed = "{\"access_token\":\"secret-a\"";      // 缺少闭合花括号
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var exception = CreateException(malformed, null);

        var act = () => redactor.Redact(exception);

        act.Should().NotThrow("畸形正文不得让擦除本身成为故障源");
        exception.RequestContent.Should().Be(malformed);
    }

    [Fact]
    public void Redact_ShouldHandleNullBodies()
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var exception = CreateException(null, null);

        var act = () => redactor.Redact(exception);

        act.Should().NotThrow();
        exception.Content.Should().BeNull();
        exception.RequestContent.Should().BeNull();
    }

    [Fact]
    public void Redact_ShouldHandleDeeplyNestedJson_WithoutStackOverflow()
    {
        var redactor = new DefaultSensitiveFieldExceptionRedactor();
        var body = string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "\"v\"" + new string('}', 100);
        var exception = CreateException(body, null);

        var act = () => redactor.Redact(exception);

        act.Should().NotThrow("深度护栏必须让畸形深嵌套输入安全退化");
    }

    [Fact]
    public void AddMudHttpClient_ShouldRegisterDefaultRedactor()
    {
        var services = new ServiceCollection();
        services.AddMudHttpClient("test");

        using var provider = services.BuildServiceProvider();

        provider.GetService<IExceptionRedactor>()
            .Should().BeOfType<DefaultSensitiveFieldExceptionRedactor>(
                "R-P2-05（C5）：默认注册擦除器，未注册时异常会长期持有请求/响应体明文");
    }

    [Fact]
    public void AddMudHttpClient_ShouldRespectHostRegisteredRedactor()
    {
        var services = new ServiceCollection();
        var custom = new DelegateExceptionRedactor(_ => { });
        services.AddSingleton<IExceptionRedactor>(custom);      // 宿主先注册
        services.AddMudHttpClient("test");

        using var provider = services.BuildServiceProvider();

        provider.GetService<IExceptionRedactor>().Should().BeSameAs(custom,
            "TryAddSingleton 语义：宿主显式注册优先");
    }
}
