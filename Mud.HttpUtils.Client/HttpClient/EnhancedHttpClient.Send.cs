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
// 纯移动拆分（自 EnhancedHttpClient.cs 按职责拆出：发送）。
public abstract partial class EnhancedHttpClient
{
/// <summary>
/// 核心HTTP请求发送方法。子类可重写此方法以添加令牌恢复、请求重试等横切关注点。
/// </summary>
/// <remarks>
/// 所有公共 HTTP 方法（<see cref="SendAsync{TResult}"/>、<see cref="SendRawAsync"/>、
/// <see cref="DownloadAsync"/>、<see cref="SendStreamAsync"/>、<c>SendAsAsyncEnumerable&lt;TResult&gt;</c> 等）
/// 最终都通过此方法发送 HTTP 请求。子类重写后可拦截所有请求路径。
/// </remarks>
/// <param name="request">HTTP请求消息</param>
/// <param name="completionOption">响应读取选项</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>HTTP响应消息</returns>
 protected virtual async Task<HttpResponseMessage> SendCoreAsync(
 HttpRequestMessage request,
 HttpCompletionOption completionOption,
 CancellationToken cancellationToken)
 {
// 主链路 handler 已关闭自动重定向（AllowAutoRedirect=false，见
// ServiceCollectionExtensions / SsrfSafeSocketsHttpHandler），此处以手动逐跳循环承接重定向语义，
// 并对每个目标执行全套 URL 复验（UrlValidator.ValidateUrlAsync：白名单 / HTTPS / 私网 / 内网域名），
// 根治「自动重定向跨主机不复验」的 SSRF 绕过面。
// 语义对齐 .NET RedirectHandler：301/302/303 非幂等方法降级为 GET 并丢弃请求体；
// 307/308 保留方法与请求体（仅内存型白名单内容可重放，与 克隆白名单同口径，
// 不可重放则中止抛出 —— 失败快于静默空体）；每跳剥离 Authorization / Cookie；
// 跳数上限 10，超限抛 InvalidOperationException。
 var currentUri = request.RequestUri ?? _httpClient.BaseAddress;
 var currentMethod = request.Method;
 var redirectHops = 0;
 var isRedirect = false;

 while (true)
 {
 var outgoing = request;
 if (isRedirect)
 {
 outgoing = new HttpRequestMessage(currentMethod, currentUri)
 {
 Version = request.Version,
#if NET5_0_OR_GREATER
 VersionPolicy = request.VersionPolicy,
#endif
 };
 foreach (var header in request.Headers)
 outgoing.Headers.TryAddWithoutValidation(header.Key, header.Value);
// 凭据剥离（口径对齐 .NET 内建跨源剥离，扩展为逐跳剥离）
 outgoing.Headers.Authorization = null;
 outgoing.Headers.Remove("Authorization");
 outgoing.Headers.Remove("Cookie");
 CopyRequestState(request, outgoing);

 if (currentMethod == HttpMethod.Get || currentMethod == HttpMethod.Head)
 {
// 降级/无体方法：不携带内容
 }
 else if (IsReplayableRequestContent(request.Content))
 {
// 共享内存型内容实例（ByteArray 家族每次发送生成新流，可安全重放）
 outgoing.Content = request.Content;
 }
 }

 HttpResponseMessage response;
 try
 {
 response = await _httpClient.SendAsync(outgoing, completionOption, cancellationToken).ConfigureAwait(false);
 }
 catch
 {
 if (isRedirect)
 outgoing.Dispose();
 throw;
 }

 if (isRedirect)
 {
// 解除对共享内容的引用后再释放，避免触达调用方的 HttpRequestMessage.Content
 outgoing.Content = null;
 outgoing.Dispose();
 }

 if (currentUri == null ||
 !IsRedirectStatus(response.StatusCode) ||
 response.Headers.Location == null)
 {
 return response;
 }

 if (++redirectHops > MaxRedirectHops)
 {
 response.Dispose();
 throw new InvalidOperationException(
 $"重定向次数超过上限（{MaxRedirectHops} 跳）: {currentUri}");
 }

 var nextUri = new Uri(currentUri, response.Headers.Location);

// 逐跳复验：与初始请求 ValidateRequestAsync 同源的全套校验（白名单 / HTTPS / 私网 / 内网域名）
 await UrlValidator.ValidateUrlAsync(nextUri.ToString(), _allowCustomBaseUrls, cancellationToken).ConfigureAwait(false);

 if (IsPreserveMethodRedirect(response.StatusCode) &&
 request.Content != null &&
 !IsReplayableRequestContent(request.Content))
 {
 response.Dispose();
 throw new InvalidOperationException(
 "收到 307/308 重定向，但请求内容不可重放（非内存型白名单内容）。" +
 "为避免重定向后以空体重发（静默数据丢失），已中止请求。" +
 "请改用可缓冲内容（byte[]/string/FormUrlEncoded/multipart）或直接请求最终地址。");
 }

 if (!IsPreserveMethodRedirect(response.StatusCode) &&
 currentMethod != HttpMethod.Get &&
 currentMethod != HttpMethod.Head)
 {
// 301/302/303：非 GET/HEAD 方法降级为 GET 并丢弃请求体（对齐 .NET RedirectHandler 语义）
 currentMethod = HttpMethod.Get;
 }

 currentUri = nextUri;
 isRedirect = true;

// 释放重定向中间响应（Location 已提取，连接归还连接池）
 response.Dispose();
 }
 }

/// <summary>重定向跳数上限。</summary>
 private const int MaxRedirectHops = 10;

