// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
/// </remarks>
internal static class ScopeKeyBuilder
{
    /// <summary>空 / null scope 集合的规范化键。</summary>
    internal const string DefaultKey = "default";

    /// <summary>
    /// 构建 scope 缓存键：Distinct(Ordinal) → OrderBy(Ordinal) → Join(",")；null / 空数组 → "default"。
    /// </summary>
    internal static string Build(string[]? scopes)
    {
        if (scopes == null || scopes.Length == 0)
            return DefaultKey;

        return string.Join(",", scopes.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
    }
}
