<!-- 请保持模板结构；不适用的检查项请显式写「不适用」并说明原因。 -->

## 变更说明

<!-- 做了什么、为什么这样做；若修复缺陷，请写出触发场景与根因。 -->

## 验证方式

<!-- 跑了哪些构建/测试/发布；无法本地验证的部分请写明由哪个 CI 作业兜底。 -->

- [ ] `dotnet build Mud.HttpUtils.slnx -c Release` 无新增警告/错误
- [ ] 相关测试工程通过（`Tests/Mud.HttpUtils.Generator.Tests`、`Tests/Mud.HttpUtils.Client.Tests` 等）
- [ ] 涉及 AOT 的改动已在本地跑过 `dotnet publish ... -p:AotStrictMode=true`（或说明由 CI 的 aot-publish / aot-packageref 作业兜底）

## 检查项

- [ ] **AOT 判定语义同步**：若改动 `Mud.HttpUtils.Generator` 的 `AotModeResolver` 三态判定语义，必须同步 `Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets` 的 `MudEnableJsonContextScaffolder` 判定，以及 `.github/workflows/ci.yml` 中 aot-publish / aot-packageref 两个作业的探针用例（「Verify scaffolder activation aligns with AotModeResolver」）
- [ ] **诊断契约**：改动诊断 ID / 级别 / 消息格式 / 定位位置时，已同步 `AnalyzerReleases.Unshipped.md`、README 与相关测试
- [ ] **AOT 信号源穷举**：若为 XML/JSON 判定新增了信号来源，已同步 `AotXmlRejectionAnalyzer.MayUseXml`（AOT007 预门控）与 `AotDtoCoverageAnalyzer`（AOT004/AOT005 豁免口径），否则会静默漏报
- [ ] **文档同步**：CHANGELOG 与相关 README 已更新（行为变更 / API 变更 / 新增开关）

## 下游影响（必填；无影响也必须显式写「无」）

<!-- 上游变更 → 下游适配代价的显式清单。四类各自回答，禁止留空；草稿可用 scripts/publicapi-impact.ps1 生成。 -->

- [ ] **公共 API 增减**：<!-- 新增/移除/签名的 PublicAPI 条目（以 PublicAPI.Unshipped.txt 为准）；无写「无」 -->
- [ ] **行为变更**：<!-- 默认行为/诊断行为/序列化行为的可观测变化（如下游实证过的缺陷修复）；无写「无」 -->
- [ ] **新增或升级默认开启的诊断**：<!-- 诊断 ID、级别、默认是否开启、下游可能需要的豁免动作；无写「无」 -->
- [ ] **传递依赖变动**：<!-- 私有资产/实现包变化及断裂类型；提醒下游遵守「直接引用原则」；无写「无」 -->
