// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Globalization;

namespace Mud.CodeGenerator;

internal static class StringExtensions
{

    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> values)
    {
        if (values == null)
            return [];
        return [.. values];
    }

    /// <summary>
    /// 移除接口名前的"I"前缀
    /// </summary>
    /// <returns>去掉"I"前缀的类型名</returns>
    public static string RemoveInterfacePrefix(string interfaceTypeName)
    {
        if (string.IsNullOrEmpty(interfaceTypeName))
            return interfaceTypeName;

        // 处理可空类型
        if (interfaceTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            // 移除末尾的'?'，递归处理内部类型
            var nonNullType = interfaceTypeName.Substring(0, interfaceTypeName.Length - 1);
            var processedType = RemoveInterfacePrefix(nonNullType);
            return processedType + "?";
        }

        // 处理数组类型
        if (interfaceTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementType = interfaceTypeName.Substring(0, interfaceTypeName.Length - 2);
            var processedType = RemoveInterfacePrefix(elementType);
            return processedType + "[]";
        }

        // 分割命名空间和类型名
        var lastDotIndex = interfaceTypeName.LastIndexOf('.');
        if (lastDotIndex >= 0)
        {
            // 有命名空间的情况
            var namespacePart = interfaceTypeName.Substring(0, lastDotIndex + 1); // 包含点
            var typeNamePart = interfaceTypeName.Substring(lastDotIndex + 1);

            // 只对类型名部分进行处理
            var processedTypeName = RemoveInterfacePrefixFromTypeName(typeNamePart);

            return namespacePart + processedTypeName;
        }
        else
        {
            // 没有命名空间，直接处理类型名
            return RemoveInterfacePrefixFromTypeName(interfaceTypeName);
        }
    }

    /// <summary>
    /// 从类型名中移除"I"前缀（不处理命名空间和可空符号）
    /// </summary>
    public static string RemoveInterfacePrefixFromTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || typeName.Length < 2)
            return typeName;

        // 处理嵌套类的情况（例如 "Outer+IInner"）
        var plusIndex = typeName.IndexOf('+');
        if (plusIndex >= 0)
        {
            // 分离外部类和嵌套类
            var outerClassName = typeName.Substring(0, plusIndex);
            var innerClassName = typeName.Substring(plusIndex + 1);

            // 递归处理两部分
            var processedOuter = RemoveInterfacePrefixFromTypeName(outerClassName);
            var processedInner = RemoveInterfacePrefixFromTypeName(innerClassName);

            return processedOuter + "+" + processedInner;
        }

        // 处理泛型类型参数（例如 "IList<T>"）
        var angleBracketIndex = typeName.IndexOf('<');
        if (angleBracketIndex >= 0)
        {
            // 分离类型名和泛型参数
            var baseTypeName = typeName.Substring(0, angleBracketIndex);
            var genericParams = typeName.Substring(angleBracketIndex);

            // 只对基础类型名进行处理
            var processedBase = RemoveInterfacePrefixFromTypeName(baseTypeName);

            return processedBase + genericParams;
        }

        // 核心逻辑：移除"I"前缀
        // 条件：以'I'开头，长度至少为2，第二个字符是大写
        if (typeName[0] == 'I' && char.IsUpper(typeName[1]))
        {
            // 检查是否是特殊情况，如"IEnumerable"等.NET内置接口
            // 这些接口虽然符合"I"+大写规则，但我们不应该移除它们的前缀
            string[] specialCases = { "IEnumerable", "IEnumerator", "IEqualityComparer", "IComparable", "IEquatable" };

            if (specialCases.Contains(typeName))
            {
                return typeName;
            }

            // 标准接口前缀，移除 'I'
            return typeName.Substring(1);
        }

        return typeName;
    }

    /// <summary>
    /// 将首字母小写（根据配置）。
    /// </summary>
    /// <param name="input">输入字符串。</param>
    /// <returns>首字母小写的字符串。</returns>
    public static string ToLowerFirstLetter(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= 2)
        {
            return input?.ToLower(CultureInfo.CurrentCulture) ?? string.Empty;
        }
        return char.ToLower(input[0], CultureInfo.CurrentCulture) + input.Substring(1);
    }

    /// <summary>
    /// 将首字母大写（根据配置）。
    /// </summary>
    /// <param name="input">输入字符串。</param>
    /// <returns>首字母大写的字符串。</returns>
    public static string ToUpperFirstLetter(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length < 2)
        {
            return input?.ToUpper(CultureInfo.CurrentCulture) ?? string.Empty;
        }
        return char.ToUpper(input[0], CultureInfo.CurrentCulture) + input.Substring(1);
    }

    /// <summary>
    /// 使用指定分隔符分割字符串，并可对每个分割结果进行处理
    /// </summary>
    /// <param name="str">要分割的字符串</param>
    /// <param name="splitChar">分隔字符，默认为逗号</param>
    /// <param name="processFunc">对每个分割结果进行处理的函数</param>
    /// <returns>处理后的字符串数组</returns>
    public static string[] SplitString(
        this string str,
        char splitChar = ',',
        Func<string, string> processFunc = null)
    {
        // 处理空字符串或空白字符串
        if (string.IsNullOrWhiteSpace(str))
            return Array.Empty<string>();

        // 分割字符串并移除空项
        var result = str.Split(new[] { splitChar }, StringSplitOptions.RemoveEmptyEntries);

        // 如果提供了处理函数，则对每个分割结果进行处理
        if (processFunc != null)
        {
            result = result.Select(processFunc).ToArray();
        }
        return result;
    }

    /// <summary>
    /// 扩展方法：移除字符串末尾指定的后缀字符串（区分大小写）
    /// </summary>
    /// <param name="str">源字符串</param>
    /// <param name="suffix">要移除的后缀字符串</param>
    /// <returns>
    /// 如果字符串以指定后缀结尾，则返回移除后缀后的字符串；
    /// 否则返回原字符串；如果输入为null或空字符串，或后缀为null，则返回原字符串
    /// </returns>
    public static string RemoveSuffix(this string str, string suffix)
    {
        // 处理边界情况
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(suffix))
            return str;

        // 检查字符串是否以指定后缀结尾
        if (str.EndsWith(suffix, StringComparison.CurrentCulture))
        {
            // 移除后缀：截取从开头到 (总长度 - 后缀长度) 的部分
            return str.Substring(0, str.Length - suffix.Length);
        }

        return str;
    }

    /// <summary>
    /// 扩展方法：移除字符串末尾指定的后缀字符串（可选择是否区分大小写）
    /// </summary>
    /// <param name="str">源字符串</param>
    /// <param name="suffix">要移除的后缀字符串</param>
    /// <param name="ignoreCase">是否忽略大小写，默认为false（区分大小写）</param>
    /// <returns>
    /// 如果字符串以指定后缀结尾（根据ignoreCase参数决定是否区分大小写），
    /// 则返回移除后缀后的字符串；否则返回原字符串
    /// </returns>
    public static string RemoveSuffix(this string str, string suffix, bool ignoreCase)
    {
        // 处理边界情况
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(suffix))
            return str;

        // 根据ignoreCase参数选择合适的比较方式
        bool endsWith = ignoreCase
            ? str.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            : str.EndsWith(suffix, StringComparison.CurrentCulture);

        if (endsWith)
        {
            return str.Substring(0, str.Length - suffix.Length);
        }

        return str;
    }

    /// <summary>
    /// 扩展方法：移除字符串末尾指定的后缀字符串（使用指定的字符串比较选项）
    /// </summary>
    /// <param name="str">源字符串</param>
    /// <param name="suffix">要移除的后缀字符串</param>
    /// <param name="comparisonType">字符串比较选项</param>
    /// <returns>
    /// 如果字符串以指定后缀结尾（根据comparisonType参数决定比较方式），
    /// 则返回移除后缀后的字符串；否则返回原字符串
    /// </returns>
    public static string RemoveSuffix(this string str, string suffix, StringComparison comparisonType)
    {
        // 处理边界情况
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(suffix))
            return str;

        // 使用指定的比较选项检查是否以指定后缀结尾
        if (str.EndsWith(suffix, comparisonType))
        {
            return str.Substring(0, str.Length - suffix.Length);
        }

        return str;
    }

    /// <summary>
    /// 将函数名转换为指定的格式
    /// </summary>
    /// <param name="originalFunctionName">原始函数名</param>
    /// <returns>转换后的函数名</returns>
    public static string ConvertFunctionName(string originalFunctionName, string appendSuffixName)
    {
        return ConvertFunctionName(originalFunctionName, "Async", appendSuffixName);
    }

    /// <summary>
    /// 将函数名转换为指定的格式
    /// </summary>
    /// <param name="originalFunctionName">原始函数名</param>
    /// <returns>转换后的函数名</returns>
    public static string ConvertFunctionName(string originalFunctionName, string replateSuffixName, string appendSuffixName)
    {
        if (string.IsNullOrWhiteSpace(originalFunctionName) || string.IsNullOrEmpty(appendSuffixName))
        {
            return originalFunctionName;
        }

        if (originalFunctionName.EndsWith(replateSuffixName, StringComparison.OrdinalIgnoreCase))
        {
            return originalFunctionName.Substring(0, originalFunctionName.Length - replateSuffixName.Length) + appendSuffixName;
        }
        else
        {
            return originalFunctionName + appendSuffixName;
        }
    }

    /// <summary>
    /// 为字符串的每一行添加缩进
    /// </summary>
    /// <param name="str">输入字符串</param>
    /// <param name="indentLevel">缩进级别（每个级别4个空格）</param>
    /// <returns>添加缩进后的字符串</returns>
    public static string IndentLines(this string str, int indentLevel)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var indent = new string(' ', indentLevel * 4);
        var lines = str.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var indentedLines = lines.Select(line => string.IsNullOrEmpty(line) ? line : indent + line);

        return string.Join(Environment.NewLine, indentedLines);
    }
}
