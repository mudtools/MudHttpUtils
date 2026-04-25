// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;

namespace Mud.HttpUtils;

/// <summary>
/// Token类型转换辅助工具
/// </summary>
internal static class TokenHelper
{
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
    /// 从Token特性中提取Scopes值
    /// </summary>
    /// <param name="tokenAttribute">Token特性数据</param>
    /// <returns>Scopes字符串，未找到时返回null</returns>
    public static string? GetScopesFromAttribute(AttributeData? tokenAttribute)
    {
        if (tokenAttribute == null)
            return null;

        var namedScopes = tokenAttribute.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("Scopes", StringComparison.OrdinalIgnoreCase)).Value.Value;

        return namedScopes?.ToString();
    }

    /// <summary>
    /// 解析Scopes字符串为数组
    /// </summary>
    /// <param name="scopesValue">逗号分隔的Scopes字符串</param>
    /// <returns>Scopes数组，空时返回空数组</returns>
    public static string[] ParseScopes(string? scopesValue)
    {
        if (string.IsNullOrWhiteSpace(scopesValue))
            return Array.Empty<string>();

        return scopesValue
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    /// <summary>
    /// 生成带Scopes的GetOrRefreshTokenAsync调用代码
    /// </summary>
    /// <param name="scopes">Scopes数组</param>
    /// <returns>生成的调用代码字符串</returns>
    public static string GenerateTokenGetCode(string[] scopes)
    {
        if (scopes.Length == 0)
        {
            return "await tokenManager.GetOrRefreshTokenAsync(cancellationToken)";
        }

        var scopesArray = string.Join(", ", scopes.Select(s => $"\"{s}\""));
        return $"await tokenManager.GetOrRefreshTokenAsync(new[] {{ {scopesArray} }}, cancellationToken)";
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
