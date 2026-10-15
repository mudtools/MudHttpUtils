# HttpClient 审查修复与功能完善方案（M6）

> **文档版本**: v1.0（待评审）
> **创建日期**: 2026-09-22
> **状态**: ⏳ 待评审 · 分阶段实施
> **作者**: Mud Studio
> **审查来源**: 三主线专项审查（性能 > 安全 > 功能，2026-09-22 完成，第 4 轮）。审查范围覆盖 Client / Resilience / Generator 约 40 个核心文件，8 个文件簇并行深读 + 关键问题二次行号核验。
> **关联文档**:
>
> - [HttpClient审查修复与功能完善方案-M5.md](./HttpClient审查修复与功能完善方案-M5.md)（M5 轮次，HC-01~HC-14）
> - [HttpClient审查修复与功能完善方案-M4.md](./HttpClient审查修复与功能完善方案-M4.md)（M4 轮次，H-x/F-x）
> - [AES-信封版本前缀歧义消除方案.md](./AES-信封版本前缀歧义消除方案.md)（M5-HC-13 版本号申请 → 本方案 HC-26 需复核其兼容性声明）

---

## 一、背景与目标

### 1.1 审查结论摘要

本轮按三主线（性能 > 安全 > 功能、12 维度）完成全量审查，总体结论：

- **风险等级：中高**。发现 **2 项 🔴 严重缺陷**：其一为安全防线被 IPv4 映射型 IPv6 地址绕过（URL 层与连接期**两道 SSRF 防线同时失效**）；其二为生成器 `[HeaderCollection]` 零接线导致字典请求头**静默丢失**（数据错误类）。
- **11 项 🟠 重要缺陷**：安全 4（SSRF 纵深防御缺失、回环豁免"任一地址"语义、自动重定向不复验、PII 正则锚点失效）+ 性能 2（缓存 fetch 锁回收竞态破坏单飞、白名单 HTTP 主机 sync-over-async DNS）+ 功能 5（错误体绕过限量、OAuth2 授权码缺客户端认证、重试克隆静默空体、下载 IOException 误判、EventId 撞号）。
- **约 20 项 🟡 一般问题 + 15 项 🔵 建议**：跨区域 culture 分叉、弹性语义边界（CB 键碎片化/缓存超限失效）、401 恢复空转、可观测性门控缺口等。
- M4/M5 已修复项经复核**确认全部生效**（SuccessResponseGuardStream BOM 回卷、LimitedContentReader 限量、H-6 连接期再验本体、H-7 克隆取消、H-9 流式连接期超时、JsonOptionsAot 全 TFM 合并、keyed 注册 net6、异常包装链顺序、`__mud_observed` 去重、双层弹性防重、AES 核心实现、生产代码零 `new HttpClient()`）。

### 1.2 方案目标

1. 清除 2 项 🔴：**SSRF 双防线绕过**（HC-01）与 **`[HeaderCollection]` 静默丢失**（HC-02）。
2. 关闭 4 个安全绕过/泄露入口（HC-03 ~ HC-06）。
3. 恢复两项性能防护的既定语义：缓存单飞（HC-07）与发送路径零 sync-over-async（HC-08）。
4. 修复 5 项功能性数据/行为错误（HC-09 ~ HC-13）。
5. P2 批次消除跨区域行为分叉、弹性语义缺口与可观测性开销；P3 收编建议项。
6. 全部修复项配套验收测试，纳入 8 测试工程回归体系与 `test.ps1`。

### 1.3 设计原则（延续 M5 §1.3）

1. **能力复用优先**：修复全部落在既有模块的既有机制上（见 §1.4），不引入平行机制。
2. **依赖方向不变**：`Resilience → Client → Abstractions`。
3. **多 TFM 语义一致**：ns2.0 与 net6+ 条件编译分支行为必须一致；确不可一致者显式声明（如 HC-28 ns2.0 条件移除）。
4. **零运行时反射**：核心路径新增代码不得引入反射（HC-18 的 Format 缓存以 `ConcurrentDictionary<ParameterInfo, string?>` 实现，属元数据缓存，编译期可裁剪）。
5. **失败快于静默**：HC-02 接线后立即生效且无诊断盲区；HC-11 不可重放一律显式降级，不接受静默空体。
6. **无兼容负担**：项目未发布、无兼容义务（同 M4 §1.3），但 HC-26 的 0x04 密文对**运行旧版库的对端**存在真实兼容窗口，必须发布说明标注。

### 1.4 既有架构能力盘点（修复项的复用基础）

| 能力                  | 位置                                                                                                                                                                            | 本方案复用点                                            |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| 私网判定单点          | `UrlValidator.IsPrivateIpAddress`（`Client/Helpers/UrlValidator.cs:498-514`），URL 层与 `DefaultIpAddressPolicy.IsAllowed`（`Client/Http/DefaultIpAddressPolicy.cs:22-23`）同源 | HC-01 单点修复，双防线同时受益                          |
| 生成器 Binder 分派    | `IParameterBinder.CanBind` + `RequestBuilder.GenerateHeaderParameters`（`Generator/Generators/Implementation/RequestBuilder.cs:331-342`）                                       | HC-02 按 CanBind 分派 `HeaderCollectionParameterBinder` |
| 错误体限量读取        | `DefaultHttpRequestExecutor.ReadErrorContentLimitedAsync`（`:365` 调用点）+ `LimitedContentReader`                                                                              | HC-09 非 2xx 分支复用                                   |
| 客户端认证            | `StandardOAuth2TokenManager.ApplyClientAuthentication` / `SendTokenRequestAsync(useClientAuth:)`（`:528-531`，Password/Refresh 流已用）                                         | HC-10 授权码流对齐                                      |
| 克隆预判框架          | `HttpRequestMessageCloner.ShouldSkipRetryForContent`（`:99-103`）+ `BufferUpToAsync`（`:209-232`）+ `MaxCloneContentSize`                                                       | HC-11 补全不可 seek 判定                                |
| 下载原子落盘模板      | `DefaultHttpRequestExecutor.DownloadLargeCoreAsync` 的 `.mudtmp` + `File.Move`（G7-03）与 bufferSize 4MiB 钳制（G8-06，`:549/592`）                                             | HC-12/HC-14 对齐                                        |
| 进度节流范例          | `EnhancedHttpClient` 下载 100ms 节流（NEW-HC-09，`:1779-1812`）                                                                                                                 | HC-15 移植至执行器与上传                                |
| 连接期校验开关        | `AddMudHttpClientSsrfProtection(builder)`（`Client/ServiceCollectionExtensions.cs:120-128`）+ 未启用 Warning（`MudHttpClientLog.cs:420`）                                       | HC-03 默认化落点                                        |
| 策略缓存              | `PollyResiliencePolicyProvider._policyCache` + `PolicyCacheKey`（`:268-272`）                                                                                                   | HC-22 键维度调整 + LRU 驱逐                             |
| 去重窗口              | `RefreshDedupTable`（"条目即任务"，`:57-58`）                                                                                                                                   | HC-23 重试循环绕行                                      |
| 头值校验              | `HttpHeaderValueValidator.IsValid`（逐字符 IndexOf 零分配，`:25-32`）                                                                                                           | HC-24 扩展 C0/DEL + 注入点接入                          |
| 诊断基建              | `Diagnostics.cs` / `AnalyzerReleases.Unshipped.md` / Verify 快照                                                                                                                | HC-02/HC-29/HC-30 新诊断登记                            |
| EventId 注册表        | `MudHttpClientLog`（[LoggerMessage] + Define 双轨）                                                                                                                             | HC-13 号段迁移 + 唯一性守护                             |
| PublicAPI strict 门禁 | `Directory.Build.targets` + 8 发布库 25 份基线                                                                                                                                  | 见 §七                                                  |

---

## 二、问题清单总览

> 编号规则：`HC-xx` 为本轮（M6）独立编号，与 `#1~#29`（M1~M3）、`CFG-xx`、`H-x/F-x`（M4）、M5 `HC-01~14` **互不通用**。
> 优先级：P0（阻塞合并）→ P1（本里程碑必须）→ P2（本里程碑内实施）→ P3（backlog）。
> 审查报告中"位置"均经二次行号核验（标注 ⚠ 者依赖子任务高置信引用，实施时先复核现场）。

