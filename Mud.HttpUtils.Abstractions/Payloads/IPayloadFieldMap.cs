// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Mud.HttpUtils.Payloads;

/// <summary>
/// 字段映射契约（由生成器产出实现，由消费方运行时引擎消费）。
/// </summary>
/// <typeparam name="T">目标载荷类型。</typeparam>
/// <remarks>
/// <para>
/// 接口以 <c>in T</c> 声明（可逆变）：<typeparamref name="T"/> 只出现在<b>输入</b>位置，
/// 故实例化职责不在此接口上，而落在非泛型桥 <see cref="IPayloadContractAccessor"/>。
/// </para>
/// <para>
/// 实现必须是<b>无状态、可多线程共享</b>的：映射表通常以 <c>static</c> 单例形式持有，
/// 不得把「当前应用模式 / 租户 / 用户」等运行期上下文写入映射表或委托（见 <see cref="PayloadFieldBinder{T}"/>）。
/// </para>
/// </remarks>
public interface IPayloadFieldMap<in T> where T : class
{
    /// <summary>契约标识（默认取载荷类型名；用于诊断与日志）。</summary>
    string ContractId { get; }

    /// <summary>作用域回退节点名（报文布局差异，如 <c>BatchJob</c>）；无回退时为 <see langword="null"/>。</summary>
    string? ScopeFallback { get; }

    /// <summary>已映射的元素名集合（声明序，可用于诊断与「作用域探测」）。</summary>
    IReadOnlyList<string> Elements { get; }

    /// <summary>
    /// 解析解析生效的作用域节点：根命中任一映射元素 ⇒ 根；否则回退 <see cref="ScopeFallback"/> 容器；
    /// 皆无 ⇒ 根（此时所有字段均收到 <see langword="null"/>，得到「全空载荷」而非异常）。
    /// </summary>
    /// <param name="root">报文根节点。</param>
    /// <returns>作用域节点；<paramref name="root"/> 为 <see langword="null"/> 时返回 <see langword="null"/>。</returns>
    PayloadNode? ResolveScope(PayloadNode root);

    /// <summary>逐条绑定到 <paramref name="target"/>（元素缺失时对应委托收到 <see langword="null"/>）。</summary>
    /// <param name="root">报文根节点。</param>
    /// <param name="target">目标实例。</param>
    void Bind(PayloadNode root, T target);
}
