// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  Mud.HttpUtils 项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils
{
    /// <summary>
    /// M4-H-2：内容可重放性提示 —— 标记 <see cref="System.Net.Http.HttpContent"/> 的请求体能否被安全地"读取后再次发送"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 请求体捕获（<c>CaptureRequestContent</c>）与诊断读取会在<b>发送前</b>先消耗一次请求体流；
    /// 若内容不可重放，本次读取会耗尽底层一次性源流，导致随后发送的空/截断请求体（静默丢数据）。
    /// </para>
    /// <para>
    /// 默认（未实现本接口）视为"交由读取端按 <c>CanSeek</c> 复位判定"（保守快路径）。
    /// 框架已知类型显式声明：<c>StreamingJsonContent&lt;T&gt; = true</c>（持 item 引用、每次发送重新序列化）；
    /// <c>ProgressableStreamContent = false</c>（缓冲动作消耗源流）。
    /// 自定义非可重放 <see cref="System.Net.Http.HttpContent"/> 请实现本接口并返回 <c>false</c>，否则捕获将自动跳过。
    /// </para>
    /// <para>本接口为 internal（Abstractions 内），经 InternalsVisibleTo 对 Client 可见；不参与 PublicAPI 基线。</para>
    /// </remarks>
    internal interface IRequestContentReplayHint
    {
        /// <summary>
        /// <c>true</c> = 可安全读取（读取端 seek 复位重放，或内容自身可重序列化）；
        /// <c>false</c> = 不可重放，读取端在捕获时应<b>跳过</b>该请求体以保护数据完整性。
        /// </summary>
        bool IsReplayable { get; }
    }
}