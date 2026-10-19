// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Payloads;

namespace PayloadDemo.Engine;

/// <summary>
/// 契约注册表：按<b>契约标识</b>持有不同 <c>T</c> 的映射表，并在收到报文时「实例化 + 绑定」。
/// </summary>
/// <remarks>
/// <para>
/// 这正是非泛型桥 <see cref="IPayloadContractAccessor"/> 存在的理由：注册表要在<b>同一个</b>容器里
/// 混装多个不同 <c>T</c> 的映射表，而 C# 禁止在泛型上下文访问类型参数的静态成员
/// （<c>TPayload.PayloadFieldMap</c> ⇒ <b>CS0712</b>），用 <c>static abstract</c> 表达该约束又需要
/// net7+ 运行时支持（消费面含 netstandard2.0 / net6.0 时不可用）。
/// </para>
/// <para>
/// 因此注册入口写作 <c>Add&lt;TPayload&gt;(key, map)</c>，而<b>调用点</b>在具体类型上：
/// <c>registry.Add("…", ContactBatchChangedPayload.PayloadFieldMap)</c> ——
/// 静态成员访问完全类型化、无反射、无 <c>dynamic</c>，AOT 严格模式下安全。
/// </para>
/// <para>
/// <b>注意</b>：生成成员的静态类型是 <see cref="IPayloadFieldMap{T}"/>（以 <c>in T</c> 声明的逆变接口），
/// 而非泛型桥是<b>独立</b>接口（前者只有输入位置成员，后者含 <c>CreateInstance()</c> / <c>Bind(object)</c>）
/// —— 两者没有继承关系，<b>不能</b>隐式赋值（<c>CS0266</c>）。
/// <see cref="Add{TPayload}"/> 内部的 <c>is IPayloadContractAccessor</c> 模式匹配就是必需的显式转换。
/// </para>
/// </remarks>
public sealed class PayloadContractRegistry
{
    private readonly Dictionary<string, IPayloadContractAccessor> _contracts =
        new Dictionary<string, IPayloadContractAccessor>(StringComparer.Ordinal);

    private readonly List<string> _order = new List<string>();

    /// <summary>已注册的契约标识（注册序）。</summary>
    public IReadOnlyList<string> Keys => _order;

    /// <summary>注册一个载荷契约。</summary>
    /// <typeparam name="TPayload">载荷类型（由调用点的静态成员访问保证具体）。</typeparam>
    /// <param name="key">契约标识（通常取 <c>[PayloadContract(ContractId = …)]</c>，留空则取类名）。</param>
    /// <param name="map">映射表，调用点写作 <c>XxxPayload.PayloadFieldMap</c>。</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">映射表未实现 <see cref="IPayloadContractAccessor"/>。</exception>
    public void Add<TPayload>(string key, IPayloadFieldMap<TPayload> map)
        where TPayload : class
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("契约标识不得为空。", nameof(key));

        // 逆变接口与非泛型桥是两条独立接口，必须显式转换（隐式赋值报 CS0266）。
        if (map is not IPayloadContractAccessor accessor)
            throw new InvalidOperationException("载荷映射表未实现 IPayloadContractAccessor（应由 PayloadFieldMap<T> 承载）。");

        if (!_contracts.ContainsKey(key))
            _order.Add(key);

        _contracts[key] = accessor;
    }

    /// <summary>按契约标识「新建载荷实例 + 绑定报文 + 返回同一实例」。</summary>
    /// <param name="key">契约标识。</param>
    /// <param name="root">报文根节点（由消费方适配器投影而来）。</param>
    /// <returns>已填充的载荷实例（静态类型为 <see cref="object"/>，按 <c>PayloadType</c> 判定后强转）。</returns>
    /// <exception cref="KeyNotFoundException">契约标识未注册。</exception>
    public object CreateAndBind(string key, PayloadNode root)
    {
        if (!_contracts.TryGetValue(key, out var accessor))
            throw new KeyNotFoundException("未注册的契约标识：" + key + "。");

        // Bind 返回同一实例，便于链式表达：accessor.Bind(root, accessor.CreateInstance())
        return accessor.Bind(root, accessor.CreateInstance());
    }

    /// <summary>取契约的元信息（供诊断 / 自检输出）。</summary>
    /// <exception cref="KeyNotFoundException">契约标识未注册。</exception>
    public IPayloadContractAccessor Get(string key)
    {
        if (!_contracts.TryGetValue(key, out var accessor))
            throw new KeyNotFoundException("未注册的契约标识：" + key + "。");

        return accessor;
    }
}
