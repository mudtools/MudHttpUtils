# 重构方案：IAppContextSwitcher 接口拆分与生成器校验增强

> 基于《代码生成器审查报告》的 P0/P1 优先级问题，制定本重构方案
> 创建时间：2026-05-13
> 关联文档：`.docs/generator-review-ifsharpappmanager-appcontextswitcher-tokenprovider.md`

***

## 一、重构目标

| # | 目标                                                                     | 对应问题         | 优先级            |
| - | ---------------------------------------------------------------------- | ------------ | -------------- |
| 1 | 拆分 `IAppContextSwitcher` → `IAppContextHolder` + `IAppContextSwitcher` | 问题1：接口契约分裂   | **P0**         |
| 2 | 简化默认模式，注入 `IAppContextHolder` 替代 `IAppContextSwitcher`                 | 问题5：过度设计     | **P2**（与目标1联动） |
| 3 | 生成器增加 `TokenManage` 类型的编译时校验                                           | 问题2：鸭子类型缺乏校验 | **P1**         |

***

## 二、Phase 1 — P0：拆分 IAppContextSwitcher 接口

### 2.1 改动范围总览

```
Mud.HttpUtils.Abstractions/
  + AppContext/IAppContextHolder.cs           ← 新增文件
  ~ AppContext/IAppContextSwitcher.cs         ← 修改：继承 IAppContextHolder，移除 Current/BeginScope(IMudAppContext)

Mud.HttpUtils.Client/
  ~ AppContext/AsyncLocalAppContextSwitcher.cs ← 修改：仅实现 IAppContextHolder，移除4个 NotSupportedException 方法
  ~ ServiceCollectionExtensions.cs            ← 修改：新增 IAppContextHolder 的 DI 注册

Tests/Mud.HttpUtils.Client.Tests/
  ~ AsyncLocalAppContextSwitcherTests.cs      ← 修改：移除4个 NotSupportedException 测试用例
  + AppContextHolderTests.cs                  ← 新增：IAppContextHolder 接口测试
```

### 2.2 步骤 1：新增 `IAppContextHolder` 接口

**文件**：`Mud.HttpUtils.Abstractions/AppContext/IAppContextHolder.cs`（新建）

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 应用上下文持有器接口，用于持有和传播当前应用上下文。
/// <para>
/// 此接口仅包含纯状态操作，不依赖任何外部服务（如 IAppManager 或 ITokenProvider）。
/// 运行时实现 <see cref="AsyncLocalAppContextSwitcher"/> 完整实现此接口，无 NotSupportedException。
/// </para>
/// </summary>
public interface IAppContextHolder
{
    /// <summary>
    /// 获取或设置当前的应用上下文。
    /// </summary>
    IMudAppContext? Current { get; set; }

    /// <summary>
    /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。
    /// </summary>
    /// <param name="context">要切换到的应用上下文实例。</param>
    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。</returns>
    IDisposable BeginScope(IMudAppContext context);
}
```

### 2.3 步骤 2：修改 `IAppContextSwitcher` 接口

**文件**：`Mud.HttpUtils.Abstractions/AppContext/IAppContextSwitcher.cs`

**改动**：继承 `IAppContextHolder`，移除 `Current` 和 `BeginScope(IMudAppContext)` 的直接声明（改为从 `IAppContextHolder` 继承）

```diff
 namespace Mud.HttpUtils;

 /// <summary>
-/// 应用上下文切换器接口，用于访问、切换和管理应用上下文及其令牌。
+/// 应用上下文切换器接口，扩展 <see cref="IAppContextHolder"/>，提供应用切换和令牌获取能力。
+/// <para>
+/// 此接口的运行时实现需要 <see cref="IAppManager{TAppContext}"/> 和 <see cref="ITokenProvider"/> 依赖，
+/// 通常由源码生成器生成的类实现。纯状态操作（<see cref="IAppContextHolder.Current"/>、
+/// <see cref="IAppContextHolder.BeginScope(IMudAppContext)"/>）由 <see cref="IAppContextHolder"/> 提供。
+/// </para>
 /// </summary>
