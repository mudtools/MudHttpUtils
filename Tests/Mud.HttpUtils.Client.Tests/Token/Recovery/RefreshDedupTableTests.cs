// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Tests;

/// <summary>
/// M6-HC-23：<see cref="RefreshDedupTable"/> 的窗口复用与 forceRefresh 语义。
/// </summary>
/// <remarks><see cref="RefreshDedupTable"/> 为 Client 内部类型，经 InternalsVisibleTo 供测试直接构造访问。</remarks>
public class RefreshDedupTableTests
{
    [Fact]
    public async Task GetOrRefreshAsync_WithinWindow_ReusesCompletedResult()
    {
        var table = new RefreshDedupTable(maxEntries: 16);
        var calls = 0;
        Func<Task<string?>> factory = () =>
        {
            calls++;
            return Task.FromResult<string?>("token-1");
        };

        var first = await table.GetOrRefreshAsync("key", factory, dedupWindowSeconds: 60);
        var second = await table.GetOrRefreshAsync("key", factory, dedupWindowSeconds: 60);

        first.Should().Be("token-1");
        second.Should().Be("token-1");
        calls.Should().Be(1, "窗口内非 forceRefresh 应复用已完成的刷新结果");
    }

    [Fact]
    public async Task GetOrRefreshAsync_ForceRefresh_AfterCompletion_RerunsFactory()
    {
        var table = new RefreshDedupTable(maxEntries: 16);
        var calls = 0;
        Func<Task<string?>> factory = () => Task.FromResult<string?>($"token-{++calls}");

        var first = await table.GetOrRefreshAsync("key", factory, dedupWindowSeconds: 60);
        var second = await table.GetOrRefreshAsync("key", factory, dedupWindowSeconds: 60, forceRefresh: true);

        first.Should().Be("token-1");
        second.Should().Be("token-2");
        calls.Should().Be(2, "forceRefresh 且条目已完成时应绕过窗口重新执行工厂");
    }

    [Fact]
    public async Task GetOrRefreshAsync_ForceRefresh_WhileInFlight_StillReusesSharedTask()
    {
        var table = new RefreshDedupTable(maxEntries: 16);
        var calls = 0;
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task<string?>> factory = () =>
        {
            calls++;
            return gate.Task;
        };

        var inFlight = table.GetOrRefreshAsync("key", factory, dedupWindowSeconds: 60);
        var forced = table.GetOrRefreshAsync("key", factory, dedupWindowSeconds: 60, forceRefresh: true);

        calls.Should().Be(1, "在途刷新仍应复用（单飞语义不受 forceRefresh 影响）");

        gate.SetResult("token-shared");

        (await inFlight).Should().Be("token-shared");
        (await forced).Should().Be("token-shared");
    }
}