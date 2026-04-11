// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// Attribute 参数提取选项
/// </summary>
internal sealed class AttributeExtractionOptions
{
    /// <summary>
    /// 是否使用语义模型获取精确值（推荐为true）
    /// </summary>
    public bool UseSemanticModel { get; set; } = true;

    /// <summary>
    /// 当无法获取编译时常量值时是否回退到语法分析
    /// </summary>
    public bool FallbackToSyntax { get; set; } = true;

    /// <summary>
    /// 对于 nameof 表达式，是否返回完全限定名
    /// </summary>
    public bool UseFullNameForNameOf { get; set; }

    /// <summary>
    /// 默认值，当参数不存在时返回
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// 创建默认选项实例
    /// </summary>
    public static AttributeExtractionOptions Default { get; } = new AttributeExtractionOptions();
}

/// <summary>
/// 扩展性强的 Attribute 参数提取器
/// </summary>
internal static class AttributeSyntaxHelper
{
    /// <summary>
    /// 获取属性声明上的特性参数值。
    /// </summary>
    /// <typeparam name="T">参数值类型。</typeparam>
    /// <param name="propertyDeclaration">属性声明语法节点。</param>
    /// <param name="attributeName">特性名称。</param>
    /// <param name="paramName">参数名。</param>
    /// <param name="defaultValue">参数默认值。</param>
    /// <returns>特性参数值，如果不存在则返回默认值。</returns>
    public static T? GetPropertyAttributeValues<T>(
        PropertyDeclarationSyntax? propertyDeclaration,
        string? attributeName,
        string? paramName,
        T? defaultValue = default)
    {
        if (string.IsNullOrEmpty(attributeName) || string.IsNullOrEmpty(paramName))
            return defaultValue;

        if (propertyDeclaration == null)
            return defaultValue;

        var attributeShortName = attributeName.Replace("Attribute", "");
        var attributes = GetAttributeSyntaxes(propertyDeclaration, attributeName);

        if (!attributes.Any())
            return defaultValue;

        // 只处理第一个找到的特性（通常一个属性上同类型特性只有一个）
        var attribute = attributes[0];
        return GetAttributeValue(attributes, paramName, defaultValue);
    }

    /// <summary>
    /// 获取成员上标注的指定特性语法节点。
    /// </summary>
    /// <typeparam name="T">成员声明类型。</typeparam>
    /// <param name="memberDeclaration">成员声明语法节点。</param>
    /// <param name="attributeName">特性名称。</param>
    /// <returns>特性语法节点只读集合。</returns>
    public static IReadOnlyList<AttributeSyntax> GetAttributeSyntaxes<T>(
        T memberDeclaration,
        string? attributeName)
        where T : MemberDeclarationSyntax
    {
        if (string.IsNullOrEmpty(attributeName) || memberDeclaration == null)
            return Array.Empty<AttributeSyntax>();

        var attributeShortName = attributeName.Replace("Attribute", "");

        var attributes = memberDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Where(a => a.Name != null)
            .Where(a =>
                a.Name.ToString().Equals(attributeName, StringComparison.Ordinal) ||
                a.Name.ToString().Equals(attributeShortName, StringComparison.Ordinal))
            .ToList();

        return attributes.AsReadOnly();
    }

    /// <summary>
    /// 从特性语法节点集合中获取指定参数的的值。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="attributes">特性语法节点集合。</param>
    /// <param name="paramName">参数名。</param>
    /// <param name="defaultValue">参数默认值。</param>
    /// <returns>特性参数值。</returns>
    public static T? GetAttributeValue<T>(
        IReadOnlyList<AttributeSyntax> attributes,
        string? paramName,
        T? defaultValue = default)
    {
        if (!attributes.Any() || string.IsNullOrEmpty(paramName))
            return defaultValue;

        var attribute = attributes[0];
        var argumentList = attribute.ArgumentList;

        if (argumentList == null)
            return defaultValue;

        // 查找命名参数
        var argument = argumentList.Arguments
            .FirstOrDefault(arg =>
                arg.NameEquals != null &&
                paramName.Equals(arg.NameEquals.Name.Identifier.ValueText, StringComparison.OrdinalIgnoreCase));

        if (argument == null)
            return defaultValue;

        var paramValue = ExtractValueFromSyntax(argument.Expression);

        return paramValue is T typedValue ? typedValue : defaultValue;
    }

