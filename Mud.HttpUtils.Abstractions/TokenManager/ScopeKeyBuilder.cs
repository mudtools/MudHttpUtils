// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// SR-M5/M9（P2.2/P3.3，D11）scope 缓存键构建的单一实现（Single Source of Truth）。
/// </summary>
/// <remarks>
/// 规范化规则：<c>Distinct(Ordinal) → OrderBy(Ordinal) → Join(",")</c>；null / 空数组 → <c>"default"</c>。
/// <para>
/// 修复 SR-M5：原 <c>TokenManagerBase.GetScopeKey</c> 不去重且大小写敏感
/// （<c>{"a","A"}</c> 两个键；<c>{"a","a"}</c> → <c>"a,a"</c>），攻击者传入海量 scope 组合时
/// 缓存与锁表无界增长。规范化后等价 scope 集合命中同一键，配合硬上限 LRU 收敛（见 UpdateToken 超限路径）。
/// </para>
/// <para>D7 用户复合键（userId + "\u001F" + scopeKey）亦委托本实现，保证两套键语义一致。</para>
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

    /// <summary>TMR-11：记忆化缓存上限（与 TokenManagerBase.MaxScopeCacheSize 对齐）。</summary>
    private const int MemoCacheLimit = 1024;

    /// <summary>TMR-11：scope 数组 → 规范化键的记忆化缓存。键为内容哈希，值为规范化字符串。</summary>
    private static readonly ConcurrentDictionary<string, string> s_memoCache = new();

    /// <summary>TMR-11：构建廉价的缓存键（长度 + 内容拼接），保证等价数组产生同一键。</summary>
    private static string BuildMemoKey(string[] scopes)
    {
        var sorted = scopes.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return string.Concat(sorted.Length.ToString(), ":", string.Join(",", sorted));
    }

    /// <summary>
    /// 构建 scope 缓存键：Distinct(Ordinal) → OrderBy(Ordinal) → Join(",")；null / 空数组 → "default"。
    /// TMR-11：结果记忆化，等价 scope 数组复用同一 string 实例。
    /// </summary>
    internal static string Build(string[]? scopes)
    {
        if (scopes == null || scopes.Length == 0)
            return DefaultKey;

        var memoKey = BuildMemoKey(scopes);

        // 无界保护：超限时清空重建（廉价清空，不逐条枚举）
        if (s_memoCache.Count >= MemoCacheLimit)
            s_memoCache.Clear();

        return s_memoCache.GetOrAdd(memoKey, _ =>
            string.Join(",", scopes.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)));
    }
}
