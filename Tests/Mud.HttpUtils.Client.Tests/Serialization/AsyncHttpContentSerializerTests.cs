// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests.Serialization;

/// <summary>
/// 异步内容构造扩展点（<see cref="IAsyncHttpContentSerializer"/>）：
/// 已注册 ⇒ 请求体经异步工厂构造（同步路径零调用）；未注册 ⇒ 逐字节保持既有同步行为。
/// </summary>
public class AsyncHttpContentSerializerTests
{
    public sealed class RequestPayload { public string Name { get; set; } = "x"; }

    /// <summary>记录型序列化器：同时实现同步与异步内容构造，分别计数。</summary>
    private sealed class RecordingContentSerializer : IHttpContentSerializer, IAsyncHttpContentSerializer
    {
        public int SyncCallCount;
        public int AsyncCallCount;

        public HttpContent? ToHttpContent<T>(T item, object? options = null)
        {
            Interlocked.Increment(ref SyncCallCount);
            return new StringContent("{\"sync\":true}", Encoding.UTF8, "application/json");
        }

        public async ValueTask<HttpContent?> ToHttpContentAsync<T>(T item, object? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref AsyncCallCount);
            await Task.Yield();
            return new StringContent("{\"async\":true}", Encoding.UTF8, "application/json");
        }

        public Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(T));

        public string Serialize<T>(T item, object? options = null) => "{}";

        public string Serialize(object? item, Type type, object? options = null) => "{}";

        public T? Deserialize<T>(string json, object? options = null) => default;

        public string? GetFieldNameForProperty(PropertyInfo propertyInfo) => propertyInfo.Name;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }

    private sealed class TestableClient : EnhancedHttpClient
    {
        public TestableClient(HttpClient httpClient, IHttpContentSerializer serializer)
            : base(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, null, serializer)
        {
        }
    }

    private static (TestableClient Client, RecordingContentSerializer Serializer) BuildClient(bool asyncCapable)
    {
        var serializer = new RecordingContentSerializer();
        var httpClient = new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://api.example.com") };
        return (new TestableClient(httpClient, asyncCapable ? serializer : new SyncOnlyWrapper(serializer)), serializer);
    }

    /// <summary>仅暴露同步面的包装：模拟"未注册异步序列化器"的存量宿主。</summary>
    private sealed class SyncOnlyWrapper : IHttpContentSerializer
    {
        private readonly RecordingContentSerializer _inner;

        public SyncOnlyWrapper(RecordingContentSerializer inner) => _inner = inner;

        public HttpContent? ToHttpContent<T>(T item, object? options = null) => _inner.ToHttpContent(item, options);

        public Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
            => _inner.FromHttpContentAsync<T>(content, options, cancellationToken);

        public string Serialize<T>(T item, object? options = null) => _inner.Serialize(item, options);

        public string Serialize(object? item, Type type, object? options = null) => _inner.Serialize(item, type, options);

        public T? Deserialize<T>(string json, object? options = null) => _inner.Deserialize<T>(json, options);

        public string? GetFieldNameForProperty(PropertyInfo propertyInfo) => _inner.GetFieldNameForProperty(propertyInfo);
    }

    [Fact]
    public async Task AsyncSerializerRegistered_ContentConstructedViaAsyncPath_SyncPathUntouched()
    {
        var (client, serializer) = BuildClient(asyncCapable: true);

        using var _ = PublicDnsResolution();
        await client.PostAsJsonAsync<RequestPayload, object>("api/pay", new RequestPayload());

        serializer.AsyncCallCount.Should().Be(1, "已注册异步序列化器 ⇒ 内容经异步工厂构造");
        serializer.SyncCallCount.Should().Be(0, "同步构造路径零调用（逐字节不回归）");
    }

    [Fact]
    public async Task AsyncSerializerAbsent_SyncPathUnchanged()
    {
        var (client, serializer) = BuildClient(asyncCapable: false);

        using var _ = PublicDnsResolution();
        await client.PostAsJsonAsync<RequestPayload, object>("api/pay", new RequestPayload());

        serializer.SyncCallCount.Should().Be(1, "未注册异步序列化器 ⇒ 既有同步行为逐字节保持");
        serializer.AsyncCallCount.Should().Be(0);
    }

    /// <summary>把测试域名解析到公网地址，避开沙箱 DNS 对 example.com 的私网解析（SSRF 校验会先于发送执行）。</summary>
    private static IDisposable PublicDnsResolution()
    {
        UrlValidator.DnsResolveOverride = _ => new[] { System.Net.IPAddress.Parse("93.184.216.34") };
        return new DisposableAction(() => UrlValidator.DnsResolveOverride = null);
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;

        public DisposableAction(Action action) => _action = action;

        public void Dispose() => _action();
    }
}
