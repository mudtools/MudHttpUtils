// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// G1：可替换的<b>系统时钟接缝</b>——使令牌过期判定、刷新失败退避、去重窗口等
/// 时间相关逻辑可被确定性测试（无需 <c>Thread.Sleep</c> / 真实等待）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为何自建而不直接用 <c>System.TimeProvider</c></b>：<c>TimeProvider</c> 仅 net8.0+ 原生，
/// 而本库覆盖 <c>netstandard2.0</c> / <c>net6.0</c>；引入 <c>Microsoft.Bcl.TimeProvider</c>
/// 会新增<b>传递依赖</b>（与 F7 的依赖治理目标冲突）。故以 3 行接口做 TFM 中立兜底，
/// 语义与 <c>TimeProvider.GetUtcNow()</c> 对齐，未来可无痛迁移。
/// </para>
/// <para>
/// 消费方式：派生 <see cref="TokenManagerBase"/> 并覆写 <c>UtcNow</c>（或在测试中注入假时钟实现）。
/// </para>
/// </remarks>
public interface ISystemClock
{
    /// <summary>获取当前 UTC 时间。</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// G1：<see cref="ISystemClock"/> 的默认实现（真实系统时钟）。
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <summary>共享实例（无状态、线程安全）。</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
