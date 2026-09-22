// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud.HttpUtils 2025
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Globalization;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// M6-HC-18：<see cref="DefaultUrlParameterFormatter"/> 的 <see cref="IFormattable"/> 回退路径
/// 必须固定使用 <see cref="CultureInfo.InvariantCulture"/>，查询串不得随 CurrentCulture 漂移。
/// </summary>
/// <remarks>
/// 覆盖 ru-RU（小数分隔符为 ','）与 ar-SA（非拉丁历法/数字）两种代表性文化；
/// 每个用例在 <c>try/finally</c> 中恢复原 culture，避免污染同程序集其它测试。
/// </remarks>
public class CultureInvariantQueryTests
{
    private static readonly decimal DecimalValue = 1.5m;

    private static readonly DateTime DateTimeValue =
        new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("ar-SA")]
    public void DefaultUrlParameterFormatter_Decimal_IsCultureInvariant(string cultureName)
    {
        var formatter = new DefaultUrlParameterFormatter();
        var expected = DecimalValue.ToString(null, CultureInfo.InvariantCulture);

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            var actual = formatter.Format(DecimalValue, null, typeof(decimal));

            // 与 Invariant 完全一致；ru-RU 下若走 CurrentCulture 会得到 "1,5"。
            actual.Should().Be(expected);
            actual.Should().Contain(".");
            actual.Should().NotContain(",");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("ar-SA")]
    public void DefaultUrlParameterFormatter_DateTime_IsCultureInvariant(string cultureName)
    {
        var formatter = new DefaultUrlParameterFormatter();
        var expected = DateTimeValue.ToString(null, CultureInfo.InvariantCulture);

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            var actual = formatter.Format(DateTimeValue, null, typeof(DateTime));

            // ar-SA 下若走 CurrentCulture，历法（UmAlQura）与年份都会变化。
            actual.Should().Be(expected);
            actual.Should().Contain("2026");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 基线：不切换 culture 时同样产出 Invariant 串（防止测试仅在切换后自证）。
    /// </summary>
    [Fact]
    public void DefaultUrlParameterFormatter_WithoutCultureOverride_UsesInvariantFormat()
    {
        var formatter = new DefaultUrlParameterFormatter();

        formatter.Format(DecimalValue, null, typeof(decimal))
            .Should().Be(DecimalValue.ToString(null, CultureInfo.InvariantCulture));

        formatter.Format(DateTimeValue, null, typeof(DateTime))
            .Should().Be(DateTimeValue.ToString(null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 端到端口径：经 <see cref="QueryParameterBuilder"/> 落串后同样不含区域敏感分隔符。
    /// </summary>
    [Theory]
    [InlineData("ru-RU")]
    [InlineData("ar-SA")]
    public void QueryParameterBuilder_Decimal_IsCultureInvariant(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            var builder = new QueryParameterBuilder();
            builder.Add("amount", DecimalValue, formatString: null);

            builder.Build().Should().Contain("1.5");
            builder.Build().Should().NotContain("1,5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}