// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-01（架构不变式 I2）：<see cref="TokenRefreshAmbient"/> 的 AsyncLocal 语义回归守卫。
/// </summary>
/// <remarks>
/// 熔断的正确性依赖三条 AsyncLocal 语义：① 随 await 向下传播；② 嵌套 Enter 逐层恢复；
/// ③ 释放时恢复<b>捕获的</b>深度（而非无条件递减），使父上下文与并行子分支互不污染。
/// 任一条回归都会让"刷新重入熔断"在某种时序下失效 —— 故这里逐条固定。
/// </remarks>
public class TokenRefreshAmbientTests
{
    [Fact]
    public async Task TokenRefreshAmbient_ShouldFlowAcrossAwait()
    {
        TokenRefreshAmbient.InRefresh.Should().BeFalse("未进入刷新作用域时不得置位");

        using (TokenRefreshAmbient.Enter())
        {
            TokenRefreshAmbient.InRefresh.Should().BeTrue();

            await Task.Yield();
            TokenRefreshAmbient.InRefresh.Should().BeTrue("AsyncLocal 必须随 await 续延传播");

            await Task.Delay(1);
            TokenRefreshAmbient.InRefresh.Should().BeTrue();
        }

        TokenRefreshAmbient.InRefresh.Should().BeFalse("作用域释放后必须复位");
    }

    [Fact]
    public void AmbientScope_NestedEntry_ShouldRestoreDepth()
    {
        using (TokenRefreshAmbient.Enter())
        {
            TokenRefreshAmbient.InRefresh.Should().BeTrue();

            using (TokenRefreshAmbient.Enter())
            {
                TokenRefreshAmbient.InRefresh.Should().BeTrue();
            }

            TokenRefreshAmbient.InRefresh.Should().BeTrue("内层释放不得清除外层的标记");
        }

        TokenRefreshAmbient.InRefresh.Should().BeFalse();
    }

    [Fact]
    public async Task AmbientScope_SiblingBranch_ShouldNotLeakOnDispose()
    {
        using (TokenRefreshAmbient.Enter())
        {
            // 子分支继承父上下文（深度 1）并再次 Enter（深度 2）；其释放只应恢复其为 1。
            var childObserved = await Task.Run(() =>
            {
                using (TokenRefreshAmbient.Enter())
                {
                    return TokenRefreshAmbient.InRefresh;
                }
            });

            childObserved.Should().BeTrue();
            TokenRefreshAmbient.InRefresh.Should().BeTrue("子分支释放不得污染父上下文");
        }

        TokenRefreshAmbient.InRefresh.Should().BeFalse();
    }

    [Fact]
    public async Task AmbientScope_ShouldNotLeakToUnrelatedAsyncFlow()
    {
        // 在一条异步流内 Enter，另一条完全无关的流（未从该作用域派生）不得看到标记。
        var branchEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var branchRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var branch = Task.Run(async () =>
        {
            branchEntered.TrySetResult(true);
            await branchRelease.Task.ConfigureAwait(false);
            return TokenRefreshAmbient.InRefresh;
        });

        await branchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (TokenRefreshAmbient.Enter())
        {
            TokenRefreshAmbient.InRefresh.Should().BeTrue();
        }

        branchRelease.SetResult(true);
        (await branch.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().BeFalse("无关异步流不得被其它流的刷新标记污染");
    }
}
