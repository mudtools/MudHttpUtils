// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 头值合法性校验器。
/// </summary>
/// <remarks>
/// <para>
/// [GEN-18][§8.6] .NET Core / .NET 5+ 的 <c>HttpRequestHeaders.Add</c> 会校验头值并拒绝 CR/LF，
/// 但 net4x / netstandard2.0 编译的 <c>HttpClient</c> 不校验 → 存在头部注入风险。
/// 由生成器在 Header 发射处调用本校验器快速失败，收敛平台差异。
/// </para>
/// <para>
/// M6-HC-24：校验口径扩展为「拒绝全部 C0 控制字符（U+0000-U+001F）与 DEL（U+007F），仅保留 HTAB（<c>\t</c>）」。
/// 依据 RFC 7230 <c>field-value</c>：CR/LF 属头部注入，其余 C0 控制字符亦不被允许；HTAB 可作为可选空白保留，
/// 故不拒绝（.NET 5+ 的实现同样放行 HTAB）。本类**无 TFM 分支**（单一实现），各目标框架行为一致。
/// </para>
/// </remarks>
public static class HttpHeaderValueValidator
{
    /// <summary>
    /// 校验指定 HTTP 头值（或头名）是否合法。
    /// </summary>
    /// <param name="value">要校验的值。</param>
    /// <returns>
    /// <c>true</c> 表示合法（含 null / 空白）；<c>false</c> 表示含 CR/LF、其它 C0 控制字符或 DEL。
    /// </returns>
    /// <remarks>M6-HC-24：HTAB（<c>\t</c>）视为合法，与 RFC 7230 及 .NET 5+ 实现一致。</remarks>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length == 0)
            return true;

        // M6-HC-24：逐字符核对，避免分配 char[]（原实现仅查 CR/LF，漏检 \0 等其它控制字符）。
        foreach (var c in value)
        {
            if ((c < 0x20 && c != '\t') || c == 0x7F)
                return false;
        }

        return true;
    }
}