 private static bool IsRedirectStatus(HttpStatusCode statusCode)
 => statusCode == HttpStatusCode.Moved
 || statusCode == HttpStatusCode.Found
 || statusCode == HttpStatusCode.SeeOther
 || statusCode == HttpStatusCode.TemporaryRedirect
 || (int)statusCode == 308;

/// <summary>307/308：保留方法与请求体的重定向。</summary>
 private static bool IsPreserveMethodRedirect(HttpStatusCode statusCode)
 => statusCode == HttpStatusCode.TemporaryRedirect || (int)statusCode == 308;

/// <summary>
/// 请求内容可重放判定 —— 与 克隆白名单同口径：
/// 仅内存型内容（ByteArray/String/FormUrlEncoded 家族、Multipart）可安全重放
/// （ByteArray 家族每次发送生成新流）；StreamContent/自定义内容发送即消费，不可重放。
/// </summary>
 private static bool IsReplayableRequestContent(HttpContent? content)
 => content == null || content is ByteArrayContent || content is MultipartContent;

/// <summary>跨 TFM 复制请求级状态（Options/Properties），保持可观测性去重标记等随跳传递。</summary>
 private static void CopyRequestState(HttpRequestMessage source, HttpRequestMessage target)
 {
#if NET5_0_OR_GREATER
 foreach (var option in source.Options)
 target.Options.TryAdd(option.Key, option.Value);
#else
 foreach (var property in source.Properties)
 target.Properties[property.Key] = property.Value;
#endif
 }

 #region IEnhancedHttpClient 接口实现

/// <inheritdoc cref="IBaseHttpClient.SendAsync{TResult}"/>
/// <param name="request">HTTP请求消息。</param>
/// <param name="jsonSerializerOptions">JSON序列化选项,如果为null则使用默认选项。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
/// <exception cref="JsonException">JSON反序列化失败时抛出。</exception>
 public async Task<TResult?> SendAsync<TResult>(
 HttpRequestMessage request,
 object? jsonSerializerOptions = null,
 CancellationToken cancellationToken = default)
 {
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

 return await ExecuteWithObservabilityAsync(
 request,
 "发送JSON请求", "JSON请求完成", "JSON请求失败", uri,
 () => SendRequestAsync<TResult>(
 request,
 jsonSerializerOptions: jsonSerializerOptions, // M3-#23：object? 透传，保留 JsonTypeInfo<T> 快路径
 cancellationToken: cancellationToken),
 cancellationToken);
 }

/// <inheritdoc cref="IBaseHttpClient.SendRawAsync"/>
/// <param name="request">HTTP请求消息。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>HTTP响应消息。</returns>
/// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<HttpResponseMessage> SendRawAsync(
 HttpRequestMessage request,
 CancellationToken cancellationToken = default)
 {
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

 return await ExecuteWithObservabilityAsync(
 request,
 "发送原始HTTP请求", "原始HTTP请求完成", "原始HTTP请求失败", uri,
 async () =>
 {
 var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
// SendRawAsync 不经过 SendAndValidateAsync，需手动存储状态码供指标采集
 MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
 MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);
 return response;
 },
 cancellationToken);
 }

/// <inheritdoc cref="IBaseHttpClient.SendStreamAsync"/>
/// <param name="request">HTTP请求消息。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>响应内容流。调用者负责释放此流。</returns>
/// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<Stream> SendStreamAsync(
 HttpRequestMessage request,
 CancellationToken cancellationToken = default)
 {
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

 return await ExecuteWithObservabilityAsync(
 request,
 "发送流式HTTP请求", "流式HTTP请求完成", "流式HTTP请求失败", uri,
 async () =>
 {
 var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

// 校验/读流失败时必须释放响应，否则连接泄漏且调用方无引用可释放
 try
 {
 await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

// 流式响应需要在响应头到达时即记录状态码（响应体可能在 Dispose 后才读取）
 MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
 MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

#if NETSTANDARD2_0
 var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
 var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
// 所有权转移：stream + response 由 DisposableStream 统一持有/释放
 return new DisposableStream(stream, response);
 }
 catch
 {
 response.Dispose();
 throw;
 }
 },
 cancellationToken);
 }
 #endregion

 #region XML 序列化支持

