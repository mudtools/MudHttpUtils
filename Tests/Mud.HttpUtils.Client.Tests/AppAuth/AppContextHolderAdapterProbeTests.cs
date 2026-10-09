// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// <b>G7 可行性探针</b>（本方案最高风险项的先决验证，对应 §3.14 / §0.5.3-G7）：
/// 验证"委托式 <see cref="IAppContextHolder"/> 适配器"能否在不改接口、不破坏 <c>init</c> 语义的前提下，
/// 把 <see cref="IAppContextHolder.Current"/>（get）/ <c>SwitchTo</c> / <c>BeginScope</c>
/// 转发给容器中<b>最后注册</b>的实现，且<b>不解析到自身</b>（防无限递归）。
/// </summary>
/// <remarks>
/// <para>
/// 本探针回答两个必须先证实的问题：
/// <list type="number">
/// <item><b>自身排除</b>：适配器自身也是一条 <c>IAppContextHolder</c> 注册。
/// 若在 <c>Current</c>/<c>SwitchTo</c>/<c>BeginScope</c> 内以
/// <c>GetServices&lt;IAppContextHolder&gt;().Last()</c> 定位目标且不排除自身，将得到自己 ⇒ 无限递归。
/// 结论：以 <c>ReferenceEquals(h, this)</c> 排除即可（构造期不解析目标 ⇒ 无递归）。</item>
/// <item><b><c>init</c> 约束</b>：接口声明为 <c>{ get; init; }</c>，故适配器<b>必须</b>实现 <c>init</c> 访问器；
/// 而 <c>init</c> 仅可在对象初始化期写入、<b>无法转发</b>。本探针以"反射断言 init 修饰符仍存在"+
/// "实现体显式抛 <see cref="NotSupportedException"/>"固化这一边界（DI 构造的 holder 不会被任何消费方经 <c>init</c> 赋值）。</item>
/// </list>
/// </para>
/// </remarks>
public class AppContextHolderAdapterProbeTests
{
    // ── 探针实现 ───────────────────────────────────────────────────────

    /// <summary>下游自建切换器（自持 <see cref="AsyncLocal{T}"/>，与 <c>AsyncLocalAppContextSwitcher</c> 同构）。</summary>
    private sealed class ProbeDownstreamHolder : IAppContextHolder
    {
        private readonly AsyncLocal<IMudAppContext?> _context = new();

        public IMudAppContext? Current
        {
            get => _context.Value;
            init => _context.Value = value;
        }

        public void SwitchTo(IMudAppContext? context) => _context.Value = context;

        public IDisposable BeginScope(IMudAppContext context)
        {
            var previous = _context.Value;
            _context.Value = context;
            return new Scope(() =>
            {
                if (ReferenceEquals(_context.Value, context))
                    _context.Value = previous;
            });
        }
    }

    /// <summary>候选的"委托式适配器"（G7 方案形态）；<c>Target</c> 公开仅供探针断言。</summary>
    private sealed class DelegatingProbeHolder : IAppContextHolder
    {
        private readonly IServiceProvider _serviceProvider;
        private IAppContextHolder? _target;
        private readonly object _gate = new();

        public DelegatingProbeHolder(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        public IAppContextHolder? Target
        {
            get
            {
                if (_target is not null)
                    return _target;

                lock (_gate)
                {
                    // 关键：排除自身（自身亦为 IAppContextHolder 注册项），否则 Last() 恒为自己 ⇒ 无限递归。
                    _target ??= _serviceProvider.GetServices<IAppContextHolder>()
                        .LastOrDefault(holder => !ReferenceEquals(holder, this));
                    return _target;
                }
            }
        }

        /// <remarks>
        /// <b>get 转发</b>：读取容器内最后注册的实现。
        /// <b>init 不可转发</b>：<c>init</c> 只能在对象初始化期写入，适配器必须显式拒绝，
        /// 以免出现"赋值被静默丢弃"的隐性错误（<c>init</c> 的既有组织约定本就是"仅对象初始化期可见"，
        /// 运行时切换必须走 <see cref="SwitchTo"/> / <see cref="BeginScope"/>）。
        /// </remarks>
        public IMudAppContext? Current
        {
            get => Target?.Current;
            init => throw new NotSupportedException(
                "委托式 AppContextHolder 不支持 init 写入；运行时切换请使用 SwitchTo / BeginScope（或生成代码的 UseAppScope）。");
        }

        public void SwitchTo(IMudAppContext? context) => Target?.SwitchTo(context);

        public IDisposable BeginScope(IMudAppContext context)
            => Target?.BeginScope(context) ?? NullScope.Instance;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }

    private sealed class FakeAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;
        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();
        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();
        public T? GetService<T>() where T : class => null;
    }

