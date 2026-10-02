// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#nullable enable

namespace Mud.HttpUtils.Payloads;

/// <summary>
/// 单字段绑定委托：把一个源节点投影到目标属性。
/// </summary>
/// <typeparam name="T">目标载荷类型。</typeparam>
/// <param name="target">目标实例（非 <see langword="null"/>）。</param>
/// <param name="node">源节点；<see langword="null"/> 表示该元素缺失，委托必须写入「缺失/默认值」而<b>不得</b>抛异常。</param>
/// <remarks>
/// <para>
/// <b>无状态约束</b>：委托所属的映射表是<b>可被多线程共享的静态单例</b>，
/// 因此委托<b>不得</b>捕获任何可变请求态（当前应用模式 / 租户 / 用户 / 请求对象 / <c>CancellationToken</c>）。
/// 确需上下文时，应在消费方的读取器 / 处理器层按上下文选择契约，而不是把上下文闭包进委托。
/// </para>
/// <para>
/// 生成器产出的委托恒为无捕获 lambda（只引用形参与转换器静态方法），故生成链天然满足本约束；
/// 手写链建议使用 <c>static</c> lambda（C# 9+）让编译器强制该约束。
/// </para>
/// </remarks>
public delegate void PayloadFieldBinder<in T>(T target, PayloadNode? node) where T : class;
