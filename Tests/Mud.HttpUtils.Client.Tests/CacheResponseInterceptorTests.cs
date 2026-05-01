using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mud.HttpUtils.Client.Tests;

public class CacheResponseInterceptorTests
{
    private readonly CacheResponseInterceptor _interceptor;
    private readonly MemoryHttpResponseCache _cache;
    private readonly Mock<ILogger<CacheResponseInterceptor>> _loggerMock;

    public CacheResponseInterceptorTests()
    {
        _cache = new MemoryHttpResponseCache();
        _loggerMock = new Mock<ILogger<CacheResponseInterceptor>>();
        _interceptor = new CacheResponseInterceptor(_cache, _loggerMock.Object);
    }

    [Fact]
    public void Order_Returns100()
    {
        _interceptor.Order.Should().Be(100);
    }

    [Fact]
    public async Task OnResponseAsync_CompletesSuccessfully()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        await _interceptor.OnResponseAsync(response, CancellationToken.None);
    }

    [Fact]
    public void TryGet_KeyNotInCache_ReturnsFalse()
    {
        var result = _interceptor.TryGet<string>("nonexistent", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsValue()
    {
        _interceptor.Set("test-key", "test-value", TimeSpan.FromSeconds(60));

        var result = _interceptor.TryGet<string>("test-key", out var value);

        result.Should().BeTrue();
        value.Should().Be("test-value");
    }

    [Fact]
    public void Set_NullValue_DoesNotCache()
    {
        _interceptor.Set<string>("null-key", null, TimeSpan.FromSeconds(60));

        var result = _interceptor.TryGet<string>("null-key", out var value);

        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_ExistingKey_RemovesFromCache()
    {
        _interceptor.Set("remove-key", "value", TimeSpan.FromSeconds(60));
        _interceptor.Remove("remove-key");

        var result = _interceptor.TryGet<string>("remove-key", out var value);

        result.Should().BeFalse();
    }

    [Fact]
    public void Set_WithSlidingExpiration_StoresValue()
    {
        _interceptor.Set("sliding-key", "sliding-value", TimeSpan.FromSeconds(60), useSlidingExpiration: true);

        var result = _interceptor.TryGet<string>("sliding-key", out var value);

        result.Should().BeTrue();
        value.Should().Be("sliding-value");
    }
}