/// <inheritdoc cref="IXmlHttpClient.SendXmlAsync{TResult}"/>
/// <param name="request">HTTP请求消息。</param>
/// <param name="encoding">XML编码方式,默认为UTF-8。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>XML反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
/// <exception cref="InvalidOperationException">XML反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> SendXmlAsync<TResult>(
 HttpRequestMessage request,
 Encoding? encoding = null,
 CancellationToken cancellationToken = default)
 {
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

 return await ExecuteWithObservabilityAsync(
 request,
 "发送XML请求", "XML请求完成", "XML请求失败", uri,
 () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken),
 cancellationToken);
 }

/// <inheritdoc cref="IXmlHttpClient.PostAsXmlAsync{TRequest,TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="requestData">请求数据对象。</param>
/// <param name="encoding">XML编码方式,默认为UTF-8。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TRequest">请求数据的类型。</typeparam>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>XML反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
/// <exception cref="InvalidOperationException">XML序列化或反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> PostAsXmlAsync<TRequest, TResult>(
 string requestUri,
 TRequest requestData,
 Encoding? encoding = null,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();
 requestData.ThrowIfNull();

 return await SendXmlWithBodyAsync<TRequest, TResult>(
 HttpMethod.Post, "发送XML POST请求", "XML POST请求完成", "XML POST请求失败",
 requestUri, requestData, encoding, cancellationToken);
 }

/// <inheritdoc cref="IXmlHttpClient.PutAsXmlAsync{TRequest,TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="requestData">请求数据对象。</param>
/// <param name="encoding">XML编码方式,默认为UTF-8。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TRequest">请求数据的类型。</typeparam>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>XML反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
/// <exception cref="InvalidOperationException">XML序列化或反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> PutAsXmlAsync<TRequest, TResult>(
 string requestUri,
 TRequest requestData,
 Encoding? encoding = null,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();
 requestData.ThrowIfNull();

 return await SendXmlWithBodyAsync<TRequest, TResult>(
 HttpMethod.Put, "发送XML PUT请求", "XML PUT请求完成", "XML PUT请求失败",
 requestUri, requestData, encoding, cancellationToken);
 }

/// <inheritdoc cref="IXmlHttpClient.GetXmlAsync{TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="encoding">XML编码方式,默认为UTF-8。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>XML反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 为 null。</exception>
/// <exception cref="InvalidOperationException">XML反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> GetXmlAsync<TResult>(
 string requestUri,
 Encoding? encoding = null,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();

// Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
 requestUri = ResolveRequestUri(requestUri)!;
 using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
 ApplyRequestConfig(request);
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);
 return await ExecuteWithObservabilityAsync(
 request,
 "发送XML GET请求", "XML GET请求完成", "XML GET请求失败", uri,
 () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken),
 cancellationToken);
 }

 private async Task<TResult?> SendXmlWithBodyAsync<TRequest, TResult>(
 HttpMethod method, string operation, string completeMsg, string errorMsg,
 string requestUri, TRequest requestData, Encoding? encoding, CancellationToken cancellationToken)
 {
 var enc = encoding ?? Encoding.UTF8;
 var xmlContent = SerializeToXml(requestData, enc);
// Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
 requestUri = ResolveRequestUri(requestUri)!;
 using var request = new HttpRequestMessage(method, requestUri)
 {
 Content = new StringContent(xmlContent, enc, "application/xml")
 };
 ApplyRequestConfig(request);
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);
 return await ExecuteWithObservabilityAsync(
 request,
 operation, completeMsg, errorMsg, uri,
 () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken),
 cancellationToken);
 }

 #endregion

 #region JSON 辅助方法

/// <inheritdoc cref="IJsonHttpClient.GetAsync{TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>JSON反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 为 null。</exception>
/// <exception cref="JsonException">JSON反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> GetAsync<TResult>(
 string requestUri,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();

 return await SendSimpleJsonRequestAsync<TResult>(
 HttpMethod.Get, "发送JSON GET请求", "JSON GET请求完成", "JSON GET请求失败",
 requestUri, cancellationToken);
 }

