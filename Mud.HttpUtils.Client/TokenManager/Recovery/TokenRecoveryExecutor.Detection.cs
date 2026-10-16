// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026 
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// 纯移动拆分（自 TokenRecoveryExecutor.cs 按职责拆出：失效检测）。

namespace Mud.HttpUtils;

public partial class TokenRecoveryExecutor
{
/// <summary>
 /// WX-01（Phase A，errcode 令牌失效恢复）：令牌失效统一判定点，首次响应与重试响应共用。
/// 默认语义 = HTTP 401（短路在前，无判定器时非 401 响应零额外工作，与既有行为逐字节等价）；
/// 注册 <see cref="TokenRecoveryOptions.TokenInvalidationDetector"/> 后追加
/// 「HTTP 200 + 业务错误码」语义：先经 <see cref="ITokenInvalidationDetector.ShouldInspect"/>
/// 同步预过滤（在读取响应体之前调用，无关请求零捕获开销），再按需限量捕获响应体并交判定器判定。
/// 判定器链路中除取消外的任何异常一律降级为「未失效」（记 Warning）——检测故障不得放大为调用失败。
/// </summary>
/// <remarks>
/// B5（多产品线）：判定源由「单槽属性」扩展为「单槽 ∪ <see cref="TokenRecoveryOptions.AdditionalTokenInvalidationDetectors"/>」
/// 的并集。求值顺序固定为「单槽优先，再按集合 Add 顺序」，任一命中即短路返回；
/// 响应体捕获仅在首个 <c>ShouldInspect</c> 为 true 的判定器处发生一次（不重复读流）。
/// 两类来源皆空时与旧实现逐字节等价（仅认 401）。
/// </remarks>
 private async ValueTask<bool> IsTokenInvalidAsync(
 HttpResponseMessage response,
 HttpRequestMessage request,
 CancellationToken cancellationToken)
 {
 if (response.StatusCode == HttpStatusCode.Unauthorized)
 return true;

 var detectors = GetEffectiveTokenInvalidationDetectors();
 if (detectors.Count == 0)
 return false;

 try
 {
 ReadOnlyMemory<byte>? body = null;
 var bodyCaptured = false;

 foreach (var detector in detectors)
 {
// 预过滤在读取响应体之前：无关请求零捕获开销（与既有单槽语义一致）。
 if (!detector.ShouldInspect(request))
 continue;

 if (!bodyCaptured)
 {
 body = await TryCaptureResponseBodyAsync(response, cancellationToken).ConfigureAwait(false);
 bodyCaptured = true;
 }

 var invalid = await detector.IsTokenInvalidAsync(response, body, cancellationToken).ConfigureAwait(false);
 if (invalid)
 {
 MudHttpClientLog.TokenRecoveryTriggeredByDetector(_logger, (int)response.StatusCode);
 return true;
 }
 }

 return false;
 }
 catch (OperationCanceledException)
 {
 throw;
 }
 catch (Exception ex)
 {
// B5：异常判定器标识为"首个参与判定的判定器类型"（链路故障归因，与既有日志字段兼容）。
 MudHttpClientLog.TokenInvalidationDetectionFailed(_logger, detectors[0].GetType().Name, ex);
 return false;
 }
 }

/// <summary>
/// B5：汇总本次判定参与的全部判定器（单槽属性优先 + 追加式集合），保持稳定顺序。
/// </summary>
/// <remarks>
/// 无任何判定器时返回空列表（非 null）—— 使调用点可用 <c>Count == 0</c> 做零开销短路，
/// 并保证"两类来源皆空 ⇒ 仅认 401"的回归等价。
/// </remarks>
 private List<ITokenInvalidationDetector> GetEffectiveTokenInvalidationDetectors()
 {
 var primary = Options.TokenInvalidationDetector;
 var additional = Options.AdditionalTokenInvalidationDetectors;
 var additionalCount = additional?.Count ?? 0;

 if (primary is null && additionalCount == 0)
 return s_noDetectors;

 var detectors = new List<ITokenInvalidationDetector>(additionalCount + (primary is null ? 0 : 1));
 if (primary is not null)
 detectors.Add(primary);

 if (additionalCount > 0)
 {
 for (var i = 0; i < additionalCount; i++)
 {
 var detector = additional![i];
 if (detector is not null)
 detectors.Add(detector);
 }
 }

 return detectors;
 }

/// <summary>
/// 按需限量捕获响应体，供失效判定器读取业务错误码。
/// 捕获仅发生在「声明 Content-Length 且 ≤ <see cref="TokenRecoveryOptions.MaxCapturedResponseBodyBytes"/>」
/// 的响应上：全量读取后以 <see cref="ByteArrayContent"/>（复制除 Content-Length 外的内容头）替换原内容，
/// 调用方拿到的响应仍可正常读取。声明超限 / 未知长度（chunked）/ 空体一律不读流（返回 null），
/// 保证调用方响应流永不被部分消费污染；「部分捕获 + 组合流回卷」方案已评审否决。
/// </summary>
 private async ValueTask<ReadOnlyMemory<byte>?> TryCaptureResponseBodyAsync(
 HttpResponseMessage response, CancellationToken cancellationToken)
 {
 var content = response.Content;
 if (content is null)
 return null;

 var maxBytes = Options.MaxCapturedResponseBodyBytes;
 if (maxBytes <= 0)
 return null;

 if (content.Headers.ContentLength is not long declared || declared <= 0 || declared > maxBytes)
 return null;

#if NETSTANDARD2_0
 var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#else
 var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
 var buffered = new byte[declared];
 var total = 0;
 try
 {
 while (total < declared)
 {
#if NETSTANDARD2_0
 int read = await stream.ReadAsync(buffered, total, (int)declared - total).ConfigureAwait(false);
#else
 int read = await stream.ReadAsync(buffered.AsMemory(total, (int)declared - total), cancellationToken).ConfigureAwait(false);
#endif
 if (read <= 0)
 break;
 total += read;
 }
 }
 catch
 {
// R-P1-01：读流失败（取消 / IO 异常）时内容流已处于半消费状态，既无法继续读取也无法被替换，
// 必须显式释放以闭合底层连接，否则该连接滞留至 GC 才归还连接池。异常沿原路径上抛。
 content.Dispose();
 throw;
 }

 if (total <= 0)
 return null;

 var replacement = new ByteArrayContent(buffered, 0, total);
 foreach (var header in content.Headers)
 {
// 长度头由 ByteArrayContent 自行重新计算，避免「声明长度 ≠ 实际字节数」
 if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
 continue;
 replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
 }
 response.Content = replacement;
// 原内容已被整体消费并替换：显式释放，闭合底层连接流（防连接滞留至 GC 才归还连接池）
 content.Dispose();

 return buffered.AsMemory(0, total);
 }
}
