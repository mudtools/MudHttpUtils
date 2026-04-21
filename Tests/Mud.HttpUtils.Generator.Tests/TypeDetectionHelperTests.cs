// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们承担任何责任！
// -----------------------------------------------------------------------

using Mud.CodeGenerator;

namespace Mud.HttpUtils.Generator.Tests;

public class TypeDetectionHelperTests
{
    #region IsCancellationToken Tests

    [Fact]
    public void IsCancellationToken_WithCancellationTokenType_ShouldReturnTrue()
    {
        TypeDetectionHelper.IsCancellationToken("CancellationToken").Should().BeTrue();
    }

    [Fact]
    public void IsCancellationToken_WithFullyQualifiedCancellationTokenType_ShouldReturnTrue()
    {
        TypeDetectionHelper.IsCancellationToken("System.Threading.CancellationToken").Should().BeTrue();
    }

    [Fact]
    public void IsCancellationToken_WithNullableCancellationTokenType_ShouldReturnTrue()
    {
        TypeDetectionHelper.IsCancellationToken("CancellationToken?").Should().BeTrue();
    }

    [Fact]
    public void IsCancellationToken_WithStringType_ShouldReturnFalse()
    {
        TypeDetectionHelper.IsCancellationToken("string").Should().BeFalse();
    }

    [Fact]
    public void IsCancellationToken_WithIntType_ShouldReturnFalse()
    {
        TypeDetectionHelper.IsCancellationToken("int").Should().BeFalse();
    }

    [Fact]
    public void IsCancellationToken_WithCaseInsensitiveMatch_ShouldReturnTrue()
    {
        TypeDetectionHelper.IsCancellationToken("cancellationtoken").Should().BeTrue();
    }

    #endregion

    #region IsSimpleType Tests

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("DateTime")]
    [InlineData("Guid")]
    [InlineData("DateTimeOffset")]
    [InlineData("TimeSpan")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("char")]
    public void IsSimpleType_WithSimpleTypes_ShouldReturnTrue(string typeName)
    {
        TypeDetectionHelper.IsSimpleType(typeName).Should().BeTrue();
    }

    [Theory]
    [InlineData("List<string>")]
    [InlineData("Dictionary<string,object>")]
    [InlineData("MyCustomClass")]
    [InlineData("CancellationToken")]
    public void IsSimpleType_WithComplexTypes_ShouldReturnFalse(string typeName)
    {
        TypeDetectionHelper.IsSimpleType(typeName).Should().BeFalse();
    }

    [Fact]
    public void IsSimpleType_WithNullableSimpleType_ShouldReturnTrue()
    {
        TypeDetectionHelper.IsSimpleType("int?").Should().BeTrue();
    }

    [Fact]
    public void IsSimpleType_WithSimpleArrayType_ShouldReturnTrue()
    {
        TypeDetectionHelper.IsSimpleType("string[]").Should().BeTrue();
    }

    [Fact]
    public void IsSimpleType_WithComplexArrayType_ShouldReturnFalse()
    {
        TypeDetectionHelper.IsSimpleType("List<string>[]").Should().BeFalse();
    }

    #endregion
}
