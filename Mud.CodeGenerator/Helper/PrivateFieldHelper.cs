// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Globalization;
using System.Text;

namespace Mud.CodeGenerator;

/// <summary>
/// 字段命名风格枚举
/// </summary>
internal enum FieldNamingStyle
{
    /// <summary>
    /// m_前缀 + Pascal风格 (例如: m_UserService)
    /// </summary>
    MPrefixPascal,

    /// <summary>
    /// _前缀 + camelCase风格 (例如: _userService)
    /// </summary>
    UnderscoreCamel,

    /// <summary>
    /// 纯camelCase风格 (例如: userService)
    /// </summary>
    PureCamel
}

/// <summary>
/// 私有字段命名工具类
/// </summary>
internal static class PrivateFieldNamingHelper
{
    /// <summary>
    /// 根据类型名生成私有字段名（支持多种命名风格）
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <param name="style">命名风格</param>
    /// <returns>生成的私有字段名</returns>
    public static string GeneratePrivateFieldName(string typeName, FieldNamingStyle style = FieldNamingStyle.UnderscoreCamel)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("类型名不能为空", nameof(typeName));

        // 处理泛型类型
        typeName = RemoveGenericParameters(typeName);

        // 处理数组类型
        typeName = RemoveArrayBrackets(typeName);

        // 处理Nullable类型
        typeName = RemoveNullableSymbol(typeName);

        // 清理类型名
        string cleanedName = CleanTypeName(typeName);

        // 移除接口前缀"I"
        cleanedName = RemoveInterfacePrefix(cleanedName);

