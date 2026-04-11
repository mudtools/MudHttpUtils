// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Tests;

/// <summary>
/// MessageSanitizer 消息脱敏工具单元测试
/// </summary>
public class MessageSanitizerTests
{
    #region Sanitize Tests

    [Fact]
    public void Sanitize_WithNullMessage_ShouldReturnNull()
    {
        var result = MessageSanitizer.Sanitize(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Sanitize_WithEmptyMessage_ShouldReturnEmpty()
    {
        var result = MessageSanitizer.Sanitize(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WithWhitespaceMessage_ShouldReturnWhitespace()
    {
        var result = MessageSanitizer.Sanitize("   ");

        result.Should().Be("   ");
    }

    [Fact]
    public void Sanitize_WithValidJson_ShouldSanitizeSensitiveFields()
    {
        var json = @"{""token"":""abc123def456"",""username"":""testuser""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("\"token\"");
        result.Should().Contain("\"username\"");
        result.Should().NotContain("abc123def456");
    }

    [Fact]
    public void Sanitize_WithPhoneField_ShouldMaskPhone()
    {
        var json = @"{""phone"":""13812345678""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("138****5678");
    }

    [Fact]
    public void Sanitize_WithEmailField_ShouldMaskEmail()
    {
        var json = @"{""email"":""test@example.com""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("t***@example.com");
    }

    [Fact]
    public void Sanitize_WithNameField_ShouldMaskName()
    {
        var json = @"{""real_name"":""张三""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("张三");
    }

    [Fact]
    public void Sanitize_WithPassword_ShouldMaskPassword()
    {
        var json = @"{""password"":""mypassword123""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("mypassword123");
    }

    [Fact]
    public void Sanitize_WithAccessToken_ShouldMaskToken()
    {
        var json = @"{""access_token"":""eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
    }

    [Fact]
    public void Sanitize_WithNestedJson_ShouldSanitizeNestedFields()
    {
        var json = @"{""user"":{""token"":""secret123"",""name"":""John""}}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("\"token\"");
        result.Should().Contain("\"name\"");
        result.Should().NotContain("secret123");
    }

    [Fact]
    public void Sanitize_WithJsonArray_ShouldSanitizeArrayElements()
    {
        var json = @"[{""token"":""token1""},{""token"":""token2""}]";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("token1");
        result.Should().NotContain("token2");
    }

    [Fact]
    public void Sanitize_WithNonJsonText_ShouldSanitizePlainText()
    {
        var text = "token: abc123def456";

        var result = MessageSanitizer.Sanitize(text);

        result.Should().NotContain("abc123def456");
    }

    [Fact]
    public void Sanitize_WithMaxLength_ShouldTruncateResult()
    {
        var longJson = @"{""token"":""" + new string('a', 1000) + @"""}";
        var maxLength = 100;

        var result = MessageSanitizer.Sanitize(longJson, maxLength);

        result.Length.Should().BeLessOrEqualTo(maxLength + 3);
    }

    [Fact]
    public void Sanitize_WithApiKey_ShouldMaskApiKey()
    {
        var json = @"{""api_key"":""sk-1234567890abcdef""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("sk-1234567890abcdef");
    }

    [Fact]
    public void Sanitize_WithSecretField_ShouldMaskSecret()
    {
        var json = @"{""secret"":""mysecretvalue""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("mysecretvalue");
    }

    [Fact]
    public void Sanitize_WithIdCard_ShouldMaskIdCard()
    {
        var json = @"{""id_card"":""110101199001011234""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("110101199001011234");
    }

    [Fact]
    public void Sanitize_WithBankCard_ShouldMaskBankCard()
    {
        var json = @"{""card_no"":""6222021234567890123""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("6222021234567890123");
    }

    [Fact]
    public void Sanitize_WithMultipleSensitiveFields_ShouldMaskAll()
    {
        var json = @"{""token"":""abc123"",""password"":""pass123"",""phone"":""13812345678"",""email"":""test@example.com""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("abc123");
        result.Should().NotContain("pass123");
        result.Should().Contain("138****5678");
        result.Should().Contain("t***@example.com");
    }

    [Fact]
    public void Sanitize_WithNonSensitiveField_ShouldNotModify()
    {
        var json = @"{""department"":""engineering"",""count"":100}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("engineering");
        result.Should().Contain("100");
    }

    #endregion
}
