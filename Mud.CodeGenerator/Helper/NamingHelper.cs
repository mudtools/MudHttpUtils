// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// COM包装器命名辅助类，提供统一的命名前缀处理逻辑
/// </summary>
internal static class NamingHelper
{
    /// <summary>
    /// 已知的COM接口前缀及其对应实现类前缀
    /// </summary>
    private static readonly (string InterfacePrefix, string ImpPrefix)[] KnownPrefixes =
    [
        ("IWord", "Word"),
        ("IExcel", "Excel"),
        ("IOffice", "Office"),
        ("IPowerPoint", "PowerPoint"),
        ("IVbe", "Vbe")
    ];

    /// <summary>
    /// 从接口类型名中移除已知的前缀
    /// </summary>
    /// <param name="interfaceTypeName">接口类型名</param>
    /// <returns>移除前缀后的类名</returns>
    public static string RemoveInterfacePrefix(string interfaceTypeName)
    {
        if (string.IsNullOrWhiteSpace(interfaceTypeName))
            return interfaceTypeName;

        // 遍历预定义前缀，检查并移除
        foreach (var (interfacePrefix, _) in KnownPrefixes.OrderByDescending(p => p.InterfacePrefix.Length))
        {
            if (interfaceTypeName.StartsWith(interfacePrefix, StringComparison.Ordinal))
            {
                // 检查移除前缀后的部分是否为空或以大写字母开头
                var remaining = interfaceTypeName.Substring(interfacePrefix.Length);
                if (remaining.Length == 0 || char.IsUpper(remaining[0]))
                {
                    return remaining.Length == 0 ? interfaceTypeName : remaining;
                }
            }
        }

        // 如果没有找到预定义前缀，返回原始类名
        return interfaceTypeName;
    }

    /// <summary>
    /// 从实现类类型名中移除已知的前缀
    /// </summary>
    /// <param name="impTypeName">实现类类型名</param>
    /// <returns>移除前缀后的类名</returns>
    public static string RemoveImpPrefix(string impTypeName)
    {
        if (string.IsNullOrWhiteSpace(impTypeName))
            return impTypeName;

        // 1. 获取最后一个点后面的部分（去掉命名空间）
        string className = impTypeName;
        int lastDotIndex = impTypeName.LastIndexOf('.');
        if (lastDotIndex >= 0 && lastDotIndex < impTypeName.Length - 1)
        {
            className = impTypeName.Substring(lastDotIndex + 1);
        }

        // 2. 遍历预定义前缀，检查并移除
        foreach (var (_, impPrefix) in KnownPrefixes.OrderByDescending(p => p.ImpPrefix.Length))
        {
            if (className.StartsWith(impPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // 检查移除前缀后的部分是否以大写字母开头或为空
                var remaining = className.Substring(impPrefix.Length);
                if (remaining.Length > 0 && char.IsUpper(remaining[0]))
                {
                    return remaining;
                }
                else if (remaining.Length == 0)
                {
                    // 如果整个类名就是前缀本身，直接返回
                    return className;
                }
                // 如果移除前缀后不以大写字母开头，可能不是正确的前缀，继续尝试其他前缀
            }
        }

        // 3. 如果没有找到预定义前缀，返回原始类名
        return className;
    }

    /// <summary>
    /// 获取COM类名的Ordinal类型（移除接口前缀）
    /// </summary>
    /// <param name="ordinalComType">Ordinal COM类型名</param>
    /// <returns>移除前缀后的类型名</returns>
    public static string GetOrdinalComType(string ordinalComType)
    {
        if (string.IsNullOrEmpty(ordinalComType))
            return ordinalComType;

        foreach (var (interfacePrefix, _) in KnownPrefixes)
        {
            if (ordinalComType.StartsWith(interfacePrefix, StringComparison.Ordinal))
            {
                return ordinalComType.Substring(interfacePrefix.Length).TrimEnd('?');
            }
        }
        return ordinalComType;
    }

    /// <summary>
    /// 检查类型名是否以已知前缀开头
    /// </summary>
    /// <param name="typeName">类型名</param>
    /// <param name="isInterface">是否为接口类型</param>
    /// <returns>如果以已知前缀开头返回true</returns>
    public static bool HasKnownPrefix(string typeName, bool isInterface)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        return isInterface
            ? KnownPrefixes.Any(p => typeName.StartsWith(p.InterfacePrefix, StringComparison.Ordinal))
            : KnownPrefixes.Any(p => typeName.StartsWith(p.ImpPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
