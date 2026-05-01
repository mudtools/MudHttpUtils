using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Tests;

public class CacheResponseInterceptorExpirationTests
{
    private readonly CacheResponseInterceptor _interceptor;
    private readonly MemoryHttpResponseCache _cache;
    private readonly Mock<ILogger<CacheResponseInterceptor>> _loggerMock;

    public CacheResponseInterceptorExpirationTests()
    {
        _cache = new MemoryHttpResponseCache();
        _loggerMock = new Mock<ILogger<CacheResponseInterceptor>>();
        _interceptor = new CacheResponseInterceptor(_cache, _loggerMock.Object);
    }

    [Fact]
    public async Task TryGet_AfterExpiration_ReturnsFalse()
    {
        _interceptor.Set("expiring-key", "expiring-value", TimeSpan.FromMilliseconds(100));

        await Task.Delay(200);

        var result = _interceptor.TryGet<string>("expiring-key", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public async Task TryGet_BeforeExpiration_ReturnsTrue()
    {
        _interceptor.Set("valid-key", "valid-value", TimeSpan.FromSeconds(60));

        await Task.Delay(50);

        var result = _interceptor.TryGet<string>("valid-key", out var value);

        result.Should().BeTrue();
        value.Should().Be("valid-value");
    }

    [Fact]
    public async Task Set_WithSlidingExpiration_ExtendsExpirationOnAccess()
    {
        _interceptor.Set("sliding-key", "sliding-value", TimeSpan.FromMilliseconds(200), useSlidingExpiration: true);

        await Task.Delay(100);
        var result1 = _interceptor.TryGet<string>("sliding-key", out var value1);
        result1.Should().BeTrue();

        await Task.Delay(100);
        var result2 = _interceptor.TryGet<string>("sliding-key", out var value2);
        result2.Should().BeTrue();
        value2.Should().Be("sliding-value");
    }

    [Fact]
    public async Task Set_WithSlidingExpiration_ExpiresWhenNotAccessed()
    {
        _interceptor.Set("sliding-expire-key", "sliding-expire-value", TimeSpan.FromMilliseconds(100), useSlidingExpiration: true);

        await Task.Delay(200);

        var result = _interceptor.TryGet<string>("sliding-expire-key", out var value);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Set_WithAbsoluteExpiration_DoesNotExtendOnAccess()
    {
        _interceptor.Set("absolute-key", "absolute-value", TimeSpan.FromMilliseconds(200), useSlidingExpiration: false);

        await Task.Delay(100);
        var result1 = _interceptor.TryGet<string>("absolute-key", out _);
        result1.Should().BeTrue();

        await Task.Delay(150);
        var result2 = _interceptor.TryGet<string>("absolute-key", out _);
        result2.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrFetchAsync_WithExpiredCache_ReFetches()
    {
        var fetchCount = 0;
        _interceptor.Set("fetch-key", "cached-value", TimeSpan.FromMilliseconds(100));

        await Task.Delay(200);

        var result = await _interceptor.GetOrFetchAsync<string>(
            "fetch-key",
            () =>
            {
                fetchCount++;
                return Task.FromResult("refreshed-value");
            },
            TimeSpan.FromSeconds(60));

        result.Should().Be("refreshed-value");
        fetchCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrFetchAsync_WithValidCache_ReturnsCachedValue()
    {
        var fetchCount = 0;
        _interceptor.Set("cached-key", "cached-value", TimeSpan.FromSeconds(60));

        var result = await _interceptor.GetOrFetchAsync<string>(
            "cached-key",
            () =>
            {
                fetchCount++;
                return Task.FromResult("fetched-value");
            },
            TimeSpan.FromSeconds(60));

        result.Should().Be("cached-value");
        fetchCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrFetchAsync_WithNoCache_FetchesAndCaches()
    {
        var fetchCount = 0;

        var result = await _interceptor.GetOrFetchAsync<string>(
            "new-key",
            () =>
            {
                fetchCount++;
                return Task.FromResult("fetched-value");
            },
            TimeSpan.FromSeconds(60));

        result.Should().Be("fetched-value");
        fetchCount.Should().Be(1);

        var cachedResult = _interceptor.TryGet<string>("new-key", out var cachedValue);
        cachedResult.Should().BeTrue();
        cachedValue.Should().Be("fetched-value");
    }

    [Fact]
    public async Task RemoveAsync_RemovesCachedItem()
    {
        _interceptor.Set("remove-key", "remove-value", TimeSpan.FromSeconds(60));

        await _interceptor.RemoveAsync("remove-key");

        var result = _interceptor.TryGet<string>("remove-key", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_RemovesAllCachedItems()
    {
        _interceptor.Set("key1", "value1", TimeSpan.FromSeconds(60));
        _interceptor.Set("key2", "value2", TimeSpan.FromSeconds(60));

        await _interceptor.ClearAsync();

        _interceptor.TryGet<string>("key1", out _).Should().BeFalse();
        _interceptor.TryGet<string>("key2", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Set_WithDifferentTypes_StoresCorrectly()
    {
        _interceptor.Set("int-key", 42, TimeSpan.FromSeconds(60));
        _interceptor.Set("date-key", DateTime.Today, TimeSpan.FromSeconds(60));

        var intResult = _interceptor.TryGet<int>("int-key", out var intValue);
        var dateResult = _interceptor.TryGet<DateTime>("date-key", out var dateValue);

        intResult.Should().BeTrue();
        intValue.Should().Be(42);
        dateResult.Should().BeTrue();
        dateValue.Should().Be(DateTime.Today);
    }
}
