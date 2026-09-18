using System.Threading;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests;

public class CacheResponseInterceptorConcurrencyTests
{
    private readonly CacheResponseInterceptor _interceptor;
    private readonly MemoryHttpResponseCache _cache;
    private readonly Mock<ILogger<CacheResponseInterceptor>> _loggerMock;

    public CacheResponseInterceptorConcurrencyTests()
    {
        _cache = new MemoryHttpResponseCache();
        _loggerMock = new Mock<ILogger<CacheResponseInterceptor>>();
        _interceptor = new CacheResponseInterceptor(_cache, _loggerMock.Object);
    }

    [Fact]
    public async Task Set_ConcurrentWrites_DoesNotCorruptCache()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() =>
            {
                _interceptor.Set($"key-{i}", $"value-{i}", TimeSpan.FromSeconds(60));
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        for (int i = 0; i < 50; i++)
        {
            var result = _interceptor.TryGet<string>($"key-{i}", out var value);
            result.Should().BeTrue($"key-{i} should exist in cache");
            value.Should().Be($"value-{i}");
        }
    }

    [Fact]
    public async Task TryGet_ConcurrentReads_ReturnsCorrectValues()
    {
        for (int i = 0; i < 50; i++)
        {
            _interceptor.Set($"key-{i}", $"value-{i}", TimeSpan.FromSeconds(60));
        }

        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() =>
            {
                var result = _interceptor.TryGet<string>($"key-{i}", out var value);
                result.Should().BeTrue();
                value.Should().Be($"value-{i}");
            }))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Set_ConcurrentReadWrite_NoDeadlockOrCorruption()
    {
        for (int i = 0; i < 20; i++)
        {
            _interceptor.Set($"key-{i}", $"initial-{i}", TimeSpan.FromSeconds(60));
        }

        var readTasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                {
                    _interceptor.TryGet<string>($"key-{i}", out _);
                }
            }))
            .ToArray();

        var writeTasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                {
                    _interceptor.Set($"key-{i}", $"updated-{i}-{j}", TimeSpan.FromSeconds(60));
                }
            }))
            .ToArray();

        var allTasks = readTasks.Concat(writeTasks).ToArray();

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
        var completedTask = await Task.WhenAny(Task.WhenAll(allTasks), timeoutTask);

        completedTask.Should().NotBe(timeoutTask, "并发读写不应导致死锁");
    }

    [Fact]
    public async Task Remove_ConcurrentWithRead_DoesNotThrow()
    {
        for (int i = 0; i < 20; i++)
        {
            _interceptor.Set($"key-{i}", $"value-{i}", TimeSpan.FromSeconds(60));
        }

        var removeTasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
            {
                _interceptor.Remove($"key-{i}");
            }))
            .ToArray();

        var readTasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
            {
                _interceptor.TryGet<string>($"key-{i}", out _);
            }))
            .ToArray();

        var act = async () => await Task.WhenAll(removeTasks.Concat(readTasks));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Set_ConcurrentSameKey_LastWriteWins()
    {
        var tasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
            {
                _interceptor.Set("same-key", $"value-{i}", TimeSpan.FromSeconds(60));
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var result = _interceptor.TryGet<string>("same-key", out var value);
        result.Should().BeTrue();
        value.Should().StartWith("value-");
    }

    [Fact]
    public async Task Set_WithSlidingExpiration_ConcurrentAccess_ExtendsExpiration()
    {
        _interceptor.Set("sliding-key", "sliding-value", TimeSpan.FromSeconds(5), useSlidingExpiration: true);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                _interceptor.TryGet<string>("sliding-key", out var value);
                value.Should().Be("sliding-value");
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var result = _interceptor.TryGet<string>("sliding-key", out var finalValue);
        result.Should().BeTrue();
        finalValue.Should().Be("sliding-value");
    }
}