/// <inheritdoc cref="IJsonHttpClient.PostAsJsonAsync{TRequest,TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="requestData">请求数据对象。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TRequest">请求数据的类型。</typeparam>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>JSON反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
/// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> PostAsJsonAsync<TRequest, TResult>(
 string requestUri,
 TRequest requestData,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();
 requestData.ThrowIfNull();

 return await SendJsonWithBodyAsync<TRequest, TResult>(
 HttpMethod.Post, "发送JSON POST请求", "JSON POST请求完成", "JSON POST请求失败",
 requestUri, requestData, cancellationToken);
 }

/// <inheritdoc cref="IJsonHttpClient.PutAsJsonAsync{TRequest,TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="requestData">请求数据对象。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TRequest">请求数据的类型。</typeparam>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>JSON反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
/// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> PutAsJsonAsync<TRequest, TResult>(
 string requestUri,
 TRequest requestData,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();
 requestData.ThrowIfNull();

 return await SendJsonWithBodyAsync<TRequest, TResult>(
 HttpMethod.Put, "发送JSON PUT请求", "JSON PUT请求完成", "JSON PUT请求失败",
 requestUri, requestData, cancellationToken);
 }

/// <inheritdoc cref="IJsonHttpClient.DeleteAsJsonAsync{TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>JSON反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 为 null。</exception>
/// <exception cref="JsonException">JSON反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> DeleteAsJsonAsync<TResult>(
 string requestUri,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();

 return await SendSimpleJsonRequestAsync<TResult>(
 HttpMethod.Delete, "发送JSON DELETE请求", "JSON DELETE请求完成", "JSON DELETE请求失败",
 requestUri, cancellationToken);
 }

/// <inheritdoc cref="IJsonHttpClient.DeleteAsJsonAsync{TRequest,TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="requestData">请求数据对象。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TRequest">请求数据的类型。</typeparam>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>JSON反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
/// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> DeleteAsJsonAsync<TRequest, TResult>(
 string requestUri,
 TRequest requestData,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();
 requestData.ThrowIfNull();

 return await SendJsonWithBodyAsync<TRequest, TResult>(
 HttpMethod.Delete, "发送带Body的JSON DELETE请求", "带Body的JSON DELETE请求完成", "带Body的JSON DELETE请求失败",
 requestUri, requestData, cancellationToken);
 }

/// <inheritdoc cref="IJsonHttpClient.PatchAsJsonAsync{TRequest,TResult}"/>
/// <param name="requestUri">请求URI。</param>
/// <param name="requestData">请求数据对象。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <typeparam name="TRequest">请求数据的类型。</typeparam>
/// <typeparam name="TResult">响应结果的类型。</typeparam>
/// <returns>JSON反序列化后的响应结果。</returns>
/// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
/// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<TResult?> PatchAsJsonAsync<TRequest, TResult>(
 string requestUri,
 TRequest requestData,
 CancellationToken cancellationToken = default)
 {
 requestUri.ThrowIfNull();
 requestData.ThrowIfNull();

 return await SendJsonWithBodyAsync<TRequest, TResult>(
 new HttpMethod("PATCH"), "发送JSON PATCH请求", "JSON PATCH请求完成", "JSON PATCH请求失败",
 requestUri, requestData, cancellationToken);
 }

 private async Task<TResult?> SendJsonWithBodyAsync<TRequest, TResult>(
 HttpMethod method, string operation, string completeMsg, string errorMsg,
 string requestUri, TRequest requestData, CancellationToken cancellationToken)
 {
// Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
 requestUri = ResolveRequestUri(requestUri)!;
// Phase 1 (T1.2)：根据 RequestBodySerializationMode 选择序列化路径
 var content = CreateHttpContentWithMode(requestData);
// Phase 2 (T2.3)：CaptureRequestContent 启用时缓冲请求体字符串（#16：长度受 MaxExceptionContentLength 约束）
 string? capturedRequestContent = null;
 if (_captureRequestContent && content != null)
 {
// 不可重放内容跳过捕获，避免读取耗尽一次性源流
 if (RequestContentCaptureUtils.CanCapture(content, _logger))
 {
 try
 {
 var (captured, _) = await LimitedContentReader
 .ReadLimitedStringAsync(content, EffectiveMaxErrorContentLength, cancellationToken)
 .ConfigureAwait(false);
 capturedRequestContent = captured;
 }
 catch { /* 读取失败不影响请求发送 */ }
 }
 }

 using var request = new HttpRequestMessage(method, requestUri)
 {
 Content = content
 };
 ApplyRequestConfig(request);
// 将捕获的请求体存入请求属性，供 EnsureSuccessStatusCodeAsync 在创建 ApiException 时读取
 if (capturedRequestContent != null)
 {
#if NETSTANDARD2_0
 request.Properties[MudHttpObservability.CapturedRequestContentPropertyKey] = capturedRequestContent;
#else
 request.Options.TryAdd(MudHttpObservability.CapturedRequestContentPropertyKey, capturedRequestContent);
#endif
 }

 var validatedUri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);
 return await ExecuteWithObservabilityAsync(
 request,
 operation, completeMsg, errorMsg, validatedUri,
 () => SendRequestAsync<TResult>(
 request,
 cancellationToken: cancellationToken),
 cancellationToken);
 }

