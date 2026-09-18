// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// AppKeyValidator 的单元测试。
/// 覆盖格式校验（接受/拒绝）与 ToSafeText 安全转换。
/// </summary>
public class AppKeyValidatorTests
{
    #region Validate - 接受合法标识

    [Theory]
    [InlineData("app1")]
    [InlineData("App-A")]
    [InlineData("app-1.a_b")]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("A.B.C-D_E")]
    public void Validate_AcceptsLegalAppKey(string appKey)
    {
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsMaxLength()
    {
        // 恰好 MaxLength 个字符
        var appKey = new string('a', AppKeyValidator.MaxLength);
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().NotThrow();
    }

    #endregion

    #region Validate - 拒绝非法标识

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsNullOrWhiteSpace(string? appKey)
    {
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey));
    }

    [Fact]
    public void Validate_RejectsOverMaxLength()
    {
        var appKey = new string('a', AppKeyValidator.MaxLength + 1);
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey))
            .WithMessage($"*{AppKeyValidator.MaxLength}*");
    }

    [Theory]
    [InlineData("a b", ' ')]
    [InlineData("1:2", ':')]
    [InlineData("app/a", '/')]
    [InlineData("app\\test", '\\')]
    [InlineData("app;drop", ';')]
    [InlineData("app\x01", '\x01')]
    public void Validate_RejectsIllegalCharacters(string appKey, char illegalChar)
    {
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey));

        // 数据驱动的前提断言：确保用例确实覆盖了目标非法字符（此前该参数未参与断言）。
        appKey.Should().Contain(illegalChar.ToString(), "用例数据本身应包含待验证的非法字符");
    }

    [Fact]
    public void Validate_RejectsCRLFInjection()
    {
        var appKey = "app\r\nX-Injected: evil";
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("_")]
    [InlineData(".abc")]
    [InlineData("-abc")]
    [InlineData("_abc")]
    public void Validate_RejectsLeadingSeparator(string appKey)
    {
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey));
    }

    [Fact]
    public void Validate_RejectsUnicodeCharacters()
    {
        var appKey = "应用1";
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey));
    }

    [Fact]
    public void Validate_RejectsTabCharacter()
    {
        var appKey = "app\t1";
        var act = () => AppKeyValidator.Validate(appKey, nameof(appKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(appKey));
    }

    #endregion

    #region Validate - 参数名

    [Fact]
    public void Validate_UsesProvidedParamName()
    {
        var act = () => AppKeyValidator.Validate(null, "myParam");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("myParam");
    }

    #endregion

    #region ToSafeText

    [Fact]
    public void ToSafeText_ReturnsEmptyForNull()
    {
        AppKeyValidator.ToSafeText(null).Should().BeEmpty();
    }

    [Fact]
    public void ToSafeText_ReturnsEmptyForEmptyString()
    {
        AppKeyValidator.ToSafeText(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ToSafeText_ReturnsUnmodifiedForLegalAppKey()
    {
        var appKey = "app-1.a_b";
        AppKeyValidator.ToSafeText(appKey).Should().Be(appKey);
    }

    [Fact]
    public void ToSafeText_ReplacesControlCharacters()
    {
        var appKey = "app\r\nX";
        var safe = AppKeyValidator.ToSafeText(appKey);
        safe.Should().NotContain("\r");
        safe.Should().NotContain("\n");
        safe.Should().Contain("app");
        safe.Should().HaveLength(appKey.Length);
    }

    [Fact]
    public void ToSafeText_ReplacesDelCharacter()
    {
        var appKey = "app\x7F";
        var safe = AppKeyValidator.ToSafeText(appKey);
        safe.Should().NotContain("\x7F");
        safe.Should().HaveLength(appKey.Length);
    }

    [Fact]
    public void ToSafeText_TruncatesOverMax()
    {
        var appKey = new string('a', AppKeyValidator.MaxLength + 50);
        var safe = AppKeyValidator.ToSafeText(appKey);
        safe.Should().HaveLength(AppKeyValidator.MaxLength);
    }

    [Fact]
    public void ToSafeText_ReplacesAllControlChars()
    {
        // 所有控制字符（0x00-0x1F + 0x7F）都应被替换为 '_'
        for (char c = '\x00'; c <= '\x1F'; c++)
        {
            var appKey = $"app{c}";
            var safe = AppKeyValidator.ToSafeText(appKey);
            safe[3].Should().Be('_', "control char 0x{0:X2} should be replaced", (int)c);
        }

        var del = AppKeyValidator.ToSafeText("app\x7F");
        del[3].Should().Be('_', "DEL char should be replaced");
    }

    [Fact]
    public void ToSafeText_PreservesLegalSeparators()
    {
        var appKey = "app-1.a_b";
        var safe = AppKeyValidator.ToSafeText(appKey);
        safe.Should().Be(appKey);
    }

    #endregion
}
