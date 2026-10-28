using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 2026-10-28 方案：应用维度解析与缺失上下文回退（AM2-05 / AM2-12）。
/// </summary>
/// <remarks>
/// <para>
/// <b>AM2-05</b>：执行器原为三处 <c>try { GetDefaultApp() } catch { }</c> —— 把懒加载工厂故障也吞掉。
/// 现按"状态"判定：默认键为空才允许捕获 <see cref="InvalidOperationException"/>，
/// 键非空时任何异常都必须上抛。
/// </para>
/// <para>
/// <b>AM2-12</b>：缓存键/熔断作用域的应用维度优先取<b>请求属性</b>（生成代码在方法入口写入），
/// 环境上下文仅作兜底 —— 修复"作用域提前释放导致 B 应用响应写入 A 应用缓存维度"。
/// </para>
/// </remarks>
public class AppDimensionResolutionTests
{
    private const string CacheKey = "Demo.IApi.GetAsync|1";
    private const char Separator = '\u001F';

    [Fact]
    public async Task CacheScope_UsesRequestAppKey_WhenAmbientContextCleared()
    {
        var cache = new CapturingCache();
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            cacheProvider: cache);

        var request = NewRequest();
        request.Options.Set(
            new HttpRequestOptionsKey<string>(HttpExecutionConstants.AppKeyPropertyKey), "app-B");

        await executor.ExecuteAsync<string>(request, NewClient(), NewDescriptor(), jsonSerializerOptions: null);

        cache.Keys.Should().ContainSingle();
        cache.Keys[0].Should().Be($"app-B{Separator}{CacheKey}",
            "AM2-12：应用维度必须来自方法入口写入的请求属性，而不是执行期重读的环境上下文");
    }

    [Fact]
    public async Task CacheScope_FallsBackToAmbientContext_WhenRequestHasNoAppKey()
    {
        var cache = new CapturingCache();
        var holder = new AsyncLocalAppContextSwitcher();
        holder.SwitchTo(new FakeContext("app-A"));

        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            cacheProvider: cache,
            appContextHolder: holder);

        await executor.ExecuteAsync<string>(NewRequest(), NewClient(), NewDescriptor(), jsonSerializerOptions: null);

        cache.Keys.Should().ContainSingle();
        cache.Keys[0].Should().Be($"app-A{Separator}{CacheKey}", "请求未携带应用维度时回退环境上下文（零漂移）");
    }

    [Fact]
    public async Task MissingContext_NoDefaultApp_FallsBackAndWarnsOnce()
    {
        var cache = new CapturingCache();
        var logger = new CountingLogger<DefaultHttpRequestExecutor>();
        var manager = new Mock<IAppManager<IMudAppContext>>();
        manager.SetupGet(m => m.DefaultAppKey).Returns((string?)null);
        manager.Setup(m => m.GetDefaultApp()).Throws(new InvalidOperationException("未设置默认应用。"));

        var executor = new DefaultHttpRequestExecutor(
            logger,
            cacheProvider: cache,
            appManager: manager.Object);

        await executor.ExecuteAsync<string>(NewRequest(), NewClient(), NewDescriptor(), jsonSerializerOptions: null);
        await executor.ExecuteAsync<string>(NewRequest(), NewClient(), NewDescriptor(), jsonSerializerOptions: null);

        cache.Keys.Should().OnlyContain(k => k.StartsWith("default" + Separator, StringComparison.Ordinal),
            "无默认应用时键维度退化为 \"default\"（fail-open 仅影响维度隔离，不产生脏读）");
        logger.WarningCount.Should().Be(1,
            "AM2-05：缺少应用上下文的告警每个执行器实例只发一次（实例闸门而非进程级静态）");
    }

    [Fact]
    public async Task MissingContext_LazyDefaultFactoryThrows_ExceptionPropagates()
    {
        var manager = new Mock<IAppManager<IMudAppContext>>();
        manager.SetupGet(m => m.DefaultAppKey).Returns("lazy-default");
        manager.Setup(m => m.GetDefaultApp())
            .Throws(new InvalidOperationException("懒加载工厂：配置尚未就绪"));

        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            cacheProvider: new CapturingCache(),
            appManager: manager.Object);

        var act = async () => await executor
            .ExecuteAsync<string>(NewRequest(), NewClient(), NewDescriptor(), jsonSerializerOptions: null)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("配置尚未就绪", StringComparison.Ordinal))
            .ConfigureAwait(false);
    }

    private static HttpRequestMessage NewRequest()
        => new(HttpMethod.Get, new Uri("https://api.example.com/demo"));

    private static ExecutionDescriptor NewDescriptor()
        => new()
        {
            Cache = new CacheOptions { DurationSeconds = 60 },
            CacheKey = CacheKey,
        };

    private static IBaseHttpClient NewClient()
    {
        var mock = new Mock<IBaseHttpClient>();
        mock.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("payload", System.Text.Encoding.UTF8, "text/plain"),
            }));
        return mock.Object;
    }

    private sealed class FakeContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    /// <summary>统计 Warning 次数的日志器（其余级别短路）。</summary>
    private sealed class CountingLogger<T> : ILogger<T>
    {
        private int _warnings;

        public int WarningCount => Volatile.Read(ref _warnings);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Interlocked.Increment(ref _warnings);
        }
    }

    /// <summary>捕获 scopedKey 的缓存替身：委托真实实现，仅记录每次取键。</summary>
    private sealed class CapturingCache : IHttpResponseCache
    {
        private readonly MemoryHttpResponseCache _inner = new();

        public List<string> Keys { get; } = new();

        public bool TryGet<T>(string key, out T? value) => _inner.TryGet(key, out value);

        public void Set<T>(string key, T? value, TimeSpan absoluteExpirationRelativeToNow)
            => _inner.Set(key, value, absoluteExpirationRelativeToNow);

        public void Set<T>(string key, T? value, TimeSpan expirationRelativeToNow, bool useSlidingExpiration)
            => _inner.Set(key, value, expirationRelativeToNow, useSlidingExpiration);

        public void Remove(string key) => _inner.Remove(key);

        public Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            return _inner.GetOrFetchAsync(key, fetchFunc, expiration, cancellationToken);
        }

        public Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, bool useSlidingExpiration, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            return _inner.GetOrFetchAsync(key, fetchFunc, expiration, useSlidingExpiration, cancellationToken);
        }

        public Task RemoveAsync(string key) => _inner.RemoveAsync(key);

        public Task ClearAsync() => _inner.ClearAsync();
    }
}
