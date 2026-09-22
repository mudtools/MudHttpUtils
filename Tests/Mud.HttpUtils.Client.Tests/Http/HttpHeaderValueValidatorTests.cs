using System.Collections.Generic;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// M6-HC-24：<see cref="HttpHeaderValueValidator"/> 头值控制字符校验。
/// </summary>
/// <remarks>
/// 校验口径：拒绝全部 C0 控制字符（U+0000-U+001F）与 DEL（U+007F），仅保留 HTAB（<c>\t</c>）；
/// null / 空串视为合法。
/// </remarks>
public class HttpHeaderValueValidatorTests
{
    [Fact]
    public void IsValid_Null_ReturnsTrue()
    {
        HttpHeaderValueValidator.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_Empty_ReturnsTrue()
    {
        HttpHeaderValueValidator.IsValid(string.Empty).Should().BeTrue();
    }

    [Theory]
    [InlineData("Bearer abc123")]
    [InlineData("application/json")]
    [InlineData("~!@#$%^&*()_+-=[]{}|;':\",./<>?")]
    [InlineData("中文头值")]
    public void IsValid_NormalText_ReturnsTrue(string value)
    {
        HttpHeaderValueValidator.IsValid(value).Should().BeTrue();
    }

    [Fact]
    public void IsValid_Tab_ReturnsTrue()
    {
        // HTAB 可作为可选空白保留（RFC 7230 field-value 与 .NET 5+ 实现一致）。
        HttpHeaderValueValidator.IsValid("a\tb").Should().BeTrue();
    }

    [Fact]
    public void IsValid_Space_ReturnsTrue()
    {
        HttpHeaderValueValidator.IsValid(" ").Should().BeTrue();
    }

    public static IEnumerable<object[]> InvalidControlCharacters => new[]
    {
        new object[] { "\r" },
        new object[] { "\n" },
        new object[] { "\r\n" },
        new object[] { "\u0000" },
        new object[] { "\u001F" },
        new object[] { "\u007F" },
    };

    [Theory]
    [MemberData(nameof(InvalidControlCharacters))]
    public void IsValid_BareControlCharacter_ReturnsFalse(string value)
    {
        HttpHeaderValueValidator.IsValid(value).Should().BeFalse();
    }

    [Theory]
    [InlineData("Bearer abc\u0000def")]
    [InlineData("Bearer abc\rdef")]
    [InlineData("Bearer abc\ndef")]
    [InlineData("Bearer abc\u007Fdef")]
    [InlineData("abc\u001F")]
    public void IsValid_ControlCharacterEmbeddedInText_ReturnsFalse(string value)
    {
        // 逐字符核对：混在正常文本中间的控制字符同样必须被拒绝（原实现仅查 CR/LF 会漏检）。
        HttpHeaderValueValidator.IsValid(value).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x1F, false)]
    [InlineData(0x20, true)]
    [InlineData(0x7E, true)]
    [InlineData(0x7F, false)]
    public void IsValid_AsciiBoundary_MatchesRfc7230(int codePoint, bool expected)
    {
        HttpHeaderValueValidator.IsValid(((char)codePoint).ToString()).Should().Be(expected);
    }

    // 关于 TokenRecoveryExecutor.ApplyTokenToRequest 的 Header/ApiKey 分支：
    // 该分支虽已调用 HttpHeaderValueValidator.IsValid 复核，但其上游 IsSafeTokenValue 更严
    // （拒绝全部 C0/DEL，无 HTAB 例外），含控制字符的令牌在到达该分支前即被拦截。
    // 因此「控制字符令牌不被注入请求头」的集成断言不可达，此处不构造死测试；
    // IsValid 的控制字符收敛属防御性加固（与 netstandard2.0 HttpClient 不校验头值的平台差异对齐）。
}