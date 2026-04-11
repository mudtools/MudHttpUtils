// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// ValidationResult 验证结果单元测试
/// </summary>
public class ValidationResultTests
{
    private readonly Type _validationResultType;
    private readonly MethodInfo _successMethod;
    private readonly MethodInfo _errorMethod;

    public ValidationResultTests()
    {
        _validationResultType = TestHelper.GetType("Mud.HttpUtils.Validators.ValidationResult");
        _successMethod = TestHelper.GetMethod(_validationResultType, "Success");
        _errorMethod = TestHelper.GetMethod(_validationResultType, "Error");
    }

    [Fact]
    public void Success_ShouldReturnValidResult()
    {
        var result = _successMethod.Invoke(null, null);

        result.Should().NotBeNull();
        var isValidProperty = _validationResultType.GetProperty("IsValid");
        var isValid = (bool)isValidProperty!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Error_WithMessage_ShouldReturnInvalidResult()
    {
        var errorMessage = "Test error message";

        var result = _errorMethod.Invoke(null, new object[] { errorMessage });

        result.Should().NotBeNull();
        var isValidProperty = _validationResultType.GetProperty("IsValid");
        var isValid = (bool)isValidProperty!.GetValue(result)!;
        isValid.Should().BeFalse();

        var errorMessageProperty = _validationResultType.GetProperty("ErrorMessage");
        var actualMessage = (string?)errorMessageProperty!.GetValue(result);
        actualMessage.Should().Be(errorMessage);
    }

    [Fact]
    public void Error_WithNullMessage_ShouldReturnInvalidResult()
    {
        var result = _errorMethod.Invoke(null, new object?[] { null });

        result.Should().NotBeNull();
        var isValidProperty = _validationResultType.GetProperty("IsValid");
        var isValid = (bool)isValidProperty!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Error_WithEmptyMessage_ShouldReturnInvalidResult()
    {
        var result = _errorMethod.Invoke(null, new object[] { "" });

        result.Should().NotBeNull();
        var isValidProperty = _validationResultType.GetProperty("IsValid");
        var isValid = (bool)isValidProperty!.GetValue(result)!;
        isValid.Should().BeFalse();
    }
}
