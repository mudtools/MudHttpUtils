// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Mud.HttpUtils.Payloads;

/// <summary>
/// 字段映射表的<b>唯一构建原语</b>与默认实现（fluent <see cref="Map"/> 链）。
/// </summary>
/// <typeparam name="T">目标载荷类型。</typeparam>
/// <remarks>
/// <para>
/// 本类的 fluent 链是<b>受支持的公共 API</b>（手写链），生成器只是「另一种写链的方式」：
/// 两条路径产出<b>完全同构</b>的运行时对象，故消费方可逐类渐进迁移（加特性 + 删手写链），
/// 随时可回退，且生成器缺席时体系仍完整可用。
/// </para>
/// <para>
/// <b>不可变性</b>：<see cref="Map"/> 仅允许在构造期（链式调用）使用；
/// 链构建完成后实例即为只读，可安全地作为 <c>static</c> 单例被多线程共享。
/// </para>
/// <para>
/// <c>new()</c> 约束服务于 <see cref="IPayloadContractAccessor.CreateInstance"/>：
/// 约束在<b>编译期</b>表达契约，运行期不做防御性检查（失败应发生在开发期而非生产期）。
/// </para>
/// </remarks>
public sealed class PayloadFieldMap<T> : IPayloadFieldMap<T>, IPayloadContractAccessor
    where T : class, new()
{
    private readonly List<KeyValuePair<string, PayloadFieldBinder<T>>> _bindings =
        new List<KeyValuePair<string, PayloadFieldBinder<T>>>();

    private string[]? _elements;

    private PayloadFieldMap(string contractId, string? scopeFallback)
    {
        ContractId = contractId;
        ScopeFallback = scopeFallback;
    }

    /// <summary>
    /// 创建一个空映射表（<c>contractId</c> 为契约标识，用于诊断与日志）。
    /// </summary>
    /// <param name="contractId">契约标识（不得为空）。</param>
    /// <param name="scopeFallback">作用域回退节点名（报文布局差异，如 <c>BatchJob</c>）；无则 <see langword="null"/>。</param>
    /// <returns>可继续链式 <see cref="Map"/> 的映射表。</returns>
    /// <exception cref="ArgumentException"><paramref name="contractId"/> 为空。</exception>
    public static PayloadFieldMap<T> Create(string contractId, string? scopeFallback = null)
    {
        if (string.IsNullOrEmpty(contractId))
            throw new ArgumentException("契约标识不得为空。", nameof(contractId));

        return new PayloadFieldMap<T>(contractId, scopeFallback);
    }

    /// <summary>
    /// 追加一条字段映射。
    /// </summary>
    /// <param name="element">源端元素名（XML 元素名 / JSON 键）。</param>
    /// <param name="binder">字段绑定委托（缺失元素时收到 <see langword="null"/>）。</param>
    /// <returns>自身，供链式调用。</returns>
    /// <exception cref="ArgumentException"><paramref name="element"/> 为空，或在同一映射表内重复。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="binder"/> 为 <see langword="null"/>。</exception>
    /// <remarks>
    /// 空 / 重复元素名<b>快速失败</b>：生成器已在编译期报 <c>PAYLOAD006</c>，运行期抛异常属
    /// 「生成物与上游版本不匹配」的信号（旧生成物 + 新上游），显式失败优于静默覆盖。
    /// </remarks>
    public PayloadFieldMap<T> Map(string element, PayloadFieldBinder<T> binder)
    {
        if (string.IsNullOrEmpty(element))
            throw new ArgumentException("元素名不得为空。", nameof(element));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        for (var i = 0; i < _bindings.Count; i++)
        {
            if (string.Equals(_bindings[i].Key, element, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "同一映射表内的元素名必须唯一，" + element + " 已被映射。", nameof(element));
            }
        }

        _bindings.Add(new KeyValuePair<string, PayloadFieldBinder<T>>(element, binder));
        return this;
    }

    /// <inheritdoc/>
    public string ContractId { get; }

    /// <inheritdoc/>
    public string? ScopeFallback { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// 首次访问时物化并缓存（避免 <see cref="Bind"/> / <see cref="ResolveScope"/> 每次分配迭代器）；
    /// 并发下最坏情况是重复构建一次完全相同的数组，属良性竞态。
    /// </remarks>
    public IReadOnlyList<string> Elements
    {
        get
        {
            var cached = _elements;
            if (cached != null)
                return cached;

            var built = new string[_bindings.Count];
            for (var i = 0; i < built.Length; i++)
                built[i] = _bindings[i].Key;

            _elements = built;
            return built;
        }
    }

    /// <inheritdoc/>
    public PayloadNode? ResolveScope(PayloadNode root)
    {
        if (root == null)
            return null;

        // ① 根节点命中任一映射元素 ⇒ 作用域为根（报文未包裹）
        for (var i = 0; i < _bindings.Count; i++)
        {
            if (root.Has(_bindings[i].Key))
                return root;
        }

        // ② 否则回退到包装容器（如 BatchJob）
        if (ScopeFallback != null)
        {
            var container = root.Child(ScopeFallback);
            if (container != null)
                return container;
        }

        // ③ 皆无 ⇒ 根：所有字段的 Child 查找均落空，得到「全空载荷」而非异常。
        //    返回非 null（而非 null）有两个作用：其一，消费方拿到的「作用域节点」仍可用于
        //    Values 全量袋等后续处理；其二，ResolveScope 对非 null 输入恒不返回 null，
        //    使调用方无需区分「作用域缺失」与「报文为空」两种情形。
        return root;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 为 <see langword="null"/>。</exception>
    public void Bind(PayloadNode root, T target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var scope = ResolveScope(root);
        for (var i = 0; i < _bindings.Count; i++)
        {
            _bindings[i].Value(target, scope?.Child(_bindings[i].Key));
        }
    }

    /// <inheritdoc/>
    Type IPayloadContractAccessor.PayloadType => typeof(T);

    /// <inheritdoc/>
    object IPayloadContractAccessor.CreateInstance() => new T();

    /// <inheritdoc/>
    object IPayloadContractAccessor.Bind(PayloadNode root, object target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (target is not T typed)
            throw new ArgumentException("目标实例类型与契约载荷类型不一致：" + typeof(T).FullName + "。", nameof(target));

        Bind(root, typed);
        return target;
    }
}
