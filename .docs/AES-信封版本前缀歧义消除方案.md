# AES 密文信封：版本前缀字节歧义消除方案

> **文档版本**：v2.0（已评审 —— 采纳 OPT-C 一步重构，实施中）
> **编制日期**：2026-09-12
> **状态**：✅ 评审通过 → 分阶段实施（见 §七）
> **编制依据**：`Mud.HttpUtils.Client/Encryption/DefaultAesEncryptionProvider.cs` 现状复核 + 配置整改方案（CFG-27）实施中暴露的用例偶发失败（E-4）
> **关联文档**：
> - [配置参数整改与功能完善方案-v2.md](./配置参数整改与功能完善方案-v2.md) —— CFG-15（`EnableAuthenticatedEncryption=false` 安全警告，**本方案已使其失效**）、CFG-27（`[Obsolete]` 残留清理）
> - [00-总体方案.md](./00-总体方案.md) §7（`#7` AES-CBC 无 MAC 的修复背景）
> - [README.md](./README.md) —— `.docs` 索引
>
> **编号规则**：问题 `AE-nn`（AES Envelope），方案项 `OPT-A/B/C/D`，实施任务 `AE-Tnn`。
>
> **v2.0 变更摘要**（相对 v1.0 草案）：见 §0 评审结论。核心变化为「主体方案由 OPT-B 改为 **OPT-C 一步重构**」，
> 并**删除**迁移开关、magic 增强、`TryReadEnvelopeVersion` 三个子项，新增 B-6 常量时间比较去重。

---

## 〇、评审结论（v2.0）

> 评审于 2026-09-12 完成，四个角色独立验证后合并。以下结论已直接并入正文相应章节。

### 0.1 产品经理视角：问题是否成立 / 是否必要 / 是否最优

| 结论 | 说明 |
| :--- | :--- |
| **问题成立** | AE-1~AE-5 均经代码复核属实；AE-1 有实证（E-4，配置方案 §14.4 已记录）。 |
| **严重性需修正** | v1.0 把 AE-1 定为 🔴 严重属**高估**。复核 `DecryptCore:167-183` 可知：裸 CBC 与版本字节冲突仅在「`_authenticated=true` 的 provider 解密裸 CBC」时发生，后果是**异常类型/消息语义不准 + 测试 flaky**，不产生数据损坏。真正的数据完整性风险是 **AE-2**。已下调为 🟠 重要（见 §1.3）。 |
| **方案必要** | AE-2（静默数据损坏）必须消除；AE-1 值得消除（flaky + 语义）。必要。 |
| **业务逻辑链条完整** | 加密侧/解密侧/异常契约/迁移路径均已覆盖。**唯一实质缺口**：`IEncryptableHttpClient.EncryptContent` 产出的密文会**经 HTTP 跨进程传输**，对端 TFM 未知，而 v2.0 前方案把「跨运行时可移植」（B-3）列为阶段 5 可选 —— 应提前到主体（见 §3.5）。 |
| **非最优，已优化** | v1.0 的 OPT-B「保留裸 CBC 并给它加 `0x01` 前缀」在**项目未发布、无存量密文**的前提下不是最优：它为了一条「无完整性保护、已被 CFG-15 判定为不安全」的路径，新增了 1 个版本值、1 个迁移开关、1 个 `[Obsolete]` 过渡期和 2 条 PublicAPI 条目。**删除该路径（OPT-C）才是帕累托最优**：格式数 3→2，代码净减少，且 AE-1/AE-2 从根上消失。 |

### 0.2 系统架构师视角：链路完整性 / 架构合理性 / 冲突

| 结论 | 说明 |
| :--- | :--- |
| **加密侧→解密侧链路闭合** | 改造后 `EncryptCore` 只有两条出口（0x02 / 0x03），`DecryptCore` 用 `switch` 全量覆盖，无「配置参与分派」。 |
| **与现有架构无冲突** | 已复核全部消费点：`MemoryEncryptedTokenStore`（只调 `Encrypt/Decrypt` 字符串 API）、`EnhancedHttpClient.DecryptContent`（从 JSON 取出 Base64 后调 `Decrypt`，无字节布局假设）、`ResilientHttpClient`（纯委托）、`DirectEnhancedHttpClient`/`HttpClientFactoryEnhancedClient`（委托 `IEncryptionProvider`）—— **全库无第二处解析密文布局**。`IEncryptionProvider` 接口形状不变。 |
| **发现冲突 1（v1.0 未识别）** | 移除裸 CBC 后，CFG-15 的 `MudHttpClientLog.AuthenticatedEncryptionDisabled`（EventId 116）**失去唯一触发点**，将成为死代码。已在 §4 / 附录 D 给出处置。 |
| **发现缺陷 2（新增 AE-6）** | 异常契约不自洽：`DecryptCore` 对空输入抛 `InvalidOperationException`，而其余格式错误抛 `CryptographicException`；`Decrypt` 的 Base64 解码失败抛 `FormatException`，未纳入契约。已在 附录 C 统一。 |
| **可观测性缺口（新增 AE-7）** | `net8+` 上 `AesGcm.IsSupported == false` 时会静默降级为 0x03，无任何记录，导致"为什么这份密文是 0x03"不可诊断。已在 §3.5 补 EventId 119。 |