| 编号  | 审查级别 | 优先级 | 类型        | 标题                                                                       | 主要涉及项目     |
| ----- | -------- | ------ | ----------- | -------------------------------------------------------------------------- | ---------------- |
| HC-01 | 🔴       | P0     | 安全        | IPv4 映射型 IPv6 绕过私网判定（双防线失效）                                | Client           |
| HC-02 | 🔴       | P0     | 功能        | `[HeaderCollection]` 生成器零接线，字典请求头静默丢失                      | Generator        |
| HC-03 | 🟠       | P1     | 安全        | 连接期 SSRF 校验默认未启用（纵深防御缺失）                                 | Client           |
| HC-04 | 🟠       | P1     | 安全        | 回环豁免"任一地址"语义，混合记录绕过私网校验                               | Client           |
| HC-05 | 🟠       | P1     | 安全        | 自动重定向跨主机不做复验                                                   | Client           |
| HC-06 | 🟠       | P1     | 安全/脱敏   | PII 正则 `^...$` 锚点致嵌入文本不掩码                                      | Client           |
| HC-07 | 🟠       | P1     | 性能        | fetch 锁回收竞态破坏缓存单飞                                               | Client           |
| HC-08 | 🟠       | P1     | 性能        | 白名单 HTTP 主机发送路径 sync-over-async DNS                               | Client           |
| HC-09 | 🟠       | P1     | 功能        | `SendAsResponseAsync` 错误体绕过 `MaxExceptionContentLength`               | Client           |
| HC-10 | 🟠       | P1     | 功能/安全   | authorization_code 换令牌缺客户端认证（RFC 6749 §4.1.3）                   | Client           |
| HC-11 | 🟠       | P1     | 功能        | 重试克隆不可重放预判漏普通 StreamContent，静默空体                         | Resilience       |
| HC-12 | 🟠       | P1     | 功能        | `DownloadAsync` IOException 一刀切重抛，残留半写文件                       | Client           |
| HC-13 | 🟠       | P1     | 功能/可观测 | EventId 170/171 双定义撞号 + 规划注释漂移                                  | Client           |
| HC-14 | 🟡       | P2     | 功能        | 公有下载 bufferSize 无上界钳制 + 非 `.mudtmp` 落盘                         | Client           |
| HC-15 | 🟡       | P2     | 性能        | 下载/上传进度回调无 100ms 节流（执行器与上传路径）                         | Client           |
| HC-16 | 🟡       | P2     | 性能        | LRU 淘汰每次 Set 全量 O(N log N) 排序                                      | Client           |
| HC-17 | 🟡       | P2     | 性能        | 指标 tag/诊断事件未按消费方存在性门控                                      | Client           |
| HC-18 | 🟡       | P2     | 功能        | URL 参数 CurrentCulture 分叉（formatter + 生成器三处）                     | Client/Generator |
| HC-19 | 🟡       | P2     | 功能        | `[QueryMap]` 索引器崩溃 + `UrlEncode=false` 语义分裂                       | Client           |
| HC-20 | 🟡       | P2     | 功能        | DI 注入 `JsonSerializerOptions` 非 resolver 设置被静默丢弃                 | Client           |
| HC-21 | 🟡       | P2     | 安全/脱敏   | `ApiException.RequestUri` 未脱敏（与 `ApiRequestException` 口径不一）      | Client           |
| HC-22 | 🟡       | P2     | 功能        | CB 缓存键含 TResult 碎片化 + 策略缓存超限熔断失效                          | Resilience       |
| HC-23 | 🟡       | P2     | 功能        | 401 恢复重试循环复用被拒令牌空转 + 异常路径 response 泄漏                  | Client           |
| HC-24 | 🟡       | P2     | 安全        | Token 注入不经头值校验；C0/DEL 控制字符未拒绝                              | Client           |
| HC-25 | 🟡       | P2     | 安全        | HMAC 签名无时间戳/nonce 防重放（决策点 D4）                                | Client           |
| HC-26 | 🟡       | P2     | 安全/功能   | AES v4 配置不匹配误报完整性失败 + 0x04 默认迁移声明                        | Client           |
| HC-27 | 🟡       | P2     | 安全/脱敏   | `SensitiveUrlRedactor` 不处理 userinfo 凭据                                | Client           |
| HC-28 | 🟡       | P2     | 功能        | TokenStore ns2.0 条件移除竞态 + 用户桶无界增长                             | Client           |
| HC-29 | 🟡       | P2     | 功能/AOT    | IAsyncEnumerable 生成调用恒传 null JsonTypeInfo，无编译期提示（决策点 D5） | Generator        |
| HC-30 | 🟡       | P2     | 安全        | 生成器非 string Header 发射点未经校验                                      | Generator        |
| HC-31 | 🟡       | P2     | 性能/清理   | 序列化器实例复用、ConfigureAwait 补齐、捕获截断守卫等零散项                | Client           |

### 决策点（需评审拍板）

| #   | 决策点                        | 关联  | 选项                                                                                                                                 | 倾向                                                                          |
| --- | ----------------------------- | ----- | ------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| D1  | 连接期 SSRF 校验默认启用范围  | HC-03 | A. 仅严格模式（`AllowCustomBaseUrls=false`）默认注册；B. 全局默认注册 + 回环豁免文档化                                               | **A**（回环豁免语义留给 `IIpAddressPolicy` 自定义，避免破坏本地调试默认体验） |
| D2  | 自动重定向策略                | HC-05 | A. 主链路 `AllowAutoRedirect=false` + 手动逐跳复验（破坏性，行为变化大）；B. 保持自动重定向，文档声明残余风险 + 重定向目标域过白名单 | **A**（未发布无兼容义务；复验逻辑可复用 `IsSameHost`+白名单判定）             |
| D3  | 序列化器注入设置语义          | HC-20 | A. 合并 injected 全量设置（NamingPolicy/Converters 生效，需处理与内置 Context 的冲突）；B. 维持仅 resolver 合并 + XML 文档明示       | **A**（消费方意图优先；内置 Context 经 Combine 兜底，冲突时 injected 优先）   |
| D4  | HMAC 防重放协议增强           | HC-25 | A. 加 `X-Timestamp`+`X-Nonce` 入签（服务端需协同改造）；B. 维持现状 + 文档声明服务端自行去重                                         | **A**，但作为可选开关（`RequireAntiReplay`，默认 false），避免破坏既有服务端  |
| D5  | IAsyncEnumerable AOT 提示形式 | HC-29 | A. 新增 Warning 诊断（默认触发，可 pragma 抑制）；B. Info 级提示                                                                     | **A**（失败模式是静默 default，必须可见）                                     |

---

## 三、P0 阻塞修复方案

### HC-01 IPv4 映射型 IPv6 绕过私网判定

- **位置**: [UrlValidator.cs:498-514](../Mud.HttpUtils.Client/Helpers/UrlValidator.cs)；网段表 `:158-168`；下游 `DefaultIpAddressPolicy.cs:22-23`。
- **根因**: `IsPrivateIpAddress` 未做 IPv4 映射归一。`::ffff:10.0.0.1` 为 `InterNetworkV6`，不命中 IPv4 网段（`IPNetwork.Contains` 按 AddressFamily 短路）、不命中 `::1/fc00::/7/fe80::/10`，`IPAddress.IsLoopback(::ffff:127.0.0.1)` 亦为 False（已运行时验证）。
- **影响**: 白名单域名被恶意 DNS 返回映射 AAAA 时，URL 层与连接期 `ConnectCallback`（经 `DefaultIpAddressPolicy`）**双双放行**，实连内网 IPv4；`SsrfSafeSocketsHttpHandler` 对 InterNetworkV6 采用双模 socket，OS 按 IPv4 实连。
- **修复**（单点，双防线同时受益）:

```csharp
internal static bool IsPrivateIpAddress(IPAddress ipAddress)
{
    if (ipAddress.IsIPv4MappedToIPv6)
        return IsPrivateIpAddress(ipAddress.MapToIPv4());

    if (ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal ||
        IPAddress.IsLoopback(ipAddress))
        return true;
    // ……其余不变
}
```

- **注意**: `IsIPv4MappedToIPv6` 在 ns2.0 可用（`IPAddress` 基础成员），无 TFM 分叉；`MapToIPv4` 同。`IsLoopbackAddress(host)`（字面量分支 `:524-525`）无需改动——映射回环经 `IPAddress.IsLoopback` 对映射地址返回 False 属**预期**（归一后由 `IsPrivateIpAddress` 拒绝，语义不变：回环豁免仅针对真回环）。
- **测试**: `SsrfProtectionTests` 新增：`IsPrivateIpAddress(IPAddress.Parse("::ffff:10.0.0.1"))` 必真、`::ffff:127.0.0.1` 必真、`::ffff:8.8.8.8` 必假；`DefaultIpAddressPolicy.IsAllowed` 同步断言；端到端 StubHttp 场景可选。

### HC-02 `[HeaderCollection]` 生成器零接线

- **位置**: [RequestBuilder.cs:331-342](../Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs)；[HeaderCollectionParameterBinder.cs](../Mud.HttpUtils.Generator/Generators/Implementation/Binders/HeaderCollectionParameterBinder.cs)（全项目仅 1 处命中 = 自身定义，零调用点）。
- **根因**: `GenerateHeaderParameters` 只 new 了 `HeaderParameterBinder` 并按其 `CanBind` 过滤，`HeaderCollectionParameterBinder` 从未进入分派。
- **影响**: `[HeaderCollection] Dictionary<string,string>` 参数被生成代码完全忽略（鉴权/租户头不发射），且无编译期提示——属静默数据错误。
- **修复**:
  1. `GenerateHeaderParameters` 改为按 `CanBind` 优先级分派：先 `HeaderParameterBinder.CanBind`，否则 `HeaderCollectionParameterBinder.CanBind` → 走集合发射（Key/Value 逐项经 `HttpHeaderValueValidator.IsValid`，不合格跳过并按 HC-30 口径记 Debug）。
  2. 新增诊断 `MUDGEN2xx`（Info）：参数未匹配任何 Header Binder 且名字含 "header" 时提示拼写/特性遗漏（低噪音，可评审裁剪）。
  3. `AnalyzerReleases.Unshipped.md` 登记新诊断。