/// <summary>
/// Phase 1 (T1.2)：根据 <see cref="_requestBodySerialization"/> 模式选择请求体序列化路径。
/// <see cref="RequestBodySerializationMode.Default"/> 使用 <see cref="IHttpContentSerializer.ToHttpContent{T}"/>（向后兼容）。
/// <see cref="RequestBodySerializationMode.Buffered"/> / <see cref="RequestBodySerializationMode.Streamed"/> 使用 <see cref="ISynchronousContentSerializer"/> fast-path。
/// </summary>
 private HttpContent? CreateHttpContentWithMode<T>(T item)
 {
 if (_requestBodySerialization == RequestBodySerializationMode.Default)
 return _contentSerializer.ToHttpContent(item);

// 检测序列化器是否实现 ISynchronousContentSerializer 以启用 fast-path
 if (_contentSerializer is ISynchronousContentSerializer syncSerializer)
 {
 return _requestBodySerialization == RequestBodySerializationMode.Streamed
 ? syncSerializer.ToStreamingHttpContent(item)
 : syncSerializer.ToHttpContentSynchronous(item);
 }

// 序列化器未实现 fast-path 接口，回退到默认路径。
 // / 不变量 配置已设置但因条件未满足而回退，必须留下 Debug 级及以上日志；
// 用 Interlocked 门控只记一次，避免每请求刷屏（该回退条件在整个客户端生命周期内恒定）。
 if (Interlocked.Exchange(ref _fastPathFallbackLogged, 1) == 0)
 {
 MudHttpClientLog.RequestBodySerializationFastPathFallback(
 _logger,
 _requestBodySerialization.ToString(),
 _contentSerializer.GetType().Name);
 }

 return _contentSerializer.ToHttpContent(item);
 }

 private async Task<TResult?> SendSimpleJsonRequestAsync<TResult>(
 HttpMethod method, string operation, string completeMsg, string errorMsg,
 string requestUri, CancellationToken cancellationToken)
 {
// Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
 requestUri = ResolveRequestUri(requestUri)!;
 using var request = new HttpRequestMessage(method, requestUri);
 ApplyRequestConfig(request);
 var validatedUri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);
 return await ExecuteWithObservabilityAsync(
 request,
 operation, completeMsg, errorMsg, validatedUri,
 () => SendRequestAsync<TResult>(
 request,
 cancellationToken: cancellationToken),
 cancellationToken);
 }

 #endregion


 #region 核心请求处理方法

 private async Task<TResult?> ExecuteHttpRequestCoreAsync<TResult>(
 Func<Task<TResult?>> coreAction,
 string requestUri,
 CancellationToken cancellationToken)
 {
 try
 {
 return await coreAction().ConfigureAwait(false);
 }
// Phase 2：ApiException 继承 HttpRequestException，须在前者之前捕获
 catch (ApiException ex)
 {
 _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
 throw;
 }
 catch (HttpRequestException ex)
 {
#if !NETSTANDARD2_0
 var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
 _logger.HttpRequestFailedWithStatusCode(requestUri!, statusCode, ex);
#else
 _logger.HttpRequestFailedSimple(requestUri!, ex);
#endif
 throw;
 }
 catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
 {
 _logger.HttpRequestTimeout(requestUri!, _httpClient.Timeout.TotalSeconds, ex);
 throw new ApiRequestException($"请求超时: {requestUri}", ex, isTimeout: true, requestUri: requestUri);
 }
 catch (TaskCanceledException ex)
 {
 _logger.HttpRequestCancelled(requestUri!, ex);
 throw;
 }
 catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
 {
// 纯 OperationCanceledException（非 TaskCanceledException）表示请求被取消，
// 直接重新抛出，避免被下面的 catch-all 包装为 HttpRequestException
 _logger.HttpRequestCancelled(requestUri!, ex);
 throw;
 }
// 在 catch-all 之前捕获 JsonException 和 InvalidOperationException，保留原始异常类型。
// 此前所有非 HttpRequestException / OperationCanceledException 的异常被统一包装为 HttpRequestException，
// 调用方无法按 JsonException 等具体类型 catch，只能通过 InnerException 检查。
 catch (JsonException ex)
 {
 _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
 throw;
 }
 catch (InvalidOperationException ex)
 {
// XML 反序列化失败抛出 InvalidOperationException，直接重抛保留原始类型
 _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
 throw;
 }
 catch (Exception ex)
 {
 _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
 throw new ApiRequestException($"HTTP请求处理失败: {requestUri}", ex, requestUri: requestUri);
 }
 }

 private async Task<HttpResponseMessage> SendAndValidateAsync(
 HttpRequestMessage request,
 CancellationToken cancellationToken)
 {
 await ExecuteRequestInterceptorsAsync(request, cancellationToken).ConfigureAwait(false);

 var response = await SendCoreAsync(
 request,
 HttpCompletionOption.ResponseHeadersRead,
 cancellationToken).ConfigureAwait(false);

// 失败/拦截器异常路径必须释放响应，否则连接不归还连接池。
// EnsureSuccessStatusCodeAsync 已在抛异常前读完错误内容写入 ApiException，此处释放不影响诊断信息。
 try
 {
 await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);

// 将状态码与内容长度存入请求属性，供 ExecuteWithObservabilityAsync 采集指标使用
 MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
 MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

 await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

 return response; // 所有权转移给调用方（调用点均为 using var）
 }
 catch
 {
 response.Dispose();
 throw;
 }
 }

