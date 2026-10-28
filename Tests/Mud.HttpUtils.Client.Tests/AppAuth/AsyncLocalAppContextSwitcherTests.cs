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

    // ── ADR-005：AsyncLocal 作用域栈行为锁定（AM2-08） ──
    // 旧实现（"当前值 + 归属判定"）在乱序释放时会残留他人应用上下文，该错误终态曾被固化；
    // 现改为链表栈，释放只标记节点、回退由读取端沿链完成 ⇒ 乱序释放同样回到正确祖先。

    /// <summary>
    /// 场景一（乱序释放）：s1(appA) → s2=BeginScope(appB) → s3=BeginScope(appC) → 先释放 s2 再释放 s3。
    /// 栈式实现：两个节点都只是被标记释放，Current 沿链回退到最近未释放节点 ⇒ 回到 <c>appA</c>。
    /// </summary>
    /// <remarks>
    /// <b>历史行为</b>：此前终态为 <c>appB</c>（回滚到 s3.previous），即"先释放外层"会把中间应用
    /// 残留为当前上下文 —— 多租户下等于串租户。本用例即 AM2-08 的锚点断言。
    /// </remarks>
    [Fact]
    public void OutOfOrderDispose_ReturnsToOuterContext()
    {
        var appA = CreateTestContext("appA");
        var appB = CreateTestContext("appB");
        var appC = CreateTestContext("appC");
        _switcher.SwitchTo(appA);

        var s2 = _switcher.BeginScope(appB);
        var s3 = _switcher.BeginScope(appC);

        s2.Dispose(); // 乱序：先释放内层（appB）
        s3.Dispose(); // 再释放外层（appC）

        _switcher.Current.Should().BeSameAs(appA,
            "ADR-005：乱序释放后必须沿链回到正确祖先（appA），不得残留 appB/appC");
    }

    /// <summary>
    /// ADR-005 补充：仅释放内层作用域时，当前值回到其外层（正序/乱序均成立）。
    /// </summary>
    [Fact]
    public void DisposeInner_ThenCurrentReturnsToOuterContext()
    {
        var appA = CreateTestContext("appA");
        var appB = CreateTestContext("appB");
        _switcher.SwitchTo(appA);

        var outer = _switcher.BeginScope(appB);
        var inner = _switcher.BeginScope(CreateTestContext("appC"));

        inner.Dispose();
        _switcher.Current.Should().BeSameAs(appB, "释放内层后应回到外层作用域的上下文");

        outer.Dispose();
        _switcher.Current.Should().BeSameAs(appA, "全部释放后应回到作用域建立前的上下文");
    }

    /// <summary>
    /// ADR-005 补充：<c>SwitchTo</c>（无作用域切换）整体替换栈顶，随后释放旧作用域不得把值写回。
    /// </summary>
    [Fact]
    public void SwitchToInsideScope_ThenDispose_KeepsSwitch()
    {
        var appA = CreateTestContext("appA");
        var appB = CreateTestContext("appB");
        var appC = CreateTestContext("appC");
        _switcher.SwitchTo(appA);

        var scope = _switcher.BeginScope(appB);
        _switcher.SwitchTo(appC);

        scope.Dispose();
        _switcher.Current.Should().BeSameAs(appC,
            "SwitchTo 是无作用域切换：替换栈顶后旧作用域释放不得把 appB/appA 写回");
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
    /// 场景四（P2-3 回归，跨流释放 + 目标流 Current 为 null）：作用域逃逸到一个从未设置过
    /// 应用上下文的异步流中释放时，不得把 previous 注入该流——否则该流后续请求会串到
    /// previous 的应用（令牌、per-app 策略、缓存 scope 随之错位）。fail-closed：宁可残留 null。
    /// </summary>
    [Fact]
    public async Task DisposeInForeignFlowWithNullCurrent_DoesNotInjectPrevious()
    {
        var appB = CreateTestContext("appB");
        var appA = CreateTestContext("appA");

        // 流 X：SwitchTo(appB) 后建立作用域（previous=appB），作用域逃逸给流 Y 释放。
        IDisposable? escaped = null;
        var flowX = Task.Run(() =>
        {
            _switcher.SwitchTo(appB);
            escaped = _switcher.BeginScope(appA); // previous=appB
        });
        flowX.Wait();

        // 流 Y：由测试主线程调度且主线程从未写入 _context（流 X 的写入不回流），
        // 故流 Y 的 Current 为 null——正是「无关异步流」场景。
        var observedAfterDispose = await Task.Run(() =>
        {
            _switcher.Current.Should().BeNull("流 Y 从未设置过应用上下文");
            escaped!.Dispose();
            return _switcher.Current;
        });

        observedAfterDispose.Should().BeNull(
            "跨流释放不得向 Current 为 null 的无关异步流注入 previous 上下文（P2-3 fail-closed）");
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
