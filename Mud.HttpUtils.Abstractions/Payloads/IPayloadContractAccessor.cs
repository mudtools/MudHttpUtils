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
/// 字段映射契约的<b>类型擦除视图</b>：供消费方引擎按 <see cref="PayloadType"/> 分派，
/// 并以单例形式在一个注册表中持有多个不同 <c>T</c> 的映射表。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：消费方需要在<b>泛型</b>上下文中拿到某个载荷类型的映射表
/// （例如注册入口写作 <c>AddPayload&lt;TPayload&gt;(key)</c> 或在泛型方法体内取值）。
/// 但 C# 禁止在泛型上下文访问类型参数的静态成员（<c>TPayload.PayloadFieldMap</c> ⇒ <c>CS0712</c>），
/// 而用 <c>static abstract</c> 接口成员表达该约束需要 net7+ 运行时支持，
/// 消费面含 netstandard2.0 / net6.0 时不可用。
/// </para>
/// <para>
/// 因此本接口由 <see cref="PayloadFieldMap{T}"/>（<c>T</c> 在该类内是具体类型）实现，
/// 消费方改为在<b>具体类型</b>上做静态成员访问 —— 合法且编译期完全类型化：
/// <code>
/// builder.AddPayload("change_external_contact", MyPayload.PayloadFieldMap);
/// </code>
/// 无反射、无 <c>dynamic</c>、无 AOT 退化。
/// </para>
/// <para>
/// 消费方<b>不得</b>用反射取静态成员（<c>Type.GetProperty</c> / <c>MakeGenericMethod</c>）——
/// 那正是本接缝要消除的路径，且在 AOT 严格模式下触发 <c>IL2070</c>/<c>IL3050</c>。
/// </para>
/// </remarks>
public interface IPayloadContractAccessor
{
    /// <summary>目标载荷类型（消费方据此做类型一致性校验 ⇒ 契约错配可在运行期定位）。</summary>
    Type PayloadType { get; }

    /// <inheritdoc cref="IPayloadFieldMap{T}.ContractId"/>
    string ContractId { get; }

    /// <inheritdoc cref="IPayloadFieldMap{T}.ScopeFallback"/>
    string? ScopeFallback { get; }

    /// <inheritdoc cref="IPayloadFieldMap{T}.Elements"/>
    IReadOnlyList<string> Elements { get; }

    /// <inheritdoc cref="IPayloadFieldMap{T}.ResolveScope(PayloadNode)"/>
    PayloadNode? ResolveScope(PayloadNode root);

    /// <summary>创建空载荷实例（实现要求 <c>T : new()</c>）。</summary>
    /// <returns>新建的目标实例。</returns>
    object CreateInstance();

    /// <summary>绑定并返回同一实例（便于链式调用）。</summary>
    /// <param name="root">报文根节点。</param>
    /// <param name="target">目标实例（须为 <see cref="PayloadType"/> 的实例）。</param>
    /// <returns><paramref name="target"/>（同一引用）。</returns>
    object Bind(PayloadNode root, object target);
}