- **注意**: 接线后 `HeaderCollectionParameterBinder` 生成代码（`:26-30` 的逐项 `Headers.Add`）必须同步接入校验（与 HC-30 合并实施），否则接线即引入 CRLF 注入面（ns2.0 不校验头值）。
- **测试**: Generator.Tests 新增 Verify 快照（`[HeaderCollection]` 单参数/与 `[Header]` 混用/空字典）；Client.Tests 端到端断言头到达 StubHttp。

---

## 四、P1 重要修复方案

### HC-03 连接期 SSRF 校验默认启用（决策点 D1）

- **位置**: [ServiceCollectionExtensions.cs:109-129](../Mud.HttpUtils.Client/ServiceCollectionExtensions.cs)；`SsrfSafeSocketsHttpHandler.cs:8`（`#if NET6_0_OR_GREATER`）。
- **方案**（按 D1-A）: 命名客户端注册路径（`RegisterNamedClient`）在 `AllowCustomBaseUrls=false`（严格模式）且 TFM ≥ net6 时，自动 `ConfigurePrimaryHttpMessageHandler(new SsrfSafeSocketsHttpHandler(...))`（幂等：已显式调用过 `AddMudHttpClientSsrfProtection(builder)` 则跳过，用 `HttpMessageHandlerBuilder` 特征或注册表标记判定）。严格模式以外的场景维持 Warning 提示（既有 `MudHttpClientLog.cs:420`）。ns2.0 保持现状 + README/XML 文档声明"连接期防线不可用，仅 URL 层校验"。
- **注意**: `ConfigurePrimaryHttpMessageHandler` 重复注册会叠加 handler，幂等判定必须落实（MT-23 教训）；`IIpAddressPolicy` 未注册时走 `TryAddSingleton<IIpAddressPolicy, DefaultIpAddressPolicy>` 兜底（`:100-107` 既有）。
- **测试**: 严格模式下解析出的 primary handler 为 `SsrfSafeSocketsHttpHandler`；显式 opt-in 不双重注册；非严格模式行为不变。

### HC-04 回环豁免改为"全部地址为回环"

- **位置**: [UrlValidator.cs:373-379](../Mud.HttpUtils.Client/Helpers/UrlValidator.cs)（异步版；同步版 `:308-311` 同步修），底层 `:395/:418` 的 `Any()` 语义。
- **修复**: 豁免条件改为 `addresses.Length > 0 && addresses.All(IPAddress.IsLoopback)`；混合记录 `[回环, 私网]` 一律拒绝。两个候选 IP 本就都会被 `IsPrivateIpAddress` 拒绝，本修复仅修正**豁免判定**，不改拒绝行为。
- **测试**: 混合记录主机（`DnsResolveOverride` 注入）必拒；纯回环主机豁免保持。

### HC-05 自动重定向跨主机复验（决策点 D2）

- **位置**: [EnhancedHttpClient.cs:1879-1884](../Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs)（URL 层校验仅覆盖初始请求）；全库无 `AllowAutoRedirect=false`。
- **方案**（按 D2-A）:
  1. 主链路 handler（含 `SsrfSafeSocketsHttpHandler` 基类与工厂默认 handler）设置 `AllowAutoRedirect=false`。
  2. 新增内部重定向循环（包装在 `SendRawAsync` 的 handler 层或执行器层）：3xx 且 `Location` 存在时，先过 `UrlValidator.ValidateUrlAsync`（白名单/HTTPS/私网全套）+ 同源策略（复用 `TokenRecoveryExecutor.IsSameHost` 口径：scheme+host+port），再以**剥离 Authorization/Cookie 后**的新请求转发（重定向剥离语义对齐 .NET 内建）；上限 10 跳，超限抛 `InvalidOperationException`。
  3. 307/308 需按方法保留语义：非幂等方法重定向需重新走 HC-11 的可重放判定（不可重放则中止并抛出）。
- **注意**: 破坏性行为变更，需在 README「破坏性变更」章节声明；`TokenRecoveryExecutor` 的重定向观测逻辑（MT-01）与本循环对齐口径。
- **测试**: 白名单域名 302 → 内网 IP 必拒；302 → 非白名单主机必拒；同主机 302 正常跟随且 Authorization 已剥离；307 POST 不可重放体中止。

### HC-06 PII 正则锚点修正

- **位置**: [MessageSanitizer.cs:76-83](../Mud.HttpUtils.Client/Helpers/MessageSanitizer.cs)（NET7+ GeneratedRegex）与 `:95-97`（回退 Regex），同改。
- **修复**: 去掉 `^...$` 改为查找式 + 边界断言：
  - 手机号: `(?<![0-9])1[3-9]\d{9}(?![0-9])`
  - 邮箱: `(?<![\w.+-])[\w.+-]+@[\w-]+\.[\w.-]+(?<!\.)`（简化，避免过度捕获）
  - 身份证: `(?<![0-9Xx])\d{17}[\dXx](?![0-9Xx])`
- **注意**: 无嵌套量词，维持无 ReDoS 结论；ns2.0 回退分支同步；`.NET` 的 `Regex` 后行断言固定长度要求满足（各为单字符类）。
- **测试**: `"手机号13812345678已注册"`、"a@b.com 主体"、长数字串中嵌入身份证均被掩码；普通长数字（订单号 18 位）误伤评估（身份证 18 位与订单号冲突面小，可接受）。

### HC-07 fetch 锁回收竞态修复

- **位置**: [MemoryHttpResponseCache.cs:129-132](../Mud.HttpUtils.Client/HttpClient/MemoryHttpResponseCache.cs)（GetOrAdd 后窗口）、`:189-208`（Prune）、`:225-231`（Timer 同逻辑）。
- **根因**: 回收判定 `!_cache.ContainsKey(key)` 会命中"正在回源"的键（回源中键必然不在 `_cache`），注释前提"剩余键均活跃回源中"不成立；GetOrAdd 与 WaitAsync 之间的窗口内锁可被他人回收。
- **修复**:
  1. 回收条件追加 `kvp.Value.CurrentCount == 0`（无人持锁才可回收）。
  2. GetOrAdd 后校验拿到的锁仍在字典中（`TryGetValue(key, out var l) && ReferenceEquals(l, fetchLock)`），不成立则重取（自旋上限 2 次后放弃单飞直接回源，正确性不受影响）。
  3. Timer 与 Prune 共用同一私有方法，消除双份逻辑。
- **测试**: 并发压测——N 线程同 key 回源 + 后台持续触发超限回收，断言 fetchFunc 执行次数为 1（现实现可稳定复现 >1）。

### HC-08 白名单 HTTP 主机异步化

- **位置**: [UrlValidator.cs:49-64](../Mud.HttpUtils.Client/Helpers/UrlValidator.cs)（`EnsureWhitelistedHostIsSecure`，同步）→ `IsLoopbackAddress`（`:519-536`）→ `ResolveWithCache`（`:93-118`，`stripe.Wait()` + `GetAwaiter().GetResult()`）。
- **修复**: `EnsureWhitelistedHostIsSecure` 增加异步版 `EnsureWhitelistedHostIsSecureAsync`，内部用既有 `IsLoopbackAddressAsync`（`:120+` 异步条带锁路径）；调用点 `ValidateUrlAsync` 切换到异步版。同步版仅供同步 `ValidateUrl`（兼容入口）保留，并在 XML 文档标注"同步路径会阻塞"。
- **测试**: 白名单 + HTTP + 主机名场景下，`ValidateUrlAsync` 在自定义 SynchronizationContext 上不回跳（死锁探测测试）。

### HC-09 `SendAsResponseAsync` 错误体限量

- **位置**: [DefaultHttpRequestExecutor.cs:279-281](../Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs)（无条件 `ReadContentAsync`）、`:340-343`（非 2xx 直接返回 rawContent）。
- **修复**: 拆分读取路径——2xx 维持 `ReadContentAsync`（成功体守卫语义不变）；非 2xx 改走 `ReadErrorContentLimitedAsync`（`:365` 既有，受 `MaxExceptionContentLength` 三态约束）。`Response<T>.ErrorContent` 字段填充限量内容（与 `SendAndDeserializeAsync` 口径一致）。
- **注意**: `EnableDecrypt` 场景非 2xx 本不解密，无影响；`Response<string>` 2xx 路径不动。
- **测试**: `MaxExceptionContentLength=1024` + StubHttp 返回 500 + 1MB chunked 体 → `ErrorContent` ≤ 1024 + 省略号；默认值（10240）与 0=不限两态断言。