### 0.3 高级程序员视角：功能重复 / 冗余代码

| 结论 | 说明 |
| :--- | :--- |
| **发现已存在的重复（B-6，v1.0 未识别）** | `FixedTimeEquals` 在 `DefaultAesEncryptionProvider.cs:328-337` 与 `DefaultHmacSignatureProvider.cs:149-163` **两处重复实现**；且前者在 net5+ 上也手搓循环，未复用 `CryptographicOperations.FixedTimeEquals`。已收敛到 `SecurityHelper`（§3.6）。 |
| **删除：`AllowLegacyUnprefixedCipherText`（v1.0 §3.6）** | 无存量密文 → 该开关的**唯一价值（兼容）为零**，却带来 1 个 `[Obsolete]` 公开属性 + 4 条 PublicAPI 条目 + 一段分派特例。属"未发布即背技术债"。**删除**。 |
| **删除：B-2（`[Obsolete]` 过渡）** | OPT-C 直接删除，无需先 `[Obsolete]` 再删除的两步走。 |
| **删除：B-4（3 字节 magic）** | 与版本前缀**功能重复**（同为格式识别）。对本库产出密文，版本前缀已是零歧义；对外部随机字节本就应拒绝。 |
| **删除：B-5（`TryReadEnvelopeVersion`）** | 当前**无任何消费点**，属"为假想未来需求预建"的冗余 internal API。待真正写迁移工具时再按需添加。 |
| **保留但不改：`ComputeHmac`** | 用 `TransformBlock + TransformFinalBlock` 而非 `Concat + ComputeHash`，是为了避免一次中间数组分配，**不是冗余**，保留并补注释。 |
| **保留：B-3（跨运行时可移植）** | 经 PM/架构双向验证为真实需求（跨进程密文 + TFM 差异），且实现成本 ≈ 10 行，**提前到主体**。 |

### 0.4 测试方案验证

v1.0 §6 测试方案**覆盖度合格**（10 条新增用例 + E-4 转正 + 回归清单），但存在 4 处缺口，已在 §6 补齐：

1. 缺 `IEncryptableHttpClient.EncryptContent/DecryptContent` 端到端往返回归（只列了 `DirectEnhancedHttpClientTests`）；
2. 缺 `EncryptBytes`/`DecryptBytes` 空数组与 1 字节边界；
3. 缺 B-3（`RequireCrossRuntimePortable=true` 在 net8+ 产出 0x03）用例；
4. 缺"公开面已移除 `EnableAuthenticatedEncryption`"的反向断言（防回潮）。

---

## 一、背景与问题陈述

### 1.1 现状：三种密文信封格式（v2.0 实施前）

`DefaultAesEncryptionProvider` 按 **首字节版本前缀** 分派解密，但**并非所有格式都写版本前缀**：

| 版本 | 常量 | 布局 | 是否写版本前缀 | 产出条件 |
| :--- | :--- | :--- | :---: | :--- |
| v1 裸 CBC | *（无常量）* | `[IV(16)][密文]` | ❌ **无** | `EnableAuthenticatedEncryption = false` |
| v2 AesGcm | `EnvelopeVersionGcm = 0x02` | `[0x02][nonce(12)][tag(16)][密文]` | ✅ | 认证加密开启 **且** net8+ 且 `AesGcm.IsSupported` |
| v3 CBC+HMAC | `EnvelopeVersionCbcHmac = 0x03` | `[0x03][IV(16)][MAC(32)][密文]` | ✅ | 认证加密开启，其余运行时 |

关键常量（`DefaultAesEncryptionProvider.cs:30-35`）：
`IvSizeBytes = 16`、`GcmNonceSize = 12`、`GcmTagSize = 16`、`HmacTagSize = 32`。

### 1.2 当前解密分派逻辑（`DecryptCore`，`:159-188`）

```
1. len == 0                         → InvalidOperationException("长度不足")
2. b[0] == 0x02 && _authenticated   → DecryptGcm
3. b[0] == 0x03 && _authenticated   → DecryptCbcThenHmac
4. !_authenticated                  → DecryptCbc（裸 CBC）
5. 其他                              → CryptographicException("缺少版本前缀")
```

**分派同时依赖「密文首字节」与「当前运行时配置 `_authenticated`」两个输入 —— 这是 AE-1/AE-2 的共同根因。**

### 1.3 根因分析（v2.0 修订）