-public interface IAppContextSwitcher
+public interface IAppContextSwitcher : IAppContextHolder
 {
-    /// <summary>
-    /// 获取或设置当前的应用上下文。
-    /// </summary>
-    IMudAppContext? Current { get; set; }

     /// <summary>
     /// 切换到指定的应用上下文。
     /// </summary>
     /// <param name="appKey">应用的唯一标识符。</param>
     /// <returns>切换后的应用上下文实例。</returns>
     IMudAppContext UseApp(string appKey);

     /// <summary>
     /// 切换到默认的应用上下文。
     /// </summary>
     /// <returns>默认的应用上下文实例。</returns>
     IMudAppContext UseDefaultApp();

-    /// <summary>
-    /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。
-    /// </summary>
-    /// <param name="context">要切换到的应用上下文实例。</param>
-    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。</returns>
-    IDisposable BeginScope(IMudAppContext context);

     /// <summary>
     /// 创建一个应用上下文作用域，切换到指定应用标识对应的应用上下文，并在作用域结束时自动恢复之前的上下文。
     /// ...（保留原有注释和示例代码）
     /// </summary>
     IDisposable BeginScope(string appKey);

     /// <summary>
     /// 异步获取当前应用上下文的访问令牌。
     /// </summary>
     Task<string> GetTokenAsync();
 }
