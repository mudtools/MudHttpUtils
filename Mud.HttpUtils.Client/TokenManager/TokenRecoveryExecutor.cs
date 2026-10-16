// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026 
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Helpers;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复执行器，封装 401 Unauthorized（默认语义）及经
/// <see cref="ITokenInvalidationDetector"/> 识别的业务错误码失效（WX-01，如企业微信 HTTP 200 + errcode）时的令牌刷新与请求重试逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 此类从 <see cref="TokenRecoveryDelegatingHandler"/> 中提取，供 Handler 管道模式和
/// <see cref="TokenRecoveryEnhancedClient"/> 子类模式共享同一套恢复逻辑。
/// </para>
/// <para>工作流程：</para>
/// <list type="number">
/// <item>保存请求体内容（在发送前读取，避免流被消耗后无法重试）</item>
/// <item>通过 <c>sendFunc</c> 发送请求</item>
/// <item>如果收到失效响应（HTTP 401，或经 <see cref="TokenRecoveryOptions.TokenInvalidationDetector"/> 判定的业务错误码失效）：使缓存令牌失效 → 强制刷新令牌 → 构建新请求并应用令牌 → 重试</item>
/// <item>根据 <see cref="TokenRecoveryOptions.RecoveryMaxRetries"/> 配置重复步骤 3，直到成功或达到最大重试次数</item>
/// </list>
/// <para>令牌注入模式感知：</para>
/// <list type="bullet">
/// <item>生成代码通过 <see cref="TokenRecoveryContext"/> 在请求属性中传递注入模式信息</item>
/// <item>恢复执行器根据注入模式将新令牌应用到正确的位置（Header/Cookie/Query）</item>
/// <item>若无 <see cref="TokenRecoveryContext"/>，回退到默认的 Authorization Header 行为</item>
/// </list>
/// <para>用户级令牌支持：</para>
/// <list type="bullet">
/// <item>当提供 <see cref="IUserTokenManager"/> 时，恢复流程使用用户级令牌管理器</item>
/// <item>用户 ID 从 <see cref="TokenRecoveryContext.UserId"/> 或 <see cref="ICurrentUserContext"/> 获取</item>
/// </list>
/// </remarks>
public partial class TokenRecoveryExecutor
{
 private readonly ITokenManager _tokenManager;
 private readonly IUserTokenManager? _userTokenManager;
 private readonly ICurrentUserContext? _currentUserContext;
 private readonly ITokenManagerRegistry? _managerRegistry;
 private readonly TokenRecoveryOptions? _staticOptions;
 private readonly IOptionsMonitor<TokenRecoveryOptions>? _optionsMonitor;
 private readonly ILogger _logger;

// 应用上下文持有器（可选）。用于在恢复链路解析出管理器后执行租户绑定守卫，
// 与 DefaultTokenProvider 的守卫形成闭环（此前仅令牌获取路径有守卫，恢复路径没有）。
 private readonly IAppContextHolder? _appContextHolder;

// 多租户选项（可选）。控制应用上下文缺失时的 fail-open/fail-closed 决策。
// 默认 MissingAppContextPolicy.Reject（fail-closed）；设为 FallbackToDefaultApp 可回退旧行为。
 private readonly MudMultiTenantOptions? _multiTenantOptions;

/// <summary>
/// 当前生效的令牌恢复选项。优先走 IOptionsMonitor（热更新），回退静态快照。
/// 删除不可达的 `?? new TokenRecoveryOptions()`——每个 ctor 至少设置 _staticOptions 或 _optionsMonitor 之一。
/// </summary>
 private TokenRecoveryOptions Options => _optionsMonitor?.CurrentValue ?? _staticOptions!;

// 并发刷新去重：同一时间段内多个 401 只触发一次令牌刷新
// 刷新结果在 TTL 窗口内保留，窗口内后续 401 直接复用结果而不重新刷新
// MT-05/去重逻辑收敛到 RefreshDedupTable（条目即任务 + 条目数硬上限）
 private readonly RefreshDedupTable _credentialRefreshTasks;
 private readonly RefreshDedupTable _userRefreshTasks;

/// <summary>测试观测钩子（经 InternalsVisibleTo）：租户级去重表当前条目数。</summary>
 internal int CredentialDedupCountForTest => _credentialRefreshTasks.Count;

/// <summary>测试观测钩子（经 InternalsVisibleTo）：用户级去重表当前条目数。</summary>
 internal int UserDedupCountForTest => _userRefreshTasks.Count;

/// <summary>
/// 初始化令牌恢复执行器。
/// </summary>
/// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
/// <param name="options">令牌恢复配置选项（可选）。</param>
/// <param name="logger">日志记录器（可选）。</param>
/// <param name="appContextHolder">
/// 应用上下文持有器（可选）。提供时，恢复链路解析出的令牌管理器会先执行
/// <c>BindTenantGuard(当前 appKey)</c> 租户绑定守卫；为 null 或当前无应用上下文时跳过（与既有行为一致）。
/// </param>
/// <remarks>
 /P0）：internal —— 容器默认构造选择要求「IOptionsMonitor 快照两族构造不同时公开」，
/// 否则同元数构造均可满足时 DI 解析抛 "The following constructors are ambiguous"。
/// 快照路径经本类内部/同程序集 <see cref="TokenRecoveryDelegatingHandler"/> 使用。
/// </remarks>
 internal TokenRecoveryExecutor(
 ITokenManager tokenManager,
 TokenRecoveryOptions? options = null,
 ILogger? logger = null,
 IAppContextHolder? appContextHolder = null,
 MudMultiTenantOptions? multiTenantOptions = null)
 {
 _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
 _staticOptions = options ?? new TokenRecoveryOptions();
 _logger = logger ?? NullLogger.Instance;
 _appContextHolder = appContextHolder;
 _multiTenantOptions = multiTenantOptions;

// ：上限以委托读取 Options 热值（而非构造期快照），使 MaxDedupEntries 的配置变更即时生效。
 _credentialRefreshTasks = new RefreshDedupTable(() => Options.MaxDedupEntries);
 _userRefreshTasks = new RefreshDedupTable(() => Options.MaxDedupEntries);
 }

/// <summary>
/// 初始化令牌恢复执行器，支持配置热更新（IOptionsMonitor）。
/// </summary>
/// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
/// <param name="optionsMonitor">令牌恢复配置选项监视器，支持热更新。</param>
/// <param name="logger">日志记录器（可选）。</param>
/// <param name="appContextHolder">应用上下文持有器（可选），用于恢复链路的租户绑定守卫。</param>
 public TokenRecoveryExecutor(
 ITokenManager tokenManager,
 IOptionsMonitor<TokenRecoveryOptions> optionsMonitor,
 ILogger? logger = null,
 IAppContextHolder? appContextHolder = null,
 MudMultiTenantOptions? multiTenantOptions = null)
 {
 _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
 _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
 _logger = logger ?? NullLogger.Instance;
 _appContextHolder = appContextHolder;
 _multiTenantOptions = multiTenantOptions;

// ：上限以委托读取 Options 热值（IOptionsMonitor 路径下配置变更后立即采用新上限）。
 _credentialRefreshTasks = new RefreshDedupTable(() => Options.MaxDedupEntries);
 _userRefreshTasks = new RefreshDedupTable(() => Options.MaxDedupEntries);
 }

/// <summary>
/// 初始化令牌恢复执行器（支持用户级令牌恢复 + 配置热更新）。
/// </summary>
/// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
/// <param name="userTokenManager">用户令牌管理器，用于用户级令牌恢复（可选）。</param>
/// <param name="currentUserContext">当前用户上下文，用于获取用户 ID（可选）。</param>
/// <param name="optionsMonitor">令牌恢复配置选项监视器，支持热更新。</param>
/// <param name="logger">日志记录器（可选）。</param>
/// <param name="managerRegistry">SR-M6（P2.4，D9）：令牌管理器注册表（可选）。</param>
/// <param name="appContextHolder">应用上下文持有器（可选），用于恢复链路的租户绑定守卫。</param>
 public TokenRecoveryExecutor(
 ITokenManager tokenManager,
 IUserTokenManager? userTokenManager,
 ICurrentUserContext? currentUserContext,
 IOptionsMonitor<TokenRecoveryOptions> optionsMonitor,
 ILogger? logger = null,
 ITokenManagerRegistry? managerRegistry = null,
 IAppContextHolder? appContextHolder = null,
 MudMultiTenantOptions? multiTenantOptions = null)
 {
 _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
 _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
 _userTokenManager = userTokenManager;
 _currentUserContext = currentUserContext;
 _logger = logger ?? NullLogger.Instance;
 _managerRegistry = managerRegistry;
 _appContextHolder = appContextHolder;
 _multiTenantOptions = multiTenantOptions;

// ：上限以委托读取 Options 热值（IOptionsMonitor 路径下配置变更后立即采用新上限）。
 _credentialRefreshTasks = new RefreshDedupTable(() => Options.MaxDedupEntries);
 _userRefreshTasks = new RefreshDedupTable(() => Options.MaxDedupEntries);
 }

/// <summary>
/// 初始化令牌恢复执行器（支持用户级令牌恢复）。
/// </summary>
/// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
/// <param name="userTokenManager">用户令牌管理器，用于用户级令牌恢复（可选）。</param>
/// <param name="currentUserContext">当前用户上下文，用于获取用户 ID（可选）。</param>
/// <param name="options">令牌恢复配置选项（可选）。</param>
/// <param name="logger">日志记录器（可选）。</param>
/// <param name="managerRegistry">SR-M6（P2.4，D9）：令牌管理器注册表（可选）。非空时按
/// TokenRecoveryContext.TokenManagerKey 路由到正确管理器；解析失败回退注入实例 + Warning。</param>
/// <param name="appContextHolder">应用上下文持有器（可选），用于恢复链路的租户绑定守卫。</param>
/// <remarks>P0）：internal（快照路径），理由见 <see cref="TokenRecoveryExecutor(ITokenManager, TokenRecoveryOptions?, ILogger?, IAppContextHolder?)"/>；
/// 外部请改用 IOptionsMonitor 重载（热更新）。</remarks>
 internal TokenRecoveryExecutor(
 ITokenManager tokenManager,
 IUserTokenManager? userTokenManager,
 ICurrentUserContext? currentUserContext = null,
 TokenRecoveryOptions? options = null,
 ILogger? logger = null,
 ITokenManagerRegistry? managerRegistry = null,
 IAppContextHolder? appContextHolder = null,
 MudMultiTenantOptions? multiTenantOptions = null)
 : this(tokenManager, options, logger, appContextHolder, multiTenantOptions)
 {
 _userTokenManager = userTokenManager;
 _currentUserContext = currentUserContext;
 _managerRegistry = managerRegistry;
 }

/// <summary>
/// 执行 HTTP 请求，在收到 401 响应时自动刷新令牌并重试。
/// </summary>
/// <param name="request">原始 HTTP 请求</param>
/// <param name="sendFunc">实际发送函数（调用底层 HttpClient 或 DelegatingHandler）</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>HTTP 响应</returns>
 public async Task<HttpResponseMessage> ExecuteAsync(
 HttpRequestMessage request,
 Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendFunc,
 CancellationToken cancellationToken)
 {
// I2重入熔断：刷新期间发出的 HTTP 请求不得再触发恢复 ——
// 否则令牌端点的 401 会重新进入本执行器、命中同一去重键的在途条目并 await 自身 → 永久挂起。
// 置于最前（早于 Options.Enabled 判定）：熔断是安全性约束，不受功能开关影响。
 if (TokenRefreshAmbient.InRefresh)
 {
 MudHttpClientLog.TokenRecoverySkippedDuringRefresh(_logger, request.Method.Method);
 return await sendFunc(request, cancellationToken).ConfigureAwait(false);
 }

 if (!Options.Enabled)
 return await sendFunc(request, cancellationToken).ConfigureAwait(false);

// 恢复上下文解析前置（供 ShouldAttemptRecovery / 幂等门控 / 主体三方复用，DTO 保持不变）。
 var recoveryContext = GetRecoveryContext(request);

 if (!ShouldAttemptRecovery(request, recoveryContext))
 return await sendFunc(request, cancellationToken).ConfigureAwait(false);

 幂等门控）：非幂等方法默认不做 401 重放 —— 重放 POST/PATCH 可能造成
// 重复下单 / 重复扣款等业务副作用，而 2.0.x 对任意方法自动重放。
// 门控置于缓冲之前：被门控拒绝的请求不应承担任何缓冲代价（的性能目标）。
 if (!IsRecoveryAllowedForRequest(request, recoveryContext))
 {
 MudHttpClientLog.TokenRecoverySkippedNonIdempotent(_logger, request.Method.Method);
 return await sendFunc(request, cancellationToken).ConfigureAwait(false);
 }

// D1 修订：三态体处理模型——缓冲只判定"能否缓冲"，不做任何提前返回。
// 不可缓冲的请求体仍正常发送，仅放弃 401 重试（禁止重试 ≠ 禁止发送）。
 var maxCachedRequestBodySize = Options.MaxCachedRequestBodyBytes;
 byte[]? contentBytes = null;

// R-P1-02/03：bodyReplayable = 该请求体是否具备无损重放能力。
// 无体 ⇒ 天然可重放；有体 ⇒ 需满足"缓冲成功"或"内存型内容（可在恢复时惰性读取）"。
// MaxCachedRequestBodyBytes == 0 表达"流式优先 / 不缓冲"：该值下带体请求一律放弃重试（保留既有逃生门语义）。
 var bodyReplayable = request.Content == null;

 if (request.Content != null && maxCachedRequestBodySize > 0)
 {
 contentBytes = await TryBufferContentAsync(
 request.Content, maxCachedRequestBodySize, cancellationToken).ConfigureAwait(false);
 bodyReplayable = contentBytes != null || IsMemoryBackedContent(request.Content);
 }

// 缓冲成功且**原内容已被消费**时回填请求内容，保证首次发送与重试同源（P2）。
// 内存型内容的原实例仍可重读，无需回填 —— 既省去一次拷贝，也避免释放调用方拥有的 HttpContent。
 if (contentBytes != null && !IsMemoryBackedContent(request.Content!))
 {
 var original = request.Content!;
 var buffered = new ByteArrayContent(contentBytes);
 foreach (var h in original.Headers)
 {
// 长度/分块头由 ByteArrayContent 重新计算，避免"声明长度 ≠ 实际写入"的挂起
 if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
 h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
 continue;
 buffered.Headers.TryAddWithoutValidation(h.Key, h.Value);
 }

// 保留上传进度语义（前序 T3 转正）——若原内容为 ProgressableStreamContent，
// 以同一 IProgress<long> 与 bufferSize 重新包装缓冲体。
 request.Content = original is ProgressableStreamContent p ? p.Rebind(buffered) : buffered;

// 所有权转移——原内容已被 TryBufferContentAsync 读至 EOF 且不再被引用，
// 由本流程负责释放（否则其包裹的流永不关闭——ProgressableStreamContent.Dispose 会释放内层内容）
 original.Dispose();
 }

// 无条件发送原请求（不可缓冲也必须发送）
 var response = await sendFunc(request, cancellationToken).ConfigureAwait(false);

 所有权栅栏）：IsTokenInvalidAsync 会把 OperationCanceledException 原样重抛
// （见其 try/catch），此时下方所有 `return response` 分支均不可达 ⇒ 原始 401 响应及其连接流泄漏。
// 这里以单一栅栏统一兜底，避免在 14 个返回分支上逐处补 Dispose 而再次遗漏。
 HttpResponseMessage recovered;
 try
 {
 recovered = await RecoverFromUnauthorizedAsync(
 response, request, recoveryContext, contentBytes, bodyReplayable, sendFunc, cancellationToken)
 .ConfigureAwait(false);
 }
 catch
 {
// HttpResponseMessage.Dispose 幂等：与内层"重试发送失败"路径的显式释放重复调用无副作用。
 response.Dispose();
 throw;
 }

 if (ReferenceEquals(recovered, response))
 {
 return recovered; // 未发生替换（含恢复耗尽 / 各类拒绝分支）：原始响应所有权归调用方
 }

// 已被新响应替换：原始 401 由本方法释放。
// 用引用相等而非布尔标志，既零侵入所有返回分支，又保证"假的 sendFunc 复用同一响应实例"时不会误释放返回值。
 response.Dispose();
 return recovered;
 }

/// <summary>
/// 401 恢复主体（从所有权栅栏中抽出的原 <c>ExecuteAsync</c> 后半段）。
/// </summary>
/// <remarks>
/// <para>
/// <b>返回约定</b>：所有"不恢复 / 恢复失败 / 恢复耗尽"路径返回<b>原始 <paramref name="response"/></b>
/// （所有权仍归调用方，调用方可继续读取真实 401 的响应体）；仅"恢复成功"路径返回新的重试响应。
/// 调用方以 <see cref="object.ReferenceEquals"/> 判定是否发生替换，并据此决定原始响应的释放。
/// </para>
/// <para>
/// <b>异常约定</b>：本方法不得吞掉异常（取消必须沿原路径上抛），异常路径的原始响应释放由调用方栅栏负责。
/// </para>
/// </remarks>
/// <returns>恢复成功的新响应；否则返回原始响应本身。</returns>
 private async Task<HttpResponseMessage> RecoverFromUnauthorizedAsync(
 HttpResponseMessage response,
 HttpRequestMessage request,
 TokenRecoveryContext? recoveryContext,
 byte[]? contentBytes,
 bool bodyReplayable,
 Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendFunc,
 CancellationToken cancellationToken)
 {
// 记录本次发送后的最终落点 URI（BCL 在每次 30x 重定向后更新 response.RequestMessage.RequestUri）。
// 原实现比较 request.RequestUri 与由它克隆出的 retryRequest.RequestUri —— 两者同源，校验恒真形同虚设。
 var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri;

// 统一失效判定点（首次响应）——默认语义仅 HTTP 401；注册判定器后追加
// 「HTTP 200 + 业务错误码」语义（ShouldInspect 预过滤 + 按需响应体捕获，见 IsTokenInvalidAsync）。
 if (!await IsTokenInvalidAsync(response, request, cancellationToken).ConfigureAwait(false))
 return response;

// 首次发送即发生跨主机重定向 —— 放弃恢复。
// 此时若继续恢复，重试请求会被发往（或被重定向到）第三方主机，导致刷新后的令牌外泄。
// 注：Authorization / Basic 方案依赖 HttpClient 自身的跨主机剥离兜底，但自定义 Header（ApiKey 等）不在此列。
 if (!IsSameHost(request.RequestUri, finalUri))
 {
 MudHttpClientLog.TokenRecoveryRedirectDetected(
 _logger, request.RequestUri?.Host, finalUri?.Host);
 return response; // D3：返回真实 401
 }

 + R-P1-02/03：不具备无损重放能力的带体请求 → 返回真实 401，不重试、不伪造（D3）
 if (!bodyReplayable)
 {
 MudHttpClientLog.TokenRecoveryBodyNotRecoverable(
 _logger, request.Content?.Headers.ContentLength);
 return response; // D3：保留真实响应
 }

// 内存型内容的**惰性缓冲** —— 仅在确实要恢复时才读取，正常请求（无 401）零成本。
// 流式内容已在缓冲阶段取到字节（contentBytes != null），此处只处理"内存型 + 未预读"的组合。
 var replayBytes = contentBytes;
 if (replayBytes == null && request.Content != null)
 {
 if (!IsMemoryBackedContent(request.Content))
 {
// 既无缓冲字节又非内存型 ⇒ 与 bodyReplayable 判定矛盾，防御性返回真实 401。
 MudHttpClientLog.TokenRecoveryBodyNotRecoverable(
 _logger, request.Content.Headers.ContentLength);
 return response;
 }

 try
 {
 replayBytes = await ReadMemoryBackedContentAsync(request.Content, cancellationToken).ConfigureAwait(false);
 }
 catch (OperationCanceledException)
 {
 throw; // 取消沿原路径上抛（原始响应由调用方栅栏释放）
 }
 catch (Exception ex)
 {
 MudHttpClientLog.TokenInvalidationDetectionFailed(_logger, "MemoryBackedContentRead", ex);
 return response; // D3：读取失败即放弃恢复，返回真实 401
 }
 }

// /L2（P2.5，D10-A）userId 一致性校验 + 上下文 UserId 回退：
// ① TokenRecoveryContext.UserId 取自请求属性（中间层可灌入不可信输入）——与受信的
// ICurrentUserContext.UserId 不一致即拒绝（失败安全，防任意用户令牌读取原语）；
// ② 请求未携带恢复上下文但当前用户上下文有 UserId 时，补全用户级恢复路径（激活原死代码承诺）。
// 上下文缺席（后台任务等场景）不拦截。
 var contextUserId = recoveryContext?.UserId ?? _currentUserContext?.UserId;
 if (!string.IsNullOrEmpty(contextUserId))
 {
 var principalUserId = _currentUserContext?.UserId;
 if (!string.IsNullOrEmpty(principalUserId)
 && !string.Equals(principalUserId, contextUserId, StringComparison.Ordinal))
 {
 MudHttpClientLog.UserTokenIdentityMismatch(_logger, principalUserId!, contextUserId!);
 return response; // D3：不一致即拒绝——返回真实 401，不触发任何刷新
 }

 if (recoveryContext == null)
 {
// SR-L2：无显式上下文但上下文有 UserId → 构造用户级恢复上下文
 recoveryContext = new TokenRecoveryContext { UserId = contextUserId };
 }
 }

 var isUserTokenRecovery = recoveryContext != null && !string.IsNullOrEmpty(recoveryContext.UserId);

 TK-07）：优先使用请求上下文中显式写入的 TokenManagerKey 作为管理器定位键与可观测性维度；
// 未显式指定时回退到注入管理器类型的短名推断（保持旧行为）。
 var tokenManagerKey = !string.IsNullOrEmpty(recoveryContext?.TokenManagerKey)
 ? recoveryContext!.TokenManagerKey
 : (isUserTokenRecovery
 ? _userTokenManager?.GetType().Name
 : _tokenManager.GetType().Name);

 TK-06）fail-fast：用户级令牌恢复但未配置用户令牌管理器时，
// 不得静默回退到租户令牌（会造成凭据错配），直接返回 401。
 if (isUserTokenRecovery && _userTokenManager == null)
 {
 MudHttpClientLog.UserTokenRefreshFailed(_logger, recoveryContext!.UserId!,
 new InvalidOperationException("请求需要用户级令牌恢复，但未注册用户令牌管理器（IUserTokenManager）。"));
 return response; // D3：返回真实 401
 }

注册表路由：解析一次、全链路复用（失效+刷新+重试走同一管理器实例）。
// 解析失败回退注入实例 + Warning（不 fail-fast——生成器默认键场景的 401 恢复可用性优先）；
// 用户级恢复解析到非 IUserTokenManager 时同样回退（绝不用租户管理器执行用户级恢复）。
// 解析结果若被租户绑定守卫拒绝则返回 null（已记结构化告警）→ 与其它恢复失败分支一致返回真实 401。
 var resolvedCredentialManager = ResolveManager(recoveryContext);
 if (resolvedCredentialManager == null)
 return response; // D3：返回服务端真实 401

 var resolvedUserManager = isUserTokenRecovery ? ResolveUserManager(recoveryContext) : null;
 if (isUserTokenRecovery && resolvedUserManager == null)
 return response; // D3：租户守卫拒绝用户令牌管理器

// 创建令牌恢复子 Activity（mud.token.recovery）
 var recoveryActivity = MudHttpActivitySource.Instance.HasListeners()
 ? MudHttpActivitySource.Instance.StartActivity(MudHttpActivitySource.ActivityNameTokenRecovery, ActivityKind.Internal)
 : null;
 var recoverySw = Stopwatch.StartNew();
 var recoverySucceeded = false;

 if (recoveryActivity != null)
 recoveryActivity.SetTag(MudHttpActivitySource.Tags.MudTokenManagerKey, tokenManagerKey);

 try
 {
 for (var retry = 0; retry < Options.RecoveryMaxRetries; retry++)
 {
 TK-03）URI 脱敏：防止 Path/Query 注入模式令牌随日志泄漏
 MudHttpClientLog.TokenRecoveryAttempting(_logger, retry + 1, Options.RecoveryMaxRetries, request.Method.Method, RedactForLog(request, recoveryContext)); 

// 首轮之后的刷新必须绕过「结果复用窗口」——首轮所得的令牌已被服务端判为无效，
// 窗口内复用同一结果不会产生任何新令牌（的窗口语义仅适用于并发 401 去重）。
 var forceRefresh = retry > 0;
 string? newToken = null;

 if (isUserTokenRecovery)
 {
 try
 {
 newToken = await RefreshUserTokenWithDedupAsync(
 tokenManagerKey ?? "", recoveryContext!.UserId!, resolvedUserManager!, recoveryContext?.Scopes, cancellationToken, forceRefresh).ConfigureAwait(false);
 }
 catch (Exception ex)
 {
 MudHttpClientLog.UserTokenRefreshFailed(_logger, recoveryContext!.UserId!, ex);
 return response; // D3：返回真实 401
 }
 }
 else
 {
 try
 {
 newToken = await RefreshTokenWithDedupAsync(
 tokenManagerKey ?? "", resolvedCredentialManager, recoveryContext?.Scopes, cancellationToken, forceRefresh).ConfigureAwait(false);
 }
 catch (Exception ex)
 {
 MudHttpClientLog.TokenRefreshFailedInRecovery(_logger, ex);
 return response; // D3：返回真实 401
 }
 }

 if (string.IsNullOrEmpty(newToken))
 {
 MudHttpClientLog.TokenRefreshReturnedEmpty(_logger);
 return response; // D3：返回真实 401
 }

 var retryRequest = BuildRetryRequest(request, replayBytes, recoveryContext);

 TK-~redirect）+ 重试目标主机必须与「首次发送的最终落点」同源。
// 原实现比较 request.RequestUri 与克隆体 RequestUri（两者同源，恒真）；
// 现与 finalUri（已含首次重定向结果）比较，恢复真正的语义。
 if (!IsSameHost(finalUri, retryRequest.RequestUri))
 {
 MudHttpClientLog.TokenRecoveryHostMismatch(_logger, retryRequest.RequestUri?.Host, finalUri?.Host);
 if (retryRequest != request) retryRequest.Dispose();
 return response; // D3：返回真实 401
 }

// 令牌值净化 + 注入异常归一
 bool applied;
 try
 {
 if (!IsSafeTokenValue(newToken))
 {
 MudHttpClientLog.TokenInjectionUnsupported(_logger, $"CRLF_in_token");
 return response; // D3：返回真实 401
 }
 applied = ApplyTokenToRequest(retryRequest, newToken!, recoveryContext);
 }
 catch (Exception ex) when (ex is FormatException or InvalidOperationException)
 {
 MudHttpClientLog.TokenInjectionUnsupported(_logger, $"{recoveryContext?.InjectionMode}:{ex.GetType().Name}");
 applied = false;
 }
 if (!applied)
 {
 MudHttpClientLog.TokenInjectionUnsupported(_logger, recoveryContext?.InjectionMode.ToString() ?? "default");
 if (retryRequest != request) retryRequest.Dispose();
 return response; // D3：返回真实 401
 }

 HttpResponseMessage retryResponse;
 try
 {
 retryResponse = await sendFunc(retryRequest, cancellationToken).ConfigureAwait(false);
 }
 catch
 {
// 重试发送抛出（含取消 / 连接失败）时，调用方只能拿到异常而无法归还资源，
// 故在此显式释放原始 401 响应与克隆请求（原实现两者均泄漏，取消路径尤为明显），再上抛原异常。
 if (retryRequest != request) retryRequest.Dispose();
 response.Dispose();
 throw;
 }

// 重试请求在发送途中被重定向到外部主机 —— 新令牌已被发往第三方，无法挽回，
// 但必须可观测（否则安全事件完全静默）。此处只记日志，不改变返回语义。
 var retryFinalUri = retryResponse.RequestMessage?.RequestUri ?? retryRequest.RequestUri;
 if (!IsSameHost(retryRequest.RequestUri, retryFinalUri))
 {
 MudHttpClientLog.TokenRecoveryRedirectDetected(
 _logger, retryRequest.RequestUri?.Host, retryFinalUri?.Host);
 }

// 重试响应与首次响应共用同一失效判定（默认 401；判定器注册时含业务错误码），
// 防止「首轮识别失效、重试误判成功」或反向的错误。
 if (!await IsTokenInvalidAsync(retryResponse, retryRequest, cancellationToken).ConfigureAwait(false))
 {
// 重试成功：返回重试响应。
// 原始 401 的释放改由调用方栅栏按"引用未相等"统一执行（此处不再就地 Dispose）。
 recoverySucceeded = true;
 recoveryActivity?.SetTag(MudHttpActivitySource.Tags.HttpStatusCode, (int)retryResponse.StatusCode);
 if (retryRequest != request) retryRequest.Dispose();
 return retryResponse;
 }

// 重试仍判定为令牌失效（401 或业务错误码）：释放重试响应，继续下一轮或返回原始响应
 retryResponse.Dispose();
 if (retryRequest != request) retryRequest.Dispose(); // TMR-03b：修复克隆体泄漏
 }

 TK-03）URI 脱敏
 MudHttpClientLog.TokenRecoveryExhausted(_logger, Options.RecoveryMaxRetries, request.Method.Method, RedactForLog(request, recoveryContext)); 

 return response; // D3：恢复耗尽，返回真实 401
 }
 finally
 {
 recoverySw.Stop();
 var elapsedMs = recoverySw.Elapsed.TotalMilliseconds;

 if (recoveryActivity != null)
 {
 recoveryActivity.SetTag(MudHttpActivitySource.Tags.MudTokenRecoverySuccess, recoverySucceeded);
 recoveryActivity.SetTag(MudHttpActivitySource.Tags.MudTokenRecoveryElapsedMs, elapsedMs);
 if (!recoverySucceeded)
 recoveryActivity.SetStatus(ActivityStatusCode.Error, "Token recovery exhausted");
 recoveryActivity.Dispose();
 }

// 记录恢复指标（经指标 tag 白名单过滤）
 var outcome = recoverySucceeded ? "success" : "failure";
 MudHttpMeter.TokenRecoveryCounter.Add(1, MudHttpMeter.FilterTags(
 new KeyValuePair<string, object?>[]
 {
 new("token_manager_key", tokenManagerKey ?? "(unknown)"),
 new("outcome", outcome),
 }));
 }
 }


/// <summary>B5：空判定器集合的共享只读快照（避免"无判定器"热路径每次分配）。</summary>
 private static readonly List<ITokenInvalidationDetector> s_noDetectors = new(0);

}
