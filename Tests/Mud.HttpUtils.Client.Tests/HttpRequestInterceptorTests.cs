namespace Mud.HttpUtils.Client.Tests;

public class HttpRequestInterceptorTests
{
    [Fact]
    public void Interceptors_OrderedCorrectly()
    {
        var interceptor1 = new TestRequestInterceptor(1);
        var interceptor2 = new TestRequestInterceptor(2);
        var interceptor3 = new TestRequestInterceptor(3);

        var interceptors = new IHttpRequestInterceptor[] { interceptor3, interceptor1, interceptor2 }
            .OrderBy(i => i.Order)
            .ToArray();

        interceptors[0].Order.Should().Be(1);
        interceptors[1].Order.Should().Be(2);
        interceptors[2].Order.Should().Be(3);
    }

    [Fact]
    public void ResponseInterceptors_OrderedCorrectly()
    {
        var interceptor1 = new TestResponseInterceptor(1);
        var interceptor2 = new TestResponseInterceptor(2);

        var interceptors = new IHttpResponseInterceptor[] { interceptor2, interceptor1 }
            .OrderBy(i => i.Order)
            .ToArray();

        interceptors[0].Order.Should().Be(1);
        interceptors[1].Order.Should().Be(2);
    }

    [Fact]
    public async Task RequestInterceptor_OnRequestAsync_Called()
    {
        var interceptor = new TestRequestInterceptor(1);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://test/api");

        await interceptor.OnRequestAsync(request);

        interceptor.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ResponseInterceptor_OnResponseAsync_Called()
    {
        var interceptor = new TestResponseInterceptor(1);
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        await interceptor.OnResponseAsync(response);

        interceptor.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RequestInterceptor_CanModifyRequest()
    {
        var interceptor = new HeaderAddingInterceptor("X-Custom", "test-value");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://test/api");

        await interceptor.OnRequestAsync(request);

        request.Headers.GetValues("X-Custom").Should().ContainSingle("test-value");
    }

    private class TestRequestInterceptor : IHttpRequestInterceptor
    {
        public int Order { get; }
        public bool WasCalled { get; private set; }

        public TestRequestInterceptor(int order)
        {
            Order = order;
        }

        public Task OnRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private class TestResponseInterceptor : IHttpResponseInterceptor
    {
        public int Order { get; }
        public bool WasCalled { get; private set; }

        public TestResponseInterceptor(int order)
        {
            Order = order;
        }

        public Task OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private class HeaderAddingInterceptor : IHttpRequestInterceptor
    {
        private readonly string _name;
        private readonly string _value;

        public int Order => 0;

        public HeaderAddingInterceptor(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public Task OnRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            request.Headers.Add(_name, _value);
            return Task.CompletedTask;
        }
    }
}
