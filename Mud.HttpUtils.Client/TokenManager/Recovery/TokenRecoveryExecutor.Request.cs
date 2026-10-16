// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026 
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// 纯移动拆分（自 TokenRecoveryExecutor.cs 按职责拆出：请求重建/缓冲/守卫）。

using Mud.HttpUtils.Helpers;

namespace Mud.HttpUtils;

public partial class TokenRecoveryExecutor
{
/// <summary>
/// 是否进入恢复链路：显式恢复上下文即视为调用方已声明需要恢复；否则要求携带 Authorization 头。
/// </summary>
 private bool ShouldAttemptRecovery(HttpRequestMessage request, TokenRecoveryContext? recoveryContext)
 {
 if (Options.RecoveryMaxRetries <= 0)
 return false;

 if (recoveryContext != null)
 return true;

 return request.Headers.Authorization != null;
 }

/// <summary>
/// 幂等门控 —— 该请求是否允许执行 401 重放。
/// </summary>
/// <remarks>
/// <para>
/// 放行条件（任一满足）：① 全局 <see cref="TokenRecoveryOptions.AllowNonIdempotentRecovery"/>；
/// ② 方法属于幂等白名单（见 <see cref="RetryableMethodPolicy"/>）；
/// ③ 契约级显式放行 <see cref="TokenRecoveryContext.IsRetryAllowedExplicitly"/>。
/// </para>
/// <para>
/// 语义与 Resilience 侧 <c>RetryGuard</c> 对齐：重试（含恢复重放）只对可安全重复执行的方法开放。
/// </para>
/// </remarks>
 private bool IsRecoveryAllowedForRequest(HttpRequestMessage request, TokenRecoveryContext? recoveryContext)
 {
 if (Options.AllowNonIdempotentRecovery)
 return true;

 if (recoveryContext?.IsRetryAllowedExplicitly == true)
 return true;

 return RetryableMethodPolicy.IsRetryable(request.Method);
 }

/// <summary>
/// 读取请求上的恢复上下文。：实现已下沉到 <see cref="TokenRecoveryContext.FromRequest"/>
/// （恢复执行器与 URL 脱敏共用同一读取口径）。
/// </summary>
 private static TokenRecoveryContext? GetRecoveryContext(HttpRequestMessage request)
 => TokenRecoveryContext.FromRequest(request);

/// <summary>
/// 恢复日志用脱敏——Query 注入模式下对已知令牌参数名做无条件掩码（不依赖全局开关与词表）。
/// </summary>
 private static string RedactForLog(HttpRequestMessage request, TokenRecoveryContext? ctx)
 {
 var url = request.RequestUri?.ToString() ?? string.Empty;
 if (ctx?.InjectionMode != TokenInjectionMode.Query || string.IsNullOrEmpty(ctx.QueryParameterName))
 return SensitiveUrlRedactor.Redact(url);

 var qIndex = url.IndexOf('?');
 if (qIndex < 0 || qIndex == url.Length - 1) return SensitiveUrlRedactor.Redact(url);

 var head = url.Substring(0, qIndex + 1);
 var masked = string.Join("&", url.Substring(qIndex + 1).Split('&').Select(p =>
 {
 var eq = p.IndexOf('=');
 if (eq < 0) return p;
 return string.Equals(Uri.UnescapeDataString(p.Substring(0, eq)), ctx.QueryParameterName, StringComparison.Ordinal)
 ? p.Substring(0, eq) + "=***REDACTED***" : p;
 }));
 return head + masked;
 }

/// <summary>
/// 令牌值净化——CR/LF 一律拒绝（防 header 注入）。
/// 扩展为拒绝所有 C0 控制字符（含 \0）与 DEL，避免 FormatException 诊断歧义。
/// </summary>
 private static bool IsSafeTokenValue(string? token)
 {
 if (token is null || token.Length == 0) return false;
 foreach (var c in token) if (c < 0x20 || c == 0x7F) return false;
 return true;
 }

