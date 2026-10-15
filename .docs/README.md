# `.docs` 文档索引

> Mud.HttpUtils 的审查 / 方案 / 报告文档集中存放目录（`/.docs` 已在 `.gitignore` 中，不参与打包与提交）。
> **新增文档请在本索引登记一行**，避免目录膨胀后无法检索。

---

## 本轮（2026-10-09 下游消费分析）

| 文档 | 内容 |
| --- | --- |
| `2026-10-09-下游消费分析-Bug修复与功能完善方案.md` | **本轮主方案**：基于下游 `Mud.Feishu`(3.0.0) 与 `Mud.Wechat`(3.0.3) 真实消费证据驱动的 Bug 修复（B1~B8）与功能完善（F1~F7）；P0-1 分层重构明确不执行、P0-2 管线收敛列远期路线 |

## 本轮（2026-10-02 多应用与令牌管理审查）

| 文档 | 内容 |
| --- | --- |
| `2026-10-02-多应用与令牌管理-审查修复方案.md` | **本轮主方案**：修复策略 / 批次划分 / ADR / 风险登记 / 验收标准 / 待决事项 + 附录 A（测试补强，含 net6.0 真实容器测试层）+ 附录 B（CHANGELOG 草稿） |
| `fix-items.md` | 批次 A/B/C 逐项修复方案（P0-1 ~ P0-5、P1-1 ~ P1-3） |
| `fix-items-d-e.md` | 批次 D/E 逐项修复方案（P1-4 ~ P1-9、P2-1 ~ P2-6） |

## 多应用与令牌管理（前序轮次）

| 文档 | 内容 |
| --- | --- |
| `多应用与令牌管理-Bug修复与功能完善方案.md` | v1.1，`MT-01 ~ MT-28`，**已全部实施**（本轮编号自 `MT-30` 起） |
| `多应用切换-API收敛-Bug修复与功能完善方案.md` | 3.0.0 的 BC-27 三个旧切换入口移除 |
| `不可信appKey无作用域切换-补缺方案.md` | `AppKeySwitchExtensions`（SW-15）设计方案 |
| `MultiTenantScenario.md` | 多租户场景说明 |
| `Token代码生成重构方案.md` | 令牌代码生成重构 |

## 生成器（Generator）

| 文档 | 内容 |
| --- | --- |
| `05-Generator生成器缺陷修复与功能完善方案.md` | 生成器缺陷修复方案 |
| `06-Generator审查缺陷修复与功能完善方案.md` | 生成器审查缺陷修复 |
| `07-Generator专项审查修复与功能完善方案.md` | 生成器专项审查 |
| `08-Generator四主线专项审查修复与功能完善方案.md` | 生成器四主线专项 |
| `09-Generator审计复核修复与功能完善方案.md` | 生成器审计复核 |
| `Mud.HttpUtils.Generator 生成代码全面审查报告.md` | 生成产物全面审查报告 |
| `PayloadFieldMapGenerator-详细设计-v1.md` | **第 4 个生成器族** `PayloadFieldMapGenerator` 详细设计（**v2.4，已实施并在真实消费方走通**，本地 3.0.1；消费方 `Mud.Wechat` 回调载荷驱动）：委托表生成 + XML-free `PayloadNode`（落 `Abstractions`）+ 9 个诊断（`PAYLOAD001~009`，全 Error）+ C#7.3/netstandard2.0 消费面基线；含 roslyn#57239 规避论证、映射表无状态不变式（§3.7）、**v2.0 的 14 项修正（§0.5）**、落地阶段与门禁结果、实施期缺陷留档（附录 C）；**v2.1 收口**：零 LINQ-to-XML 机器化守卫（含非空转验证）、CA1508 抑制失效修复、README 无状态约束；**v2.2 复核收口**：8 项修正（§0.7 P15~P22，均附「修复前实测编译错误码」）—— 类形态/属性形态/转换器签名/泛型约束四面拦截（`CS0718`/`CS0310`/`CS1001`/`CS1061`/`CS1503`/`CS0315` 消失）、泛型实参保留可空标注（`CS8619` 消失）、指纹纳入可空标注（修复增量缓存过期）、新增 17 个用例；**v2.3 消费方落地反查**：修正 §3.4 示例不可编译（`CS0266`）与 §9.2 漏写的「生成器须在声明载荷类的工程显式再挂」关键接入步骤（缺陷 21/22）、空转用例+`struct`/`interface` 断言（缺陷 23），§12 D2 由实施结论定案；**v2.4 生成物卫生收口**：5 项修正（§0.8 P23~P27，均附「修复前实测编译错误码/报错报文」）—— 契约方法首参可空标注（`CS8604` 消失，`PAYLOAD004`）、类名/属性名保留字转义（`CS1001`/`CS0260` 消失）、`PAYLOAD008` 扩面到继承链同名成员（`CS0108` 消失）、同名重载优选（消除误报）、关键字命名空间的 hintName 剥转义（`PAYLOAD001` 内部兜底不再被合法输入触发），新增 8 个用例（含通用「生成物零新增错误/告警」断言），全量 985 通过 / 0 失败（8 个测试工程 test.ps1 全绿） |

