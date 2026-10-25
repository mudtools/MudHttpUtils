// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Validators;

/// <summary>
/// 基类验证器，用于验证 InheritedFrom 指定的基类是否存在且兼容
/// </summary>
internal static class BaseClassValidator
{
    /// <summary>
    /// 判断基类是否为代码生成器生成的类。
    /// </summary>
    /// <param name="baseClassName">InheritedFrom 声明值。</param>
    /// <param name="compilation">当前编译（用于反查短名是否解析到用户手写类型）。</param>
    /// <param name="currentNamespace">接口所在命名空间（短名回退查找范围）。</param>
    /// <remarks>
    /// <para>
    /// 生成类（自动检测抽象基 / 显式 InheritedFrom 指向生成实现）在生成器执行期**不可见**
    /// （生成代码在编译末期才加入），必须跳过校验，否则必然误报「基类不存在」。
    /// </para>
    /// <para>
    /// P2-2：原实现「无点号即视为生成类」把<b>所有短名宿主基类</b>一并跳过 ⇒ 手写基类的
    /// 存在性/sealed/静态/构造函数兼容检查完全失效（缺陷静默到生成期 CS1729）。
    /// 收紧为复合判定：短名<b>能解析到本编译内的类型</b>时按手写基类完整校验；
    /// 解析不到时才视为生成类。
    /// </para>
    /// </remarks>
    private static bool IsGeneratedClass(string baseClassName, Compilation compilation, INamespaceSymbol? currentNamespace)
    {
        // Wrap 类是由代码生成器生成的，执行时无法看到
        if (baseClassName.EndsWith("Wrap", StringComparison.Ordinal))
            return true;

        // Internal 命名空间中的类可能是代码生成器生成的实现类
        if (baseClassName.Contains($".{HttpClientGeneratorConstants.ImplementationNamespaceSuffix}."))
            return true;

        // 短名：仅当本编译内反查不到同名类型时才视为生成类。
        if (!baseClassName.Contains('.'))
            return ResolveType(compilation, baseClassName, currentNamespace) == null;

        return false;
    }

    /// <summary>
    /// 基类符号解析（与 <see cref="ValidateBaseClass"/> 同一查找链，供短名归属判定复用）。
    /// </summary>
    private static INamedTypeSymbol? ResolveType(Compilation compilation, string baseClassName, INamespaceSymbol? currentNamespace)
    {
        var symbol = compilation.GetTypeByMetadataName(baseClassName);
        if (symbol != null)
            return symbol;

        var lastDotIndex = baseClassName.LastIndexOf('.');
        var typeName = lastDotIndex > 0 ? baseClassName.Substring(lastDotIndex + 1) : baseClassName;

        if (currentNamespace != null)
        {
            symbol = FindTypeInNamespace(currentNamespace, currentNamespace.ToDisplayString(), typeName);
            if (symbol != null)
                return symbol;

            foreach (var childNamespace in currentNamespace.GetNamespaceMembers())
            {
                symbol = FindTypeInNamespace(childNamespace, childNamespace.ToDisplayString(), typeName);
                if (symbol != null)
                    return symbol;
            }
        }

        if (lastDotIndex > 0)
        {
            var namespaceName = baseClassName.Substring(0, lastDotIndex);
            symbol = FindTypeInNamespace(compilation.GlobalNamespace, namespaceName, typeName);
            if (symbol != null)
                return symbol;
        }

        // 短名（无命名空间）也要在当前命名空间内找一次，否则「手写短名基类」会被误判为生成类。
        symbol = FindTypeInNamespace(compilation.GlobalNamespace, currentNamespace?.ToDisplayString() ?? string.Empty, typeName);
        if (symbol != null)
            return symbol;

        return lastDotIndex > 0 ? FindTypeInCompilation(compilation, typeName, baseClassName) : null;
    }

