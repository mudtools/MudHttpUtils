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
// 纯移动拆分（自 EnhancedHttpClient.cs 按职责拆出：下载）。
public abstract partial class EnhancedHttpClient
{
 #region IEnhancedHttpClient 接口实现

/// <inheritdoc cref="IBaseHttpClient.DownloadAsync"/>
/// <param name="request">HTTP请求消息。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>文件内容的字节数组。</returns>
/// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<byte[]?> DownloadAsync(
 HttpRequestMessage request,
 CancellationToken cancellationToken = default)
 {
 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

 return await ExecuteWithObservabilityAsync(
 request,
 "下载文件", "文件下载完成", "文件下载失败", uri,
 () => DownloadFileAsync(request, cancellationToken: cancellationToken),
 cancellationToken);
 }

/// <inheritdoc cref="IBaseHttpClient.DownloadLargeAsync"/>
/// <param name="request">HTTP请求消息。</param>
/// <param name="filePath">保存文件的路径。</param>
/// <param name="overwrite">是否覆盖已存在的文件,默认为true。</param>
/// <param name="bufferSize">下载缓冲区大小（字节），默认为 81920。</param>
/// <param name="progress">下载进度回调（报告累计已写入字节数）。可为 null。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>下载完成后的文件信息。</returns>
/// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
/// <exception cref="ArgumentException"><paramref name="filePath"/> 为空或仅包含空白字符。</exception>
/// <exception cref="IOException">当 <paramref name="overwrite"/> 为 false 且文件已存在时抛出。</exception>
/// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
 public async Task<FileInfo> DownloadLargeAsync(
 HttpRequestMessage request,
 string filePath,
 bool overwrite = true,
 int bufferSize = DefaultBufferSize,
 IProgress<long>? progress = null,
 CancellationToken cancellationToken = default)
 {
 if (string.IsNullOrWhiteSpace(filePath))
 throw new ArgumentException("文件路径不能为空", nameof(filePath));

 var uri = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

 return await ExecuteWithObservabilityAsync(
 request,
 $"下载大文件到: {filePath}", $"大文件下载完成: {filePath}", $"大文件下载失败: {filePath}", uri,
 () => DownloadLargeFileAsync(request, filePath, bufferSize: bufferSize, overwrite: overwrite, progress: progress, cancellationToken: cancellationToken),
 cancellationToken);
 }
 #endregion

 #region 下载处理方法

/// <summary>
/// 下载文件内容并以字节数组形式返回
/// </summary>
 private async Task<byte[]?> DownloadFileAsync(
 HttpRequestMessage httpRequestMessage,
 CancellationToken cancellationToken = default)
 {
// M1-#5：日志输出用 URL 脱敏（敏感 query 值掩码）
 string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;

 return await ExecuteHttpRequestCoreAsync(async () =>
 {
 using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

 var contentLength = response.Content.Headers.ContentLength;
// 成功响应体守卫（byte[] 全量缓冲路径；流式落盘走 DownloadLargeAsync，不受守卫约束）
 EnsureSuccessContentLengthWithinLimit(contentLength, requestUri);
 if (contentLength > 10 * 1024 * 1024)
 {
 _logger.DownloadFileLarge(requestUri!, contentLength.GetValueOrDefault() / (1024.0 * 1024.0));
 }

// G33：下载阶段可观测性（仅响应体读取阶段；HTTP 请求层已由外层观察窗口采集，不同仪表不构成重复计数）
 MudHttpObservability.RecordDownloadStarted(httpRequestMessage, ClientName);
 var downloadSw = ValueStopwatch.StartNew();
 try
 {
 byte[]? bytes;
 if (_maxSuccessResponseBytes > 0)
 {
#if NETSTANDARD2_0
 using var guardedStream = GuardSuccessStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), requestUri);
#else
 await using var guardedStream = GuardSuccessStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), requestUri);
#endif
 using var buffered = new MemoryStream();
 await guardedStream.CopyToAsync(buffered, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
 bytes = buffered.ToArray();
 }
 else
 {
#if NETSTANDARD2_0
 bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#else
 bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#endif
 }

 MudHttpObservability.RecordDownloadCompleted(
 httpRequestMessage, ClientName, bytes?.Length ?? 0, downloadSw.GetElapsedTime().TotalMilliseconds);
 return bytes;
 }
 catch (Exception ex)
 {
 MudHttpObservability.RecordDownloadFailed(
 httpRequestMessage, ClientName, downloadSw.GetElapsedTime().TotalMilliseconds, ex, cancellationToken);
 throw;
 }
 }, requestUri!, cancellationToken);
 }