 private bool ApplyTokenToRequest(HttpRequestMessage request, string token, TokenRecoveryContext? context)
 {
 if (context == null)
 {
 request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(Options.TokenScheme, token);
 return true;
 }

 switch (context.InjectionMode)
 {
 case TokenInjectionMode.Header:
// 注入前经共享校验器复核（netstandard2.0 的 HttpClient 不校验头值，
// 直接 Add 会把控制字符写入报文）。不合格返回 false → 调用方记 TokenInjectionUnsupported 并返回真实 401。
 if (!HttpHeaderValueValidator.IsValid(token)) return false;
 request.Headers.Remove(context.HeaderName);
 if (context.HeaderName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
 {
 request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(context.TokenScheme, token);
 }
 else
 {
 request.Headers.Add(context.HeaderName, token);
 }
 return true;

 case TokenInjectionMode.ApiKey:
// 同 Header 路径，注入前复核头值合法性。
 if (!HttpHeaderValueValidator.IsValid(token)) return false;
 request.Headers.Remove(context.HeaderName);
 request.Headers.Add(context.HeaderName, token);
 return true;

 case TokenInjectionMode.BasicAuth:
 var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
 request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicCredentials);
 return true;

 case TokenInjectionMode.Cookie:
 var cookieName = !string.IsNullOrEmpty(context.CookieName) ? context.CookieName : "access_token";
// Cookie 值按 RFC 6265 编码，防 ';' / 空格注入额外属性
 var cookieValue = $"{cookieName}={Uri.EscapeDataString(token)}";
 var existingCookies = request.Headers.Contains("Cookie")
 ? string.Join("; ", request.Headers.GetValues("Cookie"))
 : null;
 request.Headers.Remove("Cookie");
 if (existingCookies != null)
 {
 var otherCookies = existingCookies
 .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
 .Select(c => c.Trim())
 .Where(c => !c.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase))
 .ToList();
 if (otherCookies.Count > 0)
 otherCookies.Add(cookieValue);
 else
 otherCookies = null;
 request.Headers.Add("Cookie", otherCookies != null ? string.Join("; ", otherCookies) : cookieValue);
 }
 else
 {
 request.Headers.Add("Cookie", cookieValue);
 }
 return true;

 case TokenInjectionMode.Query:
 if (string.IsNullOrEmpty(context.QueryParameterName))
 {
 MudHttpClientLog.TokenInjectionUnsupported(_logger, "Query:MissingParameterName");
 return false;
 }

 var queryUri = request.RequestUri;
 if (queryUri != null)
 {
 var newQuery = ReplaceQueryParameter(queryUri.Query, context.QueryParameterName!, token);
 var builder = new UriBuilder(queryUri) { Query = newQuery };
 request.RequestUri = builder.Uri;
 return true;
 }

 MudHttpClientLog.TokenInjectionUnsupported(_logger, "Query:NullUri");
 return false;

 case TokenInjectionMode.Path:
 MudHttpClientLog.TokenInjectionUnsupported(_logger, $"Path:{context.InjectionMode}");
 return false;

 case TokenInjectionMode.HmacSignature:
 default:
 MudHttpClientLog.TokenInjectionUnsupported(_logger, context.InjectionMode.ToString());
 return false;
 }
 }

/// <summary>
/// 判断内容是否"内存型"（数据已在内存中，可反复读取，无需（也无需额外）缓冲）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StringContent"/> 与 <see cref="System.Net.Http.FormUrlEncodedContent"/> 均派生自
/// <see cref="ByteArrayContent"/>，此处显式列出仅为表达意图（可读性），不影响判定结果。
/// </para>
/// <para>
/// <c>System.Net.Http.Json</c> 系内容（<c>JsonContent</c> / <c>JsonStringContent</c> 等）为 internal 实现，
/// 无法按类型引用，故按命名空间前缀判定 —— 它们同样把已序列化字节驻留内存。
/// </para>
/// </remarks>
 private static bool IsMemoryBackedContent(HttpContent? content)
 {
 if (content is null)
 return false;

 if (content is ByteArrayContent or StringContent or FormUrlEncodedContent)
 return true;

 var ns = content.GetType().Namespace;
 return ns != null && ns.StartsWith("System.Net.Http.Json", StringComparison.Ordinal);
 }

/// <summary>
/// 惰性读取内存型内容的字节（仅在确实要恢复时调用）。
/// </summary>
/// <remarks>
/// 内存型内容在首次发送后仍可重复读取，故无需在发送前预读 —— 使<b>正常请求（无 401）零成本</b>。
/// </remarks>
 private static async Task<byte[]> ReadMemoryBackedContentAsync(
 HttpContent content, CancellationToken cancellationToken)
 {
#if NETSTANDARD2_0
// netstandard2.0 无 ReadAsByteArrayAsync(CancellationToken) 重载（编译期可发现）。
 return await content.ReadAsByteArrayAsync().ConfigureAwait(false);
#else
 return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#endif
 }

