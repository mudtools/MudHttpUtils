using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// P0-2 回归：进程级缓存单例的键必须携带「应用维度 + 客户端维度」。
/// </summary>
/// <remarks>
/// <para>
/// 修复前键只有应用维度：HttpClient 模式下应用维度恒为 <c>"default"</c>，
/// 不同客户端（不同 BaseAddress/凭据、或因多应用而分裂的命名客户端）同方法同参请求
/// 会命中同一键 ⇒ 跨客户端串缓存。
/// </para>
/// <para>
/// 本文件锁定三条契约：① 不同 <see cref="IHttpClientIdentity.ClientName"/> 的键互不相同；
/// ② 未实现 <see cref="IHttpClientIdentity"/> 的客户端退化为历史键格式（零漂移）；
/// ③ 同名客户端仍共用键（同名即同一资源，属预期）。
/// </para>
/// </remarks>
public class DefaultHttpRequestExecutorCacheScopeTests
{
    private const string CacheKey = "Demo.IApi.GetAsync|1";
    private const char Separator = '\u001F';

    [Fact]
    public async Task CacheScopeKey_ClientDimension_ShouldIsolate()
    {
        var cache = new CapturingCache();
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            cacheProvider: cache);

        var clientA = CreateClient("app-A");
        var clientB = CreateClient("app-B");

        var first = await executor.ExecuteAsync<string>(
            NewRequest(), clientA.Mock.Object, NewDescriptor(), jsonSerializerOptions: null);
        var second = await executor.ExecuteAsync<string>(
            NewRequest(), clientB.Mock.Object, NewDescriptor(), jsonSerializerOptions: null);

        first.Should().Be("payload");
        second.Should().Be("payload");

        cache.Keys.Should().HaveCount(2, "两个不同客户端名必须产生两个不同的缓存键");
        cache.Keys[0].Should().NotBe(cache.Keys[1]);
        cache.Keys[0].Should().Contain($"app-A{Separator}{CacheKey}");
        cache.Keys[1].Should().Contain($"app-B{Separator}{CacheKey}");
        clientA.Counter.SendAsyncCalls.Should().Be(1);
        clientB.Counter.SendAsyncCalls.Should().Be(1);
    }

    [Fact]
    public async Task CacheScopeKey_SameClientName_ShouldShare()
    {
        var cache = new CapturingCache();
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            cacheProvider: cache);

        var clientA1 = CreateClient("app-A");
        var clientA2 = CreateClient("app-A");

        await executor.ExecuteAsync<string>(NewRequest(), clientA1.Mock.Object, NewDescriptor(), jsonSerializerOptions: null);
        await executor.ExecuteAsync<string>(NewRequest(), clientA2.Mock.Object, NewDescriptor(), jsonSerializerOptions: null);

        cache.Keys.Should().HaveCount(2);
        cache.Keys[0].Should().Be(cache.Keys[1], "同名客户端属同一资源，必须共用缓存键");
        clientA2.Counter.SendAsyncCalls.Should().Be(0, "第二次应命中第一次写入的缓存");
    }

    [Fact]
    public async Task CacheScopeKey_NoClientIdentity_ShouldFallbackLegacyFormat()
    {
        var cache = new CapturingCache();
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            cacheProvider: cache);

        // 未实现 IHttpClientIdentity 的客户端（外部实现方 / 测试替身）
        var anonymous = new Mock<IBaseHttpClient>();
        SetupSendRaw(anonymous, new ClientCallCounter());

        await executor.ExecuteAsync<string>(NewRequest(), anonymous.Object, NewDescriptor(), jsonSerializerOptions: null);

        cache.Keys.Should().ContainSingle();
        cache.Keys[0].Should().Be($"default{Separator}{CacheKey}",
            "缺少客户端维度时必须退化为历史键格式（{appKey}␟{CacheKey}），保证零行为漂移");
    }

    private static HttpRequestMessage NewRequest()
        => new(HttpMethod.Get, new Uri("https://api.example.com/demo"));

    private static ExecutionDescriptor NewDescriptor()
        => new()
        {
            Cache = new CacheOptions { DurationSeconds = 60 },
            CacheKey = CacheKey,
        };

    /// <summary>带客户端名的 IBaseHttpClient 替身（Moq 的 As&lt;T&gt; 让同一代理同时实现两个接口）。</summary>
    private static (Mock<IBaseHttpClient> Mock, ClientCallCounter Counter) CreateClient(string clientName)
    {
        var counter = new ClientCallCounter();
        var mock = new Mock<IBaseHttpClient>();
        mock.As<IHttpClientIdentity>().Setup(x => x.ClientName).Returns(clientName);
        SetupSendRaw(mock, counter);
        return (mock, counter);
    }

    /// <summary>执行器统一走 SendRawAsync（错误码判断与内容读取都在执行器内），故据此计数与返回内容。</summary>
    private static void SetupSendRaw(Mock<IBaseHttpClient> mock, ClientCallCounter counter)
        => mock.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                counter.SendAsyncCalls++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("payload", System.Text.Encoding.UTF8, "text/plain"),
                });
            });

    private sealed class ClientCallCounter
    {
        public int SendAsyncCalls { get; set; }
    }

    /// <summary>捕获 scopedKey 的缓存替身：委托真实实现，仅记录每次 GetOrFetchAsync 的键。</summary>
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