### HC-10 authorization_code 客户端认证

- **位置**: [StandardOAuth2TokenManager.cs:233-241](../Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs)。
- **修复**: 配置了 `client_secret`（机密客户端）时改走 `RequestTokenWithClientAuthAsync`（Password/Refresh 流同款，内部 `ApplyClientAuthentication`：Basic 头优先、体兜底）；未配置 secret（公共客户端）维持现状。
- **测试**: StubHttp 断言 token 请求带 `Authorization: Basic` 或体含 `client_secret`（与既有 Password 流断言同构）。

### HC-11 重试克隆不可重放预判补全

- **位置**: [HttpRequestMessageCloner.cs:99-103](../Mud.HttpUtils.Resilience/HttpRequestMessageCloner.cs)（仅覆盖 `IRequestContentReplayHint`）；`BufferUpToAsync`（`:209-232`）无空读守卫。
- **修复**:
  1. `ShouldSkipRetryForContent` 追加分支：无声明长度且 `content` 可取出底层流（`StreamContent`/`PushStreamContent` 经 `HttpContent.ReadAsStreamAsync` 探测，或直接对 `content.Headers.ContentLength == null` 且非 `ByteArrayContent/StringContent/FormUrlEncodedContent/MultipartContent` 家族判定）→ `reason = "undeclared-non-replayable-content"`。
     - **落点取舍**: 直接探测流会消耗一次性流，故采用**类型白名单**判定：仅 `ByteArrayContent`、`StringContent`、`FormUrlEncodedContent`、`MultipartContent`（子项均为内存型）视为可重放，其余无声明长度内容一律降级。保守但零消耗，与 H-7 语义对齐。
  2. `BufferUpToAsync` 兜底守卫：`total == 0 && declared.HasValue == false` 时返回失败（不缓存空快照），reason 同上。
- **测试**: PUT + `StreamContent`（不可 seek）→ `RetryGuard`/预判降级为"超时+熔断"，StubHttp 断言无第二次请求；`ByteArrayContent` 重试正常克隆。

### HC-12 `DownloadAsync` IOException 分类

- **位置**: [EnhancedHttpClient.cs:1826-1833](../Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs)。
- **修复**: 以"FileStream 是否已成功打开"标志区分：构造期（`new FileStream` 抛 IOException 且 `!overwrite`，即文件已存在）→ 维持现状重抛且不清理；写入期 IOException → 走清理 + `RecordDownloadFailed` + `LargeFileDownloadFailed` 后再抛。
- **注意**: 与 HC-14 合并实施（同一方法体，避免二次改动）；清理后抛出原异常（保留根因）。
- **测试**: `overwrite=false` + 预置同名文件 → 抛 IOException 且原文件未动；下载中途 StubHttp 断流（写入期 IOException）→ 半写文件被清理、指标记录。

### HC-13 EventId 撞号治理

- **位置**: [MudHttpClientLog.cs:433/439](../Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs)（[LoggerMessage]）、`:737/745`（NET6+ Define 分支）、`:793/799`（尾部 Define，全 TFM 共存）、`:16`（规划注释漂移）。
- **修复**:
  1. 尾部 TMX 批次（TokenRefreshSuppressed=170、TokenCacheSerializationFailed=171 等）迁移至未占用号段（从 181 起顺延）。
  2. 同步头部规划注释为实际分配（166/169/177 纠偏）。
  3. 新增守护测试：反射扫描 `MudHttpClientLog` 全部 `[LoggerMessage]` 特性与 `LoggerMessage.Define` 调用点（后者经源码文本扫描或约定静态字段命名 `s_` 前缀 + 反射字段解析 EventId），断言 EventId 全局唯一——纳入 Client.Tests，防复发。
- **测试**: 即上述守护测试 + 现有日志相关回归。

---

## 五、P2 一般修复方案（要点）

| 编号  | 方案要点                                                                                                                                                                                                                                                                                                                                                                           | 复用/落点                                         | 测试要点                                                                                          |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| HC-14 | 公有 `DownloadLargeAsync`：bufferSize 钳制 4KiB~4MiB（越界归一默认 81920，与执行器一致）；落盘改 `.mudtmp` + `File.Move(overwrite)`；与 HC-12 同方法合并实施                                                                                                                                                                                                                       | 执行器 G7-03/G8-06 模板                           | `int.MaxValue` 入参不 OOM；崩溃路径残留检查                                                       |
| HC-15 | `CopyToWithProgressAsync` 移植 100ms 节流 + 收尾必报；`ProgressableStreamContent`：`_progress==null` 走 `CopyToAsync` 快路径、有回调加节流、默认缓冲提升至 81920（ns2.0 用常量分支）                                                                                                                                                                                               | EnhancedHttpClient NEW-HC-09                      | 进度回调次数上限断言（1MB 体 ≤ ~15 次）；null 回调路径零 Report                                   |
| HC-16 | 淘汰策略改"按 LastAccessTime 的最小堆懒维护"或每 N 次 Set 触发一次全量排序（N=16），摊销成本                                                                                                                                                                                                                                                                                       | 既有 `CacheEntry.Touch`                           | 满载 Set 基准（BenchmarkDotNet，见 §八）                                                          |
| HC-17 | 指标 tag 构造惰性化：`Meter.HasListeners()`（net6+）/`listener 计数` 短路 + 复用暂存 `KeyValuePair` 数组；诊断事件调用点叠加 `HasSubscribers \|\| Activity.Current != null`                                                                                                                                                                                                        | G28 门控思路                                      | 零监听场景分配基准对照                                                                            |
| HC-18 | `DefaultUrlParameterFormatter`：`IFormattable → ToString(null, InvariantCulture)`；生成器 `QueryParameterBinder` 分隔符模式与 `RequestBuilder` 接口级 Query 回退同样处理（`__item is IFormattable __f ? __f.ToString(null, CultureInfo.InvariantCulture) : __item.ToString()`）                                                                                                    | QueryParameterBuilder 既有 Invariant 口径         | ar-SA/ru-RU culture 下 DateTime/decimal query 快照测试                                            |
| HC-19 | `QueryMapHelper`：属性过滤 `p.GetIndexParameters().Length == 0`；`UrlEncode=false` 语义收敛为"key/value 均不转义"并更新 XML 文档                                                                                                                                                                                                                                                   | —                                                 | 索引器 POCO 回归；rawPairs 单测                                                                   |
| HC-20 | （D3-A）`BuildOptions` 模板改为 `injected` 的副本（`new JsonSerializerOptions(injected)`），再 Combine(resolver, builtIn, [JIT]Default)；injected 为 null 时维持现模板                                                                                                                                                                                                             | 既有合并骨架                                      | 注入 camelCase/枚举字符串化 options → 序列化输出断言；内置类型（MudHttpJsonContext 覆盖）仍可解析 |
| HC-21 | `ApiException` 构造点统一传 `SensitiveUrlRedactor.Redact(...)`（EnhancedHttpClient `:1998` 等 5 处；`ApiRequestException` 已达标）                                                                                                                                                                                                                                                 | `SensitiveUrlRedactor`                            | 带 token query 的异常对象 `RequestUri` 无 query 断言                                              |
| HC-22 | `PolicyCacheKey` 拆分：CB/timeout 键剥离 `ResultType`（仅 Scope+Kind），retry 保留；超限路径改 LRU 驱逐（按插入序/访问时间戳淘汰 1/8）而非放弃缓存                                                                                                                                                                                                                                 | `_policyCache` + `PolicyCacheKey`                 | 同 scope 跨结果类型失败计数合并断言；超限后第 2 次失败仍计入同一 CB                               |
| HC-23 | 401 恢复重试循环：第 2..N 轮刷新绕过结果复用窗口（`RefreshTokenWithDedupAsync(forceRefresh:true)` 或命中同 token 值即提前失败）；重试发送包 try/catch，异常路径 `response.Dispose()` 后上抛                                                                                                                                                                                        | `RefreshDedupTable`                               | 两次刷新拿到不同 token 断言；取消路径连接归还（StubHttp 计数）                                    |
| HC-24 | `TokenRecoveryExecutor` Header/ApiKey 注入前调 `HttpHeaderValueValidator.IsValid`（Cookie 路径已有 EscapeDataString）；`IsValid` 扩展拒绝 C0/DEL（保留 `\t`），同步更新 XML 文档与 ns2.0 分支                                                                                                                                                                                      | `HttpHeaderValueValidator`（逐字符 IndexOf 风格） | 含 `\r\n`/`\u0000` token 注入被拒断言                                                             |
| HC-25 | （D4-A）`DefaultHmacSignatureProvider` 增加 `X-Timestamp`（Unix 秒）+ `X-Nonce`（8B 随机 hex）入签，顺序固定在首行；新增 `RequireAntiReplay` 选项（默认 false=false 时维持现状）；nonce 缓存由**服务端**负责（文档声明，客户端不做去重存储）                                                                                                                                       | 签名串规范化既有逻辑                              | 开关开启后签名串快照；关闭行为不变                                                                |
| HC-26 | 解密前检查：`EnableKeySeparation=false` 且密文版本 == 0x04 → 抛 `CryptographicException("密文由启用密钥分离的实例产生，当前实例未启用")`（显式拒绝，不再误报完整性失败）；发布说明标注 0x04 默认化对旧版对端的兼容窗口                                                                                                                                                             | 版本分派既有骨架                                  | 0x04 密文 × 两种实例配置的 2×2 断言                                                               |
| HC-27 | `SensitiveUrlRedactor.Redact` 前置剥离 userinfo：`scheme://user:pass@` → `scheme://***@`                                                                                                                                                                                                                                                                                           | 既有 `?` 切分逻辑                                 | userinfo URL 掩码断言                                                                             |
| HC-28 | ns2.0 `MemoryTokenStore`：过期移除改"锁内复查后移除"或文档声明弱一致（倾向后者，补注释）；`MemoryUserTokenStore` 增加空壳桶后台清扫（复用 `TokenRefreshBackgroundService` 心跳或惰性：GetOrAdd 时顺带清理过期桶，桶数超 1万 全量扫）                                                                                                                                               | SR-L5 既有空壳清扫                                | ns2.0 并发 Set/Get 无异常；桶数有界断言                                                           |
| HC-29 | （D5-A）新增 Generator 诊断 `MUDGEN3xx`（Warning）：流式方法生成点提示"AOT 下需接入 JsonSerializerContext（DI 注入或手动重载）"；`AnalyzerReleases.Unshipped.md` 登记                                                                                                                                                                                                              | Diagnostics 基建                                  | Verify 快照含诊断；pragma 抑制样例                                                                |
| HC-30 | 非 string Header 发射点（`HeaderParameterBinder` `:98-103`、`RequestBuilder` `:1060-1099`）生成 `IsValid` 前置校验（同 HC-02 集合逐项校验口径），不合格跳过 + Debug 日志                                                                                                                                                                                                           | `HttpHeaderValueValidator`                        | 生成快照含校验调用；CRLF 注入端到端必拒                                                           |
| HC-31 | 零散清理：`AsyncEnumerableExtensions` 复用注入序列化器（经 `IBaseHttpClient` 暴露或静态缓存）；EnhancedHttpClient 4 处（`:1668/:1670/:1745/:1754`）与 `PollyResiliencePolicyProvider:541` 补 `ConfigureAwait(false)`；`RequestContentCaptureUtils.CanCapture` 增加非 seek 拒捕判定（配合 M4-H-2 提示）；`ProgressableStreamContent` ns2.0 `using` 过早 dispose 修正为与 net5+ 一致 | —                                                 | 既有回归全绿                                                                                      |