## HttpClient / 性能 / 弹性

| 文档 | 内容 |
| --- | --- |
| `HttpClient审查修复与功能完善方案-M4.md` / `-M5.md` / `-M6.md` | HttpClient 审查 M4 / M5 / M6 |
| `hybrid-executor-refactor-plan.md` | 混合执行器重构 |
| `Mud.HttpUtils.Client 模块冗余代码分析报告.md` | Client 模块冗余分析 |
| `Mud.HttpUtils.Client 冗余代码清理与重构方案.md` | Client 冗余清理方案 |
| `Mud.HttpUtils 代码清理报告.md` | 全仓代码清理报告 |
| `breaking-changes-http-content-serializer.md` | HttpContentSerializer 破坏性变更 |
| `refactor-http-content-serializer.md` | HttpContentSerializer 重构 |
| `config-management-bugfix-refactor-plan.md` | 配置管理修复与重构 |
| `Refit-Benchmark-Refactor-Plan-v1.md` / `-v2.md` | Refit 基准重构 |
| `JsonAotSourceGeneratorPlan.md` | JSON AOT 源生成器方案 |

## AOT 兼容性

| 文档 | 内容 |
| --- | --- |
| `AOT-兼容性审查报告.md` | AOT 兼容性审查报告 |
| `AOT-兼容性修复与功能完善方案.md` | AOT 修复与完善方案 |
| `AOT-Refactoring-Plan-v1.md` ~ `-v4.md` | AOT 重构计划 v1~v4 |
| `AOT-OpenTelemetry-Limitations.md` | AOT 下的 OpenTelemetry 限制 |

## 配置参数审查

| 文档 | 内容 |
| --- | --- |
| `配置参数审查整改与功能完善方案-v3.md` | 配置参数审查整改 v3 |
| `配置参数整改与功能完善方案-v2.md` | 配置参数整改 v2 |
| `AES-信封版本前缀歧义消除方案.md` | AES 信封版本前缀歧义消除 |

## 总体 / 功能规划

| 文档 | 内容 |
| --- | --- |
| `00-总体方案.md` | 总体方案 |
| `01-P0-阻塞修复方案.md` ~ `04-测试与验收方案.md` | 分级修复与测试验收方案 |
| `Mud.HttpUtils 功能升级改造方案.md` | 功能升级改造方案 |
| `Mud.HttpUtils 功能升级改造详细设计说明.md` | 功能升级详细设计 |
| `MudHttpUtils-推广博文.md` | 推广博文 |

## 审查提示词（`code-review/`）

| 文档 | 覆盖范围 |
| --- | --- |
| `多应用与令牌管理-AI代码审查提示词.md` | 多应用 + 令牌管理（**本轮审查所用**） |
| `HttpClient-性能功能安全三主线-AI代码审查提示词.md` | HttpClient 性能 / 功能 / 安全三主线 |
| `HttpClient-AI代码审查提示词.md` | HttpClient 专项 |
| `代码生成器专项-AI代码审查提示词.md` | 代码生成器专项 |
| `Generator-AI代码审查提示词.md` | Generator 专项 |
| `AOT兼容性专项-AI代码审查提示词.md` | AOT 兼容性专项 |
| `AI-Config-Review-Prompt.md` | AI 配置审查 |
