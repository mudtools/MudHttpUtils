// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
    /// 获取默认Token类型字符串
    /// </summary>
    /// <returns>默认Token类型为AccessToken（OAuth2 通用类型）</returns>
    /// <remarks>
    /// 此处使用字符串字面量而非 <see cref="TokenTypes.AccessToken"/> 常量，
    /// 因为源生成器项目无法引用 Abstractions 程序集。
    /// 值必须与 <c>Mud.HttpUtils.TokenTypes.AccessToken</c> 保持一致。
    /// </remarks>
    public static string GetDefaultTokenType()
    {
        return "AccessToken";
    }

    /// <summary>
    /// 从Token特性中提取TokenManagerKey值，按优先级解析：
    /// 1. 显式设置的 TokenManagerKey 命名参数
    /// 2. TokenType 命名参数（显式指定的 TokenType 优先于构造函数默认值）
    /// 3. 构造函数参数（向后兼容，旧写法 [Token("TenantAccessToken")]）
    /// 4. 返回 null（由调用方使用默认值）
    /// </summary>
    /// <param name="tokenAttribute">Token特性数据</param>
    /// <returns>TokenManagerKey字符串，未找到时返回null</returns>
    public static string? GetTokenManagerKeyFromAttribute(AttributeData? tokenAttribute)
    {
        if (tokenAttribute == null)
            return null;

        var namedKey = tokenAttribute.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("TokenManagerKey", StringComparison.OrdinalIgnoreCase)).Value.Value;
        if (namedKey != null)
            return namedKey.ToString();

        var namedTokenType = tokenAttribute.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("TokenType", StringComparison.OrdinalIgnoreCase)).Value.Value;
        if (namedTokenType != null)
            return namedTokenType.ToString();

        if (tokenAttribute.ConstructorArguments.Length > 0)
        {
            var constructorValue = tokenAttribute.ConstructorArguments[0].Value;
            if (constructorValue != null)
                return constructorValue.ToString();
        }

        return null;
    }

    /// <summary>
    /// 从Token特性中提取RequiresUserId值
    /// </summary>
    /// <param name="tokenAttribute">Token特性数据</param>
    /// <returns>是否需要UserId，未指定时返回null</returns>
    public static bool? GetRequiresUserIdFromAttribute(AttributeData? tokenAttribute)
    {
        if (tokenAttribute == null)
            return null;

        var namedArg = tokenAttribute.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("RequiresUserId", StringComparison.OrdinalIgnoreCase)).Value.Value;

        if (namedArg is bool b)
            return b;

        return null;
    }

    /// <summary>
    /// 从 TypedConstant 获取 TokenInjectionMode 枚举名称。
    /// 注意：序号必须与 Mud.HttpUtils.Attributes.TokenInjectionMode 枚举定义保持一致：
    ///   0 = Header, 1 = Query, 2 = Path, 3 = ApiKey, 4 = HmacSignature, 5 = BasicAuth, 6 = Cookie
    /// 生成器无法引用包含枚举定义的程序集，因此使用硬编码序号是必要的妥协。
    /// </summary>
    public static string GetTokenInjectionModeName(object? value)
    {
        if (value == null)
            return HttpClientGeneratorConstants.TokenInjectionModeHeader;

        var str = value.ToString();
        if (string.IsNullOrEmpty(str))
            return HttpClientGeneratorConstants.TokenInjectionModeHeader;

        if (int.TryParse(str, out var num))
        {
            return num switch
            {
                0 => HttpClientGeneratorConstants.TokenInjectionModeHeader,
                1 => HttpClientGeneratorConstants.TokenInjectionModeQuery,
                2 => HttpClientGeneratorConstants.TokenInjectionModePath,
                3 => HttpClientGeneratorConstants.TokenInjectionModeApiKey,
                4 => HttpClientGeneratorConstants.TokenInjectionModeHmacSignature,
                5 => HttpClientGeneratorConstants.TokenInjectionModeBasicAuth,
                6 => HttpClientGeneratorConstants.TokenInjectionModeCookie,
                _ => HttpClientGeneratorConstants.TokenInjectionModeHeader
            };
        }

        var lastDot = str.LastIndexOf('.');
        var name = lastDot >= 0 ? str.Substring(lastDot + 1) : str;

        return name switch
        {
            "Header" => HttpClientGeneratorConstants.TokenInjectionModeHeader,
            "Query" => HttpClientGeneratorConstants.TokenInjectionModeQuery,
            "Path" => HttpClientGeneratorConstants.TokenInjectionModePath,
            "ApiKey" => HttpClientGeneratorConstants.TokenInjectionModeApiKey,
            "HmacSignature" => HttpClientGeneratorConstants.TokenInjectionModeHmacSignature,
            "BasicAuth" => HttpClientGeneratorConstants.TokenInjectionModeBasicAuth,
            "Cookie" => HttpClientGeneratorConstants.TokenInjectionModeCookie,
            _ => HttpClientGeneratorConstants.TokenInjectionModeHeader
        };
    }
}
