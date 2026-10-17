// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

public partial class DefaultAppManager<TAppContext>
    where TAppContext : IMudAppContext
{

    private readonly ConcurrentDictionary<Type, Func<TAppContext, IAppContextSwitcher>> _switcherFactories = new();

    /// <inheritdoc />
    [Obsolete("BC-30：已弃用（3.x 保留，计划 4.0 移除）。生成器切换器场景请改用 DI 解析切换器 + UseAppScope(appKey)/UseDefaultAppScope()；本入口与生成器 3 参构造不对齐，注册的工厂对生成器切换器不生效。")]
    public virtual TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
        where TContextSwitcher : IAppContextSwitcher
    {
        var context = GetApp(appKey);
        return CreateContextSwitcher<TContextSwitcher>(context);
    }

    /// <inheritdoc />
    [Obsolete("BC-30：已弃用（3.x 保留，计划 4.0 移除）。生成器切换器场景请改用 DI 解析切换器 + UseDefaultAppScope()；本入口与生成器 3 参构造不对齐，注册的工厂对生成器切换器不生效。")]
    public virtual TContextSwitcher GetDefaultWebApi<TContextSwitcher>()
        where TContextSwitcher : IAppContextSwitcher
    {
        var context = GetDefaultApp();
        return CreateContextSwitcher<TContextSwitcher>(context);
    }

    private TContextSwitcher CreateContextSwitcher<TContextSwitcher>(TAppContext context)
        where TContextSwitcher : IAppContextSwitcher
    {
        var switcherType = typeof(TContextSwitcher);

        if (_switcherFactories.TryGetValue(switcherType, out var factory))
        {
            return (TContextSwitcher)factory(context);
        }

        // [SW-05] 原文案给出的「单参构造 lambda」示例必然编译失败：
        // 生成的实现类（默认模式）构造函数有 3 个必需参数（appContext / appContextHolder / executor），
        // 而工厂委托只有 1 个入参（Func<TAppContext, TContextSwitcher>）。
        // 此处不再给出不可编译示例，改为指向推荐路径（DI 注入 + 作用域式切换），
        // 并说明仅当宿主能自行提供全部构造依赖时才应使用工厂委托（本 API 为 AOT 友好替代反射的低层接缝）。
        throw new InvalidOperationException(
            $"无法创建类型 {switcherType.Name} 的实例，因为未注册对应的工厂委托。" +
            $"推荐做法：直接从 DI 解析该切换器（serviceProvider.GetRequiredService<{switcherType.Name}>()），" +
            $"并使用 UseAppScope(appKey) / UseDefaultAppScope()（或 IAppScopeSwitcher）进行作用域式切换。" +
            $"仅当宿主自持该切换器实例且能自行提供其全部构造依赖时，才使用 " +
            $"RegisterSwitcherFactory<{switcherType.Name}>(context => ...) 注册工厂委托" +
            $"（委托只有一个入参 TAppContext，须自行补齐其余依赖；使用工厂委托可避免反射并支持 AOT）。");
    }

    /// <inheritdoc/>
    [Obsolete("BC-30：已弃用（3.x 保留仅告警）。生成器切换器场景请改用 DI 解析切换器 + UseAppScope/UseDefaultAppScope（本工厂 1 参委托与生成器 3 参构造不对齐，对其不生效）；若作为替代反射的 AOT 低层接缝使用，3.x 继续可用，4.0 是否保留该接缝届时重评估。")]
    public void RegisterSwitcherFactory<TContextSwitcher>(Func<TAppContext, TContextSwitcher> factory)
        where TContextSwitcher : IAppContextSwitcher
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var switcherType = typeof(TContextSwitcher);

        // 索引器赋值会静默覆盖先前注册的工厂，导致"注册了却不生效"难以排查。
        // 覆盖时通过诊断出口记一次告警（由 Client 层的 AppManagerDiagnosticsWiring 接到 ILogger）。
        if (_switcherFactories.ContainsKey(switcherType))
        {
            AppManagerDiagnostics.SwitcherFactoryOverwritten?.Invoke(switcherType.Name);
        }

        _switcherFactories[switcherType] = ctx => factory(ctx);
    }
}
