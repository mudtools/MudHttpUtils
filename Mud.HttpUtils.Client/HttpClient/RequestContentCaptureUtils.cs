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
    /// 判定规则：内容显式声明 <c>IRequestContentReplayHint.IsReplayable == false</c> 时不可重放，应跳过捕获
    /// （否则读取会耗尽一次性源流，导致随后发送空/截断请求体）；其余类型交由读取端按 <c>CanSeek</c> 复位判定。
    /// </para>
    /// <para>跳过时经 <see cref="Interlocked"/> 一次性门控记 Debug 日志，避免对高频上传刷屏。</para>
    /// </remarks>
    internal static class RequestContentCaptureUtils
    {
        private static int _nonReplayableSkippedLogged;

        /// <summary>
        /// 判断请求体内容是否可安全捕获。返回 <c>false</c> 表示应跳过（内容声明为不可重放）。
        /// </summary>
        /// <param name="content">请求体内容。</param>
        /// <param name="logger">日志记录器（用于一次性 Debug 警示）。</param>
        public static bool CanCapture(HttpContent? content, ILogger logger)
        {
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

            return true;
        }
    }
}