| # | 问题 | 根因 | 级别（v2.0） | 级别（v1.0） |
| :--- | :--- | :--- | :--- | :--- |
| **AE-1** | **裸 CBC 与版本前缀字节冲突** | 裸 CBC 无前缀，其首字节是**随机 IV 的第 1 字节**；若恰为 `0x02`/`0x03`（2/256 ≈ **0.78%**），会被误判为 v2/v3 信封。实际后果为**异常语义不准 + 测试 flaky**（进入 GCM/CBC+HMAC 分支后必然因长度或 MAC 校验失败而抛 `CryptographicException`，只是消息不含"版本前缀"），**不产生静默数据损坏** | 🟠 重要 | 🔴 严重（高估） |
| **AE-2** | **解密分派依赖可变配置** | `_authenticated` 参与分派决策 → 同一段密文在配置翻转后语义改变：v2/v3 密文在 `_authenticated=false` 时被当作裸 CBC 解析，可能**解密成功但产出乱码**（CBC+PKCS7 存在约 0.4% 概率通过 padding 校验） | 🔴 严重（静默数据损坏） | 🔴 严重 |
| **AE-3** | **长度校验后置** | 极短输入先按首字节进入 GCM/CBC+HMAC 分支，再因长度不足抛异常，异常消息为「长度不足」而非「格式无法识别」 | 🟠 重要 | 🟠 重要 |
| **AE-4** | **版本字节空间无规划** | `0x00`/`0x01`/`0x04+` 均未定义且无保留声明 | 🟡 一般 | 🟡 一般 |
| **AE-5** | **跨运行时可移植性未表达** | v2（GCM）密文在非 net8 运行时直接抛「不支持 AesGcm」；调用方无法在**加密侧**选择可跨运行时解密的格式 | 🟠 重要（v2.0 上调：`EncryptContent` 密文跨进程传输，属真实需求） | 🟡 一般 |
| **AE-6** | **异常契约不自洽**（v2.0 新增） | 空输入抛 `InvalidOperationException`、其余格式错误抛 `CryptographicException`、`Decrypt(string)` 的 Base64 失败抛 `FormatException` | 🟡 一般 | — |
| **AE-7** | **GCM 静默降级不可观测**（v2.0 新增） | net8+ 上 `AesGcm.IsSupported == false` 时静默走 CBC+HMAC，无日志 | 🟡 一般 | — |

### 1.4 已观测影响（实证）

- **E-4（已记录于配置方案 §14.4）**：`AesEncryptionProviderTests.AuthenticatedEncryption_RawCbcCipherText_IsRejected` 在全量运行中**偶发失败**（约 0.78%）。根因即 AE-1：测试用 `EnableAuthenticatedEncryption=false` 产出裸 CBC，其首字节为随机 IV[0]，若恰为 `0x02`/`0x03`，则不会抛出预期的「版本前缀」异常。
  - 当前处置：测试侧临时改为确定性构造（`rawCbc[0] = 0x00`），**未消除产品侧根因**。
- **AE-2 无任何实证但后果最严重**：一旦用户切换 `EnableAuthenticatedEncryption`，历史密文可能被**静默错误解密**（无异常但内容错误），属数据完整性风险。

### 1.5 代码中的既有说明（佐证）

`DefaultAesEncryptionProvider.cs:164-166` 已明确记录该不可靠性：

> `// 0x02/0x03 是合法版本字节；裸 CBC 首字节是 IV 的第 1 字节，无法与版本可靠区分，`
> `// 因此裸 CBC 依赖"解密失败再回退"不可行 —— 采用显式配置分派（见下）。`

即：**现状是"用配置分派"规避歧义，但配置本身可变且非密文固有属性**，故未真正消除歧义。

---

## 二、目标与非目标

### 2.1 目标

1. **零歧义**：本库产出的**任何**密文，其格式均可由**密文自身**唯一确定，判定不依赖运行时配置（AE-1 / AE-2）。
2. **自描述**：每种格式强制写入版本前缀；首字节取值空间有明确定义与保留规则（AE-1 / AE-4）。
3. **失败明确**：无法识别的格式一律抛出语义清晰、含实际首字节的异常，且长度校验前置（AE-3 / AE-6）。
4. **可演进**：版本字节空间预留，支持未来算法（AE-4 / AE-5）。
5. **可测、确定性**：所有格式判定用例可在无随机性前提下断言（消除 E-4 类 flaky）。
6. **无冗余**：不新增无消费点的 API、不新增兼容开关、不重复已有实现（见 §0.3）。

### 2.2 非目标

- ❌ 不更换加密库 / 不引入新第三方依赖（维持 `System.Security.Cryptography`）。
- ❌ 不实现密钥派生（KDF）或密钥轮换（独立议题）。
- ❌ 不改变 `IEncryptionProvider` 接口形状（仅内部格式与常量调整）。
- ❌ **不提供任何旧格式兼容通道**（项目未发布、无存量密文；不提供 `AllowLegacyUnprefixedCipherText`、不做双写兼容层）。
- ❌ 不为「非本库产出的随机字节」提供格式保证 —— 只保证**本库产出密文**零歧义，并对外部输入给出确定、明确的拒绝。

---

## 三、方案设计

### 3.1 备选方案评估

