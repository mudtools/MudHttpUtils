// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026 
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Helpers;
using Mud.HttpUtils.Observability;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Mud.HttpUtils;
// 纯移动拆分（自 EnhancedHttpClient.cs 按职责拆出：流式）。
public abstract partial class EnhancedHttpClient
{
 #region IEnhancedHttpClient 接口实现

/// <inheritdoc cref="IBaseHttpClient.SendAsAsyncEnumerable{TResult}(HttpRequestMessage, object?, CancellationToken)"/>
#if NET8_0_OR_GREATER
 [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
 Justification = "通过注入的 IHttpContentSerializer（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全。")]
#endif
 public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
 HttpRequestMessage request,
 object? jsonSerializerOptions = null,
 [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
 {
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

// 可观测性：设置 client_name 供下游 DelegatingHandler 读取
 MudHttpObservability.SetClientName(request, ClientName);

// 创建日志作用域（CorrelationId / ClientName / Method / Host）
 using var scope = _enableLogging
 ? MudHttpObservability.CreateLoggerScope(_logger, request, ClientName)
 : null;

// 若已被更外层观察窗口采集，则不重复创建 Activity/指标（去重）。
// 去重协议（标记先行）：通过检查即立即标记，使内层 Handler 与重试克隆短路。
 var alreadyObserved = MudHttpObservability.IsObserved(request);
 if (!alreadyObserved)
 MudHttpObservability.MarkObserved(request);
 var activity = alreadyObserved ? null : MudHttpObservability.StartRequestActivity(request, ClientName);
 var sw = alreadyObserved ? default : ValueStopwatch.StartNew();
 var recordedSuccess = false;
// M2-#13：三态 —— 真异常才记 error；break/提前退出与完整枚举一样记成功
 Exception? pendingException = null;
 HttpResponseMessage? response = null;

// 路径 B 兜底：发出 RequestStarted 事件，与 TracingDelegatingHandler 路径 A 保持一致
// （G28：门控前移到调用点，关闭状态下不构造工厂）
 if (!alreadyObserved && MudHttpActivitySource.EventsEnabled)
 {
 MudHttpActivitySource.AddActivityEvent(
 MudHttpDiagnosticNames.RequestStarted,
 () => new HttpRequestDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName),
 MudHttpDiagnosticNames.RequestStarted,
 () => new[]
 {
 new KeyValuePair<string, object?>("method", request.Method.Method),
 new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
 new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
 });
 }

 try
 {
 LogOperation("发送流式异步枚举请求", uri);

 Stream stream;
 try
 {
 response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

 await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

// 流式响应需要在响应头到达时即记录状态码（响应体可能在 Dispose 后才读取）
 MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
 MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

#if NETSTANDARD2_0
 stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
 stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
 }
// Phase 2：ApiException 继承 HttpRequestException，须在前者之前捕获
 catch (ApiException ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw;
 }
 catch (HttpRequestException ex)
 {
 pendingException = ex;
#if !NETSTANDARD2_0
 var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
 _logger.HttpRequestFailedWithStatusCode(uri, statusCode, ex);
#else
 _logger.HttpRequestFailedSimple(uri, ex);
#endif
 throw;
 }
 catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
 {
 pendingException = ex;
 _logger.HttpRequestTimeout(uri, _httpClient.Timeout.TotalSeconds, ex);
 throw new ApiRequestException($"请求超时: {uri}", ex, isTimeout: true, requestUri: uri);
 }
 catch (TaskCanceledException ex)
 {
 pendingException = ex;
 _logger.HttpRequestCancelled(uri, ex);
 throw;
 }
// /纯 OperationCanceledException（非 TaskCanceledException）表示请求被取消，
// 直接重抛避免被下面的 catch-all 包装为 HttpRequestException
 catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
 {
 pendingException = ex;
 _logger.HttpRequestCancelled(uri, ex);
 throw;
 }
// /与 ExecuteHttpRequestCoreAsync（L750-768）保持一致，
// 保留 JsonException 与 InvalidOperationException 原始类型，使流式与非流式路径的异常处理模式统一。
// 原实现将所有非 HttpRequestException/OCE 异常包装为 HttpRequestException，
// 调用方无法用统一的 catch (JsonException) 模式处理流式与非流式响应。
 catch (JsonException ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw;
 }
 catch (InvalidOperationException ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw;
 }
 catch (Exception ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw new ApiRequestException($"HTTP请求处理失败: {uri}", ex, requestUri: uri);
 }

 var options = jsonSerializerOptions;

// M2-#13：手动驱动枚举器 —— 解析阶段的异常在局部 catch（无 yield）中捕获，
// 使 finally 能区分"真异常"与"调用方 break 提前退出"（后者按成功记录）。
// （迭代器方法限制：带 catch 的 try 块体内禁止 yield，故不能用 await foreach + 外层 catch。）
 await using var enumerator = ParseNdJsonStreamAsync<TResult>(stream, options, _contentSerializer, cancellationToken)
 .GetAsyncEnumerator(cancellationToken);
 while (true)
 {
 bool hasNext;
 try
 {
 hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
 }
 catch (Exception ex)
 {
 pendingException = ex;
 throw;
 }
 if (!hasNext)
 break;
 yield return enumerator.Current;
 }

 LogOperation("流式异步枚举请求完成", uri);
 recordedSuccess = true;
 }
 finally
 {
 response?.Dispose();

// 记录可观测性指标（仅在未被 DelegatingHandler 采集时）
 if (!alreadyObserved)
 {
 var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
// M2-#13：三态 —— faulted 记 error（真实异常）；完整枚举与提前退出（break）都记成功
// G32/取消与 4xx ApiException 语义校准（同 ExecuteWithObservabilityAsync 三态）；
// RequestFailed 事件在三态下均保持发出（事件成对性，G19 教训）
 if (pendingException is OperationCanceledException && cancellationToken.IsCancellationRequested)
 {
 MudHttpObservability.RecordCancellation(activity, elapsedMs, ClientName, request);
 }
 else if (pendingException is ApiException apiEx && (int)apiEx.StatusCode >= 400 && (int)apiEx.StatusCode < 500)
 {
 MudHttpObservability.RecordOutcomeFromStatusCode(activity, (int)apiEx.StatusCode, elapsedMs, ClientName, request);
 }
 else if (pendingException != null)
 {
 MudHttpObservability.RecordError(
 activity,
 pendingException,
 elapsedMs,
 ClientName,
 request);
 }
 else
 {
 MudHttpObservability.RecordSuccessFromRequest(activity, request, elapsedMs, ClientName);

// RequestStopped 事件（recordedSuccess 区分完整枚举与提前退出，仅作为事件 tag，非指标维度）
 int statusCode = 0;
 if (MudHttpObservability.TryGetProperty(request, MudHttpObservability.StatusCodePropertyKey, out var sc) && sc is int code)
 statusCode = code;
 if (MudHttpActivitySource.EventsEnabled)
 {
 MudHttpActivitySource.AddActivityEvent(
 MudHttpDiagnosticNames.RequestStopped,
 () => new HttpResponseDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, statusCode, elapsedMs),
 MudHttpDiagnosticNames.RequestStopped,
 () => new[]
 {
 new KeyValuePair<string, object?>("method", request.Method.Method),
 new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
 new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
 new KeyValuePair<string, object?>("status_code", statusCode),
 new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
 new KeyValuePair<string, object?>("stream_completed", recordedSuccess),
 });
 }
 }

// RequestFailed 事件（取消 / 4xx / 真实异常三态均发出，异常类型与实际抛出一致）
 if (pendingException != null && MudHttpActivitySource.EventsEnabled)
 {
 MudHttpActivitySource.AddActivityEvent(
 MudHttpDiagnosticNames.RequestFailed,
 () => new HttpRequestErrorDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, elapsedMs, pendingException),
 MudHttpDiagnosticNames.RequestFailed,
 () => new[]
 {
 new KeyValuePair<string, object?>("method", request.Method.Method),
 new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
 new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
 new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
 new KeyValuePair<string, object?>("exception_type", pendingException.GetType().Name),
 });
 }
 MudHttpObservability.MarkObserved(request);
 }

 activity?.Dispose();
 }
 }
 #endregion

