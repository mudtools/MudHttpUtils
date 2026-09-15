; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; 维护约定：
; 1. 新增诊断（Diagnostics.cs 中的 DiagnosticDescriptor）必须在此登记，否则构建报 RS2008；
; 2. 修改既有诊断的 Category / Severity 必须在对应行同步，否则构建报 RS2001；
; 3. AOT007 有两个描述符（AOT007 Error 与 AOT007 Warning(F10 降级变体)），
;    发布跟踪按 ID 记录，故仅一行，级别取主描述符（Error）；
; 4. 本包尚未正式发布，全部规则位于 "New Rules"；首次发布时整段迁入 Shipped 文件。
;
; 与 README 诊断表的一致性由 Tests/Mud.HttpUtils.Generator.Tests/DocumentationContractTests.cs 守卫。

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AOT001 | AOT | Warning | JSON Context 类名冲突
AOT002 | AOT | Warning | 开放泛型在低版本 TFM 上标注 [HttpJsonSerializable]
AOT003 | AOT | Warning | 多态类型缺少 [JsonDerivedType]
AOT004 | AOT | Warning | DTO 未被任何 JsonSerializerContext 覆盖
AOT005 | AOT | Warning | 查询参数类型使用 JSON 序列化但未被 Context 覆盖
AOT006 | AOT | Warning | [HttpJsonSerializable] 类型未被 Context 覆盖
AOT007 | AOT | Error | XML 序列化在 AOT 下不支持（F10 降级变体为 Warning，共用本 ID）
EHSG001 | 代码生成 | Error | 事件处理器生成器错误
FORM001 | 代码生成 | Error | FormContent 代码生成错误
FORM002 | 代码生成 | Error | FormContent 缺少 [FilePath]
FORM003 | 代码生成 | Error | FormContent 存在多个 [FilePath]
HTTPCLIENT001 | 代码生成 | Error | HttpClient API 生成错误
HTTPCLIENT003 | 代码生成 | Error | HttpClient API 语法分析失败
HTTPCLIENT004 | 代码生成 | Error | HttpClient API 参数配置错误
HTTPCLIENT005 | 代码生成 | Error | URL 模板无效
HTTPCLIENT007 | 代码生成 | Error | HttpClient 与 TokenManage 互斥
HTTPCLIENT008 | 代码生成 | Error | HttpClient 类型不支持加密
HTTPCLIENT009 | 代码生成 | Warning | HttpClient 类型不支持 XML 请求
HTTPCLIENT011 | 代码生成 | Warning | Cache 与 Response<T> 组合
HTTPCLIENT012 | 代码生成 | Info | 泛型接口代码生成提示
HTTPCLIENT013 | 代码生成 | Error | 路径参数不匹配
HTTPCLIENT014 | 代码生成 | Warning | HttpClient 类型未找到
HTTPCLIENT015 | 代码生成 | Error | TokenManage 类型未找到
HTTPCLIENT016 | 代码生成 | Error | TokenManage 类型缺少必需方法
HTTPCLIENT017 | 代码生成 | Warning | HttpClient 类型无法解析，兼容性校验被跳过
HTTPCLIENT018 | 代码生成 | Warning | TokenManagerKey 使用默认推断值
HTTPCLIENT020 | 代码生成 | Warning | 非幂等方法的 [Retry] 默认不生效
HTTPCLIENT021 | 代码生成 | Warning | 方法级 [Timeout] 超过 HttpClient 超时
HTTPCLIENT022 | 代码生成 | Warning | Path/HmacSignature 注入模式不支持令牌恢复
HTTPCLIENT023 | 代码生成 | Info | 增量缓存被 ForceHttpGenerator 强制失效
HTTPCLIENT024 | 代码生成 | Error | 接口成员未生成实现（已发射占位实现）
HTTPCLIENT025 | 代码生成 | Warning | 直达返回类型不参与 Cache/Resilience 编排
HTTPCLIENT026 | 代码生成 | Error | [CircuitBreaker] 特性参数取值超出有效域
HTTPCLIENT027 | 代码生成 | Error | [Timeout] 特性参数必须为正毫秒数
HTTPCLIENT028 | 代码生成 | Warning | 继承模式下应用切换成员被隐藏
HTTPCLIENTREG001 | 代码生成 | Error | HttpClient API 注册生成错误
HTTPCLIENTREG002 | 代码生成 | Error | RegistryGroupName 不是有效 C# 标识符
MUD001 | Mud.HttpUtils.Interface | Error | HttpClientApi 方法缺少 HTTP 方法特性
MUD002 | Mud.HttpUtils.Interface | Error | HttpClientApi 方法返回类型无效
MUD004 | Mud.HttpUtils.DependencyInjection | Warning | ITokenManager 实现应注册为 Singleton
MUD005 | Mud.HttpUtils.Security | Info | Query 令牌注入模式存在泄露面
