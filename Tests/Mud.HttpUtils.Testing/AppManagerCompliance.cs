// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Testing;

/// <summary>
/// F-5：多应用管理器<b>官方合规测试套件</b>（框架无关基类风格）。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机（MA-1）</b>：自实现 <see cref="IAppManager{TAppContext}"/> 的下游（如 <c>WechatAppManager</c>）
/// 无从证明其语义与官方实现一致（大小写、默认应用回退、事件隔离、懒加载语义）。本套件把
/// <see cref="DefaultAppManager{TAppContext}"/> 的语义契约抽为可复用检查：
/// 下游继承本类并实现两个工厂方法，即可对<b>任意</b> <see cref="IAppManager{TAppContext}"/> 实现跑同一套契约检查
/// （信任回收路径）；也可用于验证自定义 <see cref="IAppRegistry{TAppContext}"/> 注入后行为逐位不变。
/// </para>
/// <para>
/// <b>框架无关</b>：本包不依赖任何测试框架。断言失败经可覆写的 <see cref="Fail"/> 抛出
/// （默认 <see cref="InvalidOperationException"/>）；xUnit/MSTest/NUnit 派生类可覆写
/// <see cref="Fail"/> 接到自家断言，或用 [Fact]/[TestMethod] 逐条转调 <c>Verify_*</c> 方法
/// （无特性标注的公开方法不会被测试框架自动发现，需派生类显式转调或统一调 <see cref="VerifyAll"/>）。
/// </para>
/// <para>
/// 覆盖面（与方案 F-5 验收口径一致）：注册/查询往返、appKey 序数大小写、Update 替换、Remove 语义、
/// 默认应用指派与回退、SetDefaultApp 守卫、事件隔离（订阅者故障不影响注册表状态机）、
/// 懒加载语义（仅当被测管理器为 <see cref="DefaultAppManager{TAppContext}"/> 或派生类时执行，
/// 否则 <see cref="TryRegisterLazy"/> 返回 <c>false</c> 静默跳过）。
/// </para>
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型。</typeparam>
public abstract class AppManagerCompliance<TAppContext>
    where TAppContext : IMudAppContext
{
    /// <summary>创建被测应用管理器（每个检查方法各自独立实例，互不污染）。</summary>
    protected abstract IAppManager<TAppContext> CreateManager();

    /// <summary>创建检查用应用上下文（<paramref name="appKey"/> 为该上下文的标识）。</summary>
    protected abstract TAppContext CreateContext(string appKey);

    /// <summary>初始化合规套件（预留扩展点，派生类可不覆写）。</summary>
    protected AppManagerCompliance()
    {
    }

    /// <summary>
    /// 断言失败出口（可覆写：接到 xUnit/MSTest/NUnit 断言或自定义异常）。
    /// </summary>
    /// <param name="message">失败描述。</param>
    protected virtual void Fail(string message)
        => throw new InvalidOperationException($"AppManager 合规检查失败：{message}");

    /// <summary>
    /// 懒加载注册接缝：默认实现仅对 <see cref="DefaultAppManager{TAppContext}"/> 生效
    /// （<see cref="IAppManager{TAppContext}"/> 不含懒加载成员，属默认实现扩展面）；
    /// 不支持时返回 <c>false</c>，相关检查静默跳过。派生类（自实现管理器）可覆写接入自己的懒加载入口。
    /// </summary>
    /// <param name="manager">被测管理器。</param>
    /// <param name="appKey">应用标识。</param>
    /// <param name="factory">上下文工厂。</param>
    /// <param name="isDefault">是否同时设为默认应用。</param>
    /// <returns>已注册返回 <c>true</c>；不支持懒加载返回 <c>false</c>。</returns>
    protected virtual bool TryRegisterLazy(IAppManager<TAppContext> manager, string appKey, Func<TAppContext> factory, bool isDefault = false)
    {
        if (manager is DefaultAppManager<TAppContext> defaultManager)
        {
            defaultManager.RegisterLazy(appKey, factory, isDefault);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 依次执行全部合规检查（fail-fast：首个失败即抛出）。
    /// </summary>
    public void VerifyAll()
    {
        Verify_RegisterAndGetRoundtrip();
        Verify_AppKeyIsCaseSensitive();
        Verify_TryGetAppAndHasApp();
        Verify_UpdateReplacesContext();
        Verify_RemoveAppSemantics();
        Verify_DefaultAppAssignment();
        Verify_DefaultAppFallbackOnRemoval();
        Verify_SetDefaultAppGuard();
        Verify_EventIsolation();
        Verify_LazyRegistrationSemantics();
        // ADR-004 / AM2-11（2026-10-28 方案）：下游升级后可直接自查两条新不变量。
        Verify_LazyDeclaredIsFirstClass();
        Verify_DefaultChangeRaisesEvent();
    }

    /// <summary>注册 → GetApp/GetAllApps 往返一致；未注册键 GetApp 抛出。</summary>
    public void Verify_RegisterAndGetRoundtrip()
    {
        var manager = CreateManager();
        var context = CreateContext("app-a");
        manager.RegisterApp("app-a", context);

        Check(ReferenceEquals(manager.GetApp("app-a"), context), "RegisterApp 后 GetApp 应返回同一实例");
        Check(manager.HasApp("app-a"), "RegisterApp 后 HasApp 应为 true");
        Check(manager.GetAllApps().Contains(context), "GetAllApps 应包含已注册上下文");

        Expect<InvalidOperationException>(() => manager.GetApp("nope"), "未注册键 GetApp 应抛 InvalidOperationException");
    }

    /// <summary>appKey 为序数、区分大小写（G8-11 有意契约）。</summary>
    public void Verify_AppKeyIsCaseSensitive()
    {
        var manager = CreateManager();
        manager.RegisterApp("App-A", CreateContext("App-A"));

        Check(!manager.HasApp("app-a"), "appKey 序数比较：'app-a' 不应命中 'App-A'");
        Check(!manager.TryGetApp("app-a", out _), "appKey 序数比较：TryGetApp('app-a') 不应命中 'App-A'");
        Check(manager.HasApp("App-A"), "原大小写键应命中");
    }

    /// <summary>TryGetApp 命中/未命中两态；HasApp 同口径。</summary>
    public void Verify_TryGetAppAndHasApp()
    {
        var manager = CreateManager();
        var context = CreateContext("app-a");
        manager.RegisterApp("app-a", context);

        Check(manager.TryGetApp("app-a", out var hit) && ReferenceEquals(hit, context), "TryGetApp 命中应返回注册实例");
        Check(!manager.TryGetApp("nope", out var miss) && miss is null, "TryGetApp 未命中应返回 false 且 out 为 default");
        Check(!manager.HasApp("nope"), "HasApp 未命中应为 false");
    }

    /// <summary>UpdateApp 替换为新实例；未注册键更新抛出。</summary>
    public void Verify_UpdateReplacesContext()
    {
        var manager = CreateManager();
        manager.RegisterApp("app-a", CreateContext("app-a"));
        var replacement = CreateContext("app-a");

        manager.UpdateApp("app-a", replacement);
        Check(ReferenceEquals(manager.GetApp("app-a"), replacement), "UpdateApp 后 GetApp 应返回新实例");

        Expect<InvalidOperationException>(() => manager.UpdateApp("nope", CreateContext("nope")), "未注册键 UpdateApp 应抛 InvalidOperationException");
    }

    /// <summary>RemoveApp：命中返回 true 且移除；重复移除返回 false。</summary>
    public void Verify_RemoveAppSemantics()
    {
        var manager = CreateManager();
        manager.RegisterApp("app-a", CreateContext("app-a"));

        Check(manager.RemoveApp("app-a"), "已注册键 RemoveApp 应返回 true");
        Check(!manager.HasApp("app-a"), "移除后 HasApp 应为 false");
        Check(!manager.RemoveApp("app-a"), "重复移除应返回 false");
    }

    /// <summary>isDefault 指派默认应用；DefaultAppKey 与 GetDefaultApp 一致。</summary>
    public void Verify_DefaultAppAssignment()
    {
        var manager = CreateManager();
        var context = CreateContext("app-a");
        manager.RegisterApp("app-a", context, isDefault: true);

        Check(string.Equals(manager.DefaultAppKey, "app-a", StringComparison.Ordinal), "isDefault 后 DefaultAppKey 应为该键");
        Check(ReferenceEquals(manager.GetDefaultApp(), context), "GetDefaultApp 应返回默认应用实例");

        var another = CreateContext("app-b");
        manager.RegisterApp("app-b", another);
        Check(string.Equals(manager.DefaultAppKey, "app-a", StringComparison.Ordinal), "后续非默认注册不得改写默认键");
    }

    /// <summary>默认应用被移除时回退到剩余应用（B3：不置空、不硬失败）。</summary>
    public void Verify_DefaultAppFallbackOnRemoval()
    {
        var manager = CreateManager();
        manager.RegisterApp("app-a", CreateContext("app-a"), isDefault: true);
        var remaining = CreateContext("app-b");
        manager.RegisterApp("app-b", remaining);

        manager.RemoveApp("app-a");

        Check(!string.Equals(manager.DefaultAppKey, "app-a", StringComparison.Ordinal), "默认应用移除后默认键不得仍是被移除键");
        Check(ReferenceEquals(manager.GetDefaultApp(), remaining), "默认应用移除后应回退到剩余应用");
    }

    /// <summary>SetDefaultApp/TrySetDefaultApp 的"必须已注册"守卫。</summary>
    public void Verify_SetDefaultAppGuard()
    {
        var manager = CreateManager();

        Check(!manager.TrySetDefaultApp("nope"), "未注册键 TrySetDefaultApp 应返回 false");
        Expect<InvalidOperationException>(() => manager.SetDefaultApp("nope"), "未注册键 SetDefaultApp 应抛 InvalidOperationException");

        manager.RegisterApp("app-a", CreateContext("app-a"));
        Check(manager.TrySetDefaultApp("app-a"), "已注册键 TrySetDefaultApp 应返回 true");
        Check(string.Equals(manager.DefaultAppKey, "app-a", StringComparison.Ordinal), "TrySetDefaultApp 成功后默认键应更新");
    }

    /// <summary>
    /// 事件隔离：订阅者抛异常不得影响注册表状态机，也不得阻断后续订阅者收事件。
    /// </summary>
    public void Verify_EventIsolation()
    {
        var manager = CreateManager();
        var healthyReceived = 0;
        AppConfigurationChangeType? lastChangeType = null;

        manager.ConfigurationChanged += (_, _) => throw new InvalidOperationException("故障订阅者（合规检查注入）");
        manager.ConfigurationChanged += (_, e) =>
        {
            healthyReceived++;
            lastChangeType = e.ChangeType;
        };

        manager.RegisterApp("app-a", CreateContext("app-a"));

        Check(manager.HasApp("app-a"), "故障订阅者不得影响注册结果");
        Check(healthyReceived >= 1, "故障订阅者不得阻断后续订阅者收事件");
        Check(lastChangeType == AppConfigurationChangeType.Added, "新增注册的事件变更类型应为 Added");
    }

    /// <summary>
    /// 懒加载语义：注册不实例化、首次查询实例化一次、GetAllApps 不触发实例化、RemoveApp 同步移除声明。
    /// 被测管理器不支持懒加载时静默跳过。
    /// </summary>
    public void Verify_LazyRegistrationSemantics()
    {
        var manager = CreateManager();
        var calls = 0;
        var supported = TryRegisterLazy(manager, "lazy-app", () =>
        {
            Interlocked.Increment(ref calls);
            return CreateContext("lazy-app");
        });

        if (!supported)
            return;   // 被测实现无懒加载面（非 DefaultAppManager 体系）：跳过

        Check(calls == 0, "懒加载注册阶段不得实例化");
        Check(manager.HasApp("lazy-app"), "懒加载声明应视为已存在");

        var first = manager.GetApp("lazy-app");
        var second = manager.GetApp("lazy-app");
        Check(calls == 1, "懒加载工厂应且仅应调用一次");
        Check(ReferenceEquals(first, second), "重复 GetApp 应返回同一实例");

        manager.GetAllApps().ToArray();
        Check(calls == 1, "GetAllApps 不得触发额外实例化");

        Check(manager.RemoveApp("lazy-app"), "移除懒加载声明应返回 true");
        Check(!manager.HasApp("lazy-app"), "移除后不得因懒加载声明复活");
    }

    /// <summary>
    /// <b>ADR-004</b>：懒加载声明是"一等公民" —— <c>HasApp</c>/<c>TryGetApp</c>/<c>UpdateApp</c>/默认回退
    /// 四项必须同源（"已声明即存在"）。被测管理器不支持懒加载时静默跳过。
    /// </summary>
    /// <remarks>
    /// 覆盖的缺陷：<c>HasApp</c> 认声明、<c>GetApp</c> 会物化，但 <c>UpdateApp</c> 与默认回退只查注册表
    /// ⇒ 同一时刻"存在但无法更新"，且默认应用被移除后回退键为空 ⇒ 后续全部请求硬失败。
    /// </remarks>
    public void Verify_LazyDeclaredIsFirstClass()
    {
        var manager = CreateManager();
        if (!TryRegisterLazy(manager, "lazy-declared", () => CreateContext("lazy-declared"), isDefault: true))
            return;   // 被测实现无懒加载面：跳过

        Check(manager.HasApp("lazy-declared"), "HasApp 必须认\"仅声明\"的懒加载项");
        Check(string.Equals(manager.DefaultAppKey, "lazy-declared", StringComparison.Ordinal),
            "RegisterLazy(isDefault: true) 必须设置默认键");

        // 更新：必须在任何物化之前执行 —— 这才是"声明态 → 物化态"的跃迁路径。
        var replacement = CreateContext("lazy-declared-v2");
        try
        {
            manager.UpdateApp("lazy-declared", replacement);
        }
        catch (InvalidOperationException)
        {
            Fail("UpdateApp 必须支持\"仅声明未物化\"的懒加载键（HasApp 为 true 时不应报\"未找到应用\"）");
            return;
        }

        Check(ReferenceEquals(manager.GetApp("lazy-declared"), replacement),
            "UpdateApp 后取到的必须是新上下文（声明态已升级为物化态）");

        // 默认回退：仅剩懒加载声明时，移除默认应用必须回退到声明键而不是置空。
        var manager2 = CreateManager();
        manager2.RegisterApp("eager", CreateContext("eager"), isDefault: true);
        if (!TryRegisterLazy(manager2, "lazy-only", () => CreateContext("lazy-only")))
            return;

        manager2.RemoveApp("eager");
        Check(string.Equals(manager2.DefaultAppKey, "lazy-only", StringComparison.Ordinal),
            "移除默认应用后，回退候选必须纳入\"仅声明\"的懒加载键（否则默认键被置空 ⇒ 后续请求全部失败）");
    }

    /// <summary>
    /// <b>AM2-11</b>：默认应用切换必须可观测（<see cref="AppConfigurationChangedEventArgs.IsDefaultAppChanged"/>）。
    /// </summary>
    public void Verify_DefaultChangeRaisesEvent()
    {
        var manager = CreateManager();
        manager.RegisterApp("app-a", CreateContext("app-a"), isDefault: true);
        manager.RegisterApp("app-b", CreateContext("app-b"));

        var defaultChanged = 0;
        manager.ConfigurationChanged += (_, e) =>
        {
            if (e.IsDefaultAppChanged)
                Interlocked.Increment(ref defaultChanged);
        };

        manager.SetDefaultApp("app-b");

        Check(defaultChanged == 1, "SetDefaultApp 必须发布恰好一次 IsDefaultAppChanged=true 的事件");
        Check(string.Equals(manager.DefaultAppKey, "app-b", StringComparison.Ordinal), "默认键应已切换到 app-b");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            Fail(message);
    }

    private void Expect<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Fail($"{message}（期望 {typeof(TException).Name}，实际 {ex.GetType().Name}）");
            return;
        }

        Fail($"{message}（期望抛出 {typeof(TException).Name}，实际未抛出）");
    }
}
