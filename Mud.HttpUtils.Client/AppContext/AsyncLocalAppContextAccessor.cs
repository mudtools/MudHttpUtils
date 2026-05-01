namespace Mud.HttpUtils;

public class AsyncLocalAppContextAccessor : IAppContextAccessor
{
    private readonly AsyncLocal<IMudAppContext?> _context = new();

    public IMudAppContext? Current
    {
        get => _context.Value;
        set => _context.Value = value;
    }

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
        private readonly AsyncLocalAppContextAccessor _accessor;
        private int _disposed;

        public AppContextScope(IMudAppContext? previous, AsyncLocalAppContextAccessor accessor)
        {
            _previous = previous;
            _accessor = accessor;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _accessor._context.Value = _previous;
            }
        }
    }
}