/// <summary>
/// 发送HTTP请求并反序列化JSON响应结果
/// </summary>
/// <typeparam name="TResult">响应结果的类型</typeparam>
/// <param name="httpRequestMessage">HTTP请求消息</param>
/// <param name="jsonSerializerOptions">JSON序列化选项</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>反序列化后的响应结果</returns>
 private async Task<TResult?> SendRequestAsync<TResult>(
 HttpRequestMessage httpRequestMessage,
// M3-#23：参数从 JsonSerializerOptions? 收宽为 object?，由 IHttpContentSerializer 自行分派
// （JsonSerializerOptions / JsonTypeInfo<T> 快路径均可达）
 object? jsonSerializerOptions = null,
 CancellationToken cancellationToken = default)
 {
// M1-#5：日志输出用 URL 脱敏（敏感 query 值掩码）
 string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;

 return await ExecuteHttpRequestCoreAsync(
 async () =>
 {
 using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

 var contentLength = response.Content.Headers.ContentLength;
// 成功响应体守卫（Content-Length 预判 + 读取阶段校验）
 EnsureSuccessContentLengthWithinLimit(contentLength, requestUri);
 if (contentLength == 0)
 {
 _logger.JsonResponseBodyEmpty(requestUri!);
 return default;
 }

#if NETSTANDARD2_0
 using var stream = GuardSuccessStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), requestUri);
#else
 await using var stream = GuardSuccessStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), requestUri);
#endif

 var options = jsonSerializerOptions;

 if (_logger.IsEnabled(LogLevel.Trace))
 {
 using var memoryStream = new MemoryStream();
 await CopyUpToAsync(stream, memoryStream, MaxDebugLogBodyLength + 1, cancellationToken).ConfigureAwait(false);
 memoryStream.Position = 0;

 string rawResponse;
 if (memoryStream.Length > MaxDebugLogBodyLength)
 {
 var buffer = new byte[MaxDebugLogBodyLength];
#if NETSTANDARD2_0
 await memoryStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#else
 await memoryStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
#endif
 rawResponse = Encoding.UTF8.GetString(buffer) + $"...[已截断，总长度: {memoryStream.Length} 字节]";
 }
 else
 {
#if NETSTANDARD2_0
 using var reader = new StreamReader(memoryStream, Encoding.UTF8);
 rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
 using var reader = new StreamReader(memoryStream, Encoding.UTF8, leaveOpen: true);
 rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
 }
// Trace 级原始体同样脱敏 + 限量，与错误响应体路径同口径
 _logger.JsonResponseBodyRaw(requestUri!, SanitizeContent(rawResponse, LogBodyMaxLength));

 memoryStream.Position = 0;

 try
 {
// M7-HC-01：截断日志照旧（:1511 之前已写入），但反序列化必须读全量 ——
// 以"32KB 截断缓冲 + 原始流剩余"顺序拼接（组合流），杜绝以截断 JSON 做业务反序列化
//（>32KB 合法响应在 Trace 开启时必然 JsonException，日志级别改变业务行为）。
 using var responseContent = new StreamContent(
 new ConcatReadOnlyStream(memoryStream, stream, leaveOpen: true));
 var result = await _contentSerializer.FromHttpContentAsync<TResult>(responseContent, options, cancellationToken).ConfigureAwait(false);
 _logger.JsonDeserializeSuccess(requestUri!, typeof(TResult).Name);
 return result;
 }
 catch (JsonException jsonEx)
 {
// Error 日志写入前统一脱敏 + 限量（masker 回退 MessageSanitizer）
 _logger.JsonDeserializeFailedDetailed(requestUri!, typeof(TResult).Name,
 SanitizeContent(rawResponse, LogBodyMaxLength), jsonEx.Path, jsonEx);
 throw new JsonException($"反序列化到类型 {typeof(TResult).Name} 失败: {jsonEx.Message}", jsonEx);
 }
 }
 else
 {
 try
 {
 using var responseContent = new StreamContent(stream);
 return await _contentSerializer.FromHttpContentAsync<TResult>(responseContent, options, cancellationToken).ConfigureAwait(false);
 }
 catch (JsonException jsonEx)
 {
 _logger.JsonDeserializeFailedSimple(requestUri!, typeof(TResult).Name, jsonEx);
 throw new JsonException($"反序列化到类型 {typeof(TResult).Name} 失败: {jsonEx.Message}", jsonEx);
 }
 }
 },
 requestUri!,
 cancellationToken);
 }

