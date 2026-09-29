// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌失效判定器：在既有「HTTP 401」识别之外，扩展识别以业务错误码表达令牌失效的平台响应
/// （如企业微信恒返 HTTP 200 + <c>errcode</c> ∈ {40014, 42001, 42007, 42009, 42011}）。
/// </summary>
/// <remarks>
/// <para>
/// WX-01（Phase A，errcode 令牌失效恢复）扩展点。经
/// <see cref="TokenRecoveryOptions.TokenInvalidationDetector"/> 注册后，
/// 恢复执行器的两处失效判定点（首次响应 / 重试响应）收敛为同一判定语义：
/// <c>HTTP 401（默认语义，短路在前）|| 判定器判定为失效</c>。
/// </para>
/// <para>
/// errcode 失效码集合、响应体解析等平台知识全部归属各平台 SDK（如 Mud.Wechat 的
/// <c>WechatWorkTokenInvalidationDetector</c>），不进入通用框架。
/// </para>
/// <para>
/// 实现必须幂等且无副作用：判定器不得消费 <paramref name="response"/> 的内容流
/// （调用方拿到的响应必须保持可读），不应抛出异常——除取消外的异常会被恢复执行器
/// 降级为「未失效」处理并记 Warning。
/// </para>
/// </remarks>
public interface ITokenInvalidationDetector
{
    /// <summary>
    /// 同步预过滤：该请求的响应是否需要失效判定检查。
    /// <para>
    /// 在任何响应体读取之前调用；返回 <c>false</c> 可完全跳过响应体捕获开销。
    /// 实现方借此按接口 / 客户端声明式启用（如按 host 白名单、请求 URI 路径或
    /// <see cref="TokenRecoveryContext"/> 存在性自查），满足"仅对确会走令牌的接口启用"的开销约束。
    /// </para>
    /// </summary>
    /// <param name="request">即将判定其响应的原始请求（重试轮为重建后的重试请求）。</param>
    /// <returns><c>true</c> = 需要检查（进入响应体捕获 + <see cref="IsTokenInvalidAsync"/>）；<c>false</c> = 跳过。</returns>
    bool ShouldInspect(HttpRequestMessage request);

    /// <summary>
    /// 判断响应是否表示令牌失效（需要刷新 + 重试）。
    /// </summary>
    /// <param name="response">原始响应（非 HTTP 401 时才会到达本方法）。</param>
    /// <param name="body">
    /// 已捕获的响应体字节。仅在响应声明 Content-Length 且 ≤
    /// <see cref="TokenRecoveryOptions.MaxCapturedResponseBodyBytes"/> 时非空；
    /// 为 null 时（声明超限 / 未知长度（chunked）/ 空体 / 捕获禁用）实现应回退到仅凭状态码判定
    /// （返回 <c>false</c>，即视为未失效）。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><c>true</c> = 令牌失效，触发刷新 + 重试；<c>false</c> = 未失效。</returns>
    ValueTask<bool> IsTokenInvalidAsync(
        HttpResponseMessage response,
        ReadOnlyMemory<byte>? body,
        CancellationToken cancellationToken);
}
