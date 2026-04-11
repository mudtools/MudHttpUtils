// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// 特性数据辅助类，提供从特性数据中提取常用类型值的静态方法。
/// </summary>
internal static class AttributeDataHelper
{
    /// <summary>
    /// 检查是否应该被忽略生成代码。
    /// </summary>
    /// <param name="field"></param>
    /// <returns></returns>
    public static bool IgnoreGenerator(MemberDeclarationSyntax field)
    {
        var ignoreAttributes = AttributeSyntaxHelper.GetAttributeSyntaxes(field, GeneratedCodeConsts.IgnoreGeneratorAttribute);
        return ignoreAttributes?.Any() == true;
    }

    /// <summary>
    /// 检查是否应该被忽略生成代码。
    /// </summary>
    /// <param name="member">符号</param>
    /// <returns>如果应该忽略返回true，否则返回false</returns>
    public static bool IgnoreGenerator(ISymbol member)
    {
        if (member == null)
            return false;
        return member.GetAttributes().Any(attr => attr.AttributeClass?.Name == GeneratedCodeConsts.IgnoreGeneratorAttribute);
    }

    /// <summary>
    /// 从特性数据中获取整型属性值。
    /// </summary>
    /// <param name="attribute">特性数据对象</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="defaultVal">默认值，当属性不存在或无法解析时返回此值</param>
    /// <returns>解析得到的整型值或默认值</returns>
    public static int GetIntValueFromAttribute(AttributeData attribute, string propertyName, int defaultVal = 0)
    {
        if (attribute == null)
            return defaultVal;

        var timeoutArg = attribute.NamedArguments
            .FirstOrDefault(a => a.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return timeoutArg.Value.Value is int value ? value : defaultVal;
    }

    /// <summary>
    /// 从<see cref="ISymbol"/>对象中获取指定特性的属性值。
    /// </summary>
    /// <param name="typeSymbol"><see cref="ISymbol"/>对象</param>
    /// <param name="attributeNames">特性数组。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="defaultValue">属性默认值。</param>
    /// <returns></returns>
    public static string? GetStringValueFromSymbol(ISymbol typeSymbol, string[] attributeNames, string propertyName, string? defaultValue = null)
    {
        if (typeSymbol == null || attributeNames == null)
            return defaultValue;
        if (attributeNames.Length < 1)
            return defaultValue;

        var attributeData = GetAttributeDataFromSymbol(typeSymbol, attributeNames);
        if (attributeData == null)
            return defaultValue;

        return GetStringValueFromAttribute(attributeData, [propertyName], 0, defaultValue);
    }


    /// <summary>
    /// 从特性数据中获取字符串属性值。
    /// </summary>
    /// <param name="attribute">特性数据对象</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="defaultValue">默认值，当属性不存在或无法解析时返回此值</param>
    /// <returns>解析得到的字符串值或默认值</returns>
    public static string? GetStringValueFromAttribute(AttributeData attribute, string propertyName, string? defaultValue = null)
    {
        if (attribute == null)
            return defaultValue;
        var nameArg = attribute.NamedArguments
            .FirstOrDefault(a => a.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return nameArg.Value.Value?.ToString();
    }

    /// <summary>
    /// 从特性中获取字符串参数值，按优先级顺序检查命名参数、构造函数参数
    /// </summary>
    /// <param name="attribute">特性数据</param>
    /// <param name="namedParameterNames">命名参数名称列表（按优先级排序）</param>
    /// <param name="constructorParameterIndex">构造函数参数索引</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>参数值</returns>
    public static string? GetStringValueFromAttribute(AttributeData attribute, string[] namedParameterNames, int constructorParameterIndex = -1, string? defaultValue = null)
    {
        if (attribute == null)
            return defaultValue;

        // 检查命名参数
        foreach (var paramName in namedParameterNames)
        {
            var namedArg = attribute.NamedArguments.FirstOrDefault(arg => arg.Key.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (namedArg.Key != null && namedArg.Value.Value?.ToString() is string namedValue && !string.IsNullOrEmpty(namedValue))
                return namedValue;
        }

        // 检查构造函数参数
        if (constructorParameterIndex >= 0 && attribute.ConstructorArguments.Length > constructorParameterIndex)
        {
            var constructorArg = attribute.ConstructorArguments[constructorParameterIndex].Value?.ToString();
            if (!string.IsNullOrEmpty(constructorArg))
                return constructorArg;
        }

        return defaultValue;
    }


    /// <summary>
    /// 从特性中获取布尔值参数
    /// </summary>
    /// <param name="attribute">特性数据</param>
    /// <param name="parameterName">参数名称</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>布尔值</returns>
    public static bool GetBoolValueFromAttribute(AttributeData attribute, string parameterName, bool defaultValue = false)
    {
        if (attribute == null)
            return defaultValue;

        var namedArg = attribute.NamedArguments.FirstOrDefault(arg => arg.Key.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        if (namedArg.Key != null && namedArg.Value.Value != null)
        {
            if (namedArg.Value.Value is bool v)
                return v;
            return bool.TryParse(namedArg.Value.Value.ToString(), out var result) ? result : defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// 从特性数据中获取字符串属性值，同时兼容命名参数和构造函数参数两种方式。
    /// 优先返回命名参数的值，如果命名参数不存在，则返回构造函数参数的值。
    /// </summary>
    /// <param name="attributeData">特性数据对象</param>
    /// <param name="propertyName">属性名称</param>
    /// <returns>解析得到的字符串值，如果都未找到则返回 null</returns>
    /// <remarks>
    /// 此方法首先检查命名参数，如果找到非空的值则直接返回。
    /// 如果命名参数为空，则检查构造函数参数，返回第一个参数的值。
    /// </remarks>
    public static string? GetStringValueFromAttributeConstructor(AttributeData? attributeData, string propertyName)
    {
        if (attributeData == null)
            return null;

        var baseAddressArg = GetStringValueFromAttribute(attributeData, propertyName);
        if (!string.IsNullOrEmpty(baseAddressArg))
            return baseAddressArg;

        if (attributeData.ConstructorArguments.Length > 0 && attributeData.ConstructorArguments[0].Value is string baseAddress)
            return baseAddress;

        return null;
    }

    /// <summary>
    /// 从类型符号中获取指定名称的特性数据。
    /// </summary>
    /// <param name="typeSymbol">类型符号对象</param>
    /// <param name="attributeNames">要查找的特性名称数组，支持多个名称进行匹配</param>
    /// <returns>匹配到的特性数据对象，如果未找到则返回 null</returns>
    /// <remarks>
    /// 此方法会遍历类型的所有特性，返回第一个名称在给定名称数组中的特性。
    /// 常用于查找可能存在多个别名或不同命名空间的特性。
    /// </remarks>
    public static AttributeData? GetAttributeDataFromSymbol(ISymbol typeSymbol, string[] attributeNames)
    {
        if (typeSymbol == null || attributeNames == null)
            return null;
        if (attributeNames.Length < 1)
            return null;
        return typeSymbol.GetAttributes().FirstOrDefault(a => attributeNames.Contains(a.AttributeClass?.Name));
    }

    /// <summary>
    /// 判断类型符号上是否存在指定名称的特性。
    /// </summary>
    /// <param name="typeSymbol">类型符号对象</param>
    /// <param name="attributeNames">要查找的特性名称数组，支持多个名称进行匹配</param>
    /// <returns></returns>
    public static bool HasAttribute(ISymbol typeSymbol, string[] attributeNames)
    {
        if (typeSymbol == null || attributeNames == null)
            return false;
        if (attributeNames.Length < 1)
            return false;
        return typeSymbol.GetAttributes().Any(a => attributeNames.Contains(a.AttributeClass?.Name));
    }
}
