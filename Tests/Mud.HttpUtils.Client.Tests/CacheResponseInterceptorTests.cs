using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mud.HttpUtils.Client.Tests;

public class CacheResponseInterceptorTests
{
    private readonly CacheResponseInterceptor _interceptor;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<CacheResponseInterceptor>> _loggerMock;

    public CacheResponseInterceptorTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
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
    public void TryGetCached_KeyNotInCache_ReturnsFalse()
    {
        var result = _interceptor.TryGetCached<string>("nonexistent", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Set_ThenTryGetCached_ReturnsValue()
    {
        _interceptor.Set("test-key", "test-value", 60);

        var result = _interceptor.TryGetCached<string>("test-key", out var value);

        result.Should().BeTrue();
        value.Should().Be("test-value");
    }

    [Fact]
    public void Set_NullValue_DoesNotCache()
    {
        _interceptor.Set<string>("null-key", null!, 60);

        var result = _interceptor.TryGetCached<string>("null-key", out var value);

        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_ExistingKey_RemovesFromCache()
    {
        _interceptor.Set("remove-key", "value", 60);
        _interceptor.Remove("remove-key");

        var result = _interceptor.TryGetCached<string>("remove-key", out var value);

        result.Should().BeFalse();
    }

    [Fact]
    public void Set_WithSlidingExpiration_StoresValue()
    {
        _interceptor.Set("sliding-key", "sliding-value", 60, useSlidingExpiration: true);

        var result = _interceptor.TryGetCached<string>("sliding-key", out var value);

        result.Should().BeTrue();
        value.Should().Be("sliding-value");
    }

    [Fact]
    public void Set_WithHighPriority_StoresValue()
    {
        _interceptor.Set("high-key", "high-value", 60, priority: CacheItemPriority.High);

        var result = _interceptor.TryGetCached<string>("high-key", out var value);

        result.Should().BeTrue();
        value.Should().Be("high-value");
    }

    [Fact]
    public void TryGetCached_WrongType_ReturnsFalse()
    {
        _interceptor.Set("int-key", 42, 60);

        var result = _interceptor.TryGetCached<string>("int-key", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }
}
