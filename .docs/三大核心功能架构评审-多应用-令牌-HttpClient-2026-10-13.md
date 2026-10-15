# 三大核心功能架构评审报告 — 多应用管理 / 令牌管理 / HttpClient 客户端

- **评审日期**：2026-10-13
- **评审对象**：`Mud.HttpUtils` 3.0.6（Abstractions / Attributes / Client / Resilience / Generator 等子包）
- **下游实证样本**：
  - `D:\Repos\Mud.Wechat`（微信生态七线共仓 SDK：Work / Channels / OpenPlatform / Ads / Pay / MiniProgram / Callback）
  - `E:\Temp\MudWechat`（同族 SDK 另一变体：Work / OfficialAccount / OpenPlatform / Ads / Pay 等）
- **评审视角**：系统架构师；四个评审轴 —— ① 架构设计合理性 ② 过度设计识别 ③ 下游功能缺口 ④ 逻辑链条完整性

---

## 评审结论

> **总体评价：良好（核心链路完整、工程质量高，但契约复杂度与部分死 API 构成显著认知税；下游已在用"整体自实现"绕过多应用默认实现，暴露出信任断裂与真实功能缺口）**

> **核心亮点**：
> 1. 令牌管理域的工程深度罕见地扎实 —— 并发刷新去重（`RefreshDedupTable`）、scope 键规范化（`ScopeKeyBuilder` 单射编码）、bind-once 租户绑定守卫、401/errcode 令牌恢复（`TokenRecoveryExecutor`）、volatile/durable 双层存储契约，且大量语义被测试钉死。
> 2. 生成器（`Mud.HttpUtils.Generator`）与运行时契约的对接有诊断兜底（HTTPCLIENT024、MUD005 豁免机制等），声明式 `[HttpClientApi]/[Token]` 开发体验是下游深度采用的直接原因。
> 3. 历史缺陷的修复纪律好：影子注册表（G3 全 virtual 化）、双 AsyncLocal（G7 `DelegatingAppContextHolder`）、AppContextScope fail-closed（B8）均已在 3.0.6 内治本。

> **关键风险**：
> 1. **下游信任断裂**：两个下游仓库均在 3.0.6 上，但 `WechatAppManager` 仍整体自实现 `IAppManager<IWechatAppContext>` 而非继承 `DefaultAppManager`，其 AGENTS.md 仍把"影子注册表""同实例要求"记为硬约束 —— 已修复的缺陷没有转化为下游信心，契约变更缺乏传达与升级通道。
> 2. **令牌归属模型缺位**：微信"一企一份 / 服务商套件共享 / 第三方令牌链"三类归属只能靠 scopes 当路由键、`ISharedTokenManager` 标记接口、自造三段式 key 拼装，通用契约（OAuth2 中心）与平台契约（凭据即路由）之间存在模型错位。
> 3. **逃生场景无一等支持**：`[Token]` 声明后无法按方法豁免（免令牌应急端点须拆独立接口 workaround）；`RegisterSwitcherFactory`/`GetWebApi<T>` 与生成器产物构造签名不对齐，接近死 API。

---

## 评审范围与方法

| 维度 | 方法 |
| --- | --- |
| 核心库 | 直读契约与关键实现：`IAppManager`/`DefaultAppManager`/`IAppContextHolder`/`IAppContextSwitcher`、`ITokenManager`/`TokenManagerBase`/`ITokenStore`/`ScopeKeyBuilder`/`TokenInjectionMode`、`IBaseHttpClient`/`IEnhancedHttpClient`/`ServiceCollectionExtensions`、生成器 `TokenAttribute` 语义 |
| 下游 | 两个仓库的 csproj 引用面、`WechatWorkMultiAppExtensions`/`ChannelsMultiAppExtensions`/`WechatAppManager`/`CorpTokenManager`/`ChannelsTokenTypes`/AGENTS.md 约束文档、升级冒烟测试 |
| 交叉 | 下游"绕过/自建/workaround"证据 ↔ 核心库能力面逐条对照 |

