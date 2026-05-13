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
        set => _context.Value = value;
    }

    /// <inheritdoc/>
    public IDisposable BeginScope(IMudAppContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var previous = _context.Value;
        _context.Value = context;

        return new AppContextScope(previous, this);
    }

    private sealed class AppContextScope : IDisposable
    {
        private readonly IMudAppContext? _previous;
        private readonly AsyncLocalAppContextSwitcher _switcher;
        private int _disposed;

        public AppContextScope(IMudAppContext? previous, AsyncLocalAppContextSwitcher switcher)
        {
            _previous = previous;
            _switcher = switcher;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _switcher._context.Value = _previous;
            }
        }
    }
}