#if NET8_0_OR_GREATER
/// <inheritdoc cref="IBaseHttpClient.SendAsAsyncEnumerable{TResult}(HttpRequestMessage, System.Text.Json.Serialization.Metadata.JsonTypeInfo{TResult}, CancellationToken)"/>
/// <remarks>
/// 此重载使用 <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{TResult}"/> 进行 AOT 安全的流式反序列化，
/// 不依赖开放泛型反射。可观测性/日志/异常处理与非泛型重载一致。
/// </remarks>
 public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
 HttpRequestMessage request,
 System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> jsonTypeInfo,
 [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
 {
 if (request == null)
 throw new ArgumentNullException(nameof(request));
 if (jsonTypeInfo == null)
 throw new ArgumentNullException(nameof(jsonTypeInfo));

 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

// 可观测性：设置 client_name 供下游 DelegatingHandler 读取
 MudHttpObservability.SetClientName(request, ClientName);

// 创建日志作用域（CorrelationId / ClientName / Method / Host）
 using var scope = _enableLogging
 ? MudHttpObservability.CreateLoggerScope(_logger, request, ClientName)
 : null;

// 若已被更外层观察窗口采集，则不重复创建 Activity/指标（去重）。
// 去重协议（标记先行）：通过检查即立即标记，使内层 Handler 与重试克隆短路。
 var alreadyObserved = MudHttpObservability.IsObserved(request);
 if (!alreadyObserved)
 MudHttpObservability.MarkObserved(request);
 var activity = alreadyObserved ? null : MudHttpObservability.StartRequestActivity(request, ClientName);
 var sw = alreadyObserved ? default : ValueStopwatch.StartNew();
 var recordedSuccess = false;
// M2-#13：三态 —— 真异常才记 error；break/提前退出与完整枚举一样记成功
 Exception? pendingException = null;
 HttpResponseMessage? response = null;

// 路径 B 兜底：发出 RequestStarted 事件（G28：门控前移到调用点）
 if (!alreadyObserved && MudHttpActivitySource.EventsEnabled)
 {
 MudHttpActivitySource.AddActivityEvent(
 MudHttpDiagnosticNames.RequestStarted,
 () => new HttpRequestDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName),
 MudHttpDiagnosticNames.RequestStarted,
 () => new[]
 {
 new KeyValuePair<string, object?>("method", request.Method.Method),
 new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
 new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
 });
 }

 try
 {
 LogOperation("发送流式异步枚举请求（AOT 安全）", uri);

 Stream stream;
 try
 {
 response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

 await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

 MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
 MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

 stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
 }
 catch (ApiException ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw;
 }
 catch (HttpRequestException ex)
 {
 pendingException = ex;
 var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
 _logger.HttpRequestFailedWithStatusCode(uri, statusCode, ex);
 throw;
 }
 catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
 {
 pendingException = ex;
 _logger.HttpRequestTimeout(uri, _httpClient.Timeout.TotalSeconds, ex);
 throw new ApiRequestException($"请求超时: {uri}", ex, isTimeout: true, requestUri: uri);
 }
 catch (TaskCanceledException ex)
 {
 pendingException = ex;
 _logger.HttpRequestCancelled(uri, ex);
 throw;
 }
 catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
 {
 pendingException = ex;
 _logger.HttpRequestCancelled(uri, ex);
 throw;
 }
 catch (JsonException ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw;
 }
 catch (InvalidOperationException ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw;
 }
 catch (Exception ex)
 {
 pendingException = ex;
 _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
 throw new ApiRequestException($"HTTP请求处理失败: {uri}", ex, requestUri: uri);
 }

// M2-#13：手动驱动枚举器 —— 解析阶段异常在局部 catch（无 yield）中捕获，与第一个重载同构
 await using var aotEnumerator = ParseNdJsonStreamAsync(stream, jsonTypeInfo, _contentSerializer, cancellationToken)
 .GetAsyncEnumerator(cancellationToken);
 while (true)
 {
 bool hasNext;
 try
 {
 hasNext = await aotEnumerator.MoveNextAsync().ConfigureAwait(false);
 }
 catch (Exception ex)
 {
 pendingException = ex;
 throw;
 }
 if (!hasNext)
 break;
 yield return aotEnumerator.Current;
 }

 LogOperation("流式异步枚举请求完成（AOT 安全）", uri);
 recordedSuccess = true;
 }
 finally
 {
 response?.Dispose();

 if (!alreadyObserved)
 {
 var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
// G32/取消与 4xx ApiException 语义校准（同第一个重载三态）；
// RequestFailed 事件在三态下均保持发出（事件成对性，G19 教训）
 if (pendingException is OperationCanceledException && cancellationToken.IsCancellationRequested)
 {
 MudHttpObservability.RecordCancellation(activity, elapsedMs, ClientName, request);
 }
 else if (pendingException is ApiException apiEx && (int)apiEx.StatusCode >= 400 && (int)apiEx.StatusCode < 500)
 {
 MudHttpObservability.RecordOutcomeFromStatusCode(activity, (int)apiEx.StatusCode, elapsedMs, ClientName, request);
 }
 else if (pendingException != null)
 {
 MudHttpObservability.RecordError(
 activity,
 pendingException,
 elapsedMs,
 ClientName,
 request);
 }
 else
 {
 MudHttpObservability.RecordSuccessFromRequest(activity, request, elapsedMs, ClientName);

 int statusCode = 0;
 if (MudHttpObservability.TryGetProperty(request, MudHttpObservability.StatusCodePropertyKey, out var sc) && sc is int code)
 statusCode = code;
 if (MudHttpActivitySource.EventsEnabled)
 {
 MudHttpActivitySource.AddActivityEvent(
 MudHttpDiagnosticNames.RequestStopped,
 () => new HttpResponseDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, statusCode, elapsedMs),
 MudHttpDiagnosticNames.RequestStopped,
 () => new[]
 {
 new KeyValuePair<string, object?>("method", request.Method.Method),
 new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
 new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
 new KeyValuePair<string, object?>("status_code", statusCode),
 new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
 new KeyValuePair<string, object?>("stream_completed", recordedSuccess),
 });
 }
 }

// RequestFailed 事件（取消 / 4xx / 真实异常三态均发出）
 if (pendingException != null && MudHttpActivitySource.EventsEnabled)
 {
 MudHttpActivitySource.AddActivityEvent(
 MudHttpDiagnosticNames.RequestFailed,
 () => new HttpRequestErrorDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, elapsedMs, pendingException),
 MudHttpDiagnosticNames.RequestFailed,
 () => new[]
 {
 new KeyValuePair<string, object?>("method", request.Method.Method),
 new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
 new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
 new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
 new KeyValuePair<string, object?>("exception_type", pendingException.GetType().Name),
 });
 }
 MudHttpObservability.MarkObserved(request);
 }

 activity?.Dispose();
 }
 }
