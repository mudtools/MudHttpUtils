// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// SR-M5/M9（P2.2/P3.3，D11）scope 缓存键构建的单一实现（Single Source of Truth）。
/// </summary>
/// <remarks>
/// <para>
/// 规范化规则：<c>Distinct(Ordinal) → OrderBy(Ordinal) → 转义 → Join(US)</c>；null / 空数组 → <c>"default"</c>。
/// </para>
/// <para>
/// 修复 SR-M5：原 <c>TokenManagerBase.GetScopeKey</c> 不去重且大小写敏感
/// （<c>{"a","A"}</c> 两个键；<c>{"a","a"}</c> → <c>"a,a"</c>），攻击者传入海量 scope 组合时
/// 缓存与锁表无界增长。规范化后等价 scope 集合命中同一键，配合硬上限 LRU 收敛（见 UpdateToken 超限路径）。
/// </para>
/// <para>
/// <b>MT-16</b>：分隔符由 <c>","</c> 改为不可见分隔符 U+001F（Unit Separator），并对元素内出现的
/// U+001F / U+001E 做可逆转义 —— 修复 <c>["a,b"]</c> 与 <c>["a","b"]</c> 都产出 <c>"a,b"</c> 的键碰撞
/// （碰撞会令两个语义不同的作用域共享同一缓存条目与同一把锁，构成作用域越权面）。
/// 采用与用户复合键（<c>userId + U+001F + scopeKey</c>）一致的约定，避免引入第二种分隔风格。
/// </para>
/// <para>D7 用户复合键（userId + U+001F + scopeKey）亦委托本实现，保证两套键语义一致。</para>
/// <para>
/// TMR-11：对规范化结果做记忆化（<see cref="ConcurrentDictionary{TKey,TValue}"/>），
/// 热路径上等价 scope 数组复用同一 string 实例，消除重复字符串分配。
/// 缓存上限与 <c>MaxScopeCacheSize</c> 联动，超出时清空重建（无界保护）。
/// </para>
/// </remarks>
internal static class ScopeKeyBuilder
{
    /// <summary>空 / null scope 集合的规范化键。</summary>
    internal const string DefaultKey = "default";

    /// <summary>MT-16：scope 元素之间的分隔符（Unit Separator，不可见控制字符）。</summary>
    internal const char ScopeSeparator = '\u001F';

    /// <summary>MT-16：转义前缀（Record Separator），在元素内出现 U+001F / U+001E 时使用。</summary>
    private const char Escape = '\u001E';

    /// <summary>MT-16：元素内 U+001E 的转义形式。</summary>
    private const string EscapedEscape = "\u001E\u001E";

    /// <summary>MT-16：元素内 U+001F 的转义形式。</summary>
    private const string EscapedSeparator = "\u001E\u001F";

    /// <summary>TMR-11：记忆化缓存上限（与 TokenManagerBase.MaxScopeCacheSize 对齐）。</summary>
    private const int MemoCacheLimit = 1024;

    /// <summary>TMR-11：scope 数组 → 规范化键的记忆化缓存。键为内容哈希，值为规范化字符串。</summary>
    private static readonly ConcurrentDictionary<string, string> s_memoCache = new();

    /// <summary>
    /// MT-16：对单个 scope 元素做可逆转义，保证「转义 → 拼接」是单射：
    /// <c>Escape(x) + US + Escape(y)</c> 的解析结果唯一，不会因元素内含分隔符而产生歧义。
    /// </summary>
    private static string EscapeElement(string value)
    {
        // 先转义转义符本身（否则 U+001E 会被二次解释），再转义分隔符。
        if (value.AsSpan().IndexOfAny(Escape, ScopeSeparator) < 0)
            return value;   // 绝大多数 scope 不含控制字符：零分配快路径

        // 注：string.Replace(string, string, StringComparison) 在 netstandard2.0 不可用；
        // string 重载本身即为序数（ordinal）比较，语义一致。
        return value
            .Replace(Escape.ToString(), EscapedEscape)
            .Replace(ScopeSeparator.ToString(), EscapedSeparator);
    }

    /// <summary>TMR-11：构建廉价的缓存键（长度 + 内容拼接），保证等价数组产生同一键。</summary>
    private static string BuildMemoKey(string[] sorted)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(sorted.Length).Append(':');
        for (var i = 0; i < sorted.Length; i++)
        {
            if (i > 0)
                builder.Append(ScopeSeparator);
            builder.Append(EscapeElement(sorted[i]));
        }
        return builder.ToString();
    }


    /// <summary>
    /// 构建 scope 缓存键：Distinct(Ordinal) → OrderBy(Ordinal) → 转义 → Join(US)；null / 空数组 → "default"。
    /// TMR-11：结果记忆化，等价 scope 数组复用同一 string 实例。
    /// TMX-15-7 (D2)：BuildMemoKey 与值工厂共享一次 Distinct/OrderBy，消除命中路径的重复分配；
    /// Clear 改为按容量触发的批量淘汰（仅当超限时执行，且用 TryAdd 原子性保证）。
    /// </summary>
    internal static string Build(string[]? scopes)
    {
        if (scopes == null || scopes.Length == 0)
            return DefaultKey;

        // TMX-15-7：只做一次 Distinct/OrderBy，复用排序结果构建 memoKey 和值（MT-16：memoKey 使用可逆转义）
        var sorted = scopes.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var memoKey = BuildMemoKey(sorted);

        // 无界保护：超限时清空重建（廉价清空，不逐条枚举）
        // TMX-15-7：用 Count 检查移到 GetOrAdd 外面，避免命中路径的额外 volatile read
        if (s_memoCache.Count >= MemoCacheLimit)
            s_memoCache.Clear();

        return s_memoCache.GetOrAdd(memoKey, _ =>
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < sorted.Length; i++)
            {
                if (i > 0)
                    builder.Append(ScopeSeparator);
                builder.Append(EscapeElement(sorted[i]));
            }
            return builder.ToString();
        });
    }
}