    /// <summary>
    /// 从 AttributeSyntax 中获取指定属性的值
    /// </summary>
    /// <param name="attributeSyntax">特性语法节点</param>
    /// <param name="semanticModel">语义模型</param>
    /// <param name="propertyName">属性名</param>
    /// <param name="options">提取选项</param>
    /// <returns>属性值，如果不存在则返回默认值</returns>
    public static object? GetPropertyValue(
        this AttributeSyntax? attributeSyntax,
        SemanticModel? semanticModel,
        string? propertyName,
        AttributeExtractionOptions? options = null)
    {
        options ??= AttributeExtractionOptions.Default;

        if (attributeSyntax?.ArgumentList == null || string.IsNullOrEmpty(propertyName))
            return options.DefaultValue;

        var argument = FindArgumentByName(attributeSyntax, propertyName);
        if (argument == null)
            return options.DefaultValue;

        return ExtractArgumentValue(argument.Expression, semanticModel, options);
    }

    /// <summary>
    /// 从 AttributeSyntax 中获取指定属性的值（不使用语义模型）
    /// </summary>
    public static object? GetPropertyValue(
        this AttributeSyntax? attributeSyntax,
        string? propertyName,
        AttributeExtractionOptions? options = null)
    {
        options ??= new AttributeExtractionOptions { UseSemanticModel = false };
        return GetPropertyValue(attributeSyntax, null, propertyName, options);
    }