**下游引用面事实**（两仓一致）：全线 `PackageReference` `Mud.HttpUtils` / `Mud.HttpUtils.Generator` / `Mud.HttpUtils.Attributes` / `Mud.HttpUtils.OpenTelemetry` **3.0.6**（全仓单一版本，防 `TypeLoadException`）；**零引用** `Mud.HttpUtils.Resilience`（但被元包硬依赖带入）、`Mud.HttpUtils.Xml`、`Mud.HttpUtils.Newtonsoft.Json`。

---

## 一、逻辑链条完整性

### 1.1 主链路：声明 → 生成 → 注册 → 解析 → 注入

```
[HttpClientApi]/[Token]/[Get|Post|...]  （Attributes，纯元数据）
        │  编译期
        ▼
生成实现类 + 注册代码（Generator：TokenMethodHelper / MethodAnalyzer）
        │  运行时
        ▼
ITokenProvider → IMudAppContext.GetTokenManager(TokenManagerKey) → ITokenManager（缓存/刷新/恢复）
        │
        ▼
EnhancedHttpClient / DefaultHttpRequestExecutor（注入 token、序列化、拦截器、弹性、恢复）
```

链条**基本连贯**：
- `TokenAttribute.TokenManagerKey` 解耦业务概念与查找键，生成器有"方法级覆盖接口级"（MT-21）与"TokenType→TokenManagerKey 回退"的显式语义，并有专门测试（`TokenManagerKeyInferenceExemptionTests`）。
- appContext 由生成代码传入 `_appContext.Value`，与 `UseAppScope`/`UseDefaultAppScope` 作用域式切换对齐（旧 `UseApp`/`UseDefaultApp` 已 Obsolete 并在 3.0.0 移除，BC-27）。
- `AppContextScope.Dispose` 归属判定 fail-closed（B8），多租户隔离语义被测试钉死。

### 1.2 发现的断裂点