/// <summary>
/// 发送HTTP请求并反序列化XML响应结果
/// </summary>
/// <typeparam name="TResult">响应结果的类型</typeparam>
/// <param name="httpRequestMessage">HTTP请求消息</param>
/// <param name="encoding">XML编码方式</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>反序列化后的响应结果</returns>
 private async Task<TResult?> SendXmlRequestAsync<TResult>(
 HttpRequestMessage httpRequestMessage,
 Encoding? encoding = null,
 CancellationToken cancellationToken = default)
 {
// M1-#5：日志输出用 URL 脱敏（敏感 query 值掩码）
 string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;

 encoding ??= Encoding.UTF8;

 return await ExecuteHttpRequestCoreAsync(
 async () =>
 {
 using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

 var contentLength = response.Content.Headers.ContentLength;
 if (contentLength == 0)
 {
 _logger.XmlResponseBodyEmpty(requestUri!);
 return default;
 }

// 成功响应体守卫（Content-Length 预判 + 读取阶段校验，保留 charset 语义）
 var xmlContent = await ReadXmlContentStringAsync(response, requestUri, cancellationToken).ConfigureAwait(false);

 if (_logger.IsEnabled(LogLevel.Trace))
 {
// Trace 级原始体脱敏 + 限量
 _logger.XmlResponseBodyRaw(requestUri!, SanitizeContent(xmlContent, LogBodyMaxLength));
 }

 if (string.IsNullOrWhiteSpace(xmlContent))
 {
 _logger.XmlResponseBodyEmpty(requestUri!);
 return default;
 }

 try
 {
 var result = DeserializeFromXml<TResult>(xmlContent, encoding);
 _logger.XmlDeserializeSuccess(requestUri!, typeof(TResult).Name);
 return result;
 }
 catch (InvalidOperationException xmlEx)
 {
// Error 日志写入前统一脱敏 + 限量
 _logger.XmlDeserializeFailed(requestUri!, typeof(TResult).Name,
 SanitizeContent(xmlContent, LogBodyMaxLength), xmlEx);
 throw new InvalidOperationException($"XML反序列化到类型 {typeof(TResult).Name} 失败: {xmlEx.Message}", xmlEx);
 }
 },
 requestUri!,
 cancellationToken);
 }

 #endregion

 #region 加密成员（IEncryptableHttpClient 实现）

/// <inheritdoc cref="IEncryptableHttpClient.EncryptContent"/>
/// <param name="content">要加密的内容对象。</param>
/// <param name="propertyName">加密后JSON中的属性名,默认为"data"。</param>
/// <param name="serializeType">序列化类型,支持JSON和XML。</param>
/// <returns>加密后的字符串。</returns>
#if NET6_0_OR_GREATER
 [RequiresUnreferencedCode("EncryptContent(object, ...) 使用运行时类型分派（content.GetType()）与 XML 序列化，Native AOT 不支持。请改用 EncryptContent<T>(T, string) 强类型重载。")]
#endif
#if NET8_0_OR_GREATER
 [RequiresDynamicCode("EncryptContent 使用 object/Dictionary 反射式 JSON 序列化，Native AOT 不支持。请在 AOT 场景下改用强类型重载或避免加密内容路径。")]
#endif
 [Obsolete("此重载使用运行时反射 (content.GetType())，Native AOT 不兼容。请改用 EncryptContent<T>(T, string) 泛型重载。")]
 public string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
 {
 if (content == null)
 throw new ArgumentNullException(nameof(content));
 if (string.IsNullOrEmpty(propertyName))
 throw new ArgumentException("属性名不能为空", nameof(propertyName));

 if (EncryptionProvider == null)
 throw new InvalidOperationException(
 "未配置加密提供器。请通过 AddMudHttpClient 注册时配置 AesEncryptionOptions，" +
 "或注册自定义 IEncryptionProvider 实现。");

 string serializedContent;
 if (serializeType == SerializeType.Xml)
 {
 serializedContent = XmlSerialize.Serialize(content);
 }
 else
 {
 serializedContent = _contentSerializer.Serialize(content, content.GetType());
 }

 var encryptedData = EncryptionProvider.Encrypt(serializedContent);

 var result = new Dictionary<string, object>
 {
 [propertyName] = encryptedData
 };

 return _contentSerializer.Serialize(result);
 }