| 方案 | 思路 | 优点 | 缺点 | 结论 |
| :--- | :--- | :--- | :--- | :--- |
| **OPT-A** 多字节 magic（`0x4D 0x55 0x44`）+ 版本字节 | 3 字节 magic 定格式，1 字节定版本 | 误判概率 2^-24 | 与版本前缀**功能重复**；每份密文 +3 字节；本库产出密文本就零歧义 | ❌ **否决**（重复功能） |
| **OPT-B** 单字节版本空间重划分 + 分派与配置解耦 | 裸 CBC 也写 `0x01`；解密仅按版本字节分派 | 与现有结构一致；改动小 | 保留了一条无完整性保护的不安全路径；需额外迁移开关与 `[Obsolete]` 过渡 | ⚠️ 不作为主体（其**版本空间规划**被采纳） |
| **OPT-C** 彻底移除裸 CBC 路径 | 删除 `EnableAuthenticatedEncryption=false` 分支，仅保留 v2/v3 | 格式数 3→2，二者皆带前缀 → AE-1/AE-2 从根消失；同时消除 CFG-15 的安全隐患；**代码净减少**，无新增开关 | 移除一个公开配置属性（项目未发布，无外部义务） | ✅ **推荐（主体）** |
| **OPT-D** 长度/结构联合判别（无版本字节） | 用最小长度与块对齐推断格式 | 无额外字节 | 长度区间重叠，**不可靠**；无法表达新增算法 | ❌ 否决 |

### 3.2 最终方案：OPT-C（主体）+ OPT-B 版本空间规范 + B-3 + B-6

核心设计：

> **配置只决定「加密时产出哪种格式」，不再决定「解密时如何分派」。**
> 解密分派 **100% 由密文首字节（版本前缀）** 决定。
> 且：**不存在任何无版本前缀的合法格式**。

### 3.3 版本字节空间

| 值 | 含义 | 布局 | 状态 |
| :--- | :--- | :--- | :--- |
| `0x00` | **保留（非法哨兵）** | — | 永久保留，永不用作版本，便于识别"全零/未初始化"数据 |
| `0x01` | v1 裸 CBC（无完整性保护） | `[0x01][IV(16)][密文]` | ❌ **已废弃且永不复用**（OPT-C 移除该路径，编号冻结以免与历史调试数据混淆） |
| `0x02` | v2 AesGcm | `[0x02][nonce(12)][tag(16)][密文]` | ✅ 在用 |
| `0x03` | v3 CBC+HMAC-SHA256（Encrypt-then-MAC） | `[0x03][IV(16)][MAC(32)][密文]` | ✅ 在用 |
| `0x04`~`0x0F` | 保留（未来对称算法：ChaCha20-Poly1305、AES-GCM-SIV 等） | — | 需方案评审后分配 |
| `0x10`~`0xFF` | 保留（未来扩展：压缩、KDF 信封、多收件人等） | — | 未分配 |

> **实施约定**：`0x00` / `0x01` 仅以**文档 + 注释**形式声明保留，**不定义未使用的 `const`**（避免死常量）。

### 3.4 加密路径（实施后）

```
EncryptCore(plain):
    if (net8+ && AesGcm.IsSupported && !_requireCrossRuntimePortable)
                                     → EncryptGcm(plain)          // 0x02
    else                             → EncryptCbcThenHmac(plain)   // 0x03
```

- `_requireCrossRuntimePortable = true` 时，即使在 net8+ 也写 `0x03`，保证密文可在 netstandard2.0 / net6.0 解密（AE-5）。

### 3.5 解密路径（实施后）

```
DecryptCore(bytes):
    if (bytes.Length == 0)
        throw CryptographicException("密文数据格式无效：长度为 0，缺少版本前缀")

    switch (bytes[0])
    {
        case EnvelopeVersionGcm:            // 0x02
#if NET8_0_OR_GREATER
            if (AesGcm.IsSupported) { EnsureMinLength(len, 1+GcmNonceSize+GcmTagSize, "v2 AesGcm"); return DecryptGcm(bytes) }
            throw new CryptographicException("密文为 v2(AesGcm) 格式，但当前运行时不支持 AesGcm。…")
#else
            throw new CryptographicException("密文为 v2(AesGcm) 格式，但当前目标框架不支持 AesGcm。…")
#endif

        case EnvelopeVersionCbcHmac:        // 0x03
            EnsureMinLength(len, 1 + IvSizeBytes + HmacTagSize + CipherBlockSize, "v3 CBC+HMAC")
            return DecryptCbcThenHmac(bytes)

        default:                            // 含 0x00 / 0x01 / 0x04~0xFF
            throw new CryptographicException(
                $"密文格式无法识别：缺少 Mud.HttpUtils 信封版本前缀（期望 0x02 或 0x03），" +
                $"实际首字节为 0x{bytes[0]:X2}。")
    }
```

要点：
- **不再读取任何配置字段参与分派**（AE-2 根治）。
- 长度校验前置到各分支入口（AE-3）。
- 异常消息包含**实际首字节**，便于诊断（AE-3）。
- 空输入统一 `CryptographicException`（AE-6）。

### 3.6 子项清单（v2.0 定稿）

