namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复配置选项，用于控制 401 响应时的自动令牌刷新与重试行为。
/// </summary>
public class TokenRecoveryOptions
{
    /// <summary>
    /// 配置节的名称。
    /// </summary>
    public const string SectionName = "MudHttpTokenRecovery";

    /// <summary>
    /// 获取或设置是否启用令牌恢复机制，默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置令牌恢复的最大重试次数，默认 1。
    /// 设置为 0 可禁用恢复重试。必须大于等于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public int RecoveryMaxRetries
    {
        get => _recoveryMaxRetries;
        set => _recoveryMaxRetries = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(RecoveryMaxRetries), "最大重试次数不能为负数。");
    }
    private int _recoveryMaxRetries = 1;

    private string _tokenScheme = "Bearer";

    /// <summary>
    /// 获取或设置令牌的认证方案（如 "Bearer"、"Basic"），默认 "Bearer"。
    /// 不能为 null 或空字符串。
    /// </summary>
    /// <exception cref="ArgumentException">设置 null 或空字符串时抛出。</exception>
    public string TokenScheme
    {
        get => _tokenScheme;
        set => _tokenScheme = !string.IsNullOrEmpty(value) ? value : throw new ArgumentException("令牌认证方案不能为 null 或空字符串。", nameof(TokenScheme));
    }

    /// <summary>
    /// P1.4（TK-15）令牌刷新的超时兜底（秒），默认 30。
    /// 取消隔离后刷新任务不再受单一调用方取消影响，本超时防止远端挂起导致恢复流程无限期阻塞；
    /// 超时后抛出 <see cref="TimeoutException"/>，恢复流程按刷新失败处理并返回 401。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public double RefreshTimeoutSeconds
    {
        get => _refreshTimeoutSeconds;
        set => _refreshTimeoutSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(RefreshTimeoutSeconds), "刷新超时秒数必须大于 0。");
    }
    private double _refreshTimeoutSeconds = 30;

    /// <summary>
    /// I3（R-P0-02）等待令牌刷新的硬墙钟（秒）。默认 <c>0</c> 表示<b>自动</b>：取
    /// <see cref="RefreshTimeoutSeconds"/> + 5 秒余量（保证协作式超时先于硬墙钟生效）。
    /// <para>
    /// 语义：单个等待者对共享刷新的最长等待时间（超时抛 <see cref="TimeoutException"/>）；
    /// 同时也是<b>共享刷新自身</b>的取消预算 —— 防止第三方 <c>ITokenManager</c> 不响应取消时
    /// 刷新任务永久挂起。等待超时/取消后条目会出表或标记废弃，保证后续 401 可重新刷新。
    /// </para>
    /// <para>设为正数时以其为准（应大于 <see cref="RefreshTimeoutSeconds"/>，否则协作式超时不再有机会生效）。</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public double RefreshWaitHardTimeoutSeconds
    {
        get => _refreshWaitHardTimeoutSeconds;
        set => _refreshWaitHardTimeoutSeconds = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(RefreshWaitHardTimeoutSeconds), "等待硬超时秒数不能为负数。");
    }
    private double _refreshWaitHardTimeoutSeconds;

    /// <summary>
    /// 401 恢复重试可缓冲的请求体最大字节数，默认 1MB（1 * 1024 * 1024）。
    /// <para>
    /// <b>三态体处理模型</b>（D1 修订）：
    /// <list type="bullet">
    /// <item><b>无体</b>（<c>Content == null</c>）：正常发送 + 正常恢复。</item>
    /// <item><b>可缓冲</b>（<c>0 &lt; limit</c> 且读取未超限）：首次发送用缓冲回填的 <see cref="System.Net.Http.ByteArrayContent"/>（可重放），401 后用同一份字节重试。</item>
    /// <item><b>不可缓冲</b>（超限 / <c>limit == 0</c>）：原样发送原内容（不做任何预读改写），401 后返回真实 401 响应，记 <c>TokenRecoveryBodyNotRecoverable</c> 事件。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 设为 <c>0</c> 表示<b>流式优先模式</b>：不缓冲请求体、不进行 401 重试，但请求正常发送。
    /// 适用于超大上传场景，避免内存峰值。
    /// </para>
    /// <para>
    /// 内存代价：并发体缓冲峰值 = 并发数 × min(体大小, limit)。默认 1MB × 100 并发 = 100MB 瞬时分配。
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public long MaxCachedRequestBodyBytes
    {
        get => _maxCachedRequestBodyBytes;
        set => _maxCachedRequestBodyBytes = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxCachedRequestBodyBytes), "请求体缓冲上限不能为负数。");
    }
    private long _maxCachedRequestBodyBytes = 1 * 1024 * 1024;

    /// <summary>
    /// R-P1-03：401 恢复链路的请求体缓冲策略，默认 <see cref="RequestBodyBufferingMode.Auto"/>。
    /// </summary>
    /// <remarks>
    /// <para>"不缓冲"由 <see cref="MaxCachedRequestBodyBytes"/> = 0 表达（该值下本属性无实际作用）。</para>
    /// <para>
    /// 枚举/引用类型属性不参与 <c>IConfiguration</c> 绑定的既有约定保持不变 ——
    /// 本属性通过<b>编程式配置</b>设置（<c>services.Configure&lt;TokenRecoveryOptions&gt;(o =&gt; ...)</c>）。
    /// </para>
    /// </remarks>
    public RequestBodyBufferingMode BufferingMode { get; set; } = RequestBodyBufferingMode.Auto;

    /// <summary>
    /// R-P1-04：是否允许对<b>非幂等</b>方法（POST/PATCH 等）执行 401 重放。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>破坏性行为变更（2.1.0）</b>：2.0.x 对任意方法（含 POST）自动重放 401 请求。
    /// 重放可能造成重复下单 / 重复扣款等业务副作用，故默认收窄为"仅幂等方法"
    /// （GET/HEAD/OPTIONS/TRACE/PUT/DELETE），与 Resilience 侧的重试白名单语义对齐。
    /// </para>
    /// <para>
    /// 置 <c>true</c> 可恢复 2.0.x 行为 —— <b>仅当服务端保证"401 必然未处理请求"</b>时才应开启
    /// （例如网关在鉴权阶段即拒绝、请求未到达业务逻辑）。
    /// </para>
    /// <para>
    /// 更精细的做法是契约级放行：在接口 / 方法上声明可安全重放（生成器写入
    /// <see cref="TokenRecoveryContext.IsRetryAllowedExplicitly"/>），仅对确实幂等的 POST 开洞。
    /// </para>
    /// </remarks>
    public bool AllowNonIdempotentRecovery { get; set; }

    /// <summary>
    /// TMR-12：令牌刷新去重窗口（秒），默认 2。
    /// <para>
    /// 在去重窗口内，多个并发 401 共享同一次刷新结果，避免刷新风暴。
    /// 窗口过期后，后续 401 会触发新一轮刷新。
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public double RefreshDedupWindowSeconds
    {
        get => _refreshDedupWindowSeconds;
        set => _refreshDedupWindowSeconds = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(RefreshDedupWindowSeconds), "去重窗口秒数不能为负数。");
    }
    private double _refreshDedupWindowSeconds = 2;

    /// <summary>
    /// MT-06：刷新去重表的条目数上限，默认 1024。
    /// <para>
    /// 去重键为 <c>managerKey + US + [userId + US +] scopeKey</c>。用户级恢复时键中含 userId，
    /// 在用户基数大 + 401 风暴的场景下若无上限会构成无界内存增长。
    /// 超过上限时先清除已过期条目，仍超限则按枚举顺序淘汰多余条目（仅保证有界性，不保证精确 LRU）。
    /// </para>
    /// <para>设为较小时会以「更早失去去重保护」换取更小的内存占用；设为 1 等价于几乎不去重。</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int MaxDedupEntries
    {
        get => _maxDedupEntries;
        set => _maxDedupEntries = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxDedupEntries), "去重表条目上限必须大于 0。");
    }
    private int _maxDedupEntries = 1024;

    /// <summary>
    /// WX-01（Phase A，errcode 令牌失效恢复）：令牌失效判定器（可选，默认 null）。
    /// <para>
    /// 注册后，恢复执行器在「HTTP 401」默认语义之外，按判定器识别以业务错误码表达令牌失效的
    /// 平台响应（如企业微信恒返 HTTP 200 + <c>errcode</c> ∈ {40014, 42001, 42007, 42009, 42011}），
    /// 识别为失效即进入与 401 一致的「失效缓存令牌 → 刷新 → 重试」链路。
    /// </para>
    /// <para>
    /// 本属性为编程式注入（委托 / 接口实例不可经配置绑定），典型落点是平台 SDK 在命名客户端 /
    /// 全局注册时的 <c>PostConfigure</c> 中赋值。为 null 时行为与既有版本逐字节等价（仅认 401）。
    /// </para>
    /// <para>
    /// <b>多产品线共存请优先使用 <see cref="AdditionalTokenInvalidationDetectors"/></b>：
    /// 本属性为<b>单槽</b>，多个产品线各自在 <c>PostConfigure</c> 赋值会互相覆盖（后者胜），
    /// 导致某一产品线的恢复静默失效。
    /// </para>
    /// </summary>
    public ITokenInvalidationDetector? TokenInvalidationDetector { get; set; }

    /// <summary>
    /// B5（WX-01 扩展）：<b>追加式</b>令牌失效判定器集合（编程式注入；与
    /// <see cref="TokenInvalidationDetector"/> 为<b>并集</b>语义，任一命中即判定为失效）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为何是"只读集合属性 + Add"而非可写属性</b>：多产品线各自 <c>Add</c> 时互不覆盖；
    /// 若为可写属性则退化为单槽覆盖问题（等价于 <see cref="TokenInvalidationDetector"/>）。
    /// </para>
    /// <para>
    /// 求值顺序固定为「先 <see cref="TokenInvalidationDetector"/>（若存在），再按本集合的 Add 顺序」，
    /// 短路返回首个判定为失效者；<see cref="ITokenInvalidationDetector.ShouldInspect"/> 为 <c>false</c> 者跳过
    /// （响应体捕获仅在首个需要检查的判定器处发生一次）。
    /// </para>
    /// <para>
    /// 本集合为编程式注入（<c>IConfiguration</c> 反射绑定器对「接口元素集合」会静默跳过），
    /// 与 <see cref="TokenInvalidationDetector"/> 同约定：请在 <c>Configure</c>/<c>PostConfigure</c>
    /// 或命名客户端的编程式配置中 <c>Add</c>。集合为空且单槽为 null 时行为与既有版本逐字节等价（仅认 401）。
    /// </para>
    /// <para>
    /// 需要组合多个判定器为一个实例（例如经 DI 单例统一注册）时，可用
    /// <see cref="CompositeTokenInvalidationDetector"/> 包装后赋给 <see cref="TokenInvalidationDetector"/>。
    /// </para>
    /// </remarks>
    public IList<ITokenInvalidationDetector> AdditionalTokenInvalidationDetectors { get; }
        = new List<ITokenInvalidationDetector>();

    /// <summary>
    /// WX-01（Phase A）：判定器检查响应时可捕获的响应体最大字节数，默认 4096（4KB）。
    /// <para>
    /// 仅当响应声明 Content-Length 且 ≤ 本上限时才会读流捕获（读毕以等价可读内容替换原内容，
    /// 调用方无感）；声明超限 / 未知长度（chunked）/ 空体一律不读流，此时判定器收到
    /// <c>body = null</c>，应回退到仅凭状态码判定。
    /// </para>
    /// <para>
    /// 与 <see cref="MaxCachedRequestBodyBytes"/>（请求体三态模型）正交，两者独立配置。
    /// 设为 <c>0</c> 表示禁用响应体捕获（判定器仅凭状态码工作）。
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public int MaxCapturedResponseBodyBytes
    {
        get => _maxCapturedResponseBodyBytes;
        set => _maxCapturedResponseBodyBytes = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxCapturedResponseBodyBytes), "响应体捕获上限不能为负数。");
    }
    private int _maxCapturedResponseBodyBytes = 4096;
}
