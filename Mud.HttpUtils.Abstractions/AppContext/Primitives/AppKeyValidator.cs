// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用标识 / 命名客户端名称的格式校验器（零正则、AOT 安全）。
/// </summary>
/// <remarks>
/// 标识会被用于：<c>ConcurrentDictionary</c> 键、.NET 8+ Keyed Service 键、日志与异常消息插值。
/// 因此必须限制字符集与长度，防止日志注入与内存放大。
/// </remarks>
internal static class AppKeyValidator
{
    internal const int MaxLength = 128;

    /// <summary>
    /// 判断标识是否合法（不抛异常）：非空、长度不超过 <see cref="MaxLength"/>，
    /// 且仅由字母、数字、<c>'.'</c>、<c>'_'</c>、<c>'-'</c> 组成（首字符必须是字母或数字）。
    /// </summary>
    /// <remarks>本方法是格式规则的<b>唯一实现</b>：公开门面 <see cref="AppKey.IsValid"/> 直接委托此处，
    /// 避免规则在多处各写一份而出现行为漂移。</remarks>
    /// <param name="value">待校验的标识。</param>
    /// <returns>合法返回 <c>true</c>，否则 <c>false</c>。</returns>
    internal static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value!.Length > MaxLength)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isAsciiLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            var isDigit = c >= '0' && c <= '9';
            var isSeparator = c == '.' || c == '_' || c == '-';

            var legal = i == 0 ? isAsciiLetter || isDigit : isAsciiLetter || isDigit || isSeparator;
            if (!legal)
                return false;
        }

        return true;
    }

    /// <summary>校验标识格式；不合法时抛出 <see cref="ArgumentException"/>。</summary>
    /// <param name="value">待校验的标识。</param>
    /// <param name="paramName">异常参数名。</param>
    internal static void Validate(string? value, string paramName)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("应用标识不能为空。", paramName);

        if (value.Length > MaxLength)
            throw new ArgumentException($"应用标识长度不得超过 {MaxLength}。", paramName);

        if (!IsValid(value))
            throw new ArgumentException(
                "应用标识只能由字母、数字、'.'、'_'、'-' 组成，且首字符必须是字母或数字。", paramName);
    }

    /// <summary>把标识转换为可安全写入日志/异常消息的文本（截断 + 过滤控制字符）。</summary>
    internal static string ToSafeText(string? value)
    {
        if (value is null || value.Length == 0)
            return string.Empty;

        var limit = value.Length > MaxLength ? MaxLength : value.Length;
        var buffer = new char[limit];
        for (var i = 0; i < limit; i++)
        {
            var c = value[i];
            buffer[i] = c < ' ' || c == (char)0x7F ? '_' : c;
        }
        return new string(buffer);
    }
}
