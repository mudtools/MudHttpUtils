// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// R-P1-03：401 恢复链路对请求体的缓冲策略。
/// </summary>
/// <remarks>
/// <para>
/// 缓冲的目标是让 401 之后的<b>无损重放</b>成为可能；但它本身有代价（读取、拷贝、替换请求体，
/// 并改变流式上传的语义），而 401 是低频事件，代价却由<b>全部正常请求</b>承担 —— 故提供本开关。
/// </para>
/// <para>
/// <b>不设 <c>Disabled</c> 态（评审修订 5）</b>：既有 <see cref="TokenRecoveryOptions.MaxCachedRequestBodyBytes"/> == 0
/// 已表达"流式优先 / 不缓冲"（该值下不做任何预读，带体请求放弃 401 重试但正常发送）。
/// 增设第三个枚举态属功能冗余。
/// </para>
/// </remarks>
public enum RequestBodyBufferingMode
{
    /// <summary>
    /// 默认（兼容既有三态模型）：内存型内容惰性重放，流式内容在<b>声明长度且未超限</b>时按上限缓冲；
    /// 未声明长度（chunked）的流式内容不预读。
    /// </summary>
    Auto = 0,

    /// <summary>
    /// 仅认可内存型内容（<see cref="ByteArrayContent"/> / <see cref="StringContent"/> /
    /// <see cref="System.Net.Http.FormUrlEncodedContent"/> 及 <c>System.Net.Http.Json</c> 系内容）的重放能力，
    /// <b>完全不触碰</b>任何流式内容 —— 高吞吐上传场景推荐（避免为低频 401 承担流读取与拷贝）。
    /// </summary>
    MemoryOnly = 1,
}
