// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026 
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <inheritdoc/>
/// <remarks>
/// <para>
/// <b>作用域栈实现（AM2-08 / ADR-005）</b>：环境值不再是"当前上下文 + 归属判定"，而是
/// <see cref="AsyncLocal{T}"/> 持有的<b>不可变链表栈顶</b>（每个节点含 Context、Parent、Disposed）。
/// <see cref="Current"/> 沿链回退到最近的未释放节点。
/// </para>
/// <para>
/// <b>为什么必须改</b>：旧实现用 <c>ReferenceEquals(Current, owner)</c> 决定是否回滚 ——
/// 乱序释放（先释放外层）时，中间应用的节点释放时会回滚到"自己的 previous"，
/// 终态残留他人应用上下文（可能串租户）。该错误终态曾被测试<b>固化</b>为"已声明行为"。
/// 栈式实现下每个节点只负责"标记自己已释放"，回退由读取端沿链完成 ⇒ 乱序释放也回到正确祖先，
/// 且<b>永远不会把某个上下文写进不属于它的执行上下文</b>。
/// </para>
/// <para>
/// <b>热路径成本</b>：无释放发生时 <see cref="Current"/> 仍是"1 次 AsyncLocal 读 + 1 次 Disposed 判定"，
/// 与旧实现等价；只有链上有已释放节点时才需要额外跳数（嵌套深度实际 ≤3）。
/// </para>
/// <para>
/// <b>跨执行上下文释放</b>：节点对象可能被其它异步流共享，释放只把该节点置为 Disposed，
/// 不写入任何 <c>Current</c> ⇒ 收敛方向与旧实现一致（fail-closed，不向无关异步流注入上下文）。
/// </para>
/// </remarks>
public class AsyncLocalAppContextSwitcher : IAppContextHolder
{
    /// <summary>作用域栈节点（不可变链；<see cref="Disposed"/> 是唯一的可变位）。</summary>
    private sealed class ScopeNode(IMudAppContext context, ScopeNode? parent)
    {
        public IMudAppContext Context { get; } = context;

        public ScopeNode? Parent { get; } = parent;

        /// <summary>是否已释放。由创建它的作用域写入，读取端沿链跳过。</summary>
        public bool Disposed;
    }

    private readonly AsyncLocal<ScopeNode?> _stack = new();

    /// <inheritdoc/>
    /// <remarks>
    /// 沿链返回最近的未释放节点。<c>init</c> 写入会<b>整体替换</b>栈顶（等价于"从根开始"），
    /// 与 <see cref="SwitchTo"/> 的"无作用域切换"语义一致。
    /// </remarks>
    public IMudAppContext? Current
    {
        get
        {
            var node = _stack.Value;
            while (node is not null && node.Disposed)
                node = node.Parent;

            return node?.Context;
        }
        init => _stack.Value = value is null ? null : new ScopeNode(value, null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>无作用域切换</b>：把栈顶整体替换为一个"匿名节点"（<c>Parent = null</c>），
    /// 因此之前建立的 <see cref="BeginScope"/> 作用域在释放时<b>不会</b>把值写回 ——
    /// 这正是 <c>UseApp</c> 的危险性所在（不会自动归还上下文），推荐改用 <c>UseAppScope</c>。
    /// </remarks>
    public void SwitchTo(IMudAppContext? context)
        => _stack.Value = context is null ? null : new ScopeNode(context, null);

    /// <inheritdoc/>
    public IDisposable BeginScope(IMudAppContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var node = new ScopeNode(context, _stack.Value);
        _stack.Value = node;

        return new AppContextScope(node);
    }

    /// <summary>作用域句柄：释放时只把<b>自己的节点</b>置为已释放（幂等）。</summary>
    private sealed class AppContextScope(ScopeNode node) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // 仅标记本节点：不读取、也不写入任何 Current。
            // ⇒ 乱序释放时外层节点仍能沿链回到正确祖先；跨 EC 释放也不会污染他人上下文。
            node.Disposed = true;
        }
    }
}
