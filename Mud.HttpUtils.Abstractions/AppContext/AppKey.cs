// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// MT-18：应用标识（appKey）校验与安全文本化的公开门面。
/// </summary>
/// <remarks>
/// <para>
/// 内部实现为 <c>AppKeyValidator</c>（<c>internal</c>，零正则手写字符判断，AOT 安全）。
/// 由于 <b>源生成器产物位于消费方程序集</b>，生成代码无法访问 <c>internal</c> 成员 ——
/// 而生成代码需要在调用 <see cref="IAppAccessAuthorizer.CanSwitchTo"/> 之前先做格式校验
/// （授权器不应承担格式校验职责），并在异常消息中安全地回显 appKey（防日志注入）。
/// 故在此暴露只读门面。
/// </para>
/// <para>本类型为纯函数式工具，无状态、无分配（除 <see cref="ToSafeText"/> 的必要输出外）。</para>
/// <para>
/// <b>大小写语义（G8-11）</b>：appKey <b>区分大小写</b>，且本类<b>不做</b>任何大小写归一化 ——
/// <c>"Tenant"</c> 与 <c>"tenant"</c> 是两个不同的应用。宿主注册与查询必须使用完全一致的文本
/// （应用管理器内部以序数语义的字典存储）。该行为属<b>有意契约</b>，由
/// <c>AppManagerCaseSensitivityTests</c>（Client.Tests）钉死。
/// </para>
/// </remarks>
public static class AppKey
{
    /// <summary>应用标识允许的最大长度（128）。</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// 判断应用标识是否合法：非空、长度不超过 <see cref="MaxLength"/>，
    /// 且仅由字母、数字、<c>'.'</c>、<c>'_'</c>、<c>'-'</c> 组成（首字符必须是字母或数字）。
    /// </summary>
    /// <remarks>大小写敏感，不做归一化（见类型备注 G8-11）。</remarks>
    /// <param name="value">待校验的应用标识。</param>
    /// <returns>合法返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool IsValid(string? value)
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

    /// <summary>
    /// 把应用标识转换为可安全写入日志 / 异常消息的文本（截断到 <see cref="MaxLength"/> 并过滤控制字符）。
    /// </summary>
    /// <param name="value">原始应用标识（可含不可信输入）。</param>
    /// <returns>安全文本；输入为 null / 空时返回空字符串。</returns>
    public static string ToSafeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var limit = value!.Length > MaxLength ? MaxLength : value.Length;
        var buffer = new char[limit];
        for (var i = 0; i < limit; i++)
        {
            var c = value[i];
            buffer[i] = c < ' ' || c == (char)0x7F ? '_' : c;
        }
        return new string(buffer);
    }
}