| 子项 | 内容 | 处置 |
| :--- | :--- | :--- |
| **B-1** | 版本字节空间规范 + `switch` 分派 + 长度前置 | ✅ 主体（§3.3~§3.5） |
| **B-3** | `RequireCrossRuntimePortable`（默认 `false`，加密侧强制 0x03） | ✅ 采纳，提前到主体（AE-5） |
| **B-6** | `FixedTimeEquals` 收敛到 `SecurityHelper`（消除两处重复实现；net5+ 复用 `CryptographicOperations.FixedTimeEquals`） | ✅ 采纳（§0.3） |
| **B-7** | `MudHttpClientLog` EventId 116 删除 + EventId 119（`AesGcm` 不可用时降级提示） | ✅ 采纳（AE-7 / 冲突 1） |
| ~~B-2~~ | ~~`EnableAuthenticatedEncryption` 标记 `[Obsolete]`~~ | ❌ 删除（OPT-C 直接删除，无需过渡） |
| ~~B-4~~ | ~~3 字节 magic 强校验~~ | ❌ 删除（与版本前缀功能重复） |
| ~~B-5~~ | ~~`TryReadEnvelopeVersion` 诊断工具~~ | ❌ 删除（无消费点，属预建冗余） |
| ~~§3.6~~ | ~~`AllowLegacyUnprefixedCipherText` 迁移开关~~ | ❌ 删除（无存量密文） |

### 3.7 AOT / 性能

- 纯 `switch` 分派，无反射、无 `RequiresUnreferencedCode`/`RequiresDynamicCode` 新增。
- 相较现状减少 1 个分支，性能持平或略优。
- 常量均为 `const`，编译期内联。
- `SecurityHelper.FixedTimeEquals` 为 `static` 且无泛型/反射，AOT 安全。

---

## 四、破坏性变更与迁移

| # | 变更 | 类型 | 影响 | 迁移路径 |
| :--- | :--- | :--- | :--- | :--- |
| **BC-AE1** | 移除裸 CBC 路径（`EncryptCbc` / `DecryptCbc`） | 行为 + 数据格式 | `EnableAuthenticatedEncryption=false` 产出的密文不再可能；该类密文无法解密 | 无存量密文，无需迁移 |
| **BC-AE2** | 移除 `AesEncryptionOptions.EnableAuthenticatedEncryption` 属性 | 公开 API | 引用该属性的代码编译失败 | 删除赋值即可（认证加密恒开启） |
| **BC-AE3** | 解密分派不再依赖任何配置 | 行为 | 密文语义由自身决定（修复） | 无需迁移 |
| **BC-AE4** | 新增 `AesEncryptionOptions.RequireCrossRuntimePortable` | 新增 API | 无（默认 `false`） | — |
| **BC-AE5** | 空输入异常由 `InvalidOperationException` 改为 `CryptographicException` | 行为 | 极边缘 | 捕获基类 `Exception` 或 `CryptographicException` 均可；`MemoryEncryptedTokenStore` 已统一包装为 `InvalidOperationException`，上层无感 |
| **BC-AE6** | `MudHttpClientLog.AuthenticatedEncryptionDisabled`（EventId 116）移除 | internal 日志 | 无（internal，不在 PublicAPI） | EventId 116 冻结不再复用；新增 EventId 119 记录 GCM 降级 |

> 项目未发布，无外部兼容义务；上述变更按"一步重构"执行，不做双写兼容层、不保留 `[Obsolete]` 过渡。

---

## 五、安全收益

1. **消除静默数据损坏（AE-2）**：解密不再受运行时配置摆布，杜绝"配置翻转导致密文被错误解析且可能成功"的风险。
2. **消除格式误判（AE-1）**：合法格式由 3 种降为 2 种且**全部带版本前缀**，对本库产出密文误判率为 **0**。
3. **移除不安全的加密路径**：无完整性保护的裸 CBC 不再可用（填充预言子风险归零），CFG-15 的安全警告随之作废。
4. **强化拒绝语义（AE-3 / AE-6）**：无法识别的格式必定抛 `CryptographicException` 并附带实际首字节，不进入"猜格式"路径。

---

## 六、测试方案

### 6.1 新增用例（`Tests/Mud.HttpUtils.Client.Tests/AesEncryptionProviderTests.cs`）

