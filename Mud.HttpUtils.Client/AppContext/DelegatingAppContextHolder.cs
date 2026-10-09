// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils;

/// <summary>
/// G7：<b>委托式应用上下文持有器</b>——框架默认注册的 <see cref="IAppContextHolder"/> 实现，
/// 把请求转发给容器中<b>最后注册</b>的其它 <see cref="IAppContextHolder"/> 实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>解决的问题（F3 / G8-A 顺序不变量的治本项）</b>：此前 <c>AddMudHttpClient</c> 内部以
/// <c>TryAddSingleton&lt;IAppContextHolder, AsyncLocalAppContextSwitcher&gt;()</c> 抢先注册了一个<b>自持
/// <see cref="AsyncLocal{T}"/></b> 的默认持有器。消费方若在 <c>AddMudHttpClient</c> <b>之后</b>注册自己的持有器/切换器，
/// 就会出现"两个 <c>AsyncLocal</c>"——写入下游实现的值，框架默认实现读不到（多应用静默失效）。
/// 于是"必须早于 <c>AddMudHttpClient</c> 注册"被固化为硬性顺序约束。
/// </para>
/// <para>
/// <b>本实现如何消除该约束</b>：框架不再抢先注册具体持有器类型，而是注册本适配器；适配器在首次访问时解析
/// 容器中"最后注册的其它 <c>IAppContextHolder</c>"并转发 <see cref="Current"/>（get）/
/// <see cref="SwitchTo"/> / <see cref="BeginScope"/>。⇒ 无论消费方在 <c>AddMudHttpClient</c>
/// <b>之前还是之后</b>注册（只要使用非 <c>TryAdd</c> 的注册，即"最后注册者胜"），适配器都会转发到它。
/// </para>
/// <para>
/// <b>回退语义</b>：容器中不存在其它实现时，回退为内部的 <see cref="AsyncLocalAppContextSwitcher"/>
/// ⇒ 单应用 / 零配置场景行为与既有版本完全一致。
/// </para>
/// <para>
/// <b>两点必须知道的边界</b>：
/// <list type="bullet">
/// <item><b><c>init</c> 不可转发</b>：<see cref="IAppContextHolder.Current"/> 的访问器是 <c>init</c>（仅可在对象初始化期写入），
/// 适配器无法把它转发给目标实例，因此<b>显式抛出</b> <see cref="NotSupportedException"/>（而非静默丢弃）。
/// 运行时切换请使用 <see cref="SwitchTo"/> / <see cref="BeginScope"/>（或生成代码的 <c>UseAppScope</c>）——
/// 这与 <c>init</c> 的既有组织约定一致，DI 构造的持有器不会被任何消费方经 <c>init</c> 赋值。</item>
/// <item><b>目标在首次访问时解析并缓存</b>：故消费方的持有器注册应在"首次访问上下文（通常为首次创建客户端/首个请求）"
/// 之前完成 —— 与既有"启动期完成注册"的实践一致，且不再要求早于 <c>AddMudHttpClient</c>。
/// 目标必须是<b>单例</b>（多实例/瞬态实现下，适配器只会持有所解析到的那一个）。</item>
/// </list>
/// </para>
/// <para>
/// <b>自身排除是防递归的关键</b>：本适配器自身也是一条 <c>IAppContextHolder</c> 注册；
/// 若解析时不排除自身，<c>GetServices&lt;IAppContextHolder&gt;().Last()</c> 会恒为我们自己 ⇒ 无限递归。
/// （可行性已由 <c>AppContextHolderAdapterProbeTests</c> 探针固化。）
/// </para>
/// </remarks>
internal sealed class DelegatingAppContextHolder : IAppContextHolder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly object _gate = new();
    private IAppContextHolder? _target;

    /// <summary>
    /// 初始化委托式持有器。
    /// </summary>
    /// <param name="serviceProvider">用于惰性解析容器内其它 <see cref="IAppContextHolder"/> 实现的服务提供程序。</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> 为 null。</exception>
    public DelegatingAppContextHolder(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    /// <summary>转发目标（首次访问时解析并缓存；无其它实现时回退为内部 AsyncLocal 持有器）。</summary>
    private IAppContextHolder Target
    {
        get
        {
            var target = _target;
            if (target is not null)
                return target;

            lock (_gate)
            {
                _target ??= _serviceProvider.GetServices<IAppContextHolder>()
                                .LastOrDefault(holder => !ReferenceEquals(holder, this))
                            ?? new AsyncLocalAppContextSwitcher();
                return _target;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>get 转发到目标；<c>init</c> <b>显式不支持</b>（见类型备注）。</remarks>
    public IMudAppContext? Current
    {
        get => Target.Current;
        init => throw new NotSupportedException(
            "委托式 IAppContextHolder 不支持 init 写入（无法转发给目标实例）。运行时切换请使用 SwitchTo / BeginScope，或生成代码的 UseAppScope。");
    }

    /// <inheritdoc />
    public void SwitchTo(IMudAppContext? context) => Target.SwitchTo(context);

    /// <inheritdoc />
    public IDisposable BeginScope(IMudAppContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        return Target.BeginScope(context);
    }
}