---

## 六、P3 建议（backlog，择机实施）

- `LimitedContentReader` 预分配改可增长缓冲（大上限 × 小响应场景的 200MB 预分配）。
- `Response<T>.Headers` 合并 `Content.Headers`（Content-Type/Disposition 可见性）。
- ns2.0 下载 `File.Delete→Move` 非原子窗口（已注释取舍，可改平台 P/Invoke 或接受）。
- KebabCase/SnakeCase 命名器连续大写缩写策略（`HTTPServer` → `http-server` 需 acronym 表）。
- 拒连异常消息仅输出 host（不披露解析 IP 列表，防内网拓扑泄露）。
- `EncryptedTokenCache` Warning 日志 Key 脱敏（PII）。
- `MessageSanitizer` 词表补 `pwd/credential/sessionid/bearer/sign/auth`、收敛 `code/nonce/address/name`；Base64 启发式加长度阈值 ≥16 或要求 `=` 填充。
- GCM 随机 nonce 文档标注密钥轮换节奏（~2³² 次加密阈值）；HKDF 直接 Expand（无 Extract、16B 主密钥）规范性偏差声明。
- JIT 路径 `ToHttpContent` 默认 UTF-8 直编（消除 string 双份编码）。
- `[RawQueryString]`/[Path] `UrlEncode=false` 裸通道加 Info 级诊断。
- `ResilienceOptionsValidator` "总重试延迟 vs 单次超时" 硬失败降级为警告（与 CrossValidator 口径统一）。
- 白名单子域匹配倒序累拼（消除 Skip/Join 分配）、`[Query(Format)]` 反射按 ParameterInfo 缓存、PolicyCacheKey 改 readonly struct、scope 预解析——热路径微优化打包一轮基准驱动。
- `EnhancedHttpClientOptionsCloner` 对 `HttpRequestMessageOptions` 深拷贝（现共享可变字典）。
- `TracingDelegatingHandler` 诊断负载 internal 类型 AOT 裁剪风险（改 public 契约或补保留声明）。
- `Debug.WriteLine` 口径统一为 Trace（MemoryHttpResponseCache 清理异常）。

---

## 七、公共 API 与基线影响

| 项                                                    | 影响                                                                  | 处置                                                                                                              |
| ----------------------------------------------------- | --------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| HC-01/04/06/07/08/09/11/12/16/17/21/23/24/26/27/28/31 | 全部 internal 实现，公共签名不变                                      | 无基线变更；`rebuild-publicapi.ps1` 常规校验                                                                      |
| HC-02/29/30                                           | 生成代码行为变化，**无公共 API 签名变化**；新增 2~3 个 Generator 诊断 | `AnalyzerReleases.Unshipped.md` 登记；Verify 快照更新                                                             |
| HC-03                                                 | 注册行为变化（严格模式默认注册 SSRF handler），无签名变化             | XML 文档更新；README 声明                                                                                         |
| HC-05                                                 | **破坏性行为变更**（关闭自动重定向）                                  | README「破坏性变更」章节 + 替代路径说明                                                                           |
| HC-10/25                                              | OAuth2/HMAC 行为变化；HC-25 新增选项 `RequireAntiReplay`              | 若选项挂接公共 Options 类型 → PublicAPI.Unshipped.txt 登记（`tools/rebuild-publicapi.ps1`）；Shipped 固化基线不动 |
| HC-20                                                 | 序列化语义变化（D3-A 下 injected 设置生效）                           | XML 文档 + README 声明                                                                                            |

门禁要求：每阶段收尾跑全解决方案 `PublicApiStrictMode` 构建（0 错误）+ `tools/rebuild-publicapi.ps1` 基线对齐（8 发布库 25 份基线，与 M4-H-4 口径一致）。

---

## 八、测试与验收计划

### 8.1 新增测试映射（Client.Tests xUnit + StubHttp / Generator.Tests Verify / Benchmarks）

| 测试                              | 覆盖                                                | 工程                           |
| --------------------------------- | --------------------------------------------------- | ------------------------------ |
| `SsrfMappedAddressTests`          | HC-01（6 断言）+ HC-04（混合记录）                  | Client.Tests/Security          |
| `HeaderCollectionGenerationTests` | HC-02（Verify 快照 + 端到端头到达）                 | Generator.Tests + Client.Tests |
| `RedirectRevalidationTests`       | HC-05（4 场景）                                     | Client.Tests/Http              |
| `PiiMaskingTests`                 | HC-06（嵌入文本掩码）                               | Client.Tests/Security          |
| `CacheSingleFlightRaceTests`      | HC-07（并发 fetchFunc 计数=1）                      | Client.Tests/Caching           |
| `ResponseLimitTests` 扩展         | HC-09（三态限量）                                   | Client.Tests/Response          |
| `OAuth2AuthCodeTests`             | HC-10（Basic/体两种认证断言）                       | Client.Tests/Token             |
| `CloneNonReplayableTests`         | HC-11（StreamContent 降级 + ByteArrayContent 正常） | Resilience.Tests               |
| `DownloadCleanupTests`            | HC-12/HC-14（半写清理/钳制/.mudtmp）                | Client.Tests/Client            |
| `EventIdUniquenessGuardTests`     | HC-13（全表唯一，防复发）                           | Client.Tests                   |
| `CultureInvariantQueryTests`      | HC-18（ar-SA/ru-RU 快照）                           | Client.Tests + Generator 快照  |
| `PolicyCacheTests` 扩展           | HC-22（CB 合并/超限 LRU）                           | Resilience.Tests               |
| `TokenRecoveryRetryTests`         | HC-23（forceRefresh/响应释放）                      | Client.Tests/Token             |
| `HeaderValueControlCharTests`     | HC-24（C0/DEL）                                     | Client.Tests/Security          |

### 8.2 基准（Tests/Mud.HttpUtils.Benchmarks/HttpClientBenchmarks.cs 扩展）

- 缓存满载 `Set`（HC-16 前后对照）；零监听指标 tag 构造（HC-17）；上传进度吞吐（HC-15）；白名单未命中匹配（P3 微优化验收）。

### 8.3 验收标准