| 用例 | 断言 |
| :--- | :--- |
| `Envelope_VersionSpace_Gcm_HasPrefix02` | net8+ 且 `RequireCrossRuntimePortable=false` 产出首字节 `0x02`（循环 200 次，无随机性） |
| `Envelope_VersionSpace_CbcHmac_HasPrefix03` | `RequireCrossRuntimePortable=true` 或非 GCM 运行时 → 首字节 `0x03` |
| `Envelope_CrossRuntimePortable_ProducesCbcHmacOnNet8` | net8+ 且开关为 true → 首字节 `0x03`；且能被"按 0x03 解析"的路径解密（AE-5） |
| `Decrypt_DispatchIgnoresRuntimeConfiguration` | 0x03 密文与 0x02 密文在**任意配置**的 provider 上均能解密成功且结果一致（证明分派不依赖配置） |
| `Decrypt_UnrecognizedVersion_ThrowsWithActualByte` | 遍历 `0x00`、`0x01`、`0x04`~`0xFF`（253 个）作为首字节 → 均抛 `CryptographicException` 且消息含 `0x{字节:X2}` |
| `Decrypt_ZeroLength_ThrowsCryptographicException` | 空数组 → `CryptographicException`（AE-6） |
| `Decrypt_CbcHmacTooShort_ThrowsFormatError` | v3 长度 1 / 48 / 64 → 抛「密文数据格式无效」且含「最少 65 字节」（AE-3） |
| `Decrypt_GcmTooShort_ThrowsCryptographicException` | v2 长度 1 → 抛 `CryptographicException`（GCM 可用时为格式异常，否则为不可解密提示） |
| `RoundTrip_AllProducedFormats` | 0x02 / 0x03 两者加密→解密往返一致（含空明文、1 字节、1MB） |
| `RoundTrip_EmptyAndSingleByteBytes` | `EncryptBytes(Array.Empty<byte>())` / 1 字节 → 往返一致（边界） |
| `PublicApi_EnableAuthenticatedEncryption_Removed` | `typeof(AesEncryptionOptions).GetProperty("EnableAuthenticatedEncryption")` 为 `null`（防回潮） |
| `PublicApi_RequireCrossRuntimePortable_DefaultFalse` | 新选项默认值为 `false` |
| `Envelope_Tamper_AnyFormat_Throws` | 对 0x02 / 0x03 密文各翻转密文末位与 nonce/IV 位 → 均抛 `CryptographicException` |

### 6.2 消除既有 flaky（E-4 转正）

- 原 `AuthenticatedEncryption_RawCbcCipherText_IsRejected` 依赖 `EnableAuthenticatedEncryption=false` 产出裸 CBC —— 该配置已移除，**用例随之删除**，其语义被 `Decrypt_UnrecognizedVersion_ThrowsWithActualByte` 完全覆盖（且为确定性构造，无随机 IV 依赖）。
- 同步移除此前为规避 flaky 加入的 `rawCbc[0] = 0x00` 临时处置（该代码随用例一并删除）。

### 6.3 回归清单

- `AesEncryptionProviderTests` 全量（含 `BuildCbcHmacEnvelope` 辅助 —— 手工构造的 v3 信封已含 `0x03`，**无需改动**）。
- `MemoryEncryptedTokenStoreTests`（TokenStore 依赖 provider；加密/解密往返）。
- `DirectEnhancedHttpClientTests` / `HttpClientFactoryEnhancedClientTests` / `EnhancedHttpClientSubclassTests`（加密往返）。
- **`EncryptContent` / `DecryptContent` 端到端往返**（`IEncryptableHttpClient`；JSON 包装 → 解密 → 原文一致）。
- `ConfigurationBindingTests` / `ConfigP0FixTests` / `OptionsDefaultValueTests`（确认未引用已移除属性）。
- `DefaultHmacSignatureProviderTests`（B-6 抽取 `FixedTimeEquals` 后的回归）。
- 4 TFM（netstandard2.0 / net6.0 / net8.0 / net10.0）Debug + Release 构建。
- `PublicAPI.Unshipped.txt` 零 RS0016 / RS0017。
- `test.ps1` 6 个测试工程全绿。

---

## 七、实施阶段

> 图例：✅ 已完成 · ⏳ 进行中 · ⬜ 待开始

```
阶段 1（前置复核）                                                    ✅
└─ AE-T01 复核：确认 EncryptCbc/DecryptCbc 全部调用点、最小长度约束；
               确认全库无第二处解析密文布局（已复核：仅 DefaultAesEncryptionProvider）

阶段 2（主体：OPT-C）                                                 ✅
├─ AE-T02 版本字节空间常量集中为 private const 区块 + XML 注释（含版本空间表）
├─ AE-T03 删除 EncryptCbc / DecryptCbc 与 _authenticated 字段
├─ AE-T04 重写 DecryptCore 为 switch 分派（仅 0x02 / 0x03），移除配置参与
├─ AE-T05 长度校验前置 + 异常消息统一（含实际首字节；空输入改 CryptographicException）
└─ AE-T06 移除 AesEncryptionOptions.EnableAuthenticatedEncryption + PublicAPI ×4 同步

阶段 3（B-3 跨运行时 + 日志）                                          ✅
├─ AE-T07 新增 AesEncryptionOptions.RequireCrossRuntimePortable（默认 false）
├─ AE-T08 MudHttpClientLog：删除 EventId 116，新增 EventId 119（GCM 降级提示）
└─ AE-T09 PublicAPI ×4 同步（移除 2 行 / 新增 2 行）

阶段 4（B-6 去重）                                                    ✅
└─ AE-T10 SecurityHelper.FixedTimeEquals：收敛两处重复实现，
          两处调用点改为调用 SecurityHelper（net5+ 复用 CryptographicOperations）

阶段 5（测试）                                                        ✅
├─ AE-T11 §6.1 全部用例落地（12 条）
├─ AE-T12 E-4 用例转正/删除，移除 rawCbc[0] = 0x00 临时处置
└─ AE-T13 §6.3 回归清单全量执行

阶段 6（文档与出口）                                                   ✅
├─ AE-T14 Client/README.md + Abstractions/README.md：密文格式规范表、版本字节分配表
└─ AE-T15 4 TFM Debug + Release 构建 0 错误；test.ps1 6 工程全绿
```

