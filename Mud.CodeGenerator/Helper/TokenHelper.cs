// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Globalization;

namespace Mud.CodeGenerator;

/// <summary>
/// Token类型转换辅助工具
/// </summary>
internal static class TokenHelper
{
    /// <summary>
    /// 将Token类型枚举值转换为字符串
    /// </summary>
    /// <param name="enumValue">枚举值</param>
    /// <returns>Token类型字符串</returns>
    public static string ConvertTokenEnumValueToString(int enumValue)
    {
        return enumValue switch
        {
            0 => "TenantAccessToken",
            1 => "UserAccessToken",
            2 => "AppAccessToken",
            3 => "Both",
            _ => "TenantAccessToken"
        };
    }

    /// <summary>
    /// 从Token特性中提取TokenType值
    /// </summary>
    /// <param name="tokenAttribute">Token特性数据</param>
    /// <returns>Token类型字符串，未找到时返回null</returns>
    public static string? GetTokenTypeFromAttribute(AttributeData? tokenAttribute)
    {
        if (tokenAttribute == null)
            return null;

        // 检查命名参数 TokenType（现在是字符串）
        var namedTokenType = tokenAttribute.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("TokenType", StringComparison.OrdinalIgnoreCase)).Value.Value;

        if (namedTokenType != null)
        {
            return namedTokenType.ToString();
        }

        // 检查构造函数参数（现在是字符串）
        if (tokenAttribute.ConstructorArguments.Length > 0)
        {
            var tokenTypeValue = tokenAttribute.ConstructorArguments[0].Value;
            if (tokenTypeValue != null)
            {
                return tokenTypeValue.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// 获取默认Token类型字符串
    /// </summary>
    /// <returns>默认Token类型为TenantAccessToken</returns>
    public static string GetDefaultTokenType()
    {
        return "TenantAccessToken";
    }
}
