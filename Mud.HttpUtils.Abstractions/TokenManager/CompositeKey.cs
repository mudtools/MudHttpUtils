// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text;

namespace Mud.HttpUtils;

/// <summary>
/// 架构不变式 I1（R-P0-04）：复合标识的<b>长度前缀</b>编码 —— 对任一分段内容天然单射，
/// 无需转义、无控制字符约定、O(n) 单次分配。
/// </summary>
/// <remarks>
/// <para>
/// 格式：<c>{len1}:{part1}{len2}:{part2}...</c>（len 为 UTF-16 码元数，与 <see cref="string.Length"/> 同口径）。
/// 因每段都以"自身长度 + 冒号"开头，任一分段内容（含分隔符、冒号、控制字符）都不可能跨越段边界，
/// 故 <c>Combine(a, b) == Combine(c, d)</c> 当且仅当 <c>a == c &amp;&amp; b == d</c>。
/// </para>
/// <para>
/// <b>适用场景</b>：分段内容<b>不可信</b>（如 <c>TokenRecoveryContext.TokenManagerKey</c>、
/// <c>UserId</c> —— 本项目已记录"中间层可灌入不可信输入"）的复合键，例如 401 恢复的去重键。
/// </para>
/// <para>
/// <b>不适用/不需替换</b>：分段可经前置守卫约束（如 <c>UserTokenManagerBase</c> 对 <c>userId</c>
/// 的控制字符拒绝）或已由 <see cref="ScopeKeyBuilder"/> 的可逆转义保证单射的场景 ——
/// 后者已有测试覆盖，为统一风格而改写属无收益的高回归改动（见修复方案 §0.3.2 修订 1/8）。
/// </para>
/// <para>
/// 刻意<b>不提供</b> <c>params string[]</c> 重载：去重键构造位于 401 恢复热路径，
/// <c>params</c> 数组分配与该路径的性能目标冲突。需要可变段数时应以
/// <c>Combine(ReadOnlySpan&lt;string&gt;)</c> + <c>stackalloc</c> 形式显式补充。
/// </para>
/// </remarks>
internal static class CompositeKey
{
    /// <summary>两段复合键。</summary>
    /// <param name="a">第一分段（不可为 null，null 视为空串）。</param>
    /// <param name="b">第二分段（不可为 null，null 视为空串）。</param>
    /// <returns>长度前缀编码后的单射键。</returns>
    public static string Combine(string? a, string? b)
    {
        var first = a ?? string.Empty;
        var second = b ?? string.Empty;

        var sb = new StringBuilder(first.Length + second.Length + 8);
        AppendPart(sb, first);
        AppendPart(sb, second);
        return sb.ToString();
    }

    /// <summary>三段复合键（如 <c>appKey + managerKey + scopeKey</c>）。</summary>
    /// <param name="a">第一分段。</param>
    /// <param name="b">第二分段。</param>
    /// <param name="c">第三分段。</param>
    /// <returns>长度前缀编码后的单射键。</returns>
    public static string Combine(string? a, string? b, string? c)
    {
        var first = a ?? string.Empty;
        var second = b ?? string.Empty;
        var third = c ?? string.Empty;

        var sb = new StringBuilder(first.Length + second.Length + third.Length + 12);
        AppendPart(sb, first);
        AppendPart(sb, second);
        AppendPart(sb, third);
        return sb.ToString();
    }

    /// <summary>四段复合键（如 <c>appKey + managerKey + userId + scopeKey</c>）。</summary>
    /// <param name="a">第一分段。</param>
    /// <param name="b">第二分段。</param>
    /// <param name="c">第三分段。</param>
    /// <param name="d">第四分段。</param>
    /// <returns>长度前缀编码后的单射键。</returns>
    public static string Combine(string? a, string? b, string? c, string? d)
    {
        var first = a ?? string.Empty;
        var second = b ?? string.Empty;
        var third = c ?? string.Empty;
        var fourth = d ?? string.Empty;

        var sb = new StringBuilder(first.Length + second.Length + third.Length + fourth.Length + 16);
        AppendPart(sb, first);
        AppendPart(sb, second);
        AppendPart(sb, third);
        AppendPart(sb, fourth);
        return sb.ToString();
    }

    /// <summary>追加一段长度前缀编码（<c>{len}:{value}</c>）。</summary>
    private static void AppendPart(StringBuilder sb, string value)
        => sb.Append(value.Length).Append(':').Append(value);
}