    /// <summary>
    /// 在命名空间中递归查找类型
    /// </summary>
    private static INamedTypeSymbol? FindTypeInNamespace(INamespaceSymbol namespaceSymbol, string targetNamespace, string typeName)
    {
        // 检查当前命名空间是否匹配
        if (namespaceSymbol.ToDisplayString() == targetNamespace)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member.Name == typeName && member is INamedTypeSymbol typeSymbol)
                {
                    return typeSymbol;
                }
            }
        }

        // 递归检查子命名空间
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            var result = FindTypeInNamespace(childNamespace, targetNamespace, typeName);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// 验证基类是否存在且构造函数兼容
    /// </summary>
    public static ValidationResult ValidateBaseClass(Compilation compilation, string baseClassName, bool hasTokenManager, INamespaceSymbol? currentNamespace = null)
    {
        if (string.IsNullOrEmpty(baseClassName))
            return ValidationResult.Success();

        // 检查基类是否为代码生成器生成的类（Wrap 类 / Internal 命名空间中的实现类 / 反查不到的短名）
        // 如果是生成的类，则跳过验证，因为生成器执行时无法看到自己生成的代码
        if (IsGeneratedClass(baseClassName, compilation, currentNamespace))
            return ValidationResult.Success();

        var baseClassSymbol = ResolveType(compilation, baseClassName, currentNamespace);

        if (baseClassSymbol == null)
            return ValidationResult.Error($"基类 '{baseClassName}' 不存在");

        if (baseClassSymbol.TypeKind != TypeKind.Class)
            return ValidationResult.Error($"'{baseClassName}' 不是类");

        // 检查是否为 sealed 类（无法被继承）
        if (baseClassSymbol.IsSealed)
            return ValidationResult.Error($"'{baseClassName}' 是密封类（sealed），无法被继承");

        // 检查是否为 static 类（无法被继承）
        if (baseClassSymbol.IsStatic)
            return ValidationResult.Error($"'{baseClassName}' 是静态类（static），无法被继承");

        // 验证构造函数：与 ConstructorGenerator 实际发射的 base(...) 契约对齐。
        //   位置实参（顺序敏感）：
        //     · TokenManager 模式：ITokenManager, IAppContextHolder, ITokenProvider, IHttpRequestExecutor
        //     · 其余模式（默认 / HttpClient）：见 TryMatchPositionalBaseCall
        //   命名实参（名称敏感，缺失即 CS1739）：logger:, contentSerializer:
        // P2-2：原实现只查「参数类型里有没有 IHttpContentSerializer」，既漏掉 appContextHolder/executor
        // 位置契约，也与真实发射的命名实参无关 ⇒ 不兼容的宿主基类会被放行到生成期才炸。
        var requiredTypes = new List<string> { "IHttpContentSerializer", hasTokenManager ? "ITokenManager" : "IBaseHttpClient" };

        var hasCompatibleConstructor = baseClassSymbol.Constructors.Any(ctor => IsBaseCallCompatible(ctor, hasTokenManager));

        if (!hasCompatibleConstructor)
        {
            // 构建详细的错误信息，列出所有构造函数签名
            var constructorSignatures = new List<string>();
            foreach (var ctor in baseClassSymbol.Constructors)
            {
                var paramList = string.Join(", ", ctor.Parameters.Select(p => p.Type.ToDisplayString()));
                constructorSignatures.Add($"({paramList})");
            }

            return ValidationResult.Error(
                $"基类 '{baseClassName}' 缺少与生成代码 base(...) 兼容的构造函数。" +
                $"需要位置实参（令牌/客户端实例、上下文持有器、请求执行器）与命名形参 logger、contentSerializer；" +
                $"相关类型: {string.Join(", ", requiredTypes)}。可用构造函数: {string.Join("; ", constructorSignatures)}");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// 判定某个构造函数能否接受生成代码发射的 <c>base(...)</c> 实参。
    /// </summary>
    /// <remarks>
    /// 契约与 <c>ConstructorGenerator</c> 的 base 调用发射**同源**：
    /// 位置实参按基类运行模式分派，命名实参恒为 <c>logger:</c> / <c>contentSerializer:</c>
    /// （基类为生成类时另有 appManager/appAuthorizer/cacheProvider/resilienceResolver，
    /// 但那类基类在 <see cref="IsGeneratedClass"/> 已短路，不进入本检查）。
    /// 判定刻意宽松（类型名包含 + 命名形参存在）：宿主基类的形参名允许自定义前缀，
    /// 只要求能绑定命名实参 <c>logger</c> / <c>contentSerializer</c>。
    /// </remarks>
    private static bool IsBaseCallCompatible(IMethodSymbol ctor, bool hasTokenManager)
    {
        if (ctor.Parameters.Length == 0)
            return false;

        var paramNames = ctor.Parameters.Select(static p => p.Name).ToList();
        // 命名实参：base(..., logger: logger, contentSerializer: contentSerializer)
        if (!paramNames.Contains("logger") || !paramNames.Contains("contentSerializer"))
            return false;

        var typeNames = ctor.Parameters.Select(static p => p.Type.ToDisplayString()).ToList();

        static bool ContainsAny(IReadOnlyList<string> names, params string[] needles)
            => names.Any(name => needles.Any(needle => name.Contains(needle)));

        // IHttpRequestExecutor：所有模式的位置实参都包含
        if (!ContainsAny(typeNames, "IHttpRequestExecutor"))
            return false;

        if (hasTokenManager)
        {
            return ContainsAny(typeNames, "ITokenManager", "ITenantTokenManager", "IUserTokenManager")
                   && ContainsAny(typeNames, "IAppContextHolder");
        }

        // 非 TokenManager 模式（默认模式 / HttpClient 模式，均可能作为宿主基类形态）：
        //   默认      → (IMudAppContext, IAppContextHolder, IHttpRequestExecutor, ...)
        //   HttpClient→ (IEnhancedHttpClient|IBaseHttpClient, IHttpRequestExecutor, ...)
        var defaultModeShape = ContainsAny(typeNames, "IMudAppContext") && ContainsAny(typeNames, "IAppContextHolder");
        var httpClientModeShape = ContainsAny(typeNames, "IBaseHttpClient", "IEnhancedHttpClient");

        return defaultModeShape || httpClientModeShape;
    }

    /// <summary>
    /// 在编译中搜索类型（按名称匹配）。
    /// 这是最后的回退查找方式，会遍历所有语法树，开销较大。
    /// 仅在前面的符号查找和命名空间查找均失败时触发。
    /// </summary>
    private static INamedTypeSymbol? FindTypeInCompilation(Compilation compilation, string typeName, string fullTypeName)
    {
        GeneratorDebugLogger.Log(
            $"FindTypeInCompilation 回退路径触发: 正在所有语法树中搜索 '{typeName}' (完整名称: {fullTypeName})");

        // 遍历所有语法树并查找类型声明
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDecl in classDeclarations)
            {
                if (classDecl.Identifier.Text == typeName)
                {
                    // 获取完整的类型名称
                    var semanticModel = SemanticModelCache.GetOrCreate(compilation, syntaxTree);
                    var symbol = semanticModel.GetDeclaredSymbol(classDecl);
                    if (symbol != null)
                    {
                        // 检查完整类型名称是否匹配
                        var displayName = symbol.ToDisplayString();
                        if (displayName == fullTypeName || displayName.EndsWith("." + fullTypeName, StringComparison.Ordinal))
                        {
                            return symbol;
                        }
                    }
                }
            }
        }
        return null;
    }
}
