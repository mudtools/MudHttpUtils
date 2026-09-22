// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  Mud.HttpUtils 项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Mud.HttpUtils
{
    /// <summary>
    /// M4-H-2：请求体捕获的可重放性守卫。供 <see cref="DefaultHttpRequestExecutor.CaptureRequestContentAsync"/>
    /// 与 <c>EnhancedHttpClient</c> 两条捕获路径共用，确保"非可重放内容自动跳过"语义一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定规则（任一命中即拒捕）：
    /// <list type="number">
    /// <item>内容显式声明 <c>IRequestContentReplayHint.IsReplayable == false</c>；</item>
    /// <item>
    /// M6-HC-31：<b>无声明长度且非内存型内容</b> —— 即底层为一次性（不可 seek）源流。
    /// 捕获需读取内容流，而 <c>LimitedContentReader</c> 只能对 <c>CanSeek</c> 的流复位；
    /// 一次性流读完即耗尽，随后发送得到空/截断请求体。判定采用"类型白名单"（零消耗，不探测流）。
    /// </item>
    /// </list>
    /// </para>
    /// <para>跳过时经 <see cref="Interlocked"/> 一次性门控记 Debug 日志，避免对高频上传刷屏。</para>
    /// </remarks>
    internal static class RequestContentCaptureUtils
    {
        private static int _nonReplayableSkippedLogged;
        private static int _nonSeekableSkippedLogged;

        /// <summary>
        /// 判断请求体内容是否可安全捕获。返回 <c>false</c> 表示应跳过（内容不可重放 / 底层流不可 seek）。
        /// </summary>
        /// <param name="content">请求体内容。</param>
        /// <param name="logger">日志记录器（用于一次性 Debug 警示）。</param>
        public static bool CanCapture(HttpContent? content, ILogger logger)
        {
            if (content is null)
                return true;

            if (content is IRequestContentReplayHint { IsReplayable: false })
            {
                // 一次性门控日志（Interlocked）—— 避免对高频上传刷屏
                if (System.Threading.Interlocked.Exchange(ref _nonReplayableSkippedLogged, 1) == 0)
                {
                    logger.LogDebug(
                        "捕获请求体已自动跳过：HttpContent 声明为不可重放（IsReplayable=false），" +
                        "以避免读取耗尽一次性源流导致发送空/截断请求体。" +
                        "如需捕获，请改用可重放内容或为自定义 HttpContent 实现 IRequestContentReplayHint。");
                }
                return false;
            }

            // M6-HC-31：无声明长度 + 非内存型 + 未显式声明可重放 ⇒ 视为不可 seek 的一次性源流。
            // 声明长度本身即"长度可预知"的证明（StreamContent 仅在流可 seek 时才能算出长度），
            // 故有声明长度时无需再探测。
            if (!content.Headers.ContentLength.HasValue
                && content is not ByteArrayContent
                && content is not IRequestContentReplayHint { IsReplayable: true })
            {
                if (System.Threading.Interlocked.Exchange(ref _nonSeekableSkippedLogged, 1) == 0)
                {
                    logger.LogDebug(
                        "捕获请求体已自动跳过：HttpContent 无声明长度且非内存型，底层很可能是不可 seek 的一次性源流，" +
                        "读取会使其耗尽（如 StreamContent / MultipartContent / JsonContent）。" +
                        "如需捕获，请改用 ByteArrayContent 家族，或为该 HttpContent 实现 IRequestContentReplayHint 并声明 IsReplayable=true。");
                }
                return false;
            }

            return true;
        }
    }
}