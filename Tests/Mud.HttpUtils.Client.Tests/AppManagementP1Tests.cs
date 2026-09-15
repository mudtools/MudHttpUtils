using System.Collections.Concurrent;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// Phase 1 测试套件：注册表状态语义、事件幂等、失败释放、跨 flow 归属校验。
/// 对应方案 §6.2。
/// </summary>
public class AppManagementP1Tests
{
    // ──────────────────────────────────────────────────────────────
    // 1. 并发 GetDefaultApp + RemoveApp
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ConcurrentGetDefaultAppAndRemoveApp_DoesNotProduceKeyNotFound()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        for (var i = 0; i < 10; i++)
            manager.RegisterApp($"app{i}", new TestAppContext($"app{i}"), isDefault: i == 0);

        var exceptions = new ConcurrentQueue<Exception>();

        // 并发：一边不断 GetDefaultApp，一边移除非默认应用
        Parallel.Invoke(
            () =>
            {
                for (var i = 1; i < 10; i++)
                    manager.RemoveApp($"app{i}");
            },
            () =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    try
                    {
                        // 只验证不产出"未找到应用标识"异常
                        // 默认应用被移除后允许抛"默认应用已被移除或未注册"
                        _ = manager.GetDefaultApp();
                    }
                    catch (InvalidOperationException ex)
                    {
                        // "未找到应用标识" 是不允许的，"默认应用已被移除或未注册" 是允许的
                        if (!ex.Message.Contains("已被移除") && !ex.Message.Contains("未设置默认应用"))
                            exceptions.Enqueue(ex);
                    }
                }
            });

        exceptions.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // 2. SetDefaultApp / TrySetDefaultApp
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SetDefaultApp_UnregisteredKey_ThrowsInvalidOperationException()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"), isDefault: true);

        var act = () => manager.SetDefaultApp("unregistered");

        act.Should().Throw<InvalidOperationException>().WithMessage("*未注册*");
    }

    [Fact]
    public void TrySetDefaultApp_UnregisteredKey_ReturnsFalse()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"), isDefault: true);

        var result = manager.TrySetDefaultApp("unregistered");

        result.Should().BeFalse();
    }

    [Fact]
    public void TrySetDefaultApp_RegisteredKey_ReturnsTrueAndUpdatesDefaultAppKey()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"), isDefault: true);
        manager.RegisterApp("app2", new TestAppContext("app2"));

        var result = manager.TrySetDefaultApp("app2");

        result.Should().BeTrue();
        manager.DefaultAppKey.Should().Be("app2");
        manager.GetDefaultApp().AppKey.Should().Be("app2");
    }

    [Fact]
    public void SetDefaultApp_RegisteredKey_UpdatesDefault()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"), isDefault: true);
        manager.RegisterApp("app2", new TestAppContext("app2"));

        manager.SetDefaultApp("app2");

        manager.DefaultAppKey.Should().Be("app2");
        manager.GetDefaultApp().AppKey.Should().Be("app2");
    }

    [Fact]
    public void DefaultAppKey_ReflectsCurrentDefault()
    {
        var manager = new DefaultAppManager<TestAppContext>();

        manager.DefaultAppKey.Should().BeNull();

        manager.RegisterApp("app1", new TestAppContext("app1"), isDefault: true);
        manager.DefaultAppKey.Should().Be("app1");

        manager.TrySetDefaultApp("app1");
        manager.DefaultAppKey.Should().Be("app1");
    }

    // ──────────────────────────────────────────────────────────────
    // 3. RegisterApp 并发同 key → Added 事件恰好 1 次
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ConcurrentRegisterSameKey_AddedEventFiresExactlyOnce()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var addedCount = 0;
        var updatedCount = 0;

        manager.ConfigurationChanged += (_, e) =>
        {
            if (e.ChangeType == AppConfigurationChangeType.Added)
                Interlocked.Increment(ref addedCount);
            else if (e.ChangeType == AppConfigurationChangeType.Updated)
                Interlocked.Increment(ref updatedCount);
        };

        // 并发注册同一 key
        Parallel.For(0, 20, _ =>
        {
            try
            {
                manager.RegisterApp("same-key", new TestAppContext("same-key"));
            }
            catch (ArgumentException)
            {
                // AppKeyValidator 不应拒绝 "same-key"
            }
        });

        // Added 恰好 1 次（第一个 TryAdd 成功），其余都是 Updated
        addedCount.Should().Be(1);
        (addedCount + updatedCount).Should().Be(20);
    }

    // ──────────────────────────────────────────────────────────────
    // 4. 订阅者抛异常 → HasApp 为 true 且后续订阅者仍被调用
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SubscriberThrows_HasAppRemainsTrue_AndSubsequentSubscribersStillInvoked()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var secondSubscriberCalled = false;

        manager.ConfigurationChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
        manager.ConfigurationChanged += (_, _) => secondSubscriberCalled = true;

        manager.RegisterApp("app1", new TestAppContext("app1"));

        // 状态变更已提交
        manager.HasApp("app1").Should().BeTrue();
        // 第二个订阅者仍被调用
        secondSubscriberCalled.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────
    // 5. UpdateAppAsync 初始化失败 → GetApp 返回旧上下文；IDisposable 被释放
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAppAsync_InitializationFails_OldContextPreserved_AndNewContextDisposed()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var oldContext = new TestAppContext("app1");
        manager.RegisterApp("app1", oldContext);

        var newContext = new DisposableTestAppContext("app1", initShouldThrow: true);

        var act = () => manager.UpdateAppAsync("app1", newContext);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // 旧上下文保留
        manager.GetApp("app1").Should().BeSameAs(oldContext);
        // 新上下文被释放
        newContext.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAppAsync_InitializationFails_AppNotRegistered_AndContextDisposed()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        var newContext = new DisposableTestAppContext("app1", initShouldThrow: true);

        var act = () => manager.RegisterAppAsync("app1", newContext);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // 未注册
        manager.HasApp("app1").Should().BeFalse();
        // 上下文被释放
        newContext.IsDisposed.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────
    // 6. 跨 flow Dispose（B8）→ 当前 flow 的 Current 不被污染
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossFlowDispose_DoesNotPolluteCurrentFlow()
    {
        var switcher = new AsyncLocalAppContextSwitcher();
        var ctxA = CreateTestContext("ctxA");
        var ctxB = CreateTestContext("ctxB");

        // 在当前 flow 设置 ctxA
        switcher.SwitchTo(ctxA);

        // 在另一个 flow 中创建 scope，然后在另一个 flow 中 Dispose
        var scope = await Task.Run(() =>
        {
            switcher.SwitchTo(ctxB);
            return switcher.BeginScope(ctxB);
        });

        // 当前 flow 的 Current 应该仍然是 ctxA（不受其他 flow 的 scope 创建影响）
        switcher.Current.Should().BeSameAs(ctxA);

        // 在当前 flow 中 Dispose 另一个 flow 创建的 scope
        // B8 归属校验：不应当把 ctxA 污染为 ctxB 的 previous
        scope.Dispose();

        // 当前 flow 的 Current 应该仍然指向 ctxA（或被还原，但不应该是 ctxB 的 previous）
        switcher.Current.Should().BeSameAs(ctxA);

        switcher.SwitchTo(null);
    }

    [Fact]
    public async Task CrossFlowDispose_WhenCurrentIsNull_RestoresPrevious()
    {
        var switcher = new AsyncLocalAppContextSwitcher();
        var ctxB = CreateTestContext("ctxB");
        var ctxC = CreateTestContext("ctxC");

        // 在另一个 flow 中创建 scope
        var scope = await Task.Run(() =>
        {
            switcher.SwitchTo(null);
            return switcher.BeginScope(ctxC);
        });

        // 当前 flow 的 Current 是 null
        switcher.Current.Should().BeNull();

        // 在当前 flow 中 Dispose — Current is null，应该还原为 previous (null)
        scope.Dispose();

        switcher.Current.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────
    // 7. GetAllApps 快照 → 事件回调内枚举时，后续注册不影响已取快照
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetAllApps_ReturnsSnapshot_ResistantToConcurrentRegistration()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));
        manager.RegisterApp("app2", new TestAppContext("app2"));

        var snapshot = manager.GetAllApps().ToList();

        // 在快照之后注册新应用
        manager.RegisterApp("app3", new TestAppContext("app3"));

        // 快照仍是 2 个
        snapshot.Should().HaveCount(2);
        // 实际注册表有 3 个
        manager.GetAllApps().Should().HaveCount(3);
    }

    [Fact]
    public void GetAllApps_ReturnsSnapshot_StableDuringEventCallback()
    {
        var manager = new DefaultAppManager<TestAppContext>();
        manager.RegisterApp("app1", new TestAppContext("app1"));

        var appsDuringCallback = new List<IEnumerable<TestAppContext>>();
        var recursionGuard = false;

        manager.ConfigurationChanged += (_, _) =>
        {
            if (recursionGuard)
                return; // 防止递归
            recursionGuard = true;

            // 在事件回调中取快照
            appsDuringCallback.Add(manager.GetAllApps());
            // 事件回调中注册新应用（递归事件，但被 guard 拦截）
            manager.RegisterApp("app2", new TestAppContext("app2"));
        };

        manager.RegisterApp("app0", new TestAppContext("app0"));

        // 事件回调中的快照应包含至少 app1（回调中注册的 app2 不影响已取快照）
        appsDuringCallback.Should().NotBeEmpty();
        appsDuringCallback[0].Should().Contain(c => c.AppKey == "app1");
    }

    // ──────────────────────────────────────────────────────────────
    // 辅助类型
    // ──────────────────────────────────────────────────────────────

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

    /// <summary>
    /// 支持 IAsyncInitializable 和 IDisposable 的测试上下文，
    /// 用于验证初始化失败时的释放行为。
    /// </summary>
    private class DisposableTestAppContext : TestAppContext, IAsyncInitializable, IDisposable
    {
        private readonly bool _initShouldThrow;

        public DisposableTestAppContext(string appId, bool initShouldThrow = false) : base(appId)
        {
            _initShouldThrow = initShouldThrow;
        }

        public bool IsDisposed { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initShouldThrow)
                throw new InvalidOperationException("模拟初始化失败");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