#endif

 #region NDJSON 流式解析

/// <summary>
/// 从 NDJSON 流中逐行解析并异步枚举结果。
/// </summary>
/// <typeparam name="T">每行数据反序列化的目标类型。</typeparam>
/// <param name="stream">包含 NDJSON 内容的流。</param>
/// <param name="options">JSON 序列化选项；为 null 时使用 <see cref="JsonSerializer"/> 默认选项。</param>
/// <param name="contentSerializer">HTTP 内容序列化器，负责逐行反序列化。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>流式返回的异步枚举。</returns>
/// <remarks>
/// 此方法使用 <see cref="StreamReader"/> 读取流。调用方应负责释放底层 <paramref name="stream"/>（StreamReader 释放时也会释放流，重复释放是幂等的）。
/// <para>
/// <b>Native AOT 注意</b>：此重载使用开放泛型 <c>JsonSerializer.Deserialize&lt;T&gt;</c>，
/// AOT 场景下须确保 <typeparamref name="T"/> 已在 <c>JsonSerializerContext</c> 中声明，
/// 否则可能静默返回空对象。推荐使用接收 <c>JsonTypeInfo&lt;T&gt;</c> 的 <c>ParseNdJsonStreamAsync</c> 重载。
/// </para>
/// </remarks>
#if NET8_0_OR_GREATER
 [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("NDJSON 反序列化使用开放泛型 JsonSerializer.Deserialize<T>，AOT 场景须确保 T 已在 JsonSerializerContext 中声明。推荐使用 JsonTypeInfo<T> 重载。")]
