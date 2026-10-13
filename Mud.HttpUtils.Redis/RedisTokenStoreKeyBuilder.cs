// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Redis;

/// <summary>
/// F-3：Redis 令牌键构建（<b>键布局单一事实来源</b>）。
/// </summary>
/// <remarks>
/// <para>
/// 键形态：<c>{prefix}:{tokenType}:{appKey}:{field}</c>，其中 <c>{field}</c> ∈
/// <see cref="AccessField"/> / <see cref="RefreshField"/>，基形
/// <c>{prefix}:{tokenType}:{appKey}</c> 与方案 F-3 钉死的
/// <c>mud:httputils:{tokenType}:{appKey}</c> 一致。
/// </para>
/// <para>
/// <b>解析鲁棒性</b>：<c>tokenType</c> 允许含 <c>':'</c>（不转义）—— 枚举/解析从<b>右侧</b>剥除
/// 固定的 <c>{appKey}:{field}</c> 段（appKey 与 field 均为实例内固定值），剩余部分整体即 tokenType，
/// 无需转义、天然单射。appKey 为空时取 <see cref="RedisTokenStoreOptions.SharedAppKeySegment"/>。
/// </para>
/// </remarks>
public static class RedisTokenStoreKeyBuilder
{
    /// <summary>访问令牌字段后缀。</summary>
    public const string AccessField = "access";

    /// <summary>刷新令牌字段后缀。</summary>
    public const string RefreshField = "refresh";

    private const char Separator = ':';

    /// <summary>构建访问令牌键。</summary>
    /// <param name="prefix">键前缀（<see cref="RedisTokenStoreOptions.KeyPrefix"/>）。</param>
    /// <param name="tokenType">令牌类型（可含 <c>':'</c>）。</param>
    /// <param name="appKey">应用标识；null/空白时取 <see cref="RedisTokenStoreOptions.SharedAppKeySegment"/>。</param>
    public static string BuildAccessKey(string prefix, string tokenType, string? appKey)
        => Build(prefix, tokenType, appKey, AccessField);

    /// <summary>构建刷新令牌键。</summary>
    /// <param name="prefix">键前缀。</param>
    /// <param name="tokenType">令牌类型。</param>
    /// <param name="appKey">应用标识；null/空白时取共享段。</param>
    public static string BuildRefreshKey(string prefix, string tokenType, string? appKey)
        => Build(prefix, tokenType, appKey, RefreshField);

    /// <summary>构建令牌类型枚举用的键模式（<c>{prefix}:*:{appKey}:access</c>）。</summary>
    /// <param name="prefix">键前缀。</param>
    /// <param name="appKey">应用标识；null/空白时取共享段。</param>
    public static string BuildAccessPattern(string prefix, string? appKey)
        => prefix + Separator + "*" + Separator + NormalizeAppKey(appKey) + Separator + AccessField;

    /// <summary>构建前缀下全量清理用的键模式（<c>{prefix}:*:*:*</c>）。</summary>
    /// <param name="prefix">键前缀。</param>
    public static string BuildAllPattern(string prefix)
        => prefix + Separator + "*" + Separator + "*" + Separator + "*";

    /// <summary>
    /// 从访问令牌键反解 tokenType（<see cref="BuildAccessKey"/> 的逆操作；键不匹配本布局时返回 <c>null</c>）。
    /// </summary>
    /// <param name="prefix">键前缀。</param>
    /// <param name="appKey">应用标识；null/空白时取共享段。</param>
    /// <param name="key">Redis 键。</param>
    /// <returns>tokenType；键不匹配返回 <c>null</c>。</returns>
    public static string? TryParseTokenType(string prefix, string? appKey, string key)
    {
        if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(key))
            return null;

        var suffix = Separator + NormalizeAppKey(appKey) + Separator + AccessField;
        if (!key.StartsWith(prefix + Separator, StringComparison.Ordinal))
            return null;
        if (!key.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        // 剥离 "{prefix}:" 前缀与 ":{appKey}:access" 后缀；tokenType 允许含 ':'（右侧剥除即单射）。
        // 边界：后缀可能与前缀区域重叠（如 "mud:httputils:app-a:access" 的 tokenType 段为空），
        // 此时 rest 长度不足后缀长度，必须整键拒绝而不是做负长度切片。
        var rest = key.Substring(prefix.Length + 1);
        if (rest.Length <= suffix.Length)
            return null;

        var middle = rest.Substring(0, rest.Length - suffix.Length);
        return middle.Length == 0 ? null : middle;
    }

    private static string Build(string prefix, string tokenType, string? appKey, string field)
    {
        if (string.IsNullOrEmpty(prefix))
            throw new ArgumentException("键前缀不得为空。", nameof(prefix));
        if (string.IsNullOrEmpty(tokenType))
            throw new ArgumentException("令牌类型不得为空。", nameof(tokenType));

        return prefix + Separator + tokenType + Separator + NormalizeAppKey(appKey) + Separator + field;
    }

    private static string NormalizeAppKey(string? appKey)
        // netstandard2.0 的 string.IsNullOrWhiteSpace 无 NotNullWhen 注解，false 分支需显式断言。
        => string.IsNullOrWhiteSpace(appKey) ? RedisTokenStoreOptions.SharedAppKeySegment : appKey!;
}
