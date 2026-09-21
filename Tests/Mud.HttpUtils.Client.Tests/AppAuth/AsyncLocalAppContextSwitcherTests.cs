namespace Mud.HttpUtils.Client.Tests;

public class AsyncLocalAppContextSwitcherTests
{
    private readonly AsyncLocalAppContextSwitcher _switcher = new();

    [Fact]
    public void Current_DefaultIsNull()
    {
        _switcher.Current.Should().BeNull();
    }

    [Fact]
    public void Current_SetValue_ReturnsSameValue()
    {
        var context = CreateTestContext("app1");

        _switcher.SwitchTo(context);

        _switcher.Current.Should().BeSameAs(context);

        _switcher.SwitchTo(null);
    }

    [Fact]
    public void Current_SetNull_ClearsValue()
    {
        _switcher.SwitchTo(CreateTestContext("app1"));
        _switcher.SwitchTo(null);

        _switcher.Current.Should().BeNull();
    }

    [Fact]
    public void BeginScope_SetsCurrentAndRestoresOnDispose()
    {
        var original = CreateTestContext("original");
        var scoped = CreateTestContext("scoped");
        _switcher.SwitchTo(original);

        using (_switcher.BeginScope(scoped))
        {
            _switcher.Current.Should().BeSameAs(scoped);
        }

        _switcher.Current.Should().BeSameAs(original);

        _switcher.SwitchTo(null);
    }