1. 全部 🔴/🟠 修复项测试绿 + 既有 8 测试工程回归全绿（net6/net8/net10 全 TFM，Client.Tests 参照 M4 基线 1359/1410/1410）。
2. 全解决方案 PublicApiStrictMode 构建 0 错误；基线经 rebuild-publicapi.ps1 对齐。
3. HC-01/HC-04 端到端 SSRF 复现用例（映射 AAAA → 拒连）在 SsrfProtectionTests 可稳定复现修复前后差异。
4. 决策点 D1~D5 评审记录回填本方案（含选择与理由）。

---

## 九、实施顺序与依赖

```
阶段一（P0，阻塞）  : HC-01 → HC-02（含 HC-30 校验口径，二者共享发射点改动）
阶段二（P1-安全）   : HC-03(D1) → HC-04 → HC-05(D2) → HC-06（互相独立，可并行）
阶段三（P1-性能/功能）: HC-07 → HC-08 → HC-09 → HC-10 → HC-11 → HC-12+HC-14（同方法） → HC-13
阶段四（P2）        : HC-15 ~ HC-31（按 §五 表序；HC-20 依赖 D3，HC-25 依赖 D4，HC-29 依赖 D5）
阶段五（收尾）      : P3 择项 + 基准对照 + 全量回归 + PublicAPI 门禁 + 文档（README 破坏性变更清单）
```

关键依赖说明：

- HC-12 与 HC-14 触碰同一方法体（`DownloadLargeAsync` catch 块与落盘路径），**必须合并实施**避免二次回归。
- HC-02 与 HC-30 同为生成器 Header 发射点，合并实施；HC-29 独立可并行。
- HC-11 修改 `ShouldSkipRetryForContent` 后，需复核 HC-05 的 307/308 重定向路径对非幂等体的复用判定（两处共享"可重放"概念）。
- 每阶段结束执行：`test.ps1` 全量回归 → `rebuild-publicapi.ps1` → 更新本方案 §十 实施记录。

## 十、实施记录

