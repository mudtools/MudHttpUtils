// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
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
    /// 判断基类是否为代码生成器生成的类
    /// 如果是 Wrap 类或在 Internal 命名空间中的类（可能是生成的），则跳过验证
    /// </summary>
    private static bool IsGeneratedClass(string baseClassName)
    {
        // Wrap 类是由代码生成器生成的，执行时无法看到
        if (baseClassName.Contains("Wrap"))
            return true;

        // 如果基类名称不包含点（即没有命名空间），可能是生成的类（如 Internal 命名空间中的实现类）
        // 执行时无法看到自己生成的代码，跳过验证
        if (!baseClassName.Contains('.'))
            return true;

        // Internal 命名空间中的类可能是代码生成器生成的实现类
        // 如果带命名空间且包含 .Internal.，则可能是生成的
        if (baseClassName.Contains($".{HttpClientGeneratorConstants.ImplementationNamespaceSuffix}."))
            return true;

        return false;
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

        // 检查基类是否为代码生成器生成的类（Wrap 类或 Internal 命名空间中的类）
        // 如果是生成的类，则跳过验证，因为生成器执行时无法看到自己生成的代码
        if (IsGeneratedClass(baseClassName))
            return ValidationResult.Success();

        // 尝试通过元数据名称查找（适用于外部引用类型）
        var baseClassSymbol = compilation.GetTypeByMetadataName(baseClassName);

        // 如果找不到且传入当前命名空间，尝试在当前命名空间和子命名空间中查找
        if (baseClassSymbol == null && currentNamespace != null)
        {
            var lastDotIndex = baseClassName.LastIndexOf('.');
            var typeName = lastDotIndex > 0 ? baseClassName.Substring(lastDotIndex + 1) : baseClassName;

            // 在当前命名空间中查找
            baseClassSymbol = FindTypeInNamespace(currentNamespace, currentNamespace.ToDisplayString(), typeName);

            // 如果当前命名空间中找不到，尝试在子命名空间中查找（如 Internal）
            if (baseClassSymbol == null)
            {
                foreach (var childNamespace in currentNamespace.GetNamespaceMembers())
                {
                    baseClassSymbol = FindTypeInNamespace(childNamespace, childNamespace.ToDisplayString(), typeName);
                    if (baseClassSymbol != null)
                        break;
                }
            }
        }

        // 如果还是找不到，尝试通过命名空间查找（适用于同一项目中的类型）
        if (baseClassSymbol == null)
        {
            var lastDotIndex = baseClassName.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                var namespaceName = baseClassName.Substring(0, lastDotIndex);
                var typeName = baseClassName.Substring(lastDotIndex + 1);

                // 在全局命名空间中查找类型
                baseClassSymbol = FindTypeInNamespace(compilation.GlobalNamespace, namespaceName, typeName);
            }
        }

        // 如果还是找不到，尝试在所有类型中搜索（更宽泛的查找）
        if (baseClassSymbol == null)
        {
            var lastDotIndex = baseClassName.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                var typeName = baseClassName.Substring(lastDotIndex + 1);
                baseClassSymbol = FindTypeInCompilation(compilation, typeName, baseClassName);
            }
        }

        if (baseClassSymbol == null)
            return ValidationResult.Error($"基类 '{baseClassName}' 不存在");

        if (baseClassSymbol.TypeKind != TypeKind.Class)
            return ValidationResult.Error($"'{baseClassName}' 不是类");

        // 验证构造函数
        var requiredParams = new List<string> { "IEnhancedHttpClient", "IOptions<JsonSerializerOptions>" };
        if (hasTokenManager)
            requiredParams.Add("ITokenManager");

        var hasCompatibleConstructor = baseClassSymbol.Constructors.Any(ctor =>
        {
            var parameters = ctor.Parameters;
            if (parameters.Length != requiredParams.Count)
                return false;

            for (int i = 0; i < requiredParams.Count; i++)
            {
                var paramTypeString = parameters[i].Type.ToDisplayString();
                var requiredParam = requiredParams[i];

                // 检查参数类型是否匹配
                if (requiredParam == "IEnhancedHttpClient")
                {
                    if (!paramTypeString.Contains("IEnhancedHttpClient"))
                        return false;
                }
                else if (requiredParam.Contains("IOptions<JsonSerializerOptions>"))
                {
                    // 检查是否是IOptions<JsonSerializerOptions>或其完整类型
                    if (!paramTypeString.Contains("IOptions") && !paramTypeString.Contains("JsonSerializerOptions"))
                        return false;
                }
                else if (requiredParam == "ITokenManager")
                {
                    // 检查是否是ITokenManager、ITenantTokenManager或IUserTokenManager
                    var isTokenManagerCompatible = paramTypeString.Contains("ITokenManager") ||
                                                    paramTypeString.Contains("ITenantTokenManager") ||
                                                    paramTypeString.Contains("IUserTokenManager");
                    if (!isTokenManagerCompatible)
                        return false;
                }
                else
                {
                    // 其他参数必须完全包含所需类型名称
                    if (!paramTypeString.Contains(requiredParam))
                        return false;
                }
            }

            // 找到匹配的构造函数，返回true
            return true;
        });

        if (!hasCompatibleConstructor)
        {
            // 构建详细的错误信息，列出所有构造函数签名
            var constructorSignatures = new List<string>();
            foreach (var ctor in baseClassSymbol.Constructors)
            {
                var paramList = string.Join(", ", ctor.Parameters.Select(p => p.Type.ToDisplayString()));
                constructorSignatures.Add($"({paramList})");
            }

            return ValidationResult.Error($"基类 '{baseClassName}' 缺少兼容的构造函数。需要: {string.Join(", ", requiredParams)}。可用构造函数: {string.Join("; ", constructorSignatures)}");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// 在编译中搜索类型（按名称匹配）
    /// </summary>
    private static INamedTypeSymbol? FindTypeInCompilation(Compilation compilation, string typeName, string fullTypeName)
    {
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
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
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