    [Fact]
    public void BeginScope_NullContext_ThrowsArgumentNullException()
    {
        var act = () => _switcher.BeginScope((IMudAppContext)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void BeginScope_NestedScopes_RestoresInCorrectOrder()
    {
        var level0 = CreateTestContext("level0");
        var level1 = CreateTestContext("level1");
        var level2 = CreateTestContext("level2");
        _switcher.SwitchTo(level0);

        using (_switcher.BeginScope(level1))
        {
            _switcher.Current.Should().BeSameAs(level1);

            using (_switcher.BeginScope(level2))
            {
                _switcher.Current.Should().BeSameAs(level2);
            }

            _switcher.Current.Should().BeSameAs(level1);
        }

        _switcher.Current.Should().BeSameAs(level0);

        _switcher.SwitchTo(null);
    }

    [Fact]
    public void BeginScope_DisposeCalledMultipleTimes_DoesNotCorruptState()
    {
        var original = CreateTestContext("original");
        var scoped = CreateTestContext("scoped");
        _switcher.SwitchTo(original);

        var scope = _switcher.BeginScope(scoped);
        scope.Dispose();
        scope.Dispose();
        scope.Dispose();

        _switcher.Current.Should().BeSameAs(original);

        _switcher.SwitchTo(null);
    }

    [Fact]
    public async Task BeginScope_FlowsAcrossAsyncBoundary()
    {
        var context = CreateTestContext("async-context");
        _switcher.SwitchTo(context);

        await Task.Yield();

        _switcher.Current.Should().BeSameAs(context);

        _switcher.SwitchTo(null);
    }

    [Fact]
    public async Task BeginScope_IsolatedAcrossConcurrentTasks()
    {
        _switcher.SwitchTo(null);

        var task1 = Task.Run(async () =>
        {
            var ctx1 = CreateTestContext("task1");
            using (_switcher.BeginScope(ctx1))
            {
                await Task.Delay(50);
                return _switcher.Current;
            }
        });

        var task2 = Task.Run(async () =>
        {
            var ctx2 = CreateTestContext("task2");
            using (_switcher.BeginScope(ctx2))
            {
                await Task.Delay(50);
                return _switcher.Current;
            }
        });

        var results = await Task.WhenAll(task1, task2);

        results[0].Should().NotBeSameAs(results[1]);
    }

    [Fact]
    public void Implements_IAppContextHolder()
    {
        var holder = _switcher as IAppContextHolder;
        holder.Should().NotBeNull();
        holder.Should().BeSameAs(_switcher);
    }

    // ── F-08：AsyncLocal 作用域行为锁定（generator-review-fix-plan-2026-09-21 §1.8） ──
    // 以下三个用例锁定的是「已声明行为」而非理想行为：修复乱序释放的全序问题需引入作用域栈
    // （方案 §1.8 可选演进项，未排期）。若未来无意识变更导致断言失败，请先核对方案文档再动状态机。

    /// <summary>
    /// 场景一（乱序释放）：s1(appA) → s2=BeginScope(appB) → s3=BeginScope(appC) → 先释放 s2 再释放 s3。
    /// B8 归属判定：s2 释放时 Current=appC ≠ owner=appB → 跳过；s3 释放时 Current=appC == owner → 回滚到
    /// s3.previous=appB。终态为 appB（正常嵌套语义应回到 appA）——此为已声明行为，乱序释放会打断回滚链。
    /// </summary>
    [Fact]
    public void OutOfOrderDispose_RollbackChainBreaksAtAppB()
    {
        var appA = CreateTestContext("appA");
        var appB = CreateTestContext("appB");
        var appC = CreateTestContext("appC");
        _switcher.SwitchTo(appA);

        var s2 = _switcher.BeginScope(appB); // previous=appA
        var s3 = _switcher.BeginScope(appC); // previous=appB

        s2.Dispose(); // 乱序：在 appC 环境下释放非 owner 作用域 → 跳过
        s3.Dispose(); // Current=appC == owner → 回滚到 s3.previous=appB

        _switcher.Current.Should().BeSameAs(appB,
            "B8：乱序释放跳过非 owner 作用域、owner 等值时回滚——回滚链断裂后终态残留 appB（已声明行为，" +
            "修复需作用域栈方案，见 generator-review-fix-plan §1.8 演进项）");
    }

    /// <summary>
    /// 场景一补充：UseApp(SwitchTo) 与 using 作用域混用后乱序释放，非 owner 环境下释放不回滚
    /// （防止把他人当前值改写为陈旧值）。
    /// </summary>
    [Fact]
    public void OutOfOrderDispose_AfterUseApp_SkipsRollback()
    {
        var appA = CreateTestContext("appA");
        var appB = CreateTestContext("appB");
        var appC = CreateTestContext("appC");
        var switcher2 = new AsyncLocalAppContextSwitcher();
        switcher2.SwitchTo(appA);

        var s2 = switcher2.BeginScope(appB);
        switcher2.SwitchTo(appC); // UseApp("appC") 语义
        s2.Dispose(); // 乱序释放：Current=appC ≠ owner=appB → 跳过

        switcher2.Current.Should().BeSameAs(appC,
            "B8：非 owner 环境下释放作用域不回滚（防止把他人当前值改写为陈旧值）——已声明行为");
    }

    /// <summary>
    /// 场景二（跨执行上下文释放）：ExecutionContext.Run 内 BeginScope、外部 Dispose。
    /// B8 归属判定保证外部释放不会把 run 内的 previous 写入外部上下文（Current 残留外部原值 outer）。
    /// </summary>
    [Fact]
    public void DisposeAcrossExecutionContext_DoesNotLeakInnerPrevious()
    {
        var outer = CreateTestContext("outer");
        var inner = CreateTestContext("inner");
        var scoped = CreateTestContext("scoped");
        _switcher.SwitchTo(outer);

        IDisposable? scope = null;
        ExecutionContext.Run(
            ExecutionContext.Capture(),
            _ =>
            {
                _switcher.SwitchTo(inner); // run 内切换（仅影响 run 内 EC）
                scope = _switcher.BeginScope(scoped); // scoped.previous = inner
            },
            null);

        // run 结束后外部 Current 恢复 outer（AsyncLocal 不跨 EC 回流）
        _switcher.Current.Should().BeSameAs(outer);

        scope!.Dispose(); // 外部释放：Current=outer ≠ owner=scoped → 跳过

        _switcher.Current.Should().BeSameAs(outer,
            "B8：跨 EC 释放不得把 run 内的 previous(inner) 写入外部上下文，Current 残留 outer——已声明行为");
    }

    /// <summary>
    /// 场景三：fire-and-forget 任务继承发起时的应用上下文（G7-11 声明行为，AsyncLocal 随 EC 流动）。
    /// </summary>
    [Fact]
    public async Task FireAndForget_InheritsCurrentContext()
    {
        var appA = CreateTestContext("appA");
        _switcher.SwitchTo(appA);

        IMudAppContext? observed = null;
        await Task.Run(() => observed = _switcher.Current);

        observed.Should().BeSameAs(appA,
            "G7-11：fire-and-forget 任务继承发起时的应用上下文（AsyncLocal 流动）——已声明行为");
    }

    private static TestAppContext CreateTestContext(string appId) => new(appId);

    private class TestAppContext : IMudAppContext
    {
        public string AppId { get; }

        public TestAppContext(string appId)
        {
            AppId = appId;
        }

        public string AppKey => AppId;

        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();

        public ITokenManager GetTokenManager(string tokenType = "") => null!;

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();

        public T? GetService<T>() where T : class => null;
    }
}
