// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests.Infrastructure;

/// <summary>
/// 测试用日志收集器：把 <see cref="ILogger"/> 输出按 (EventId, Level, 格式化消息) 记录，
/// 供断言"某事件是否被记录""日志中是否泄漏了敏感内容"。
/// </summary>
/// <remarks>
/// 线程安全（内部加锁）—— 被测代码可能在定时器 / 后台线程上写日志。
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _gate = new();

    /// <summary>已收集的日志条目快照。</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>是否存在指定 EventId 的日志。</summary>
    /// <param name="eventId">事件编号。</param>
    public bool HasEventId(int eventId) => Entries.Any(e => e.EventId == eventId);

    /// <summary>是否存在包含指定文本（Ordinal）的日志消息。</summary>
    /// <param name="text">待查找文本。</param>
    public bool ContainsText(string text)
        => Entries.Any(e => e.Message.Contains(text, StringComparison.Ordinal));

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Add(int eventId, LogLevel level, string message)
    {
        lock (_gate)
        {
            _entries.Add(new LogEntry(eventId, level, message));
        }
    }

    /// <summary>单条日志。</summary>
    /// <param name="EventId">事件编号（0 表示未指定）。</param>
    /// <param name="Level">日志级别。</param>
    /// <param name="Message">已格式化的消息。</param>
    internal readonly record struct LogEntry(int EventId, LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger
    {
        private readonly RecordingLoggerProvider _owner;

        public RecordingLogger(RecordingLoggerProvider owner) => _owner = owner;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _owner.Add(eventId.Id, logLevel, formatter(state, exception));
    }
}