```

**向后兼容性分析**：

- ✅ `IAppContextSwitcher : IAppContextHolder`，现有通过 `IAppContextSwitcher` 调用 `Current` / `BeginScope(IMudAppContext)` 的代码**无需修改**
- ✅ 现有实现 `IAppContextSwitcher` 的生成类已经实现了所有6个方法，拆分后仍然满足接口契约
- ✅ `IAppManager<T>` 中 `where TContextSwitcher : IAppContextSwitcher` 约束不受影响
- ✅ Demo 中 `ITestBaseTokenApi : IAppContextSwitcher` 等声明无需修改

### 2.4 步骤 3：修改 `AsyncLocalAppContextSwitcher`

**文件**：`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs`

**改动**：

1. 实现接口从 `IAppContextSwitcher` 改为 `IAppContextHolder`
2. 移除4个 `NotSupportedException` 方法

```diff
-public class AsyncLocalAppContextSwitcher : IAppContextSwitcher
+public class AsyncLocalAppContextSwitcher : IAppContextHolder
 {
     private readonly AsyncLocal<IMudAppContext?> _context = new();

     /// <inheritdoc/>
     public IMudAppContext? Current
     {
         get => _context.Value;
         set => _context.Value = value;
     }

-    /// <inheritdoc/>
-    public IMudAppContext UseApp(string appKey)
-    {
-        throw new NotSupportedException(
-            "UseApp 需要通过生成的 API 实现类调用，该类包含 IAppManager 依赖。" +
-            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
-    }
-
-    /// <inheritdoc/>
-    public IMudAppContext UseDefaultApp()
-    {
-        throw new NotSupportedException(
-            "UseDefaultApp 需要通过生成的 API 实现类调用，该类包含 IAppManager 依赖。" +
-            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
-    }

     /// <inheritdoc/>
     public IDisposable BeginScope(IMudAppContext context)
     {
         // ... 保持不变
     }

-    /// <inheritdoc/>
-    public IDisposable BeginScope(string appKey)
-    {
-        throw new NotSupportedException(
-            "BeginScope(string) 需要通过生成的 API 实现类调用，该类包含 IAppManager 依赖。" +
-            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
-    }
-
-    /// <inheritdoc/>
-    public Task<string> GetTokenAsync()
-    {
-        throw new NotSupportedException(
-            "GetTokenAsync 需要通过生成的 API 实现类调用，该类包含 ITokenProvider 依赖。" +
-            "请使用生成的接口实现类而非直接使用 AsyncLocalAppContextSwitcher。");
-    }

     private sealed class AppContextScope : IDisposable
     {
         // ... 保持不变
     }
 }
```

### 2.5 步骤 4：修改 DI 注册

**文件**：`Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`

**改动**：在 `AddCurrentUserContext()` 方法中新增 `IAppContextHolder` 的 DI 注册

```diff
 public static IServiceCollection AddCurrentUserContext(
     this IServiceCollection services)
 {
     if (services == null)
         throw new ArgumentNullException(nameof(services));

     services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext<CurrentUserInfo>>();
+    services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
     services.TryAddSingleton<IAppContextSwitcher, AsyncLocalAppContextSwitcher>();
     return services;
 }
```

**说明**：

- `IAppContextHolder` 和 `IAppContextSwitcher` 都解析到同一个 `AsyncLocalAppContextSwitcher` 实例（DI 容器 Singleton 特性）
- `IAppContextSwitcher` 注册保留，确保现有代码不断裂
- 需要两种方式都注册，因为 DI 容器按服务类型分别解析

### 2.6 步骤 5：修改默认模式生成器（与 P2 联动）

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs`

**改动**：默认模式（无 TokenManager 无 HttpClient）下，字段类型和构造函数参数改为 `IAppContextHolder`

**GenerateClassFields 方法**（第115-121行）：

```diff
 else
 {
     codeBuilder.AppendLine("        /// <summary>");
     codeBuilder.AppendLine("        /// 应用上下文持有器，用于获取、设置和切换当前应用上下文。");
     codeBuilder.AppendLine("        /// </summary>");
-    codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IAppContextSwitcher _appContextSwitcher;");
+    codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IAppContextHolder _appContextHolder;");
 }
```

**GenerateConstructorDocumentation 方法**（第240-244行）：

```diff
 else
 {
     codeBuilder.AppendLine("        /// <param name=\"appContext\">应用上下文</param>");
-    codeBuilder.AppendLine("        /// <param name=\"appContextSwitcher\">应用上下文切换器</param>");
+    codeBuilder.AppendLine("        /// <param name=\"appContextHolder\">应用上下文持有器</param>");
 }
```

**GenerateConstructorSignature 方法**（第281-285行）：

```diff
 else
 {
     parameters.Add("IMudAppContext appContext");
-    parameters.Add("IAppContextSwitcher appContextSwitcher");
+    parameters.Add("IAppContextHolder appContextHolder");
 }
```

**GenerateConstructorBody 方法**（第363-367行）：

```diff
 else
 {
-    codeBuilder.AppendLine("            _appContextSwitcher = appContextSwitcher ?? throw new ArgumentNullException(nameof(appContextSwitcher));");
-    codeBuilder.AppendLine("            _appContextSwitcher.Current = appContext ?? throw new ArgumentNullException(nameof(appContext));");
+    codeBuilder.AppendLine("            _appContextHolder = appContextHolder ?? throw new ArgumentNullException(nameof(appContextHolder));");
+    codeBuilder.AppendLine("            _appContextHolder.Current = appContext ?? throw new ArgumentNullException(nameof(appContext));");
 }
```

**GenerateIAppContextSwitcherMembers 方法**（第412-476行）：

默认模式下不再需要为 `IAppContextSwitcher` 的 `UseApp`/`UseDefaultApp`/`BeginScope(string)`/`GetTokenAsync` 生成 `NotSupportedException` 桩。但需要确认：默认模式的生成类是否仍需要实现 `IAppContextSwitcher`？

**设计决策**：默认模式的生成类**不再实现** **`IAppContextSwitcher`**，改为仅实现 `IAppContextHolder`。这是合理的，因为：

1. 默认模式下 `UseApp`/`GetTokenAsync` 等方法无意义
2. 调用方不应期望在默认模式的接口上调用 `UseApp()`

```diff
 private void GenerateIAppContextSwitcherMembers(StringBuilder codeBuilder)
 {
     if (_context.HasHttpClient)
         return;

+    // 默认模式：仅实现 IAppContextHolder 的代理方法
+    if (!_context.HasTokenManager)
+    {
         codeBuilder.AppendLine("        /// <summary>");
         codeBuilder.AppendLine("        /// 获取或设置当前的应用上下文。");
         codeBuilder.AppendLine("        /// </summary>");
         codeBuilder.AppendLine("        public IMudAppContext? Current");
         codeBuilder.AppendLine("        {");
-        codeBuilder.AppendLine("            get => _appContextSwitcher.Current;");
-        codeBuilder.AppendLine("            set => _appContextSwitcher.Current = value;");
+        codeBuilder.AppendLine("            get => _appContextHolder.Current;");
+        codeBuilder.AppendLine("            set => _appContextHolder.Current = value;");
         codeBuilder.AppendLine("        }");
         codeBuilder.AppendLine();

         codeBuilder.AppendLine("        /// <summary>");
         codeBuilder.AppendLine("        /// 创建一个应用上下文作用域...");
         codeBuilder.AppendLine("        /// </summary>");
         codeBuilder.AppendLine("        public IDisposable BeginScope(IMudAppContext context)");
         codeBuilder.AppendLine("        {");
-        codeBuilder.AppendLine("            return _appContextSwitcher.BeginScope(context);");
+        codeBuilder.AppendLine("            return _appContextHolder.BeginScope(context);");
         codeBuilder.AppendLine("        }");
         codeBuilder.AppendLine();
-    // ← 注意这里原来的 if (_context.HasTokenManager) return; 在 TokenManager 模式下才 return
-    // 默认模式继续生成 NotSupportedException 桩，现在不需要了
-    if (_context.HasTokenManager)
-        return;
-
-    // 以下4个 NotSupportedException 桩方法全部移除
-    codeBuilder.AppendLine("        public IMudAppContext UseApp(string appKey)");
-    ...
-    codeBuilder.AppendLine("        public Task<string> GetTokenAsync()");
-    ...
+        return;  // 默认模式到此结束，不生成 UseApp 等方法
+    }

+    // TokenManager 模式：实现 IAppContextSwitcher 的代理方法
     codeBuilder.AppendLine("        /// <summary>");
     codeBuilder.AppendLine("        /// 获取或设置当前的应用上下文。");
     codeBuilder.AppendLine("        /// </summary>");
     // ... 后续 TokenManager 模式的代码保持不变
 }
```

**⚠️ 重要**：此改动影响默认模式生成类的方法集。默认模式的生成类不再生成 `UseApp`/`UseDefaultApp`/`BeginScope(string)`/`GetTokenAsync` 方法，仅生成 `Current` 和 `BeginScope(IMudAppContext)` 两个方法。

### 2.7 步骤 6：确认生成类接口列表无需修改

**经代码验证**，生成类的接口列表由 `ClassStructureGenerator.GenerateClassDeclaration`（行46-63）生成，其继承列表**仅包含用户接口名称**，不显式添加 `IAppContextSwitcher`：

```csharp
// ClassStructureGenerator.cs
string inheritance = string.Empty;
if (context.HasInheritedFrom)
    inheritance = $" : {context.Configuration.InheritedFrom}, {_interfaceSymbol.Name}";
else
    inheritance = $" : {_interfaceSymbol.Name}";
```

`IAppContextSwitcher` 是通过用户接口的继承链隐式实现的（如 `ITestBaseTokenApi : IAppContextSwitcher`）。因此：

- **TokenManager 模式**：用户接口继承 `IAppContextSwitcher` → 生成类隐式实现 `IAppContextSwitcher`，无需改动
- **默认模式**：用户接口不继承 `IAppContextSwitcher` → 生成类不实现 `IAppContextSwitcher`，生成的 `Current`/`BeginScope` 方法只是普通公开方法

**结论**：`ClassStructureGenerator` **无需修改**。

### 2.8 步骤 7：修改单元测试

**文件**：`Tests/Mud.HttpUtils.Client.Tests/AsyncLocalAppContextSwitcherTests.cs`

**改动**：

1. 移除4个 `NotSupportedException` 测试用例（`UseApp`、`UseDefaultApp`、`BeginScope(string)`、`GetTokenAsync`）
2. 新增测试验证 `AsyncLocalAppContextSwitcher` 实现 `IAppContextHolder`
3. 确保现有 `Current`、`BeginScope(IMudAppContext)` 测试不受影响

```diff
+ [Fact]
+ public void Implements_IAppContextHolder()
+ {
+     var switcher = new AsyncLocalAppContextSwitcher();
+     switcher.Should().BeAssignableTo<IAppContextHolder>();
+ }

- [Fact]
- public void UseApp_Throws_NotSupportedException()
- {
-     var act = () => _switcher.UseApp("app1");
-     act.Should().Throw<NotSupportedException>();
- }
- // ... 移除其他3个 NotSupportedException 测试
```

### 2.9 步骤 8：修改生成器测试

搜索并修改 `Tests/Mud.HttpUtils.Generator.Tests/` 中所有验证生成代码的测试——默认模式下生成类应实现 `IAppContextHolder` 而非 `IAppContextSwitcher`，且不应包含 `NotSupportedException` 桩方法。

### 2.10 Phase 1 编译验证清单

- [x] `Mud.HttpUtils.Abstractions` 编译通过
- [x] `Mud.HttpUtils.Client` 编译通过
- [x] `Mud.HttpUtils.Generator` 编译通过
- [x] Demo 项目编译通过（`IFeishuAppManager` 仍能正确解析）
- [x] 所有单元测试通过
- [x] 所有生成器测试通过
- [x] 验证生成代码中：默认模式 → `IAppContextHolder`，TokenManager 模式 → `IAppContextSwitcher`
- [x] 验证现有 DI 注册不受影响（`IAppContextSwitcher` 仍可解析到 `AsyncLocalAppContextSwitcher`）

***

## 三、Phase 2 — P1：生成器增加 TokenManage 编译时校验

### 3.1 新增 DiagnosticDescriptor

**文件**：`Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs`

在 `#region HttpClient API生成器诊断信息` 区域末尾追加：

```csharp
public static readonly DiagnosticDescriptor TokenManagerTypeNotFound = new(
    id: "HTTPCLIENT015",
    title: "TokenManage 类型未找到",
    messageFormat: "接口 {0} 的 TokenManage 属性指定的类型 '{1}' 在当前编译中未找到。请确认类型名称正确，或确保包含该类型的项目已正确引用。",
    category: "代码生成",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor TokenManagerMissingMethod = new(
    id: "HTTPCLIENT016",
    title: "TokenManage 类型缺少必需方法",
    messageFormat: "接口 {0} 的 TokenManage 属性指定的类型 '{1}' 缺少必需的方法 '{2}'。TokenManage 类型必须提供 'IMudAppContext GetDefaultApp()' 和 'IMudAppContext GetApp(string appKey)' 方法，或实现 IAppManager<TAppContext> 接口。",
    category: "代码生成",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

### 3.2 实现校验逻辑

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs`

在 `ValidateConfiguration` 方法（行110-158）中追加 TokenManage 类型校验：

```csharp
/// <summary>
/// 校验 TokenManage 指定的类型是否满足鸭子类型契约
/// </summary>
private bool ValidateTokenManagerType(GenerationConfiguration configuration, Location location)
{
    if (string.IsNullOrEmpty(configuration.TokenManager))
        return true;

    var tokenManagerTypeName = configuration.TokenManager;
    var tokenManagerType = _compilation.GetTypeByMetadataName(tokenManagerTypeName);

    // 1. 类型是否存在
    if (tokenManagerType == null)
    {
        // 尝试通过名称搜索（处理短名称情况）
        tokenManagerType = _compilation.GetTypesByMetadataName(tokenManagerTypeName)
            .FirstOrDefault();

        if (tokenManagerType == null)
        {
            ReportDiagnostic(Diagnostics.TokenManagerTypeNotFound,
                location, _interfaceSymbol.Name, tokenManagerTypeName);
            return false;
        }
    }

    // 2. 检查 GetDefaultApp() 方法
    var getDefaultAppMethod = tokenManagerType.GetMembers("GetDefaultApp")
        .OfType<IMethodSymbol>()
        .FirstOrDefault(m => m.Parameters.IsEmpty && m.TypeParameters.IsEmpty);

    if (getDefaultAppMethod == null)
    {
        ReportDiagnostic(Diagnostics.TokenManagerMissingMethod,
            location, _interfaceSymbol.Name, tokenManagerTypeName, "GetDefaultApp()");
        return false;
    }

    // 3. 检查 GetDefaultApp() 返回类型兼容性
    var mudAppContextType = _compilation.GetTypeByMetadataName("Mud.HttpUtils.IMudAppContext");
    if (mudAppContextType != null && getDefaultAppMethod.ReturnType != null)
    {
        var returnType = getDefaultAppMethod.ReturnType;
        if (!SymbolEqualityComparer.Default.Equals(returnType, mudAppContextType) &&
            returnType.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, mudAppContextType)))
        {
            // 返回类型不是 IMudAppContext 或其子类型 → 发出警告而非错误（因为可能有自定义转换）
            ReportDiagnostic(Diagnostics.TokenManagerMissingMethod,
                location, _interfaceSymbol.Name, tokenManagerTypeName,
                $"GetDefaultApp() 返回类型 '{returnType.ToDisplayString()}' 不是 IMudAppContext 或其子类型");
            return false;
        }
    }

    // 4. 检查 GetApp(string) 方法
    var getAppMethod = tokenManagerType.GetMembers("GetApp")
        .OfType<IMethodSymbol>()
        .FirstOrDefault(m => m.Parameters.Length == 1 &&
            m.Parameters[0].Type.SpecialType == SpecialType.System_String);

    if (getAppMethod == null)
    {
        ReportDiagnostic(Diagnostics.TokenManagerMissingMethod,
            location, _interfaceSymbol.Name, tokenManagerTypeName, "GetApp(string appKey)");
        return false;
    }

    return true;
}
```

**调用位置**：在 `ValidateConfiguration` 方法的 `HttpClient` 互斥校验之后（约行135）调用：

```csharp
// 现有校验后追加
if (!string.IsNullOrEmpty(configuration.TokenManager))
{
    if (!ValidateTokenManagerType(configuration, location))
        return; // 校验失败，跳过后续生成
}
```

### 3.3 Phase 2 测试计划

新增测试用例：

| 测试场景                              | 输入                                 | 期望结果             |
| --------------------------------- | ---------------------------------- | ---------------- |
| TokenManage 类型不存在                 | `TokenManage = "INonExistentType"` | HTTPCLIENT015 错误 |
| TokenManage 类型无 `GetDefaultApp()` | 自定义接口无此方法                          | HTTPCLIENT016 错误 |
| TokenManage 类型无 `GetApp(string)`  | 自定义接口无此方法                          | HTTPCLIENT016 错误 |
| TokenManage 类型返回类型不兼容             | `GetDefaultApp()` 返回 `string`      | HTTPCLIENT016 错误 |
| TokenManage 类型实现 `IAppManager<T>` | 正确类型                               | 无诊断错误            |
| TokenManage 类型鸭子类型兼容              | 如 `IFeishuAppManager`              | 无诊断错误            |

### 3.4 Phase 2 编译验证清单

- [x] `Mud.HttpUtils.Generator` 编译通过
- [x] 新增 DiagnosticDescriptor ID 连续且不与现有冲突
- [x] 校验逻辑对现有 Demo 代码不产生新的诊断错误
- [x] 校验逻辑对错误的 `TokenManage` 值产生清晰的编译时错误
- [x] 生成器测试通过

> **实现备注**：`HTTPCLIENT015`（TokenManage 类型未找到）在实际实现中改为 `DiagnosticSeverity.Warning` 而非 `Error`，因为短名称（如 `IFeishuAppManager`）在编译上下文中可能无法解析但生成的代码仍然有效。当类型无法解析时，校验逻辑发出警告但不阻止代码生成。

***

## 四、Phase 3 — 可选后续优化（维持现状）

以下问题经评估后决定维持现状，仅在此记录以备未来参考：

### 4.1 `_appManager` 字段类型（维持用户类型）

**原因**：

- C# 泛型不变性使 `IAppManager<Derived>` → `IAppManager<Base>` 转换不可行
- DI 容器需精确类型匹配
- Demo 中 `IFeishuAppManager` 未继承 `IAppManager<T>`，鸭子类型提供了更灵活的约束

### 4.2 生成类代理方法重复（维持自包含策略）

**原因**：

- 源码生成器的自包含策略是业界常见实践
- 每个生成类独立、无外部基类依赖，便于调试
- `InheritedFrom` 继承机制已避免子类重复生成

### 4.3 `GetTokenAsync()` 语义（维持现状）

**原因**：

- `GetTokenAsync()` 在语义上属于"当前上下文的令牌获取"，与 `IAppContextSwitcher` 职责相关
- `public virtual` / `public override` 的访问性设计合理
- 拆分到 `ITokenAccessor` 接口增加复杂度但无实质收益

***

## 五、执行顺序与依赖关系

```
Phase 1 (P0)                          Phase 2 (P1)
┌──────────────────────────┐          ┌──────────────────────────┐
│ Step 1: 新增 IAppContextHolder     │          │ Step 1: 新增 DiagnosticDescriptor  │
│ Step 2: 修改 IAppContextSwitcher   │          │ Step 2: 实现校验逻辑              │
│ Step 3: 修改 AsyncLocalAppCtx...   │          │ Step 3: 测试                     │
│ Step 4: 修改 DI 注册              │          └──────────────────────────┘
│ Step 5: 修改默认模式生成器        │                    │
│ Step 6: 修改生成类接口列表        │                    │
│ Step 7: 修改单元测试              │                    │
│ Step 8: 修改生成器测试            │                    │
└──────────────────────────┘                    │
         │                                      │
         │  可并行                               │  可并行
         │                                      │
         └──────────────────┬───────────────────┘
                            │
                     编译验证 & 集成测试
```

**Phase 1 和 Phase 2 可以并行执行**，但建议按以下顺序完成：

1. **先完成 Phase 1**，因为 Phase 2 的校验逻辑需要考虑拆分后的接口关系
2. Phase 1 完成后运行全量测试
3. 再执行 Phase 2，在 Phase 1 的基础上添加校验逻辑

***

## 六、风险与回退策略

### 6.1 风险评估

| 风险                                                                                                                                                 | 概率 | 影响 | 缓解措施                                                             |
| -------------------------------------------------------------------------------------------------------------------------------------------------- | -- | -- | ---------------------------------------------------------------- |
| `IAppContextSwitcher` 拆分后，外部消费者通过 `IAppContextSwitcher` 调用 `Current`/`BeginScope` 仍可编译，但通过具体类型 `AsyncLocalAppContextSwitcher` 调用 `UseApp` 等方法将编译失败 | 低  | 中  | `AsyncLocalAppContextSwitcher` 是 internal 类，外部不应直接引用；如有需要可提供扩展方法 |
| 默认模式生成类不再实现 `IAppContextSwitcher`，已有用户代码可能通过 `IAppContextSwitcher` 引用默认模式的生成类                                                                      | 低  | 高  | **需调研**：搜索项目中是否有默认模式接口继承 `IAppContextSwitcher` 的情况               |
| DI 注册变更后，现有 `AddCurrentUserContext()` 调用者不受影响，但新增了 `IAppContextHolder` 注册                                                                          | 低  | 低  | 使用 `TryAddSingleton`，不覆盖已有注册                                     |
| `TokenManage` 校验可能对现有代码产生新的编译错误                                                                                                                    | 低  | 中  | 校验仅在 `TokenManage` 类型找不到或缺少方法时报错，现有合法代码不受影响                      |

### 6.2 回退策略

- Phase 1 和 Phase 2 各自独立，可单独回退
- 回退方式：通过 Git revert 恢复到重构前的提交
- 关键保护措施：重构前确保所有现有测试通过，作为回归基线

***

## 七、验收标准

### Phase 1 验收

1. `AsyncLocalAppContextSwitcher` 不再包含任何 `NotSupportedException`
2. `IAppContextSwitcher : IAppContextHolder`，现有代码编译无错误
3. 默认模式生成类不再生成 `UseApp`/`UseDefaultApp`/`BeginScope(string)`/`GetTokenAsync` 的 `NotSupportedException` 桩方法，字段类型改为 `IAppContextHolder`
4. TokenManager 模式生成类实现 `IAppContextSwitcher`（行为不变）
5. 所有单元测试和生成器测试通过
6. Demo 项目编译运行正常

### Phase 2 验收

1. 指定不存在的 `TokenManage` 类型时，产生 `HTTPCLIENT015` 错误
2. 指定缺少 `GetDefaultApp()` 方法的类型时，产生 `HTTPCLIENT016` 错误
3. 指定缺少 `GetApp(string)` 方法的类型时，产生 `HTTPCLIENT016` 错误
4. 指定合法的鸭子类型（如 `IFeishuAppManager`）时，无诊断错误
5. 所有现有测试通过

