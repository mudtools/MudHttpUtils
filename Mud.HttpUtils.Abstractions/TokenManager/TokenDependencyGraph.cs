// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// F-7：令牌失效通知事件参数（<c>TokenManagerBase.TokenInvalidated</c>）。
/// </summary>
public sealed class TokenInvalidatedEventArgs : EventArgs
{
    /// <summary>被失效令牌所属管理器声明的令牌类型（<c>TokenType</c> 未声明时为 null）。</summary>
    public string? TokenType { get; }

    /// <summary>失效作用域（null/空数组 = 默认作用域）。</summary>
    public string[]? Scopes { get; }

    /// <summary>失效前的令牌信息（缓存未命中时为 <c>TokenResult.Empty</c>）。</summary>
    public TokenResult Removed { get; }

    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="tokenType">令牌类型；未声明时为 null。</param>
    /// <param name="scopes">失效作用域；null/空数组 = 默认作用域。</param>
    /// <param name="removed">失效前的令牌信息。</param>
    public TokenInvalidatedEventArgs(string? tokenType, string[]? scopes, TokenResult removed)
    {
        TokenType = tokenType;
        Scopes = scopes;
        Removed = removed;
    }
}

/// <summary>
/// F-7（G-7/TK-1）：<b>链式令牌依赖图</b> —— 声明令牌类型之间的派生依赖关系，
/// 供 <c>TokenManagerBase</c> 的级联失效钩子查询"某令牌失效后还应连带失效谁"。
/// </summary>
/// <remarks>
/// <para>
/// <b>依赖语义</b>：<c>Add("corp_access_token", "suite_access_token")</c> 表示
/// <c>corp_access_token</c> <b>由</b> <c>suite_access_token</c> <b>派生</b>（换码链：
/// suite token → 换 corp token）—— 因此<b>被依赖者失效 ⇒ 依赖者失效</b>：
/// <c>suite_access_token</c> 失效时 <c>corp_access_token</c> 也必须失效并重取
/// （典型场景：企业微信 <c>42001 → suite_access_token → 重注入</c>）。
/// </para>
/// <para>
/// <b>生命周期</b>：构建期可变（<see cref="Add"/> 链式），构建完成后视为<b>只读</b>
/// （查询线程安全；构建与查询不要并发）。图本身不做任何 I/O 与失效动作 ——
/// 级联传播由 <c>TokenManagerBase.EnableTokenDependencyCascade</c> 开关
/// （<b>默认关闭</b>）与 <c>TokenInvalidated</c> 事件协作完成。
/// </para>
/// <para>
/// <b>环</b>：自依赖（<c>A → A</c>）在 <see cref="Add"/> 时拒绝；互相依赖（<c>A → B → A</c>）
/// 不禁止（语义上罕见但无害），查询端以访问集合保证遍历终止。
/// </para>
/// </remarks>
public sealed class TokenDependencyGraph
{
    /// <summary>空图（未声明任何依赖）。单例，<see cref="IsEmpty"/> 为 true。</summary>
    public static TokenDependencyGraph Empty { get; } = new();

    // tokenType → 其直接依赖的 tokenType 列表（"我由谁派生"）。
    private readonly Dictionary<string, List<string>> _edges = new(StringComparer.Ordinal);

    /// <summary>已声明过依赖关系的全部 tokenType（含两端）。序数比较。</summary>
    public IReadOnlyCollection<string> DeclaredTokenTypes => _edges.Keys;

    /// <summary>是否未声明任何依赖。</summary>
    public bool IsEmpty => _edges.Count == 0;

    /// <summary>
    /// 声明 <paramref name="tokenType"/> 由 <paramref name="dependsOn"/> 各令牌派生（可链式调用）。
    /// </summary>
    /// <param name="tokenType">依赖者（被派生出的令牌类型）。</param>
    /// <param name="dependsOn">被依赖的令牌类型（其失效将连带失效 <paramref name="tokenType"/>）。不得为空，元素不得为空白。</param>
    /// <returns>this（链式）。</returns>
    /// <exception cref="ArgumentException"><paramref name="tokenType"/> 为空白、<paramref name="dependsOn"/> 为空/含空白元素、或自依赖。</exception>
    public TokenDependencyGraph Add(string tokenType, params string[] dependsOn)
    {
        if (string.IsNullOrWhiteSpace(tokenType))
            throw new ArgumentException("令牌类型不得为空。", nameof(tokenType));
        if (dependsOn == null || dependsOn.Length == 0)
            throw new ArgumentException("依赖的令牌类型不得为空。", nameof(dependsOn));

        foreach (var dependency in dependsOn)
        {
            if (string.IsNullOrWhiteSpace(dependency))
                throw new ArgumentException("依赖的令牌类型元素不得为空白。", nameof(dependsOn));
            if (string.Equals(tokenType, dependency, StringComparison.Ordinal))
                throw new ArgumentException($"令牌类型 '{tokenType}' 不得自依赖。", nameof(dependsOn));
        }

        if (!_edges.TryGetValue(tokenType!, out var list))
        {
            list = new List<string>();
            _edges[tokenType!] = list;
        }
        foreach (var dependency in dependsOn)
        {
            if (!list.Contains(dependency, StringComparer.Ordinal))
                list.Add(dependency);
            // 被依赖端也登记进节点集（保证 DeclaredTokenTypes 含叶子、查询可定位）。
            if (!_edges.ContainsKey(dependency))
                _edges[dependency] = new List<string>();
        }

        return this;
    }

    /// <summary>
    /// 查询 <paramref name="tokenType"/> 失效时应连带失效的<b>全部</b>（直接 + 传递）依赖者，
    /// 顺序为依赖距离由近及远（同层按声明序），去重。
    /// </summary>
    /// <param name="tokenType">失效源头令牌类型。</param>
    /// <returns>依赖者 tokenType 快照；无依赖返回空集合（非 null）。</returns>
    /// <exception cref="ArgumentException"><paramref name="tokenType"/> 为空白。</exception>
    public IReadOnlyList<string> GetDependentsOf(string tokenType)
    {
        if (string.IsNullOrWhiteSpace(tokenType))
            throw new ArgumentException("令牌类型不得为空。", nameof(tokenType));

        // 反向 BFS：依赖边是"我由谁派生"，失效传播方向相反（被依赖者 → 依赖者）。
        var visited = new HashSet<string>(StringComparer.Ordinal) { tokenType! };
        var result = new List<string>();
        var frontier = new Queue<string>();
        frontier.Enqueue(tokenType!);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var pair in _edges)
            {
                // pair.Value 是 pair.Key 的直接依赖 ⇒ pair.Key 是 current 的依赖者。
                if (!pair.Value.Contains(current, StringComparer.Ordinal))
                    continue;
                if (!visited.Add(pair.Key))
                    continue;   // 互依环：访问集合保证终止
                result.Add(pair.Key);
                frontier.Enqueue(pair.Key);
            }
        }

        return result;
    }
}