| 日期       | 阶段               | 内容                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | 结果                                                                                                                                                                                                                                                                                                                                                                                |
| ---------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-09-22 | —                  | 方案创建（基于第 4 轮三主线审查）                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | v1.0 待评审                                                                                                                                                                                                                                                                                                                                                                         |
| 2026-09-22 | 一（P0）           | **HC-01**：`UrlValidator.IsPrivateIpAddress` 入口加 `IsIPv4MappedToIPv6 → MapToIPv4` 归一（ns2.0 无 TFM 分叉），`DefaultIpAddressPolicy` 同源受益                                                                                                                                                                                                                                                                                                                                                                   | 已实施；`SsrfProtectionTests` 增 4+ 断言（`::ffff:10.0.0.1`/`172.16/192.168/169.254/0.0.0.0/127.0.0.1` 必真、`::ffff:8.8.8.8`/`2001:db8::1` 必假、策略层同步断言）                                                                                                                                                                                                                  |
| 2026-09-22 | 一（P0）           | **HC-02 + HC-30**：`RequestBuilder.GenerateHeaderParameters` 改为按 `CanBind` 分派（`HeaderParameterBinder` → `HeaderCollectionParameterBinder`），调用点传入 `context`/`methodSymbol`；新增诊断 **HTTPCLIENT037**（Info，参数名含 "header" 但无 Header 特性）；`HeaderCollectionParameterBinder` 与 `HeaderParameterBinder` 非 string 分支、`GenerateInterfaceHeaderProperties` 非 string 属性分支全部接入 `HttpHeaderValueValidator.IsValid` 逐项校验（不合格跳过 + `Debug.WriteLine` 仅输出头名，不输出值）      | 已实施；诊断登记 `AnalyzerReleases.Unshipped.md` 与 Generator `README.md`；新增 `HeaderCollectionBindingTests`（3 Verify 快照 + 2 编译断言端到端）、`GeneratorSnapshotCompileTests` 补样例、`Tests/Mud.HttpUtils.Integration.Tests/HeaderCollectionEndToEndTests.cs`                                                                                                                |
| 2026-09-22 | 一（P0）验证       | 生成器测试 net8：**882/882 通过**（含新增）；集成测试 `HeaderCollection` 筛选用例：**5/5 通过**；Client.Tests net8：**1540/1540 通过**                                                                                                                                                                                                                                                                                                                                                                              | 全绿                                                                                                                                                                                                                                                                                                                                                                                |
| 2026-09-22 | 二（P1 安全）      | **HC-03（D1-A）**：`AddMudHttpClient` 注册路径对严格模式（`AllowCustomBaseUrls=false`）客户端自动接线连接期 SSRF primary handler（`ConfigureSsrfPrimaryHandlerInStrictMode`）；判定与 `CreateEnhancedClient` 同源（配置节优先、回退编程式基线，handler 构建期决策以随热更新生效）；显式 `AddMudHttpClientSsrfProtection(builder)` 经 `ConditionalWeakTable`（容器+客户端名）登记实现幂等；引导日志 (H-6) 对严格模式客户端不再输出                                                                                   | 已实施；**既有用例语义更新**：`SsrfGuidance_RegisteredPolicy_NoHandlerProtection_LogsOnce` → `SsrfGuidance_RegisteredPolicy_NonStrictMode_LogsOnce`（经配置节放开 `AllowCustomBaseUrls` 才保留引导），新增 `SsrfGuidance_StrictMode_AutoWired_DoesNotLog`                                                                                                                           |
| 2026-09-22 | 二（P1 安全）      | **HC-04**：`IsLoopbackAddress`（同步 + 异步两版）由 `Any(IsLoopback)` 改 `All(IsLoopback)` 且要求非空集合 —— 混合记录 [回环, 私网] 不再整体豁免                                                                                                                                                                                                                                                                                                                                                                     | 已实施                                                                                                                                                                                                                                                                                                                                                                              |
| 2026-09-22 | 二（P1 安全）      | **HC-05（D2-A）**：`SsrfSafeSocketsHttpHandler` 与注册路径（net6+ 非严格分支、ns2.0 分支）统一 `AllowAutoRedirect=false`；`EnhancedHttpClient.SendCoreAsync` 由直通改为手动逐跳循环：每跳 `UrlValidator.ValidateUrlAsync` 全套复验、301/302/303 非 GET/HEAD 降级为 GET、307/308 保留方法与内容（仅内存型白名单内容可重放，不可重放中止抛出）、逐跳剥离 Authorization/Cookie、跳数上限 10、`Options`/`Properties` 跨跳复制                                                                                           | 已实施                                                                                                                                                                                                                                                                                                                                                                              |
| 2026-09-22 | 二（P1 安全）      | **HC-06**：`MessageSanitizer` 手机/邮箱/身份证三正则由 `^...$` 锚点改查找式 + 单字符类边界断言（NET7+ `GeneratedRegex` 与 ns2.0 `Regex` 两分支同步），嵌入文本中的 PII 亦掩码                                                                                                                                                                                                                                                                                                                                       | 已实施                                                                                                                                                                                                                                                                                                                                                                              |
| 2026-09-22 | 二验证             | Client.Tests 全 TFM：**net6 1484/1484、net8 1540/1540、net10 1540/1540**；Generator.Tests 全 TFM：**net8 882/882、net10 882/882**；Integration.Tests `HeaderCollection` **5/5**；全解决方案 strict 构建 **0 错误**                                                                                                                                                                                                                                                                                                  | 全绿（阶段一 + 阶段二完成）                                                                                                                                                                                                                                                                                                                                                         |
| 2026-09-23 | 三（P1 性能/功能） | **HC-07**：单飞锁获取改「自旋校验」——`PruneFetchLocks` 回收条件追加「无人持锁」（`CurrentCount > 0`）避免误回收活跃回源中的锁；持有后校验引用仍为字典当前锁；自旋上限 2 次失败即放弃单飞直接回源。`PruneFetchLocksIfOverCapacity` 更名 `PruneFetchLocks`，Timer 的 `CleanupExpiredEntries` 与 `GetOrFetchAsync` 共用同一方法（消除双份逻辑），移除按引用比对                                                                                                                                                        | 已实施；新增 `CacheSingleFlightRaceTests`（活跃回源 + 冷键回收压力下同 key 并发仅回源 1 次、冷键锁可回收无界增长反向保障）。**注**：方案 §四 HC-07 文字写 `CurrentCount == 0`，与其标签「无人持锁才可回收」语义矛盾，实施按标签语义取 `CurrentCount > 0`（`SemaphoreSlim(1,1)` 未被获取时计数为 1）                                                                                 |
| 2026-09-23 | 三（P1 性能/功能） | **HC-08**：新增 `EnsureWhitelistedHostIsSecureAsync`（回环判定经 `IsLoopbackAddressAsync` 异步 DNS），`ValidateUrlAsync` 白名单命中分支改调异步版；同步版保留并加 XML 文档标注「会阻塞线程」                                                                                                                                                                                                                                                                                                                        | 已实施；`UrlValidatorAsyncTests` 增 3 用例（白名单 HTTP 非回环拒绝 / 回环豁免放行 / HTTPS 命中零 DNS），并在 Dispose 清理 DNS 缓存保证解析计数确定性                                                                                                                                                                                                                                |
| 2026-09-23 | 三（P1 性能/功能） | **HC-09**：`SendAsResponseAsync<TInner>` 读取路径按状态码分流 —— 2xx 维持成功体守卫语义，非 2xx 改走 `ReadErrorContentLimitedAsync`（受 `MaxExceptionContentLength` 三态约束）                                                                                                                                                                                                                                                                                                                                      | 已实施；新增 `ResponseErrorContentLimitTests`（超限截断 / 未超限原样 / `<= 0` 不限长 / 成功路径不受错误体上限约束，共 4 用例）                                                                                                                                                                                                                                                      |
| 2026-09-23 | 三（P1 性能/功能） | **HC-10**：`GetTokenByAuthorizationCodeAsync` 末尾按是否配置 `ClientSecret` / `ClientSecretProviderName` 分流 —— 机密客户端改走 `RequestTokenWithClientAuthAsync`（Basic 头优先、体兜底），公共客户端维持 `RequestTokenAsync`                                                                                                                                                                                                                                                                                       | 已实施；`StandardOAuth2TokenManagerHttpInteractionTests` 增 2 用例（机密客户端断言 `Basic base64(clientId:secret)` 与 `grant_type=authorization_code`；公共客户端断言不发送认证头）                                                                                                                                                                                                 |
| 2026-09-23 | 三（P1 性能/功能） | **HC-11**：`ShouldSkipRetryForContent` 追加「无声明长度 + 非内存型白名单内容」分支（reason `undeclared-non-replayable-content`），新增 `IsKnownReplayableContent`（`ByteArrayContent` / `MultipartContent`，与 HC-05 可重放口径一致）；`BufferUpToAsync` 返回类型扩为 `(Bytes, Exceeded, Failed)`，无声明长度且读得 0 字节判为失败；`CloneAsync` 在 failed 时抛 `InvalidOperationException`，`TryCloneAsync` 据此返回 null 降级为不重试                                                                             | 已实施；新增 `CloneNonReplayableTests`（7 用例：非 seekable StreamContent / 未知自定义内容预判、可 seek 流与 MultipartContent 不预判、源流耗尽时 CloneAsync 抛出与 TryCloneAsync 返回 null、ByteArrayContent 正常克隆）                                                                                                                                                             |
| 2026-09-23 | 三（P1 性能/功能） | **HC-12 + HC-14（合并）**：`EnhancedHttpClient.DownloadLargeFileAsync` 改 `.mudtmp` 临时文件 + 流关闭后原子 `File.Move`（net6+ 用 overwrite 重载；ns2.0 先删后移），最终路径永不出现半写文件；`fileStreamOpened` 标志区分「构造期 IOException」（overwrite=false 目标已存在，维持不清理直接重抛）与「写入期 IOException」（清理 tmp 并记失败）；`bufferSize` 钳制到 `[4 KiB, 4 MiB]` 越界归一为默认 81920（删除原 `<= 0` 抛 `ArgumentException`）；抽出 `CopyToWithThrottledProgressAsync`（100ms 节流 + 收尾必报） | 已实施；新增 `DownloadCleanupTests`（5 用例：写入期 IOException 无残留、overwrite=false 原文件字节不变、`int.MaxValue` 钳制不 OOM、越界归一 Theory(0/-1/2048)、成功后无 tmp 残留）                                                                                                                                                                                                  |
| 2026-09-23 | 三（P1 性能/功能） | **HC-13**：`TokenRefreshSuppressed` EventId 170→181、`TokenCacheSerializationFailed` 171→182（消除与 `PolicyCacheFull`(170) / `TokenRecoveryRedirectDetected`(171) 撞号）；头部 EventId 规划注释重写为实际分配全表                                                                                                                                                                                                                                                                                                  | 已实施；新增 `EventIdUniquenessGuardTests`（全文件扫描「同名方法去重后 EventId 全局唯一」+ TMX 迁移落点断言），补足既有 `MudHttpClientLogContractTests` 仅覆盖「方法数最多的一对 `#if`/`#else` 分支」、不覆盖条件块外尾部批次的缺口                                                                                                                                                 |
| 2026-09-23 | 三验证             | Client.Tests 全 TFM：**net6 1504/1504、net8 1560/1560、net10 1560/1560**（较阶段二各 +20）；Resilience.Tests：**net8 289/289、net10 289/289**（新增 7 用例）；Integration.Tests `HeaderCollection` **5/5**；全解决方案构建 **0 错误**                                                                                                                                                                                                                                                                               | 全绿（阶段三完成）                                                                                                                                                                                                                                                                                                                                                                  |
| 2026-09-23 | 四（P2 A）         | **HC-15**：抽出 `ThrottledStreamCopier.CopyWithThrottledProgressAsync`（100ms 节流 + 收尾必报）供下载/上传/执行器三路共用，原 `CopyToWithProgressAsync`/`EnhancedHttpClient.CopyToWithThrottledProgressAsync` 下沉；`ProgressableStreamContent` 默认缓冲 `4096 → 81920`、`_progress == null` 走 `CopyToAsync` 快路径                                                                                                                                                                                                | 已实施；`ProgressableStreamContentExtendedTests` 增节流断言（小缓冲多次写入 → 回调 ≤ ~15 次）；**公共 API 变更**：`ProgressableStreamContent` 的 `bufferSize` 默认值 4096→81920（RS0017 待阶段五基线重建）                                                                                                                                                                          |
| 2026-09-23 | 四（P2 A）         | **HC-16**：`MemoryHttpResponseCache` 淘汰由"严格容量语义（每次仅淘汰 1 条）"改按**最久未访问**（`LastAccessTime` 懒维护）选目标，降低满载时的 CPU 开销                                                                                                                                                                                                                                                                                                                                                              | 已实施；基准对照见 §八（阶段五 `Benchmarks` 补测）                                                                                                                                                                                                                                                                                                                                  |
| 2026-09-23 | 四（P2 A）         | **HC-17**：新增公共观测门控 `MudHttpMeter.HasListeners`（任一内建 instrument 处于 Enabled）；`MudHttpObservability` 5 处指标写入前短路，避免 tags 数组分配；`MudHttpActivitySource` 诊断事件调用点叠加 `HasSubscribers \|\| Activity.Current != null`                                                                                                                                                                                                                                                               | 已实施；**公共 API 新增**：`MudHttpMeter.HasListeners`（RS0016 待阶段五基线重建）                                                                                                                                                                                                                                                                                                   |
| 2026-09-23 | 四（P2 A）         | **HC-18**：`DefaultUrlParameterFormatter` 在无显式 format 时改 `IFormattable.ToString(null, CultureInfo.InvariantCulture)`（原 `ToString()` 随 `CurrentCulture` 漂移）；生成器侧 `QueryParameterBinder` 分隔符模式与 `RequestBuilder` 接口级 `[Query]` 回退同样追加 Invariant 口径（**仅可格式化值类型**追加，引用类型无该重载）                                                                                                                                                                                    | 已实施；测试见本阶段验证行（ar-SA/ru-RU 下 DateTime/decimal 输出恒等于 Invariant）                                                                                                                                                                                                                                                                                                  |
| 2026-09-23 | 四（P2 A）         | **HC-19**：`QueryMapHelper` 属性过滤补 `GetIndexParameters().Length == 0`（原实现索引器属性会抛 `TargetParameterCountException`）；`QueryParameterBinder` 的 `rawPairs` 模式语义收敛为「key/value 均不转义」并同步 XML 文档                                                                                                                                                                                                                                                                                         | 已实施                                                                                                                                                                                                                                                                                                                                                                              |
| 2026-09-23 | 四（P2 A）         | **HC-20（D3-A）**：`HttpContentSerializerFactory.BuildOptions` 在 `injected != null` 时改以 `new JsonSerializerOptions(injected)` **副本**为合并基座（原实现丢弃消费方注入的 camelCase/枚举字符串化等设置），再 Combine resolver → builtIn → JIT Default；`injected == null` 维持原模板                                                                                                                                                                                                                             | 已实施；**语义变更**：注入 options 现可生效（阶段五 README 破坏性变更清单登记）                                                                                                                                                                                                                                                                                                     |
| 2026-09-23 | 四（P2 B）         | **HC-21**：`ApiException.RequestUri` 全部构造点统一经 `SensitiveUrlRedactor.Redact`（`ApiException` / `ExceptionFactories` / `ApiResponseExtensions` / `DefaultHttpRequestExecutor` / `EnhancedHttpClient` 共 5 处）；`MudHttpObservabilityOptions` 补文档说明「错误路径不受 `RedactUrlInTelemetry` 开关影响，始终脱敏」                                                                                                                                                                                            | 已实施                                                                                                                                                                                                                                                                                                                                                                              |
| 2026-09-23 | 四（P2 B）         | **HC-22**：`PolicyCacheKey` 拆分 —— 熔断/超时策略键剥离 `ResultType`（改走 `_sharedPolicyCache`，同一 scope 下不同结果类型共享同一熔断器失败计数），并按结果类型另行缓存适配实例以保持引用标识；retry 键保留 `ResultType`；策略缓存超上限由「放弃缓存」改为**按插入序淘汰 `max/8`（至少 1 条）**，`PolicyCacheFull` 事件语义同步改写                                                                                                                                                                                | 已实施；测试见本阶段验证行（跨结果类型同实例 + 超限后仍命中同一 CB）                                                                                                                                                                                                                                                                                                                |
| 2026-09-23 | 四（P2 B）         | **HC-23**：`RefreshDedupTable.GetOrRefreshAsync` 增 `forceRefresh`（窗口内条目仅在「非 forceRefresh 或该条目未完成」时复用）；`TokenRecoveryExecutor` 恢复循环以 `forceRefresh = retry > 0` 透传，第 2..N 轮刷新不再复用已被服务端拒绝的令牌；重试发送包 `try/catch`，异常/取消路径先归还克隆请求与原始 401 响应再上抛                                                                                                                                                                                              | 已实施；**未采用**方案备选的「命中同 token 值即提前失败」（会破坏既有 `callCount` 契约），故同步删除曾拟用的 EventId 183 与日志方法                                                                                                                                                                                                                                                 |
| 2026-09-23 | 四（P2 B）         | **HC-24**：`HttpHeaderValueValidator.IsValid` 由「仅查 CR/LF」改为逐字符拒绝全部 C0 控制字符与 DEL（保留 HTAB），避免分配 `char[]`；`TokenRecoveryExecutor` 的 Header 与 ApiKey 注入前复核（ns2.0 的 `HttpClient` 不校验头值）                                                                                                                                                                                                                                                                                      | 已实施；`IsSafeTokenValue` 更严（拒全部 C0/DEL，无 HTAB 例外），故该校验属防御性收敛                                                                                                                                                                                                                                                                                                |
| 2026-09-23 | 四（P2 B）         | **HC-25（D4-A）**：`DefaultHmacSignatureProvider` 增 `RequireAntiReplay`（默认 `false`，维持签名确定性）；为 `true` 时把 `X-Timestamp`（Unix 秒，Invariant）与 `X-Nonce`（8 字节 CSPRNG 的 16 位小写 hex）**固定置于签名串首两行**并**写回请求头**（已有值则复用，保证同请求验签成立）；nonce 去重与时间窗校验由服务端负责（文档声明）；新增 DI 重载 `AddHmacSignatureProvider(bool requireAntiReplay)`                                                                                                             | 已实施；**公共 API 新增**：构造器重载、`RequireAntiReplay`、DI 重载（RS0016 待阶段五基线重建）                                                                                                                                                                                                                                                                                      |
| 2026-09-23 | 四（P2 C）         | **HC-26**：`DefaultAesEncryptionProvider` 解密版本分派中，密文为 `0x04`（CBC+HMAC 密钥分离）而本实例 `EnableKeySeparation == false` 时**前置显式拒绝**并抛 `CryptographicException`（提示改配置或由加密侧产出 v3 信封），不再回落主密钥后误报「密文完整性校验失败」                                                                                                                                                                                                                                                 | 已实施；发布说明需标注 0x04 默认化对旧版对端的兼容窗口（阶段五 CHANGELOG 登记）                                                                                                                                                                                                                                                                                                     |
| 2026-09-23 | 四（P2 C）         | **HC-27**：`SensitiveUrlRedactor` 由 `Mud.HttpUtils.Client/Helpers` **迁至 `Mud.HttpUtils.Abstractions/Helpers`**（使 `ApiException` 所在程序集可用）；`Redact` 前置剥离 userinfo（`scheme://user:pass@host` → `scheme://***@host`），与既有 query 掩码串联                                                                                                                                                                                                                                                         | 已实施；**文件移动**：`Mud.HttpUtils.Client/Helpers/SensitiveUrlRedactor.cs` 删除，新址见上                                                                                                                                                                                                                                                                                         |
| 2026-09-23 | 四（P2 C）         | **HC-28**：`MemoryTokenStore` 过期移除的 ns2.0 分支补**弱一致声明**（ns2.0 无 `TryRemove(KeyValuePair)`，无条件 key 移除；并发 Set 的新条目可能被误删、由下次 Set 恢复，不引入全局锁）；`MemoryUserTokenStore` 增惰性空壳桶清扫（桶数 > `UserBucketSweepThreshold`(1 万) 时在写入路径触发一次全量回收，`Interlocked` 门控防惊群；清扫位于本次 `AddOrUpdate` 之后以避免回收自身新桶）                                                                                                                                | 已实施；未采用后台心跳方案（避免纯内存存储依赖 `TokenRefreshBackgroundService` 注册契约）；新增内部观测钩子 `UserBucketCount`                                                                                                                                                                                                                                                       |
| 2026-09-23 | 四（P2 C）         | **HC-29（D5-A）**：新增生成器诊断 **`MUDGEN301`**（Warning，category `AOT`）—— `IAsyncEnumerable<T>` 流式方法的生成调用恒传 `null` JsonTypeInfo，AOT/裁剪下元素类型未入 `JsonSerializerContext` 会**静默**反序列化为 `default`；报告点定位在方法声明（便于就地 `#pragma warning disable`），不加 `NotConfigurable` 以保证可抑制；方案 §五「MUDGEN3xx」具体化为 `MUDGEN301`                                                                                                                                          | 已实施；登记 `AnalyzerReleases.Unshipped.md`、`DiagnosticIds.cs` 与 Generator `README.md` AOT 诊断表；新增 3 用例（报告/不报告/可抑制）                                                                                                                                                                                                                                             |
| 2026-09-23 | 四（P2 C）         | **HC-30**：已在阶段一随 HC-02 合并实施（`HeaderCollectionParameterBinder` Key/Value 逐项、`HeaderParameterBinder` 非 string 分支、接口级 `[Header]` 非 string 属性的 `IsValid` 前置校验），本阶段无新增改动                                                                                                                                                                                                                                                                                                         | 见阶段一记录                                                                                                                                                                                                                                                                                                                                                                        |
| 2026-09-23 | 四（P2 C）         | **HC-31**：`AsyncEnumerableExtensions` 两个重载改为优先复用 `client is EnhancedHttpClient → ContentSerializer`（避免另建默认序列化器丢失消费方 options；未改公共接口 `IBaseHttpClient`）；`EnhancedHttpClient` 4 处与 `PollyResiliencePolicyProvider` 1 处补 `ConfigureAwait(false)`；`RequestContentCaptureUtils.CanCapture` 增「无声明长度且非内存型 ⇒ 拒捕」分支（含一次性 Debug 日志）；`ProgressableStreamContent` ns2.0 的 `using` 过早 dispose 修正为与 net5+ 一致                                           | 已实施；`IBaseHttpClient` 未暴露序列化器，故用类型判定而非接口扩展（避免破坏性变更）                                                                                                                                                                                                                                                                                                |
| 2026-09-23 | 四（P2 C 补强）    | **HC-22 回归修复（自查）**：`_sharedPolicyCache` 原实现完全独立于容量上限，会随 scope 基数无界增长。现拆为「适配器册（`_policyCache`，受 `MaxPolicyCacheSize` 约束 + 插入序淘汰）」与「共享策略册（`_sharedPolicyCache`，自带插入序淘汰队列，上限 `max(256, MaxPolicyCacheSize)`，`MaxPolicyCacheSize<=0` 时不限）」；`TrimPolicyCache` 泛化为可同时服务两册（共享册兜底淘汰不重复输出 `PolicyCacheFull`）                                                                                                          | 已实施；**关键约束**：两册不可共用淘汰队列 —— 否则「适配器被淘汰 → 重新解析」会把仍在使用的熔断状态一并丢弃，使 HC-22 想修的「计数随调用重置」从另一条路径复发（违反方案 §五 HC-22 验收「超限后第 2 次失败仍计入同一 CB」）。同步修正 `ResilienceOptions.MaxPolicyCacheSize` 的过期 XML 注释；新增守卫测试 `SharedPolicyCache_ScopeCardinalityBeyondFloor_EvictsOldestBreakerState` |
| 2026-09-23 | 四验证             | Client.Tests 全 TFM：**net6 1550/1550、net8 1606/1606、net10 1606/1606**（较阶段三 net8/net10 各 +46）；Resilience.Tests：**net8 293/293、net10 293/293**（+4）；Generator.Tests 全 TFM：**net8 886/886、net10 886/886**（+4）；Integration.Tests `HeaderCollection` **5/5**；全解决方案构建 **0 错误**                                                                                                                                                                                                             | 全绿（阶段四完成）                                                                                                                                                                                                                                                                                                                                                                  |