    private static (DelegatingProbeHolder Adapter, ProbeDownstreamHolder Downstream) Build(bool downstreamRegisteredFirst)
    {
        var services = new ServiceCollection();
        var downstream = new ProbeDownstreamHolder();

        if (downstreamRegisteredFirst)
            services.AddSingleton<IAppContextHolder>(downstream);

        // 适配器（框架默认持有器的候选形态）：以工厂委托注册，注入 IServiceProvider 供惰性解析。
        services.AddSingleton<IAppContextHolder>(sp => new DelegatingProbeHolder(sp));

        if (!downstreamRegisteredFirst)
            services.AddSingleton<IAppContextHolder>(downstream);

        var provider = services.BuildServiceProvider();
        var adapter = provider.GetServices<IAppContextHolder>().OfType<DelegatingProbeHolder>().Single();
        return (adapter, downstream);
    }

    // ── ① 自身排除（防无限递归） ───────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Adapter_ShouldResolveDownstream_NotItself(bool downstreamRegisteredFirst)
    {
        var (adapter, downstream) = Build(downstreamRegisteredFirst);

        adapter.Target.Should().NotBeNull("必须能定位到下游实现");
        adapter.Target.Should().BeSameAs(downstream, "目标必须是下游实例（不得解析到适配器自身）");
        adapter.Target.Should().NotBeSameAs(adapter, "排除自身是防无限递归的关键");
        adapter.Target.Should().NotBeOfType<DelegatingProbeHolder>();
    }

    // ── ② Current(get) / SwitchTo 转发 ────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Adapter_ShouldForwardCurrentAndSwitchTo(bool downstreamRegisteredFirst)
    {
        var (adapter, downstream) = Build(downstreamRegisteredFirst);
        var appA = new FakeAppContext("app-A");

        adapter.Current.Should().BeNull("初始无上下文");

        adapter.SwitchTo(appA);

        downstream.Current.Should().BeSameAs(appA, "SwitchTo 必须转发到下游实现");
        adapter.Current.Should().BeSameAs(appA, "适配器读取必须看到下游已切换的上下文");
    }

    // ── ③ BeginScope 转发与恢复 ──────────────────────────────────────

    [Fact]
    public void Adapter_ShouldForwardBeginScope_AndRestoreOnDispose()
    {
        var (adapter, downstream) = Build(downstreamRegisteredFirst: false);
        var appA = new FakeAppContext("app-A");
        var appB = new FakeAppContext("app-B");

        adapter.SwitchTo(appA);

        using (adapter.BeginScope(appB))
        {
            adapter.Current.Should().BeSameAs(appB);
            downstream.Current.Should().BeSameAs(appB, "作用域写入必须落到下游实现");
        }

        adapter.Current.Should().BeSameAs(appA, "作用域释放后必须恢复到前值");
    }

    // ── ④ init 约束（接口即 init；适配器不可转发） ─────────────────────

    [Fact]
    public void Adapter_MustImplementInitAccessor_ButCannotForwardIt()
    {
        // 接口声明即 { get; init; }：访问器带 IsExternalInit 修饰符。
        var interfaceSetter = typeof(IAppContextHolder)
            .GetProperty(nameof(IAppContextHolder.Current))!
            .SetMethod;

        interfaceSetter.Should().NotBeNull("接口 Current 必须带有 init 访问器");
        interfaceSetter!.ReturnParameter.GetRequiredCustomModifiers()
            .Should().Contain(typeof(System.Runtime.CompilerServices.IsExternalInit),
                "接口 Current 的访问器是 init（E11）");

        // 适配器同样以 init 实现（编译期要求），但显式拒绝写入 ⇒ 不产生"静默丢弃"。
        var adapterSetter = typeof(DelegatingProbeHolder)
            .GetProperty(nameof(IAppContextHolder.Current))!
            .SetMethod;

        adapterSetter.Should().NotBeNull();
        adapterSetter!.ReturnParameter.GetRequiredCustomModifiers()
            .Should().Contain(typeof(System.Runtime.CompilerServices.IsExternalInit));

        var (adapter, _) = Build(downstreamRegisteredFirst: false);

        // init 属性无法在对象初始化器之外直接赋值（CS8852），故以反射调用访问器验证运行时行为。
        var act = () => adapterSetter.Invoke(adapter, new object?[] { new FakeAppContext("x") });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>(
                "init 无法转发，必须显式失败而非静默丢弃（运行时切换请用 SwitchTo/BeginScope）");
    }

    // ── ⑤ 无下游实现时的回落语义 ──────────────────────────────────────

    [Fact]
    public void Adapter_WithoutDownstream_ShouldDegradeToNoOp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppContextHolder>(sp => new DelegatingProbeHolder(sp));
        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetServices<IAppContextHolder>().OfType<DelegatingProbeHolder>().Single();

        adapter.Target.Should().BeNull("无下游实现时目标为空");

        var act = () =>
        {
            adapter.SwitchTo(new FakeAppContext("app-A"));
            using var scope = adapter.BeginScope(new FakeAppContext("app-B"));
        };

        act.Should().NotThrow("无下游实现时必须安全降级（不抛、不递归）");
        adapter.Current.Should().BeNull();
    }
}