        // 根据不同的命名风格生成字段名
        return style switch
        {
            FieldNamingStyle.MPrefixPascal => ToMPrefixPascal(cleanedName),
            FieldNamingStyle.UnderscoreCamel => ToUnderscoreCamel(cleanedName),
            FieldNamingStyle.PureCamel => ToPureCamel(cleanedName),
            _ => ToUnderscoreCamel(cleanedName)
        };
    }

    /// <summary>
    /// 根据类型生成私有字段名（重载版本，接受Type参数）
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="style">命名风格</param>
    /// <returns>生成的私有字段名</returns>
    public static string GeneratePrivateFieldName(Type type, FieldNamingStyle style = FieldNamingStyle.UnderscoreCamel)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        // 对于内置类型，使用更友好的名称
        var builtInTypes = new Dictionary<Type, string>
        {
            [typeof(string)] = "string",
            [typeof(int)] = "int",
            [typeof(bool)] = "bool",
            [typeof(double)] = "double",
            [typeof(float)] = "float",
            [typeof(decimal)] = "decimal",
            [typeof(long)] = "long",
            [typeof(short)] = "short",
            [typeof(byte)] = "byte",
            [typeof(char)] = "char",
            [typeof(object)] = "object"
        };

        if (builtInTypes.TryGetValue(type, out string typeName))
        {
            return GeneratePrivateFieldName(typeName, style);
        }

        // 对于接口类型，移除接口前缀"I"
        string processedTypeName = type.Name;
        if (type.IsInterface)
        {
            processedTypeName = RemoveInterfacePrefix(processedTypeName);
        }

        return GeneratePrivateFieldName(processedTypeName, style);
    }

    /// <summary>
    /// 移除接口前缀"I"
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <returns>移除前缀后的类型名称</returns>
    private static string RemoveInterfacePrefix(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        // 如果类型名以"I"开头，且第二个字符是大写字母（符合接口命名规范），则移除"I"前缀
        if (typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1]))
        {
            return typeName.Substring(1);
        }

        return typeName;
    }

    /// <summary>
    /// m_前缀 + Pascal风格
    /// </summary>
    private static string ToMPrefixPascal(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "m_Value";

        string pascalCase = ToPascalCase(input);

        // 确保不以数字开头
        if (char.IsDigit(pascalCase[0]))
        {
            pascalCase = "Value" + pascalCase;
        }

        return "m_" + pascalCase;
    }

    /// <summary>
    /// _前缀 + camelCase风格
    /// </summary>
    private static string ToUnderscoreCamel(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "_value";

        string camelCase = ToCamelCase(input);

        // 确保不以数字开头
        if (char.IsDigit(camelCase[0]))
        {
            camelCase = "value" + camelCase;
        }

        return "_" + camelCase;
    }

    /// <summary>
    /// 纯camelCase风格
    /// </summary>
    private static string ToPureCamel(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "value";

        string camelCase = ToCamelCase(input);

        // 确保不以数字开头
        if (char.IsDigit(camelCase[0]))
        {
            camelCase = "value" + camelCase;
        }

        return camelCase;
    }

    /// <summary>
    /// 转换为camelCase命名
    /// </summary>
    private static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // 如果已经是camelCase（首字母小写），直接返回
        if (char.IsLower(input[0]))
        {
            // 但需要确保后续的单词首字母大写
            return EnsureProperCamelCase(input);
        }

        var sb = new StringBuilder();
        bool makeUpper = false;
        bool firstCharProcessed = false;

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (!firstCharProcessed)
                {
                    // 第一个字符确保小写
                    sb.Append(char.ToLower(c, CultureInfo.CurrentCulture));
                    firstCharProcessed = true;
                }
                else if (makeUpper)
                {
                    sb.Append(char.ToUpper(c, CultureInfo.CurrentCulture));
                    makeUpper = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                // 遇到分隔符，下一个字符要大写
                makeUpper = firstCharProcessed;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 确保已经是camelCase的字符串中后续单词首字母大写
    /// </summary>
    private static string EnsureProperCamelCase(string input)
    {
        var sb = new StringBuilder();
        bool nextShouldBeUpper = false;
        bool firstCharProcessed = false;

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (!firstCharProcessed)
                {
                    sb.Append(c);
                    firstCharProcessed = true;
                }
                else if (nextShouldBeUpper)
                {
                    sb.Append(char.ToUpper(c, CultureInfo.CurrentCulture));
                    nextShouldBeUpper = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                nextShouldBeUpper = firstCharProcessed;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 移除泛型参数
    /// </summary>
    private static string RemoveGenericParameters(string typeName)
    {
        int genericIndex = typeName.IndexOf('<');
        if (genericIndex > 0)
        {
            return typeName.Substring(0, genericIndex);
        }
        return typeName;
    }

    /// <summary>
    /// 移除数组括号
    /// </summary>
    private static string RemoveArrayBrackets(string typeName)
    {
        return typeName.Replace("[]", "").Replace("[", "").Replace("]", "");
    }

    /// <summary>
    /// 移除可空类型符号
    /// </summary>
    private static string RemoveNullableSymbol(string typeName)
    {
        return typeName.Replace("?", "");
    }

    /// <summary>
    /// 清理类型名，只保留有效的标识符字符
    /// </summary>
    private static string CleanTypeName(string typeName)
    {
        var sb = new StringBuilder();
        bool lastWasSeparator = true;

        foreach (char c in typeName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && (c == '.' || c == '+' || c == ' ' || c == '_' || c == '-'))
            {
                // 这些字符被视为单词分隔符
                lastWasSeparator = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 根据类型名生成私有字段名（简化版本）
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <param name="style">命名风格</param>
    /// <returns>生成的私有字段名</returns>
    public static string GenerateSimplePrivateFieldName(string typeName, FieldNamingStyle style = FieldNamingStyle.UnderscoreCamel)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return GetDefaultName(style);

        // 移除泛型、数组等符号
        typeName = typeName
            .Replace("<", "")
            .Replace(">", "")
            .Replace("[]", "")
            .Replace("?", "");

        // 移除接口前缀"I"
        typeName = RemoveInterfacePrefix(typeName);

        // 根据不同的命名风格生成字段名
        if (typeName.Length == 0)
            return GetDefaultName(style);

        return style switch
        {
            FieldNamingStyle.MPrefixPascal => "m_" + char.ToUpper(typeName[0], CultureInfo.CurrentCulture) + typeName.Substring(1).ToLower(CultureInfo.CurrentCulture),
            FieldNamingStyle.UnderscoreCamel => "_" + char.ToLower(typeName[0], CultureInfo.CurrentCulture) + typeName.Substring(1).ToLower(CultureInfo.CurrentCulture),
            FieldNamingStyle.PureCamel => char.ToLower(typeName[0], CultureInfo.CurrentCulture) + typeName.Substring(1).ToLower(CultureInfo.CurrentCulture),
            _ => "_" + char.ToLower(typeName[0], CultureInfo.CurrentCulture) + typeName.Substring(1).ToLower(CultureInfo.CurrentCulture)
        };
    }

    /// <summary>
    /// 获取默认字段名（用于空输入的情况）
    /// </summary>
    private static string GetDefaultName(FieldNamingStyle style)
    {
        return style switch
        {
            FieldNamingStyle.MPrefixPascal => "m_Value",
            FieldNamingStyle.UnderscoreCamel => "_value",
            FieldNamingStyle.PureCamel => "value",
            _ => "_value"
        };
    }

    /// <summary>
    /// 根据私有字段名生成属性名（支持多种命名风格）
    /// </summary>
    /// <param name="fieldName">私有字段名</param>
    /// <returns>生成的属性名（PascalCase风格）</returns>
    public static string GeneratePropertyName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("字段名不能为空", nameof(fieldName));

        // 移除常见的私有字段前缀
        string cleanedName = RemovePrivateFieldPrefixes(fieldName);

        // 转换为PascalCase命名
        return ToPascalCase(cleanedName);
    }

    /// <summary>
    /// 根据私有字段名生成属性名（支持指定原始字段的命名风格）
    /// </summary>
    /// <param name="fieldName">私有字段名</param>
    /// <param name="originalFieldStyle">原始字段的命名风格</param>
    /// <returns>生成的属性名（PascalCase风格）</returns>
    public static string GeneratePropertyName(string fieldName, FieldNamingStyle originalFieldStyle)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("字段名不能为空", nameof(fieldName));

        // 根据原始命名风格进行相应的处理
        string cleanedName = originalFieldStyle switch
        {
            FieldNamingStyle.MPrefixPascal => RemoveMPrefix(fieldName),
            FieldNamingStyle.UnderscoreCamel => RemoveUnderscorePrefix(fieldName),
            FieldNamingStyle.PureCamel => fieldName, // 纯camelCase不需要移除前缀
            _ => RemovePrivateFieldPrefixes(fieldName) // 默认处理所有前缀
        };

        // 转换为PascalCase命名
        return ToPascalCase(cleanedName);
    }

    /// <summary>
    /// 尝试根据私有字段名生成属性名（不会抛出异常）
    /// </summary>
    /// <param name="fieldName">私有字段名</param>
    /// <param name="propertyName">生成的属性名</param>
    /// <returns>是否成功生成属性名</returns>
    public static bool TryGeneratePropertyNameFromPrivateField(string fieldName, out string propertyName)
    {
        propertyName = null;

        if (string.IsNullOrWhiteSpace(fieldName))
            return false;

        try
        {
            propertyName = GeneratePropertyName(fieldName);
            return !string.IsNullOrEmpty(propertyName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检测字段的命名风格
    /// </summary>
    /// <param name="fieldName">字段名</param>
    /// <returns>检测到的命名风格</returns>
    public static FieldNamingStyle DetectFieldNamingStyle(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return FieldNamingStyle.UnderscoreCamel;

        if (fieldName.StartsWith("m_", StringComparison.CurrentCulture))
            return FieldNamingStyle.MPrefixPascal;

        if (fieldName.StartsWith("s_", StringComparison.CurrentCulture))
            return FieldNamingStyle.MPrefixPascal; // s_前缀也视为MPrefix风格

        if (fieldName.StartsWith("_", StringComparison.CurrentCulture))
            return FieldNamingStyle.UnderscoreCamel;

        // 检查是否为camelCase（首字母小写）
        if (fieldName.Length > 0 && char.IsLower(fieldName[0]))
            return FieldNamingStyle.PureCamel;

        return FieldNamingStyle.UnderscoreCamel; // 默认
    }

    /// <summary>
    /// 移除常见的私有字段前缀
    /// </summary>
    private static string RemovePrivateFieldPrefixes(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return fieldName;

        // 按优先级移除前缀
        if (fieldName.StartsWith("m_", StringComparison.CurrentCulture) && fieldName.Length > 2)
            return fieldName.Substring(2);

        if (fieldName.StartsWith("s_", StringComparison.CurrentCulture) && fieldName.Length > 2)
            return fieldName.Substring(2);

        if (fieldName.StartsWith("_", StringComparison.CurrentCulture) && fieldName.Length > 1)
            return fieldName.Substring(1);

        return fieldName; // 没有前缀，直接返回
    }

    /// <summary>
    /// 移除m_前缀
    /// </summary>
    private static string RemoveMPrefix(string fieldName)
    {
        if (!string.IsNullOrEmpty(fieldName) && fieldName.StartsWith("m_", StringComparison.CurrentCulture) && fieldName.Length > 2)
            return fieldName.Substring(2);

        return fieldName;
    }

    /// <summary>
    /// 移除_前缀
    /// </summary>
    private static string RemoveUnderscorePrefix(string fieldName)
    {
        if (!string.IsNullOrEmpty(fieldName) && fieldName.StartsWith("_", StringComparison.CurrentCulture) && fieldName.Length > 1)
            return fieldName.Substring(1);

        return fieldName;
    }

    /// <summary>
    /// 转换为PascalCase命名
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // 如果已经是PascalCase，直接返回
        if (input.Length > 0 && char.IsUpper(input[0]))
            return input;

        // 将camelCase转换为PascalCase
        var sb = new StringBuilder();
        bool makeUpper = true;

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (makeUpper)
                {
                    sb.Append(char.ToUpper(c, CultureInfo.CurrentCulture));
                    makeUpper = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                // 遇到分隔符，下一个字符要大写
                makeUpper = true;
            }
        }

        string result = sb.ToString();

        // 确保不以数字开头
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result = "Value" + result;
        }

        return result;
    }
}