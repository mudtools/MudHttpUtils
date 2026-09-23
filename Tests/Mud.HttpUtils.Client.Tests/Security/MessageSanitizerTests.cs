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

    [Fact]
    public void Sanitize_WithNameField_ShouldMaskAsNameNotAsToken()
    {
        // P3（M6 阶段五）：通用键 `name` 已收窄，改用明确的姓名键名验证「按姓名掩码」语义。
        var json = @"{""user_name"":""张三""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotBeEquivalentTo(json);
        result.Should().NotContain("张三");
    }

    [Fact]
    public void Sanitize_WithGenericNameField_ShouldNotOverMask()
    {
        // P3（M6 阶段五）：`name` 过于宽泛（产品名 / 城市名等），不再整体掩码。
        var json = @"{""name"":""Beijing""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("Beijing");
    }

    [Fact]
    public void Sanitize_WithShortPlainWord_ShouldNotBeTreatedAsToken()
    {
        // P3（M6 阶段五）：Base64 启发式加「长度 ≥16 且带 = 填充」约束后，
        // "testuser"（8 字符、长度恰为 4 的倍数）不再被误判为令牌而整体掩码。
        var json = @"{""label"":""testuser""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("testuser");
    }

    [Theory]
    [InlineData("pwd")]
    [InlineData("credential")]
    [InlineData("sessionid")]
    [InlineData("bearer")]
    [InlineData("sign")]
    [InlineData("auth")]
    public void Sanitize_WithNewlyAddedCredentialField_ShouldMaskValue(string fieldName)
    {
        // P3（M6 阶段五）：补齐易漏的凭据类键名。
        var json = $@"{{""{fieldName}"":""s3cr3t-value-123456""}}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("s3cr3t-value-123456");
    }

    [Theory]
    [InlineData("auth_code")]
    [InlineData("verify_code")]
    [InlineData("sms_code")]
    [InlineData("captcha")]
    [InlineData("otp")]
    public void Sanitize_WithNarrowedCodeVariant_ShouldMaskValue(string fieldName)
    {
        // P3（M6 阶段五）：`code` 收窄后，具体变体仍需掩码。
        var json = $@"{{""{fieldName}"":""a1b2c3d4""}}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("a1b2c3d4");
    }

    [Fact]
    public void Sanitize_WithGenericCodeField_ShouldNotOverMask()
    {
        // P3（M6 阶段五）：通用键 `code` 已收窄（业务编码不应被掩码）。
        var json = @"{""code"":""PRODUCT-0001""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("PRODUCT-0001");
    }

    [Fact]
    public void Sanitize_WithFieldNameContainingName_ShouldNotOverMask()
    {
        var json = @"{""file_name"":""report.pdf""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().Contain("report.pdf");
    }

    [Fact]
    public void Sanitize_WithRealNameField_ShouldMaskAsName()
    {
        var json = @"{""realName"":""李四""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotBeEquivalentTo(json);
        result.Should().NotContain("李四");
    }

    [Fact]
    public void Sanitize_WithAddressField_ShouldMaskValue()
    {
        // P3（M6 阶段五）：通用键 `address` 收窄为具体变体（网络地址等场景常直接叫 address）。
        var json = @"{""home_address"":""北京市朝阳区某某路123号""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("北京市朝阳区某某路123号");
    }

    [Fact]
    public void Sanitize_WithPassportField_ShouldMaskValue()
    {
        var json = @"{""passport"":""E12345678""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("E12345678");
    }

    [Fact]
    public void Sanitize_WithDriverLicenseField_ShouldMaskValue()
    {
        var json = @"{""driver_license"":""110101199001011234""}";

        var result = MessageSanitizer.Sanitize(json);

        result.Should().NotContain("110101199001011234");
    }

    #endregion
}
