using Mud.HttpUtils.Validators;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// ParameterValidationHelper 单元测试
/// 验证参数验证代码生成的正确性
/// </summary>
public class ParameterValidationHelperTests
{
    private static StringBuilder BuildValidationCode(List<ParameterInfo> parameters)
    {
        var codeBuilder = new StringBuilder();
        ParameterValidationHelper.GenerateParameterValidation(codeBuilder, parameters);
        return codeBuilder;
    }

    #region string 参数验证

    [Fact]
    public void GenerateParameterValidation_NonNullableString_GeneratesIsNullOrWhiteSpaceAndTrim()
    {
        // 非空 string 参数应生成 IsNullOrWhiteSpace 检查 + Trim
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "name", Type = "string", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().Contain("string.IsNullOrWhiteSpace(name)");
        code.Should().Contain("throw new ArgumentNullException(nameof(name))");
        code.Should().Contain("name = name!.Trim()");
        // 不应包含 IsNullOrEmpty（已改用 IsNullOrWhiteSpace）
        code.Should().NotContain("string.IsNullOrEmpty(name)");
    }

    [Fact]
    public void GenerateParameterValidation_NullableString_NoValidationGenerated()
    {
        // string? 参数不应生成验证
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "name", Type = "string?", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().BeEmpty();
    }

    #endregion

    #region Body 参数验证

    [Fact]
    public void GenerateParameterValidation_BodyParameter_GeneratesNullCheck()
    {
        var parameters = new List<ParameterInfo>
        {
            new()
            {
                Name = "data", Type = "UserData",
                Attributes = [new ParameterAttributeInfo { Name = "BodyAttribute" }]
            }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().Contain("if (data == null)");
        code.Should().Contain("throw new ArgumentNullException(nameof(data))");
    }

    [Fact]
    public void GenerateParameterValidation_NullableBodyParameter_GeneratesNullCheck()
    {
        // Body 参数即使是可空类型也需要验证
        var parameters = new List<ParameterInfo>
        {
            new()
            {
                Name = "data", Type = "UserData?",
                Attributes = [new ParameterAttributeInfo { Name = "BodyAttribute" }]
            }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().Contain("if (data == null)");
    }

    #endregion

    #region 引用类型参数验证

    [Fact]
    public void GenerateParameterValidation_NonNullableReferenceType_GeneratesNullCheck()
    {
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "filter", Type = "QueryFilter", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().Contain("if (filter == null)");
        code.Should().Contain("throw new ArgumentNullException(nameof(filter))");
    }

    [Fact]
    public void GenerateParameterValidation_NullableReferenceType_NoValidationGenerated()
    {
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "filter", Type = "QueryFilter?", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().BeEmpty();
    }

    #endregion

    #region 简单类型参数验证

    [Fact]
    public void GenerateParameterValidation_SimpleType_NoValidationGenerated()
    {
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "page", Type = "int", Attributes = [] },
            new() { Name = "size", Type = "int", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().BeEmpty();
    }

    [Fact]
    public void GenerateParameterValidation_CancellationToken_NoValidationGenerated()
    {
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "cancellationToken", Type = "CancellationToken", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        code.Should().BeEmpty();
    }

    #endregion

    #region 多参数混合验证

    [Fact]
    public void GenerateParameterValidation_MixedParameters_GeneratesCorrectValidations()
    {
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "keyword", Type = "string", Attributes = [] },
            new() { Name = "page", Type = "int", Attributes = [] },
            new()
            {
                Name = "data", Type = "UserData",
                Attributes = [new ParameterAttributeInfo { Name = "BodyAttribute" }]
            },
            new() { Name = "cancellationToken", Type = "CancellationToken", Attributes = [] }
        };

        var code = BuildValidationCode(parameters).ToString();

        // keyword (string) -> IsNullOrWhiteSpace + Trim
        code.Should().Contain("string.IsNullOrWhiteSpace(keyword)");
        // page (int) -> 不验证
        code.Should().NotContain("page");
        // data (Body) -> null check
        code.Should().Contain("if (data == null)");
        // cancellationToken -> 不验证
        code.Should().NotContain("cancellationToken");
    }

    #endregion

    #region 空参数列表

    [Fact]
    public void GenerateParameterValidation_EmptyParameters_GeneratesNothing()
    {
        var code = BuildValidationCode([]).ToString();

        code.Should().BeEmpty();
    }

    #endregion
}