/// <summary>
/// D1 三态体处理模型：读取阶段限量缓冲请求体。峰值内存 ≤ maxBytes + CopyBufferSize。
/// </summary>
/// <remarks>
/// <para>
/// <b>修复 B8 首发送截断）</b>：未声明长度（chunked）且非内存型的内容<b>一律不预读</b>。
/// 原实现在此会读取真实流：一旦读取过程中超限即弃置缓冲并返回 null，而 <c>request.Content</c> 未被替换
/// ⇒ 流已被部分消费，<b>首次发送即被截断</b>（对直接暴露不可寻址流的自定义 <see cref="HttpContent"/> 尤为明显）。
/// 修复后此类请求体放弃 401 重放能力，但首次发送完整（该取舍已写入迁移说明）。
/// </para>
/// <para>
/// <b>消除无谓代价）</b>：内存型内容交由恢复时的惰性读取处理（返回 null），
/// 避免为低频 401 让全部正常请求承担 <c>MemoryStream</c> + <c>ToArray()</c> 双份拷贝；
/// 流式内容的 <see cref="MemoryStream"/> 容量按<b>实际声明长度</b>估算（原实现未声明长度时预分配 maxBytes = 1MB）。
/// </para>
/// <para>
/// 声明超限（Content-Length &gt; maxBytes）零缓冲直接返回 null；返回 null 时调用方仍正常发送原内容。
/// </para>
/// </remarks>
 private const int CopyBufferSize = 8192;

 private async Task<byte[]?> TryBufferContentAsync(
 HttpContent content, long maxBytes, CancellationToken cancellationToken)
 {
 var declared = content.Headers.ContentLength;

// 未声明长度且非内存型 → 不预读真实流（见上方 remarks 的 B8 说明）。
 if (declared is null && !IsMemoryBackedContent(content))
 return null;

 if (declared is long d && d > maxBytes)
 return null; // 声明超限：零缓冲

// 内存型内容不做预缓冲 —— 原实例可反复读取，交由恢复路径惰性读取。
 if (IsMemoryBackedContent(content))
 return null;

// MemoryOnly：完全不触碰流式内容（高吞吐上传场景）；
// 走到这里的内容必为"已声明长度且未超限"的流式内容（Auto 模式）。
 if (Options.BufferingMode == RequestBodyBufferingMode.MemoryOnly)
 return null;

#if NETSTANDARD2_0
 var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#else
 var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
// 注意：不 dispose 该流 —— ReadAsStreamAsync 返回的是 HttpContent 自有流，
// 生命周期归 HttpContent 所有（与 LimitedContentReader 同一惯例）。
// 容量按实际声明长度估算（declared 此处必为有值），不再回退到 maxBytes。
 var capacity = (int)Math.Min(declared!.Value, maxBytes);
 using var buffered = capacity > 0
 ? new MemoryStream(capacity)
 : new MemoryStream();
 var buffer = new byte[CopyBufferSize];
 while (true)
 {
#if NETSTANDARD2_0
 int read = await stream.ReadAsync(buffer, 0, CopyBufferSize).ConfigureAwait(false);
#else
 int read = await stream.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false);
#endif
 if (read <= 0)
 break;
 buffered.Write(buffer, 0, read);
 if (buffered.Length > maxBytes)
 {
// 声明长度与实际不符（服务端内容被压缩/拼装）：已消费部分无法回卷。
// 此处不再"静默返回 null 让调用方发送残缺体"——调用方据 bodyReplayable 判定
// 时同样会得到 false（contentBytes == null 且非内存型）→ 返回真实 401，重新发送交由业务层决定。
 buffered.SetLength(0); // 立即弃置已缓冲数据
 return null;
 }
 }
 return buffered.ToArray();
 }

 private static HttpRequestMessage BuildRetryRequest(HttpRequestMessage original, byte[]? contentBytes, TokenRecoveryContext? recoveryContext)
 {
 var clone = new HttpRequestMessage(original.Method, original.RequestUri);

// /复制 Version 和 VersionPolicy
 clone.Version = original.Version;
#if !NETSTANDARD2_0
 clone.VersionPolicy = original.VersionPolicy;
#endif

 if (contentBytes != null) // 允许长度 0，保持 Content-Type 等体头
 {
 clone.Content = new ByteArrayContent(contentBytes);

 foreach (var header in original.Content!.Headers)
 {
 clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
 }
 }

 foreach (var header in original.Headers)
 {
 if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
 continue;

 var recoveryContextHeader = recoveryContext?.HeaderName;
 if (recoveryContextHeader != null && header.Key.Equals(recoveryContextHeader, StringComparison.OrdinalIgnoreCase))
 continue;

// Cookie 模式下仍需复制原始 Cookie 头，ApplyTokenToRequest 会保留非目标 Cookie 并替换目标 Cookie
 if (recoveryContext?.InjectionMode == TokenInjectionMode.Cookie && header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
 {
 clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
 continue;
 }

 clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
 }

// /跳过 __mud_* 可观测性内部属性，让重试请求被独立采集为新的 Trace/Metric。
// 原实现复制所有 Properties/Options，导致：
// 1. TracingDelegatingHandler 检测到 __mud_observed=true 跳过 Activity 创建，重试请求无独立 Trace
// 2. ExecuteWithObservabilityAsync 检测到已 observed 跳过指标采集，重试耗时与状态码不计入指标
// 3. __mud_status_code 被覆盖，Jaeger 中看不到 401→200 的恢复轨迹
// 业务属性（如 TokenRecoveryContext）不受 __mud_ 前缀限制，仍正常复制。
// 兼容 netstandard2.0 与历史写入路径：Properties 已过时但仍需按原语义复制非 __mud_ 属性。
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时
 foreach (var property in original.Properties)
 {
 if (property.Key.StartsWith("__mud_", StringComparison.Ordinal))
 continue;
 clone.Properties.Add(property);
 }
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时


#if !NETSTANDARD2_0
 foreach (var option in original.Options)
 {
 if (option.Key.StartsWith("__mud_", StringComparison.Ordinal))
 continue;
 clone.Options.TryAdd(option.Key, option.Value);
 }
#endif

 return clone;
 }

/// <summary>
 // /TK-~redirect）跨主机校验：比较原始请求与重试请求的 scheme + host + port 是否一致。
/// 不一致说明发生了重定向到不受信任的地址，恢复流程应放弃，避免把刷新令牌注入到第三方主机。
/// 任一 URI 为 null 时不认为相同。
/// </summary>
 private static bool IsSameHost(Uri? original, Uri? retry)
 {
 if (original == null || retry == null)
 return original == null && retry == null;

 return string.Equals(original.Scheme, retry.Scheme, StringComparison.OrdinalIgnoreCase)
 && string.Equals(original.Host, retry.Host, StringComparison.OrdinalIgnoreCase)
 && original.Port == retry.Port;
 }

/// <summary>
 // /说明：原 <c>WaitForTaskAsync</c>（等待者在自身 CT 取消时提前脱离共享刷新任务）已随
/// 「条目即任务」改造一并移除 —— 现在所有等待者与赢者 await 同一个 <see cref="Task"/>，
/// 共享刷新不受任一调用方取消影响（取消隔离语义保持一致），异常也必然被观察。
/// 移除后同时消除了 ns2.0 路径 <c>Task.Delay(Timeout.Infinite, ct)</c> 的取消注册滞留
/// </summary>
 private static string ReplaceQueryParameter(string? queryString, string paramName, string newValue)
 {
 var query = queryString ?? "";
 if (query.StartsWith("?"))
 query = query.Substring(1);

 var parameters = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
 .Select(p =>
 {
 var idx = p.IndexOf('=');
 if (idx < 0) return new { Key = Uri.UnescapeDataString(p), Value = "" };
 return new { Key = Uri.UnescapeDataString(p.Substring(0, idx)), Value = Uri.UnescapeDataString(p.Substring(idx + 1)) };
 })
 .ToList();

 var found = false;
 for (var i = 0; i < parameters.Count; i++)
 {
 if (parameters[i].Key == paramName)
 {
 parameters[i] = new { Key = paramName, Value = newValue };
 found = true;
 break;
 }
 }

 if (!found)
 {
 parameters.Add(new { Key = paramName, Value = newValue });
 }

 return string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
 }
}