### 出口准则

- 4 TFM（netstandard2.0 / net6.0 / net8.0 / net10.0）Debug + Release 构建 **0 错误**，无 RS0016/RS0017。
- `AesEncryptionProviderTests` 及相关回归用例**连续 3 次全量运行全绿**（验证 flaky 已消除）。
- 6 个测试工程 `test.ps1` 全绿。
- `PublicAPI.Unshipped.txt` 中 `EnableAuthenticatedEncryption` 条目已全部移除，`RequireCrossRuntimePortable` 已全部补录。

---

## 八、风险登记与回滚

| # | 风险 | 等级 | 影响 | 缓解 | 回滚 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| R-AE1 | 移除裸 CBC 后，开发环境遗留密文无法解密 | 🟢 低（项目未发布、无存量） | 开发环境历史数据 | 无需缓解；如确有调试密文，重新加密即可 | 仅恢复 `EncryptCbc`/`DecryptCbc` 两方法（单文件） |
| R-AE2 | 移除 `EnableAuthenticatedEncryption` 导致下游编译失败 | 🟢 低 | 仅本仓库 1 处测试引用 | 已同步删除；外部无使用者 | 恢复属性 + PublicAPI 条目 |
| R-AE3 | 分派与配置解耦后，个别调用方"靠切换配置兼容旧密文"的用法失效 | 🟢 低 | 该用法本身即 AE-2 的缺陷 | 异常消息明确说明期望的版本前缀 | 恢复配置参与分派（单方法） |
| R-AE4 | 最小长度常量写错导致误拒 | 🟢 低 | 合法短密文被拒 | AE-T11 含空明文 / 1 字节边界用例 | 修正常量 |
| R-AE5 | B-3 默认 `false`，net8+ 仍写 0x02，跨运行时问题未自动消失 | 🟢 低 | 需显式开启 | 默认 false 保持现状；README 说明 | 移除开关 |
| R-AE6 | B-6 抽取 `FixedTimeEquals` 时语义不一致引入安全回归 | 🟡 中 | 时序攻击面 | 保留原 XOR 累积实现不变，仅搬移；`DefaultHmacSignatureProviderTests` 与 AE-T11 加密完整性用例双重守护 | revert 单文件 |

**全局回滚**：阶段 2~4 仅涉及 `DefaultAesEncryptionProvider.cs`、`AesEncryptionOptions.cs`、`SecurityHelper.cs`、`DefaultHmacSignatureProvider.cs`、`MudHttpClientLog.cs` 五个文件（+ PublicAPI ×4），可按阶段独立 revert。

---

## 九、实施结果（2026-09-12 完成）

### 9.1 变更清单

| 文件 | 变更 |
| :--- | :--- |
| `Mud.HttpUtils.Abstractions/Encryption/AesEncryptionOptions.cs` | 移除 `EnableAuthenticatedEncryption`；新增 `RequireCrossRuntimePortable`（默认 `false`）；XML 补充信封格式与版本空间说明 |
| `Mud.HttpUtils.Abstractions/Encryption/SecurityHelper.cs` | 新增 `FixedTimeEquals`（B-6 全局唯一实现；net5+ 委托 `CryptographicOperations.FixedTimeEquals`） |
| `Mud.HttpUtils.Abstractions/PublicAPI/{ns2.0,net6.0,net8.0,net10.0}/PublicAPI.Unshipped.txt` | 移除 `EnableAuthenticatedEncryption` ×2 行；新增 `RequireCrossRuntimePortable` ×2 行（各 TFM） |
| `Mud.HttpUtils.Client/Encryption/DefaultAesEncryptionProvider.cs` | 移除 `EncryptCbc`/`DecryptCbc` 与 `_authenticated`；`DecryptCore` 改为纯 `switch(首字节)` 分派；长度校验前置（`EnsureMinLength`）；异常消息含实际首字节；空输入改 `CryptographicException`；`FixedTimeEquals` 改用 `SecurityHelper` |
| `Mud.HttpUtils.Client/Security/DefaultHmacSignatureProvider.cs` | 删除本地 `FixedTimeEquals` 重复实现，改用 `SecurityHelper`（B-6） |
| `Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs` | 删除 EventId 116（`AuthenticatedEncryptionDisabled`）；新增 EventId 119（`AesGcmUnavailableFallbackToCbcHmac`，解决 AE-7） |
| `Tests/Mud.HttpUtils.Client.Tests/AesEncryptionProviderTests.cs` | 删除 `AuthenticatedEncryption_RawCbcCipherText_IsRejected`（E-4 转正）；新增 13 组 / 21 个用例（§6.1） |
| `Mud.HttpUtils.Client/README.md`、`Mud.HttpUtils.Abstractions/README.md` | 补充密文信封格式规范表、版本字节分配表、`RequireCrossRuntimePortable` 使用指引 |
| `.docs/配置参数整改与功能完善方案-v2.md` | CFG-15 条目标注「已被 AES 信封方案（OPT-C）取代」 |

### 9.2 验收结果

