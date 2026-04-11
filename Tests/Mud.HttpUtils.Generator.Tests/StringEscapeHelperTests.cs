// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// StringEscapeHelper 字符串转义辅助工具单元测试
/// </summary>
public class StringEscapeHelperTests
{
    private readonly Type _stringEscapeHelperType;
    private readonly MethodInfo _escapeStringMethod;
    private readonly MethodInfo _escapeCharMethod;
    private readonly MethodInfo _normalizeEventTypeMethod;

    public StringEscapeHelperTests()
    {
        _stringEscapeHelperType = TestHelper.GetType("Mud.CodeGenerator.StringEscapeHelper");
        _escapeStringMethod = TestHelper.GetMethod(_stringEscapeHelperType, "EscapeString");
        _escapeCharMethod = TestHelper.GetMethod(_stringEscapeHelperType, "EscapeChar");
        _normalizeEventTypeMethod = TestHelper.GetMethod(_stringEscapeHelperType, "NormalizeEventType");
    }

    #region EscapeString Tests

    [Fact]
    public void EscapeString_WithBackslash_ShouldEscapeBackslash()
    {
        var input = "test\\path";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("test\\\\path");
    }

    [Fact]
    public void EscapeString_WithDoubleQuote_ShouldEscapeDoubleQuote()
    {
        var input = "test\"value";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("test\\\"value");
    }

    [Fact]
    public void EscapeString_WithNewline_ShouldEscapeNewline()
    {
        var input = "test\nvalue";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("test\\nvalue");
    }

    [Fact]
    public void EscapeString_WithTab_ShouldEscapeTab()
    {
        var input = "test\tvalue";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("test\\tvalue");
    }

    [Fact]
    public void EscapeString_WithCarriageReturn_ShouldEscapeCarriageReturn()
    {
        var input = "test\rvalue";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("test\\rvalue");
    }

    [Fact]
    public void EscapeString_WithMultipleSpecialChars_ShouldEscapeAll()
    {
        var input = "test\n\r\t\"path\\";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("test\\n\\r\\t\\\"path\\\\");
    }

    [Fact]
    public void EscapeString_WithNoSpecialChars_ShouldReturnOriginal()
    {
        var input = "normal text";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("normal text");
    }

    [Fact]
    public void EscapeString_WithEmptyString_ShouldReturnEmpty()
    {
        var input = "";

        var result = (string)_escapeStringMethod.Invoke(null, new object[] { input })!;

        result.Should().Be("");
    }

    #endregion

    #region EscapeChar Tests

    [Theory]
    [InlineData('\\', "\\\\")]
    [InlineData('\'', "\\'")]
    [InlineData('\n', "\\n")]
    [InlineData('\r', "\\r")]
    [InlineData('\t', "\\t")]
    [InlineData('a', "a")]
    [InlineData('Z', "Z")]
    [InlineData('1', "1")]
    public void EscapeChar_WithVariousChars_ShouldReturnExpectedResult(char input, string expected)
    {
        var result = (string)_escapeCharMethod.Invoke(null, new object[] { input })!;

        result.Should().Be(expected);
    }

    #endregion

    #region NormalizeEventType Tests

    [Fact]
    public void NormalizeEventType_WithNullString_ShouldReturnEmptyLiteral()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object?[] { null })!;

        result.Should().Be("\"\"");
    }

    [Fact]
    public void NormalizeEventType_WithEmptyString_ShouldReturnEmptyLiteral()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "" })!;

        result.Should().Be("\"\"");
    }

    [Fact]
    public void NormalizeEventType_WithPlainString_ShouldWrapInQuotes()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "event.type" })!;

        result.Should().Be("\"event.type\"");
    }

    [Fact]
    public void NormalizeEventType_WithAlreadyQuotedString_ShouldReturnAsIs()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "\"event.type\"" })!;

        result.Should().Be("\"event.type\"");
    }

    [Fact]
    public void NormalizeEventType_WithSingleQuotedString_ShouldReturnAsIs()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "'event.type'" })!;

        result.Should().Be("'event.type'");
    }

    [Fact]
    public void NormalizeEventType_WithSpecialChars_ShouldEscapeAndWrap()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "event\ntype" })!;

        result.Should().Be("\"event\\ntype\"");
    }

    [Fact]
    public void NormalizeEventType_WithQuotesInside_ShouldEscape()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "\"event\"type\"" })!;

        result.Should().Contain("\\\"");
    }

    [Fact]
    public void NormalizeEventType_WithWhitespace_ShouldTrimAndWrap()
    {
        var result = (string)_normalizeEventTypeMethod.Invoke(null, new object[] { "  event.type  " })!;

        result.Should().Be("\"event.type\"");
    }

    #endregion
}
