// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  Mud.HttpUtils 项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience
{
    /// <summary>
    /// M4-H-3：<see cref="TaskCanceledException"/> 三态判定（多 TFM 统一入口）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// .NET 5+ 中 <c>HttpClient.Timeout</c> 触发的 TCE 具有特征 <c>InnerException is TimeoutException</c> 且异常 token
    /// <c>IsCancellationRequested == true</c>；用户取消触发的 TCE 其 inner 为 <see cref="TaskCanceledException"/>
    /// （非 TimeoutException）。据此可仅凭异常形态区分平台超时与用户取消。
    /// </para>
    /// <para>
    /// <b>架构约束</b>：Polly v7 策略谓词为 <c>Predicate&lt;Exception&gt;</c>，无法取得调用方 CancellationToken，
    /// 因此策略过滤器只用 <see cref="IsPlatformTimeout"/>（异常形态判定）；调用方 token 仅在其可达处
    /// （策略边界外，如 <see cref="PollyExceptionNormalizer"/>）用于三态归类排除用户取消。
    /// netstandard2.0 的 TCE 无一致 inner 特征，平台超时将按保守"不重试"处理（差异见方案文档 §3.3/§7.2）。
    /// </para>
    /// </remarks>
    internal static class TaskCancellationClassifier
    {
        public enum Category
        {
            /// <summary>调用方令牌已触发（用户取消）。</summary>
            UserCancelled,
            /// <summary>平台超时（net5+ 特征：inner 为 <see cref="TimeoutException"/>）。</summary>
            PlatformTimeout,
            /// <summary>其余取消（不可判定为平台超时）。</summary>
            Other,
        }

        /// <summary>
        /// 依据异常形态判定"是否为平台超时"（net5+ 特征：inner 为 <see cref="TimeoutException"/>）。
        /// 用于 Polly 过滤谓词（无调用方 token 可用）。
        /// </summary>
        public static bool IsPlatformTimeout(Exception ex)
            => ex is TaskCanceledException { InnerException: TimeoutException };

        /// <summary>
        /// 三态归类（调用方上下文可达时使用，排除用户取消导致的误判）。
        /// </summary>
        public static Category Classify(TaskCanceledException ex, CancellationToken userToken)
        {
            if (userToken.IsCancellationRequested)
                return Category.UserCancelled;

            if (IsPlatformTimeout(ex))
                return Category.PlatformTimeout;

            return Category.Other;
        }
    }
}