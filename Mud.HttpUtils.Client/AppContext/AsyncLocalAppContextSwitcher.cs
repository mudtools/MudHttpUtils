namespace Mud.HttpUtils;

public class AsyncLocalAppContextSwitcher : IAppContextSwitcher
{
    private readonly AsyncLocal<IMudAppContext?> _context = new();

    public IMudAppContext? Current
    {
        get => _context.Value;
        set => _context.Value = value;
    }

    public IMudAppContext UseApp(string appKey)
    {
        throw new NotSupportedException(
            "UseApp 需要通过生成的 API 实现类调用，该类包含 IAppManager 依赖。" +
            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
    }

    public IMudAppContext UseDefaultApp()
    {
        throw new NotSupportedException(
            "UseDefaultApp 需要通过生成的 API 实现类调用，该类包含 IAppManager 依赖。" +
            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
    }

    public IDisposable BeginScope(IMudAppContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var previous = _context.Value;
        _context.Value = context;

        return new AppContextScope(previous, this);
    }

    public IDisposable BeginScope(string appKey)
    {
        throw new NotSupportedException(
            "BeginScope(string) 需要通过生成的 API 实现类调用，该类包含 IAppManager 依赖。" +
            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
    }

    public Task<string> GetTokenAsync()
    {
        throw new NotSupportedException(
            "GetTokenAsync 需要通过生成的 API 实现类调用，该类包含 ITokenProvider 依赖。" +
            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
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
