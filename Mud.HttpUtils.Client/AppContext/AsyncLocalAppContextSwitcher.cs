// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <inheritdoc/>
public class AsyncLocalAppContextSwitcher : IAppContextHolder
{
    private readonly AsyncLocal<IMudAppContext?> _context = new();

    /// <inheritdoc/>
    public IMudAppContext? Current
    {
        get => _context.Value;
        init => _context.Value = value;
    }

    /// <inheritdoc/>
    public void SwitchTo(IMudAppContext? context)
    {
        _context.Value = context;
    }

    /// <inheritdoc/>
    public IDisposable BeginScope(IMudAppContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var previous = _context.Value;
        // B8：记录本作用域写入的值，用于释放时归属判定。
        var owner = context;
        _context.Value = context;

        return new AppContextScope(previous, owner, this);
    }

    private sealed class AppContextScope(
        IMudAppContext? previous,
        IMudAppContext owner,
        AsyncLocalAppContextSwitcher switcher) : IDisposable
    {
        private readonly IMudAppContext? _previous = previous;
        private readonly IMudAppContext _owner = owner;
        private readonly AsyncLocalAppContextSwitcher _switcher = switcher;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // B8：仅当环境上下文仍等于本作用域写入的值时才回滚。
            // 跨执行上下文释放（如 Task.Run(() => scope.Dispose())）时不会把"他人的当前值"改写成陈旧值，
            // 同时避免把 ctxA 的 previous 写入 ctxB。代价是该场景下 ctxA 的值不再被自动还原，
            // 由宿主显式 UseDefaultApp/BeginScope 收敛。
            if (ReferenceEquals(_switcher._context.Value, _owner))
                _switcher._context.Value = _previous;
            else if (_switcher._context.Value is null)
                _switcher._context.Value = _previous;
        }
    }
}
