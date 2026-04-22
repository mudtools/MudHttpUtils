// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

internal static class StringExtensions
{
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
