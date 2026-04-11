// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// NamingHelper 命名辅助工具单元测试
/// </summary>
public class NamingHelperTests
{
    private readonly Type _namingHelperType;
    private readonly MethodInfo _removeInterfacePrefixMethod;
    private readonly MethodInfo _removeImpPrefixMethod;
    private readonly MethodInfo _getOrdinalComTypeMethod;
    private readonly MethodInfo _hasKnownPrefixMethod;

    public NamingHelperTests()
    {
        _namingHelperType = TestHelper.GetType("Mud.CodeGenerator.NamingHelper");
        _removeInterfacePrefixMethod = TestHelper.GetMethod(_namingHelperType, "RemoveInterfacePrefix");
        _removeImpPrefixMethod = TestHelper.GetMethod(_namingHelperType, "RemoveImpPrefix");
        _getOrdinalComTypeMethod = TestHelper.GetMethod(_namingHelperType, "GetOrdinalComType");
        _hasKnownPrefixMethod = TestHelper.GetMethod(_namingHelperType, "HasKnownPrefix");
    }

    [Fact]
    public void RemoveInterfacePrefix_WithWordPrefix_ShouldRemovePrefix()
    {
        var result = _removeInterfacePrefixMethod.Invoke(null, new object[] { "IWordDocument" });

        result.Should().Be("Document");
    }

    [Fact]
    public void RemoveInterfacePrefix_WithExcelPrefix_ShouldRemovePrefix()
    {
        var result = _removeInterfacePrefixMethod.Invoke(null, new object[] { "IExcelWorksheet" });

        result.Should().Be("Worksheet");
    }

    [Fact]
    public void RemoveInterfacePrefix_WithUnknownPrefix_ShouldReturnOriginal()
    {
        var result = _removeInterfacePrefixMethod.Invoke(null, new object[] { "ICustomInterface" });

        result.Should().Be("ICustomInterface");
    }

    [Fact]
    public void RemoveInterfacePrefix_WithNullInput_ShouldReturnNull()
    {
        var result = _removeInterfacePrefixMethod.Invoke(null, new object?[] { null });

        result.Should().BeNull();
    }

    [Fact]
    public void RemoveInterfacePrefix_WithEmptyInput_ShouldReturnEmpty()
    {
        var result = _removeInterfacePrefixMethod.Invoke(null, new object[] { "" });

        result.Should().Be("");
    }

    [Fact]
    public void RemoveImpPrefix_WithWordPrefix_ShouldRemovePrefix()
    {
        var result = _removeImpPrefixMethod.Invoke(null, new object[] { "WordDocument" });

        result.Should().Be("Document");
    }

    [Fact]
    public void RemoveImpPrefix_WithExcelPrefix_ShouldRemovePrefix()
    {
        var result = _removeImpPrefixMethod.Invoke(null, new object[] { "ExcelWorksheet" });

        result.Should().Be("Worksheet");
    }

    [Fact]
    public void RemoveImpPrefix_WithNamespace_ShouldRemoveNamespaceAndPrefix()
    {
        var result = _removeImpPrefixMethod.Invoke(null, new object[] { "MyNamespace.WordDocument" });

        result.Should().Be("Document");
    }

    [Fact]
    public void RemoveImpPrefix_WithNullInput_ShouldReturnNull()
    {
        var result = _removeImpPrefixMethod.Invoke(null, new object?[] { null });

        result.Should().BeNull();
    }

    [Fact]
    public void GetOrdinalComType_WithWordPrefix_ShouldRemovePrefix()
    {
        var result = _getOrdinalComTypeMethod.Invoke(null, new object[] { "IWordDocument" });

        result.Should().Be("Document");
    }

    [Fact]
    public void GetOrdinalComType_WithNullableType_ShouldRemoveQuestionMark()
    {
        var result = _getOrdinalComTypeMethod.Invoke(null, new object[] { "IWordDocument?" });

        result.Should().Be("Document");
    }

    [Fact]
    public void GetOrdinalComType_WithNullInput_ShouldReturnNull()
    {
        var result = _getOrdinalComTypeMethod.Invoke(null, new object?[] { null });

        result.Should().BeNull();
    }

    [Fact]
    public void HasKnownPrefix_WithWordInterfacePrefix_ShouldReturnTrue()
    {
        var result = _hasKnownPrefixMethod.Invoke(null, new object[] { "IWordDocument", true });

        result.Should().Be(true);
    }

    [Fact]
    public void HasKnownPrefix_WithWordImpPrefix_ShouldReturnTrue()
    {
        var result = _hasKnownPrefixMethod.Invoke(null, new object[] { "WordDocument", false });

        result.Should().Be(true);
    }

    [Fact]
    public void HasKnownPrefix_WithUnknownPrefix_ShouldReturnFalse()
    {
        var result = _hasKnownPrefixMethod.Invoke(null, new object[] { "ICustomInterface", true });

        result.Should().Be(false);
    }

    [Fact]
    public void HasKnownPrefix_WithNullInput_ShouldReturnFalse()
    {
        var result = _hasKnownPrefixMethod.Invoke(null, new object?[] { null, true });

        result.Should().Be(false);
    }
}