#endif
 internal static async IAsyncEnumerable<T> ParseNdJsonStreamAsync<T>(
 Stream stream,
 object? options,
 IHttpContentSerializer contentSerializer,
 [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
 {
 using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

 while (true)
 {
#if NET7_0_OR_GREATER
 var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#else
 cancellationToken.ThrowIfCancellationRequested();
 var line = await reader.ReadLineAsync().ConfigureAwait(false);
#endif
 if (line == null)
 {
 yield break;
 }

 if (string.IsNullOrEmpty(line))
 {
 continue;
 }

 var item = contentSerializer.Deserialize<T>(line, options);
 if (item != null)
 {
 yield return item;
 }
 }
 }

#if NET8_0_OR_GREATER
/// <summary>
/// 从 NDJSON 流中逐行解析并异步枚举结果（AOT 安全重载）。
/// </summary>
/// <typeparam name="T">每行数据反序列化的目标类型。</typeparam>
/// <param name="stream">包含 NDJSON 内容的流。</param>
/// <param name="jsonTypeInfo">来自 <c>JsonSerializerContext</c> 的类型信息（AOT 安全）。</param>
/// <param name="contentSerializer">HTTP 内容序列化器，负责逐行反序列化。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>流式返回的异步枚举。</returns>
 internal static async IAsyncEnumerable<T> ParseNdJsonStreamAsync<T>(
 Stream stream,
 System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
 IHttpContentSerializer contentSerializer,
 [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
 {
 using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

 while (true)
 {
 var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
 if (line == null)
 {
 yield break;
 }

 if (string.IsNullOrEmpty(line))
 {
 continue;
 }

 var item = contentSerializer.Deserialize<T>(line, jsonTypeInfo);
 if (item != null)
 {
 yield return item;
 }
 }
 }
#endif

 #endregion
}
