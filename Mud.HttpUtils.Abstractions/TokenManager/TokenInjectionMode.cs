namespace Mud.HttpUtils;

/// <summary>
/// 令牌注入模式枚举，指定令牌在 HTTP 请求中的注入位置。
/// </summary>
public enum TokenInjectionMode
{
    /// <summary>
    /// 将令牌注入到 HTTP 请求头中。
    /// </summary>
    Header = 0,

    /// <summary>
    /// 将令牌注入到 HTTP 请求的查询参数中。
    /// </summary>
    Query = 1,

    /// <summary>
    /// 将令牌注入到 HTTP 请求的路径中。
    /// </summary>
    Path = 2,
}