/// <summary>
/// 加密内容对象（AOT 安全泛型重载）。
/// </summary>
/// <typeparam name="T">内容对象的类型。该类型必须在 <c>JsonSerializerContext</c> 中声明。</typeparam>
/// <param name="content">要加密的内容对象。</param>
/// <param name="propertyName">加密数据所在的属性名称，默认为 "data"。</param>
/// <returns>加密后的字符串内容。</returns>
/// <remarks>
/// 使用编译期类型 <typeparamref name="T"/> 进行 JSON 序列化，不依赖运行时反射，
/// 适用于 Native AOT 场景。仅支持 JSON 序列化（不支持 XML）。
/// 外层 JSON 包装使用 <see cref="Utf8JsonWriter"/> 直接写入，无反射开销。
/// </remarks>
 public string EncryptContent<T>(T content, string propertyName = "data")
 {
 if (content == null)
 throw new ArgumentNullException(nameof(content));
 if (string.IsNullOrEmpty(propertyName))
 throw new ArgumentException("属性名不能为空", nameof(propertyName));

 if (EncryptionProvider == null)
 throw new InvalidOperationException(
 "未配置加密提供器。请通过 AddMudHttpClient 注册时配置 AesEncryptionOptions，" +
 "或注册自定义 IEncryptionProvider 实现。");

// AOT 安全：使用编译期类型 T 序列化，不使用 content.GetType()
 var serializedContent = _contentSerializer.Serialize(content);
 var encryptedData = EncryptionProvider.Encrypt(serializedContent);

// AOT 安全：使用 Utf8JsonWriter 直接构建外层 JSON，避免 Dictionary<string, object> 反射序列化
 using var stream = new MemoryStream();
 using (var writer = new Utf8JsonWriter(stream))
 {
 writer.WriteStartObject();
 writer.WritePropertyName(propertyName);
 writer.WriteStringValue(encryptedData);
 writer.WriteEndObject();
 writer.Flush();
 }
 return Encoding.UTF8.GetString(stream.ToArray());
 }

/// <inheritdoc cref="IEncryptableHttpClient.DecryptContent"/>
/// <param name="encryptedContent">要解密的加密字符串。</param>
/// <returns>解密后的原始字符串。</returns>
 public string DecryptContent(string encryptedContent)
 {
 if (string.IsNullOrEmpty(encryptedContent))
 return string.Empty;

 if (EncryptionProvider == null)
 throw new InvalidOperationException("未配置加密提供器。");

 string cipherText = encryptedContent;

 try
 {
 using var doc = JsonDocument.Parse(encryptedContent);
 var root = doc.RootElement;

 if (root.ValueKind == JsonValueKind.Object)
 {
 foreach (var property in root.EnumerateObject())
 {
 if (property.Value.ValueKind == JsonValueKind.String)
 {
 cipherText = property.Value.GetString()!;
 break;
 }
 }
 }
 }
 catch (JsonException ex)
 {
 _logger.LogDebug(ex, "解密内容不是JSON格式，将整个字符串作为密文解密");
 }

 return EncryptionProvider.Decrypt(cipherText);
 }

/// <inheritdoc cref="IEncryptableHttpClient.EncryptBytes"/>
/// <param name="data">要加密的字节数组。</param>
/// <returns>加密后的字节数组。</returns>
 public byte[] EncryptBytes(byte[] data)
 {
 if (data == null)
 throw new ArgumentNullException(nameof(data));

 if (EncryptionProvider == null)
 throw new InvalidOperationException("未配置加密提供器。");

 return EncryptionProvider.EncryptBytes(data);
 }

/// <inheritdoc cref="IEncryptableHttpClient.DecryptBytes"/>
/// <param name="encryptedData">要解密的加密字节数组。</param>
/// <returns>解密后的原始字节数组。</returns>
 public byte[] DecryptBytes(byte[] encryptedData)
 {
 if (encryptedData == null)
 throw new ArgumentNullException(nameof(encryptedData));

 if (EncryptionProvider == null)
 throw new InvalidOperationException("未配置加密提供器。");

 return EncryptionProvider.DecryptBytes(encryptedData);
 }
 #endregion
}