    /// <summary>
    /// 从 AttributeSyntax 中获取所有命名属性的值
    /// </summary>
    public static IReadOnlyDictionary<string, object?> GetAllPropertyValues(
        this AttributeSyntax? attributeSyntax,
        SemanticModel? semanticModel,
        AttributeExtractionOptions? options = null)
    {
        options ??= AttributeExtractionOptions.Default;
        var result = new Dictionary<string, object?>();

        if (attributeSyntax?.ArgumentList == null)
            return result;

        foreach (var argument in attributeSyntax.ArgumentList.Arguments)
        {
            if (argument.NameEquals != null)
            {
                var propertyName = argument.NameEquals.Name.Identifier.ValueText;
                object? value = ExtractArgumentValue(argument.Expression, semanticModel, options);
                result[propertyName] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// 从 AttributeSyntax 中获取构造函数参数的值
    /// </summary>
    public static object? GetConstructorArgument(
        this AttributeSyntax? attributeSyntax,
        SemanticModel? semanticModel,
        int parameterIndex,
        AttributeExtractionOptions? options = null)
    {
        options ??= AttributeExtractionOptions.Default;

        if (attributeSyntax?.ArgumentList == null || parameterIndex < 0)
            return options.DefaultValue;

        var positionalArguments = attributeSyntax.ArgumentList.Arguments
            .Where(arg => arg.NameEquals == null)
            .ToList();

        if (parameterIndex >= positionalArguments.Count)
            return options.DefaultValue;

        return ExtractArgumentValue(positionalArguments[parameterIndex].Expression, semanticModel, options);
    }

    /// <summary>
    /// 从 AttributeSyntax 中获取所有构造函数参数的值
    /// </summary>
    public static IReadOnlyList<object?> GetAllConstructorArguments(
        this AttributeSyntax? attributeSyntax,
        SemanticModel? semanticModel,
        AttributeExtractionOptions? options = null)
    {
        options ??= AttributeExtractionOptions.Default;
        var result = new List<object?>();

        if (attributeSyntax?.ArgumentList == null)
            return result.AsReadOnly();

        var positionalArguments = attributeSyntax.ArgumentList.Arguments
            .Where(arg => arg.NameEquals == null);

        foreach (var argument in positionalArguments)
        {
            object? value = ExtractArgumentValue(argument.Expression, semanticModel, options);
            result.Add(value);
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// 检查 Attribute 是否包含指定属性
    /// </summary>
    public static bool HasProperty(
        this AttributeSyntax? attributeSyntax,
        string? propertyName)
    {
        if (attributeSyntax?.ArgumentList == null || string.IsNullOrEmpty(propertyName))
            return false;

        return FindArgumentByName(attributeSyntax, propertyName) != null;
    }

    /// <summary>
    /// 从表达式语法中提取值。
    /// </summary>
    public static object? ExtractValueFromSyntax(ExpressionSyntax? expression)
    {
        if (expression == null)
            return null;

        try
        {
            // 处理字面量表达式
            if (expression is LiteralExpressionSyntax literal)
            {
                return literal.Kind() switch
                {
                    SyntaxKind.StringLiteralExpression => literal.Token.ValueText,
                    SyntaxKind.NumericLiteralExpression => HandleNumericLiteral(literal.Token),
                    SyntaxKind.FalseLiteralExpression => false,
                    SyntaxKind.TrueLiteralExpression => true,
                    SyntaxKind.NullLiteralExpression => null,
                    SyntaxKind.CharacterLiteralExpression => literal.Token.ValueText,
                    _ => literal.Token.Value ?? literal.ToString(),
                };
            }

            // 处理 nameof 表达式
            if (expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "nameof" &&
                invocation.ArgumentList.Arguments.Count > 0)
            {
                return invocation.ArgumentList.Arguments[0].Expression.ToString();
            }

            // 处理 typeof 表达式
            if (expression is TypeOfExpressionSyntax typeOfExpression)
            {
                return typeOfExpression.Type.ToString();
            }

            // 处理数组初始化表达式
            if (expression is ArrayCreationExpressionSyntax arrayCreation)
            {
                return HandleArrayCreation(arrayCreation);
            }

            // 处理集合初始化表达式
            if (expression is ImplicitArrayCreationExpressionSyntax implicitArray)
            {
                return HandleImplicitArrayCreation(implicitArray);
            }

            return expression.ToString();
        }
        catch (Exception ex)
        {
            // 记录日志或根据需要进行处理
            System.Diagnostics.Debug.WriteLine($"提取语法值时发生错误: {ex.Message}");
            return expression.ToString();
        }
    }

    #region 私有辅助方法

    private static AttributeArgumentSyntax? FindArgumentByName(AttributeSyntax attribute, string propertyName)
    {
        return attribute.ArgumentList?.Arguments
            .FirstOrDefault(arg =>
                arg.NameEquals != null &&
                string.Equals(arg.NameEquals.Name.Identifier.ValueText, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static object? ExtractArgumentValue(
        ExpressionSyntax? expression,
        SemanticModel? semanticModel,
        AttributeExtractionOptions options)
    {
        if (expression == null)
            return options.DefaultValue;

        // 优先使用语义模型获取精确值
        if (options.UseSemanticModel && semanticModel != null)
        {
            try
            {
                var value = ExtractValueWithSemanticModel(expression, semanticModel, options);
                if (value != null || !options.FallbackToSyntax)
                    return value ?? options.DefaultValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"语义模型提取失败: {ex.Message}");
                // 语义分析失败时继续回退到语法分析
            }
        }

        // 回退到语法分析
        return ExtractValueFromSyntax(expression);
    }

    private static object? ExtractValueWithSemanticModel(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        AttributeExtractionOptions options)
    {
        // 处理 nameof 表达式
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == "nameof")
        {
            return ExtractNameOfValue(invocation, semanticModel, options);
        }

        // 处理 typeof 表达式
        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            return ExtractTypeOfValue(typeOfExpression, semanticModel, options);
        }

        // 尝试获取编译时常量值
        var constantValue = semanticModel.GetConstantValue(expression);
        if (constantValue.HasValue)
        {
            return constantValue.Value;
        }

        // 对于其他表达式类型，尝试获取符号信息
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        if (symbolInfo.Symbol != null)
        {
            return options.UseFullNameForNameOf
                ? symbolInfo.Symbol.ToDisplayString()
                : symbolInfo.Symbol.Name;
        }

        return null;
    }

    private static object? ExtractNameOfValue(
        InvocationExpressionSyntax nameofExpression,
        SemanticModel semanticModel,
        AttributeExtractionOptions options)
    {
        if (nameofExpression.ArgumentList.Arguments.Count == 0)
            return null;

        var argumentExpression = nameofExpression.ArgumentList.Arguments[0].Expression;
        var symbolInfo = semanticModel.GetSymbolInfo(argumentExpression);

        if (symbolInfo.Symbol != null)
        {
            return options.UseFullNameForNameOf
                ? symbolInfo.Symbol.ToDisplayString()
                : symbolInfo.Symbol.Name;
        }

        // 回退到语法分析
        return argumentExpression.ToString();
    }

    private static object? ExtractTypeOfValue(
        TypeOfExpressionSyntax typeOfExpression,
        SemanticModel semanticModel,
        AttributeExtractionOptions options)
    {
        var typeInfo = semanticModel.GetTypeInfo(typeOfExpression.Type);
        if (typeInfo.Type != null)
        {
            return options.UseFullNameForNameOf
                ? typeInfo.Type.ToDisplayString()
                : typeInfo.Type.Name;
        }

        return typeOfExpression.Type.ToString();
    }

    /// <summary>
    /// 处理数值字面量，支持多种数值类型
    /// </summary>
    private static object HandleNumericLiteral(SyntaxToken token)
    {
        try
        {
            var valueText = token.ValueText;
            var value = token.Value;

            if (value == null)
                return 0;

            // 根据后缀判断类型
            if (valueText.EndsWith("f", StringComparison.OrdinalIgnoreCase) ||
                valueText.EndsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (valueText.EndsWith("d", StringComparison.OrdinalIgnoreCase) ||
                     valueText.EndsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (valueText.EndsWith("m", StringComparison.OrdinalIgnoreCase) ||
                     valueText.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (valueText.EndsWith("l", StringComparison.OrdinalIgnoreCase) ||
                     valueText.EndsWith("L", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (valueText.EndsWith("u", StringComparison.OrdinalIgnoreCase) ||
                     valueText.EndsWith("U", StringComparison.OrdinalIgnoreCase))
            {
                if (valueText.EndsWith("ul", StringComparison.OrdinalIgnoreCase) ||
                    valueText.EndsWith("UL", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                // 默认返回int，但如果值超出int范围则返回long
                var numericValue = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                return numericValue <= int.MaxValue && numericValue >= int.MinValue
                    ? (int)numericValue
                    : numericValue;
            }
        }
        catch
        {
            return 0;
        }
    }

    private static object? HandleArrayCreation(ArrayCreationExpressionSyntax arrayCreation)
    {
        var values = new List<object?>();

        if (arrayCreation.Initializer?.Expressions != null)
        {
            foreach (var expression in arrayCreation.Initializer.Expressions)
            {
                values.Add(ExtractValueFromSyntax(expression));
            }
        }

        return values.ToArray();
    }

    private static object? HandleImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax implicitArray)
    {
        var values = new List<object?>();

        if (implicitArray.Initializer?.Expressions != null)
        {
            foreach (var expression in implicitArray.Initializer.Expressions)
            {
                values.Add(ExtractValueFromSyntax(expression));
            }
        }

        return values.ToArray();
    }
    #endregion
}