- **J-1（高）`[Token]` 无豁免/可选语义**：`TokenAttribute`（[TokenAttribute.cs](../Mud.HttpUtils.Attributes/Interface/TokenAttribute.cs)）`AllowMultiple=false`，只有"怎么注入"没有"不注入"。下游 `IChannelsBasicTokenFreeService.ClearQuotaV2Async`（小店 `clear_quota/v2` —— 官方"access_token 耗尽后唯一能重置配额"的逃生端点）**被迫拆独立接口**规避自动取令牌。影响：应急/匿名端点无法与常规端点同接口表达，接口拆分是纯 workaround。
- **J-2（高）切换器工厂契约与生成器产物不对齐**：[DefaultAppManager.cs#L484-L505](../Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs) `CreateContextSwitcher` 在未注册工厂时抛出的异常消息本身就承认："生成的实现类构造函数有 3 个必需参数，而工厂委托只有 1 个入参……推荐不要用本 API"（SW-05）。即 `GetWebApi<T>`/`RegisterSwitcherFactory` 处于"存在但不可用"状态。
- **J-3（中）模式矩阵的认知外溢**：`IAppContextSwitcher.GetTokenAsync` 已 Obsolete 且仅 TokenManager 模式可生成（否则 HTTPCLIENT024 Error）；接口继承组合不匹配另有专门诊断（基接口模式混用）。约束合理但数量多，下游 AGENTS.md 用大段篇幅记录"生成器使用纪律"，说明契约复杂度已外溢为下游文档负担。
- **J-4（中）`ITokenStore` 暗门**：注册 `ITokenStore` **不会**自动获得持久化能力，必须经 `TokenStoreBackedTokenCache<T>` 桥接器接入；库靠启动校验 EventId 191 提示补救（[ITokenStore.cs 注释](../Mud.HttpUtils.Abstractions/TokenManager/ITokenStore.cs)）。"注册了不生效"是典型契约暗门。

---

## 二、多应用管理

### 2.1 架构设计评估

**合理性：良好。** `IAppManager<TAppContext>` + `IMudAppContext`（AppKey + HttpClient + TokenManager + GetService）的组合是干净的多租户投影：appKey 区分大小写属**有意契约**（G8-11，测试钉死）、`ConfigurationChanged` 事件逐订阅者隔离（B4）、`RegisterLazy` 懒加载（G3/B8）、默认应用回退（B3/MT-08 锁内收敛）、异常回显走 `ToSafeText` 防日志注入。并发语义（TryAdd / TryUpdate CAS）严谨。

**但下游没有用它。** 这是本次评审最重要的发现：

> `WechatAppManager` **直接实现** `IAppManager<IWechatAppContext>`，不继承 `DefaultAppManager<T>` —— 下游 AGENTS.md 记载的原因是"基类有影子注册表和非 virtual 写入口，注册会静默写入读不到的表"。

对照核心侧：该缺陷（查询入口 virtual、写入口非 virtual 的不对称）**已在 G3 修复**（`RegisterApp`/`UpdateApp`/`DefaultAppKey` 等全部 virtual 化，见 [DefaultAppManager.cs#L114-L121](../Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs) 注释原文"形成影子注册表，派生类的查询视图看不到基类注册项，只能整体弃用基类继承"）。但下游认知仍停留在缺陷时代，且继续加固自实现（"重复 `AddChannelsApp` 会覆盖注册表"等约束全部自行看管）。**修复已落地，信任未修复** —— 缺陷期间形成的 workaround 无人回收。

### 2.2 发现的问题

- **MA-1（高）默认实现的可替换性/可信度不足**：一旦默认实现出过"静默失效"级别的缺陷，下游选择永久自实现。建议：① 为自实现注册表提供官方"认可路径"（文档 + 合规测试套件）；② 把 `DefaultAppManager` 的注册表存储抽为可注入（或提供 `IAppRegistry` SPI），使"自定义注册表"不再需要整体重写 15 个接口成员。
- **MA-2（中）回调场景"外部标识反查应用"无扩展点**：企微回调需 `SuiteId → appKey` 匹配，且明确"不得走 TryGetApp"（`TryGetApp` 会构造 HttpClient/DI scope/Timer）。`IAppManager` 只有 appKey 精确查找；`IAppAccessAuthorizer`/`AppKeyAllowListAuthorizer` 管"能不能访问"不管"怎么找到"。缺一个 `IAppLookupProvider`（外部标识 → appKey 映射）扩展点。
- **MA-3（中）per-app 惰性构造能力不足**：`RegisterLazy` 工厂签名仅 `Func<TAppContext>`，而下游 `TryGetApp` 内要构造 per-app HttpClient + DI scope + Timer。上下文工厂若需要 per-app DI scope/命名 HttpClient 组合，当前契约给不了，只能自建。
- **MA-4（低）历史 API 遗留**：`GetWebApi`/`GetDefaultWebApi`/`RegisterSwitcherFactory`（见 J-2）在推荐路径（DI 解析 + `UseAppScope`）确立后应降级或移除，避免新用户踩坑。

---

## 三、令牌管理

### 3.1 架构设计评估

**合理性：良好，深度过剩偏正面。** 契约层次清晰：

| 层 | 契约 | 职责 |
| --- | --- | --- |
| 获取 | `ITokenManager` / `TokenManagerBase` / `OAuth2TokenManagerBase` / `UserTokenManagerBase` | 缓存、并发刷新去重、失效、后台刷新开关 |
| 注入 | `ITokenProvider` + `TokenInjectionMode`（Header/Query/Path/ApiKey/Hmac/Basic/Cookie） | 生成代码 → 请求注入 |
| 存储 | `ITokenCache`（volatile）↔ `ITokenStore`（durable）+ `TokenStoreBackedTokenCache` 桥 | 进程内/跨实例分层 |
| 恢复 | `TokenRecoveryExecutor` + `ITokenInvalidationDetector` + `TokenRecoveryDelegatingHandler` | 401/errcode 自愈、Query 令牌重注入 |
| 运维 | `TokenRefreshHostedService`/`BackgroundService`/`RefreshDedupTable`/HealthCheck/Stats | 后台刷新、观测 |

亮点：`ScopeKeyBuilder` 的单射键设计（US 分隔 + 可逆转义 + 记忆化）修掉了真实的作用域越权面（MT-16）；`BindTenantGuard` bind-once 防单实例跨租户凭据错配（SR-H5）；`ISharedTokenManager`（WX-02 注释明写"服务商/套件令牌"）**就是微信需求塑造库的直接痕迹**。

### 3.2 发现的问题

- **TK-1（高）令牌归属模型缺一等表达**。库的隐含模型是"OAuth2：tokenManager × scopes"；微信的模型是**归属**驱动：
  - 企业级令牌**一企一份**（`scopeKey = authCorpId`，显式 `GetTokenAsync(new[]{ authCorpId })` —— scopes 被当作路由键用，语义已泛化偏离 OAuth scope）；
  - 服务商/套件令牌**全租户共享**（只能靠 `ISharedTokenManager` 标记豁免租户绑定）；
  - 第三方/代开发是**链式令牌**（`provider_access_token → suite_access_token → 授权企业 access_token`），链上游失效要级联刷新 —— 下游在 `CorpTokenManager` 内按 `AppType` 自行分流，库没有"令牌依赖链"概念。
  - 下游自造三段式键 `{tokenType}:{appKey}:{scopeKey}` 佐证：复合键模型本应由库公开（`CompositeKey` 目前是 internal），下游只能自拼。
- **TK-2（高）跨实例共享无官方实现**：默认全进程内（`MemoryTokenStore`/`ConcurrentDictionaryTokenCache`），分布式必须自实现 `ITokenStore` + 经桥接器（J-4 暗门）；下游靠 `Mud.Wechat.Redis` 自救，`MudHttpUtilsUpgradeSmokeTests` 把"升级不失效 errcode 恢复"钉成回归。对多实例部署（微信 token 全局唯一的刚性场景）这是**必备项而非可选项**。
- **TK-3（中）免令牌/条件令牌逃生**：见 J-1。微信 `clear_quota` 场景证明"强制取令牌"会**复刻官方事故**（token 耗尽时调用重置接口本身需要 token）。
- **TK-4（低）独立令牌类型靠命名纪律**：小店线为避免共享 `ITokenManagerRegistry` 冲突而新增 `Wechat.Channels.AccessToken`（注释：复用会导致 `Resolve` 返回 null、errcode 自愈静默失效）。注册表键冲突应当在注册期报错，而非运行期静默 null —— 需核实 `DelegateTokenManagerRegistry`/`ITokenManagerRegistry` 重复键注册是否有诊断。

### 3.3 契约评估

- 清晰度：**4/5**（接口职责分明，但 ITokenCache/ITokenStore 双层 + 桥接器的"暗门"扣分）
- 扩展性：**3/5**（归属模型靠标记接口与 scopes 技巧拼装，链式令牌无支持）
- 并发安全性：**5/5**（ScopeKeyBuilder 单射、RefreshDedupTable、bind-once、CAS 更新、fail-closed 释放，均有测试）

---

## 四、HttpClient 客户端

### 4.1 架构设计评估

**接口分层合理，实现聚合过度。** `IBaseHttpClient`/`IJsonHttpClient`/`IXmlHttpClient`/`IEncryptableHttpClient` → `IEnhancedHttpClient` 的接口隔离是教科书式；能力面覆盖度高：

| 场景 | 支持状态 | 备注 |
| --- | --- | --- |
| GET/POST/PUT/DELETE/PATCH/HEAD | ✅ | 生成器声明式 |
| Query/Header/Path 令牌注入 | ✅ | 微信强制 Query 注入 + MUD005 豁免机制（`Justification`）成熟 |
| 文件上传（multipart） | ✅ | `MultipartItem`/`UploadAttribute`/`ProgressableStreamContent` |
| 文件下载（小文件/大文件流式+进度） | ✅ | `DownloadLargeAsync` + `IProgress<long>` |
| 流式响应（SSE/NDJSON） | ✅ | `IAsyncEnumerable<T>` + AOT 安全 `JsonTypeInfo` 重载 |
| 序列化（STJ + 自定义） | ✅ | `IHttpContentSerializer` + JsonContext（AOT） |
| 拦截器 | ✅ | 请求/响应拦截 + 响应缓存 |
| 弹性（重试/超时/熔断） | ✅（Resilience 包） | 下游零直接使用，见 O-5 |
| 可观测性 | ✅ | Activity/Meter/HealthChecks/脱敏 |
| SSRF 防护 | ✅ | `SsrfSafeSocketsHttpHandler`/URL 校验 |

**但实现是巨型类**：`EnhancedHttpClient.cs` 133KB（44 个 public 成员）、`DefaultHttpRequestExecutor.cs` 60KB、`TokenRecoveryExecutor.cs` 83KB、`MudHttpClientLog.cs` 69KB（**一个日志类 69KB**）。接口隔离了，实现没隔离 —— 可维护性/评审成本/回归风险都在这四个文件里。

### 4.2 发现的问题

- **HC-1（中）实现层 God Class**：上述四文件合计约 330KB。建议按职责拆分（发送/下载/流式/序列化装配、恢复状态机、日志局部函数化）。
- **HC-2（中）生成式与手拼式双轨并存无统一指引**：下游声明式为主，但文件上传封装、加密消息体、回调加解密仍自实现手拼 `HttpRequestMessage`。前者是垂直需求（微信 XML/加密信封）可以理解；但"何时该用生成器、何时该手拼、两者如何共享拦截器/恢复/可观测性管线"缺少官方裁决文档 —— 手拼路径若绕过 `DefaultHttpRequestExecutor`，恢复/注入/观测能力会静默缺失。
- **HC-3（低）垂直能力（微信加密消息体、回调验签）不属通用库职责**，现状（下游自实现）合理，不必吸收；但可提供"自定义 HttpContent + 拦截器"的官方示例锁定管线复用方式。

---

## 五、过度设计识别

| # | 证据 | 判定 |
| --- | --- | --- |
| O-1 | `GetWebApi<T>`/`RegisterSwitcherFactory`：工厂签名与生成器构造（3 参）不对齐，异常消息自己劝退（SW-05） | **死 API**，删或重设计（P1） |
| O-2 | `IAppContextSwitcher.GetTokenAsync`：Obsolete 且模式受限（HTTPCLIENT024 陷阱） | 过渡期遗留，应定移除版本 |
| O-3 | `TokenInjectionMode` 7 种（Header/Query/Path/ApiKey/Hmac/Basic/Cookie），两下游仅用 Query/Header；Cookie/Path 未见使用 | 广撒网式枚举，可保留但应标注成熟度 |
| O-4 | 刷新/恢复体系类型过多：`TokenRecoveryExecutor`(83KB) + `RefreshDedupTable` + `TokenRefreshHelper` + `TokenRefreshHostedService` + `TokenRefreshBackgroundService` + `ITokenRefreshBackgroundService` + 两套 Options | 能力真实（下游依赖 errcode 恢复），但**机制碎片化**，建议收敛为单一"刷新编排器"门面 |
| O-5 | `Mud.HttpUtils.Resilience` 被元包硬依赖，两下游零引用（Xml/Newtonsoft 作为可选适配器未被引用则合理） | 元包不应硬绑 Resilience；改为可选或文档明示 |
| O-6 | `TokenRecoveryExecutor` 单类 83KB、`UserTokenManagerBase` 60KB、`TokenManagerBase` 52KB | 不是"多余功能"而是"未拆分的复杂度" |

> 反向证据（非过度设计，做得对）：可观测性面大但下游真实接入（`Mud.Wechat.OpenTelemetry` 引用 `Mud.HttpUtils.OpenTelemetry`）；AOT/裁剪治理、PublicAPI 面管控、诊断豁免审计（`Justification`）都是下游直接吃到的红利。

---

## 六、功能缺口清单（下游需要但当前不支持/支持不好）

| # | 缺口 | 优先级 | 下游证据 |
| --- | --- | --- | --- |
| G-1 | **方法级免令牌/条件令牌**：`[Token]` 无 opt-out | **P0** | `IChannelsBasicTokenFreeService.ClearQuotaV2Async` 拆独立接口规避强制取令牌（会复刻官方"token 耗尽无法重置配额"事故） |
| G-2 | **令牌归属一等模型**（per-app / per-corp 一企一份 / 全局共享 / 链式依赖） | **P0** | `scopeKey = authCorpId` 把 scopes 当路由键；`ISharedTokenManager` 标记；下游自拼 `{tokenType}:{appKey}:{scopeKey}` 三段式 |
| G-3 | **官方分布式令牌存储/缓存（Redis）** + 消除 `ITokenStore`→桥接器暗门 | **P1** | 下游 `Mud.Wechat.Redis` 自救；AGENTS.md"多实例部署需宿主预注册分布式实现覆盖" |
| G-4 | **外部标识反查应用**（回调 `SuiteId → appKey`）扩展点 | **P1** | 回调匹配明确"不得用 TryGetApp"，全部自建映射 |
| G-5 | **多应用注册表可替换性**（SPI 或官方自实现合规套件） | **P1** | `WechatAppManager` 整体自实现 `IAppManager`，弃用 `DefaultAppManager` |
| G-6 | **per-app 惰性上下文工厂**（DI scope + 命名 HttpClient 组合） | **P2** | `TryGetApp` 内自构造 HttpClient/DI scope/Timer |
| G-7 | **链式令牌级联刷新**（provider→suite→authorizer） | **P2** | `CorpTokenManager` 按 `AppType` 内部分流，`42001 → 刷新 suite_access_token → 重注入 Query` 冒烟测试 |
| G-8 | **契约变更传达/升级通道** | **P1** | 下游 AGENTS.md 仍钉"影子注册表/同实例要求"等 3.0.6 已修复项；重打包缓存不失效问题被记录为风险 |

---

## 七、改进建议清单

| 优先级 | 类别 | 建议 | 预期收益 |
| --- | --- | --- | --- |
| **P0** | 令牌/生成器 | 增加免令牌一等支持：方法级 `[Token(Disabled = true)]` 或 `[NoToken]`，显式阻断接口级继承 | 逃生端点与匿名端点可同接口表达，消除 G-1 拆接口 workaround |
| **P0** | 令牌契约 | 公开令牌归属键契约：把 internal `CompositeKey`/下游三段式需求吸收为 `TokenScopeKey`（tokenType + appKey + scopeKey）公开模型；为"共享凭据/一企一份/链式令牌"提供命名概念（Ownership 枚举或标记接口族） | 消除 scopes 语义泛化与自拼键；微信/钉钉/飞书类平台可直接映射 |
| **P1** | 多应用 | `IAppLookupProvider`（外部标识 → appKey）扩展点 + `DefaultAppManager` 注册表 SPI（或官方自实现合规测试包） | 回调匹配入库；自实现注册表不再是唯一出路 |
| **P1** | 令牌 | 官方 `Mud.HttpUtils.Redis`（`ITokenStore`/`ITokenCache` 实现）；`ITokenStore` 注册后自动经 `TokenStoreBackedTokenCache` 接入，移除 EventId 191 暗门 | 多实例部署开箱即用；消除"注册不生效"陷阱 |
| **P1** | API 治理 | 移除/重设计 `GetWebApi`/`RegisterSwitcherFactory`（对齐生成器 3 参构造或删除）；为 `IAppContextSwitcher.GetTokenAsync` 定移除版本 | 死 API 出清，新用户不踩 J-2/J-3 |
| **P1** | 生态 | CHANGELOG 契约变更专章 + "下游约束解绑清单"（影子注册表 G3、双 AsyncLocal G7、同实例要求哪些已失效），与下游 AGENTS.md 对账 | 修复转化为下游信心，workaround 可回收 |
| **P2** | 结构 | 拆分 `EnhancedHttpClient`(133KB)/`TokenRecoveryExecutor`(83KB)/`DefaultHttpRequestExecutor`(60KB)/`MudHttpClientLog`(69KB) | 可维护性、评审成本、回归面 |
| **P2** | 打包 | 元包 Resilience 硬依赖 → 可选（下游零引用证据） | 包面与实际使用对齐 |
| **P2** | 令牌 | 链式令牌（依赖图 + 级联失效/刷新）一等支持 | G-7 入库，第三方平台场景通用化 |
| **P2** | 文档 | "生成器声明式 vs 手拼 HttpRequestMessage"裁决指引：手拼路径如何复用拦截器/恢复/观测管线 | 消除 HC-2 双轨静默能力缺失 |

---

## 附录：关键证据索引

**核心库**
- 影子注册表修复自述：[DefaultAppManager.cs#L114-L121](../Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs)（G3"查询虚、写入不虚"不对称 → 形成影子注册表 → 派生类只能整体弃用继承）
- 死 API 自证：[DefaultAppManager.cs#L484-L505](../Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs)（SW-05：工厂委托 1 参 vs 生成构造 3 参，"必然编译失败"）
- 双 AsyncLocal 治本：[DelegatingAppContextHolder.cs#L12-L51](../Mud.HttpUtils.Client/AppContext/DelegatingAppContextHolder.cs)（G7 说明"两个 AsyncLocal ⇒ 多应用静默失效"及顺序约束消除）
- fail-closed 作用域：[AsyncLocalAppContextSwitcher.cs#L53-L65](../Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs)（B8 归属判定）
- 租户绑定守卫：[TokenManagerBase.cs#L474-L538](../Mud.HttpUtils.Abstractions/TokenManager/TokenManagerBase.cs)（SR-H5 bind-once + `ISharedTokenManager` WX-02 豁免——微信服务商/套件令牌痕迹）
- 存储暗门：[ITokenStore.cs#L15-L23](../Mud.HttpUtils.Abstractions/TokenManager/ITokenStore.cs)（须 `TokenStoreBackedTokenCache` 桥接，EventId 191 提示）
- 令牌注入枚举：[TokenInjectionMode.cs](../Mud.HttpUtils.Abstractions/TokenManager/TokenInjectionMode.cs)（7 种模式）
- 声明式令牌：[TokenAttribute.cs](../Mud.HttpUtils.Attributes/Interface/TokenAttribute.cs)（无 opt-out；`Justification` 豁免审计；安全约束"生成器不接触密钥"）

**下游（D:\Repos\Mud.Wechat）**
- `Src\Work\Mud.Wechat.Work.Abstractions\Mud.Wechat.Work.Abstractions.csproj`：`Mud.HttpUtils`/`Generator` 3.0.6
- `Src\Work\Mud.Wechat.Work.Abstractions\Extensions\WechatWorkMultiAppExtensions.cs`：per-app 命名 HttpClient、令牌基础设施、`WechatAppManager` 注册
- `AGENTS.md`：TokenKey 三段式、AppKey 约束、企业级令牌一企一份、`UseCorpScope`、`IAppContextHolder`/`IAppContextSwitcher` 同实例要求、Redis 覆盖顺序

**下游（E:\Temp\MudWechat）**
- `AGENTS.md`：`WechatAppManager` 直接实现 `IAppManager<IWechatAppContext>`（影子注册表 workaround）、`TryGetApp` 构造 HttpClient/DI scope/Timer、回调 `SuiteId → appKey` 不走 TryGetApp、三种 `AppType` 令牌链、全仓单一版本与重打包缓存风险
- `Src\Channels\Mud.Wechat.Channels.Abstractions\ChannelsTokenTypes.cs`：独立 `Wechat.Channels.AccessToken` 防注册表冲突（复用会导致 Resolve null、errcode 自愈静默失效）
- `IChannelsBasicTokenFreeService.ClearQuotaV2Async`：免令牌逃生端点拆接口证据
- `MudHttpUtilsUpgradeSmokeTests`：42001 → 刷新 `suite_access_token` → Query 重注入回归