| 出口准则 | 结果 |
| :--- | :--- |
| 4 TFM Debug + Release 构建 0 错误 | ✅ `EXITCODE=0`，`0 error` |
| `PublicAPI.Unshipped.txt` 零 RS0016 / RS0017 | ✅ 0 条 |
| `AesEncryptionProviderTests` | ✅ 47/47 |
| `Mud.HttpUtils.Client.Tests` 连续 3 次全量 | ✅ 1185 / 1185 / 1185（较重构前 1169 净增 16） |
| `test.ps1` 6 个测试工程全绿 | ✅ Client / Resilience / Generator / CodeFixes / Integration / OpenTelemetry |
| `EnableAuthenticatedEncryption` 无残留消费点 | ✅ 全仓仅剩 2 处注释性/防回潮断言引用 |

> **Flaky 消除说明**：原 E-4 用例依赖随机 IV 首字节（约 0.78% 失败率）。重构后合法格式仅 `0x02`/`0x03` 且全部带前缀，
> 新用例 `Decrypt_UnrecognizedVersion_ThrowsWithActualByte` 遍历 253 个非法首字节做**确定性**构造，无随机性依赖。

---

## 附录 A：目标格式规范（实施后）

| 版本 | 首字节 | 布局 | 最小长度（字节） | 完整性保护 | 产出条件 |
| :--- | :---: | :--- | ---: | :---: | :--- |
| ~~v1 裸 CBC~~ | ~~`0x01`~~ | — | — | — | ❌ **已移除**（编号冻结） |
| v2 AesGcm | `0x02` | `[0x02][nonce(12)][tag(16)][密文]` | 29 | ✅ AEAD | 认证加密 + net8+ 且 `AesGcm.IsSupported` 且 `RequireCrossRuntimePortable=false` |
| v3 CBC+HMAC | `0x03` | `[0x03][IV(16)][MAC(32)][密文]` | 65 | ✅ Encrypt-then-MAC | 其余运行时；或 `RequireCrossRuntimePortable=true` |

> 最小长度按「密文段 ≥ 16 字节」（PKCS7 恒填充至块整数倍）计算；v2 密文段可为 0（GCM 为流模式，无需填充）。

## 附录 B：版本字节分配表（实施后）

| 范围 | 用途 |
| :--- | :--- |
| `0x00` | 保留（非法哨兵，永不用作版本） |
| `0x01` | **已废弃并冻结**：v1 裸 CBC（路径已移除，永不复用） |
| `0x02` | v2 AesGcm |
| `0x03` | v3 CBC+HMAC-SHA256 |
| `0x04`–`0x0F` | 保留：未来对称算法（ChaCha20-Poly1305、AES-GCM-SIV 等） |
| `0x10`–`0xFF` | 保留：未来扩展（压缩、KDF 信封、多收件人等） |

## 附录 C：异常消息约定（v2.0 统一）

| 场景 | 类型 | 消息要点 |
| :--- | :--- | :--- |
| Base64 解码失败（`Decrypt(string)`） | `FormatException` | 由 `Convert.FromBase64String` 抛出，**保持现状**（非本库信封层职责） |
| 长度为 0 | `CryptographicException`（原 `InvalidOperationException`） | 「密文数据格式无效：长度为 0，缺少版本前缀」 |
| 首字节非 `{0x02,0x03}`（含 `0x00`/`0x01`） | `CryptographicException` | 「密文格式无法识别：缺少 Mud.HttpUtils 信封版本前缀（期望 0x02 或 0x03），实际首字节为 0x??」 |
| v2 密文但运行时/目标框架不支持 GCM | `CryptographicException` | 「密文为 v2(AesGcm) 格式，但当前运行时/目标框架不支持 AesGcm」+ 建议 `RequireCrossRuntimePortable` |
| 各格式长度不足 | `CryptographicException` | 「密文数据格式无效：{格式名} 密文长度不足（实际 ? 字节，最少 ? 字节）」 |
| MAC/tag 校验失败 | `CryptographicException` | 「密文完整性校验失败」（保持不变） |

> **契约原则**：**所有密文格式类问题 → `CryptographicException`**；`InvalidOperationException` 仅保留给"对象状态/配置"问题（如 `ObjectDisposedException`、`未配置加密提供器`）。
> `MemoryEncryptedTokenStore` 已把一切解密异常包装为 `InvalidOperationException`，对上层无感。

## 附录 D：与本轮配置整改的关系

- **CFG-15** 为 `EnableAuthenticatedEncryption=false` 增加了启动期安全警告（EventId 116）。本方案 **OPT-C 直接移除该不安全路径，使 CFG-15 的触发点消失**：
  - EventId 116 的 `MudHttpClientLog.AuthenticatedEncryptionDisabled` 定义已删除，**编号冻结不再复用**；
  - 配置方案-v2.md §13/§14 中 CFG-15 的条目应视为"已被 AES 信封重构取代"。
- **CFG-27** 已移除 `AesEncryptionOptions.IV` —— 本方案不重新引入 IV 相关成员。
- **E-4**（配置方案 §14.4）是本方案的直接触发点，已在 AE-T12 转正/消除。