/// <summary>
/// 下载大文件并保存到指定路径
/// </summary>
 private async Task<FileInfo> DownloadLargeFileAsync(
 HttpRequestMessage httpRequestMessage,
 string filePath,
 int bufferSize = DefaultBufferSize,
 bool overwrite = true,
 IProgress<long>? progress = null,
 CancellationToken cancellationToken = default)
 {
 if (string.IsNullOrWhiteSpace(filePath))
 throw new ArgumentException("文件路径不能为空", nameof(filePath));

// bufferSize 上下界钳制 —— 与 DefaultHttpRequestExecutor（G8-06）同口径防御。
// 原实现仅拒绝 <= 0；超大值会直接进入 new byte[bufferSize] / new FileStream(…, bufferSize) 触发 OOM。
// 越界（< 4 KiB 或 > 4 MiB）一律归一回默认值 81920。
 const int MaxDownloadBufferSize = 4 * 1024 * 1024;
 const int MinDownloadBufferSize = 4 * 1024;
 if (bufferSize < MinDownloadBufferSize || bufferSize > MaxDownloadBufferSize)
 bufferSize = DefaultBufferSize;

// G27：日志输出用 URL 脱敏（与 DownloadFileAsync 的 SafeUrl 模式对齐；发送路径不受影响）
 string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;
 string directoryPath = Path.GetDirectoryName(filePath)!;

// 临时文件 + 原子落盘（对齐执行器 模板）。
// fileStreamOpened 区分「构造期 IOException」（如 overwrite=false 且目标已存在 —— 尚未触碰任何
// 文件，维持既有语义：不清理、不记失败、直接重抛）与「写入期 IOException」（需清理半写 tmp 并记失败）。
 var tmpPath = filePath + ".mudtmp";
 var fileStreamOpened = false;

// G33：下载阶段可观测性状态（downloadStarted 区分"响应头到达前失败"与"响应体阶段失败"）
 var downloadStarted = false;
 var downloadSw = default(ValueStopwatch);

 try
 {
 if (!string.IsNullOrEmpty(directoryPath))
 Directory.CreateDirectory(directoryPath);

 if (File.Exists(filePath))
 {
 if (overwrite)
 {
 _logger.FileExistsWillOverwrite(filePath);
 }
 else
 {
 throw new IOException($"文件已存在: {filePath}");
 }
 }

 using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

 var contentLength = response.Content.Headers.ContentLength;
 _logger.DownloadFileStarted(
 requestUri!,
 contentLength.HasValue ? contentLength.Value / (1024.0 * 1024.0) : 0.0,
 filePath);

// G33：下载阶段可观测性（仅响应体下载与文件写入阶段；HTTP 请求层已由外层观察窗口采集）
 MudHttpObservability.RecordDownloadStarted(httpRequestMessage, ClientName);
 downloadSw = ValueStopwatch.StartNew();
 downloadStarted = true;

#if NETSTANDARD2_0
// 落盘改为 .mudtmp 临时文件，流关闭后再原子 Move，最终路径永不出现半写文件
 using (var fileStream = new FileStream(
 tmpPath,
 FileMode.Create,
 FileAccess.Write,
 FileShare.None,
 bufferSize: bufferSize,
 useAsync: true))
 {
 fileStreamOpened = true;
 using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

 if (progress == null)
 {
 await contentStream.CopyToAsync(fileStream, bufferSize, cancellationToken).ConfigureAwait(false);
 }
 else
 {
 await CopyToWithThrottledProgressAsync(
 contentStream, fileStream, bufferSize, progress, cancellationToken).ConfigureAwait(false);
 }

 await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
 }

// netstandard2.0：File.Move 无 overwrite 重载
 if (overwrite && File.Exists(filePath))
 File.Delete(filePath);
 File.Move(tmpPath, filePath);
#else
 await using (var fileStream = new FileStream(
 tmpPath,
 FileMode.Create,
 FileAccess.Write,
 FileShare.None,
 bufferSize: bufferSize,
 useAsync: true))
 {
 fileStreamOpened = true;
 await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

 if (progress == null)
 {
 await contentStream.CopyToAsync(fileStream, bufferSize, cancellationToken).ConfigureAwait(false);
 }
 else
 {
 await CopyToWithThrottledProgressAsync(
 contentStream, fileStream, bufferSize, progress, cancellationToken).ConfigureAwait(false);
 }

 await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
 }

// 流已关闭（Windows 下 File.Move 才可移动）；overwrite 语义由 Move 的 overwrite 参数强制
 File.Move(tmpPath, filePath, overwrite);
#endif

 var fileInfo = new FileInfo(filePath);
 _logger.DownloadFileCompleted(filePath, fileInfo.Length / (1024.0 * 1024.0));

// G33：下载完成（字节数以落盘文件大小为准，含 chunked 无 Content-Length 场景）
 MudHttpObservability.RecordDownloadCompleted(
 httpRequestMessage, ClientName, fileInfo.Length,
 downloadStarted ? downloadSw.GetElapsedTime().TotalMilliseconds : 0);

 return fileInfo;
 }
 catch (Exception ex)
 {
// 构造期 IOException（目标已存在等）尚未写入任何文件 → 维持既有语义直接重抛
 if (ex is IOException && !fileStreamOpened)
 {
 throw;
 }

// G33：下载阶段失败（响应头到达前的失败不记入下载耗时指标）
 if (downloadStarted)
 {
 MudHttpObservability.RecordDownloadFailed(
 httpRequestMessage, ClientName, downloadSw.GetElapsedTime().TotalMilliseconds, ex, cancellationToken);
 }

// 清理半写临时文件（最终路径永不出现半写文件）
 try
 {
 if (File.Exists(tmpPath))
 File.Delete(tmpPath);
 }
 catch (Exception cleanupEx)
 {
 _logger.CleanupPartialFileFailed(tmpPath, cleanupEx);
 }

 _logger.LargeFileDownloadFailed(requestUri!, filePath, ex);

// 取消/超时原样透传 —— 与 byte[] 下载路径（:1821-1826）及执行器
// DownloadLargeCoreAsync 口径一致；包装为 HttpRequestException 会使外层
// ExecuteWithObservabilityAsync 三态判定失去 cancelled 态、TaskCancellationClassifier
// 无法归类平台超时、使用者无法以 catch (OperationCanceledException) 识别自身取消。
 if (ex is HttpRequestException || ex is OperationCanceledException)
 throw;

 throw new HttpRequestException($"大文件下载失败: {ex.Message}", ex);
 }
 }

/// <summary>
/// 抽出带时间节流的进度复制（行为与 NEW-一致：100ms 节流 + 收尾必报）。
/// </summary>
/// <remarks>
//进度回调按时间节流，避免大文件下载时每 buffer（默认 81920 字节）触发一次回调。
/// GB 级文件每秒可能触发数百到数千次回调，造成 UI 线程高频刷新、IProgress.Post 排队堆积、日志海量输出。
/// 实现下沉至 <see cref="ThrottledStreamCopier"/>，与生成器执行路径、上传路径共用同一口径。
/// </remarks>
 private static Task CopyToWithThrottledProgressAsync(
 Stream source,
 Stream destination,
 int bufferSize,
 IProgress<long> progress,
 CancellationToken cancellationToken)
 => ThrottledStreamCopier.CopyWithThrottledProgressAsync(
 source, destination, bufferSize, progress, 0, cancellationToken);

 #endregion
}
