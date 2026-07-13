// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// AOT004 诊断分析器：检测 [HttpClientApi] 接口方法的请求/响应 DTO 是否被任何已引用的 JsonSerializerContext 覆盖。
/// </summary>
/// <remarks>
/// <para>
/// 此分析器在 HttpClient API 程序集上运行（而非实体程序集），通过扫描编译单元引用的所有程序集中
/// 的 <c>JsonSerializerContext</c> 子类上的 <c>[JsonSerializable]</c> 特性，判断 DTO 覆盖情况。
/// </para>
/// <para>
/// 若 DTO 未被任何 Context 覆盖，AOT 下序列化可能返回空对象或失败。
/// 建议将此类型纳入 <c>JsonSerializerContext</c>（手写或通过 <c>HttpJsonContextScaffolder</c> 生成）。
/// </para>
/// </remarks>
internal static class AotDtoCoverageAnalyzer
{
    private const string JsonSerializableAttributeFullName = "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string JsonSerializerContextFullName = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";
    private const string BodyAttributeFullName = "Mud.HttpUtils.Attributes.BodyAttribute";

    /// <summary>
    /// 分析编译单元中所有 [HttpClientApi] 接口方法的 DTO 覆盖情况，报告未覆盖的 AOT004 诊断。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="context">源生成上下文。</param>
    public static void Analyze(Compilation compilation, SourceProductionContext context)
    {
        // 1. 收集所有已引用的 JsonSerializerContext 子类上的 [JsonSerializable] 类型集合
        var coveredTypes = CollectCoveredTypes(compilation);
        if (coveredTypes.Count == 0)
            return; // 无 Context 引用，跳过（避免在未配置 AOT 的项目中产生噪音）

        // 2. 查找 HttpClientApiAttribute 符号
        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return;

        // 3. 遍历所有标注 [HttpClientApi] 的接口
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (context.CancellationToken.IsCancellationRequested)
                return;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl);
                if (interfaceSymbol == null)
                    continue;

                var hasHttpClientApi = interfaceSymbol.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr));
                if (!hasHttpClientApi)
                    continue;

                // 4. 检查每个方法的 DTO 覆盖情况
                foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (context.CancellationToken.IsCancellationRequested)
                        return;

                    CheckMethodDtoCoverage(compilation, context, interfaceSymbol, method, coveredTypes);
                }
            }
        }
    }

    /// <summary>
    /// 收集编译单元引用的所有 JsonSerializerContext 子类上的 [JsonSerializable] 类型。
    /// </summary>
    private static HashSet<INamedTypeSymbol> CollectCoveredTypes(Compilation compilation)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var jsonSerializableAttr = compilation.GetTypeByMetadataName(JsonSerializableAttributeFullName);
        var jsonSerializerContext = compilation.GetTypeByMetadataName(JsonSerializerContextFullName);

        if (jsonSerializableAttr == null || jsonSerializerContext == null)
            return result;

        // 扫描当前编译单元中的所有类型
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (typeSymbol == null)
                    continue;

                // 检查是否继承自 JsonSerializerContext
                if (!InheritsFromJsonSerializerContext(typeSymbol, jsonSerializerContext))
                    continue;

                // 收集 [JsonSerializable] 特性中的类型
                foreach (var attr in typeSymbol.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, jsonSerializableAttr))
                        continue;

                    if (attr.ConstructorArguments.Length > 0 &&
                        attr.ConstructorArguments[0].Value is INamedTypeSymbol coveredType)
                    {
                        result.Add(coveredType);
                    }
                }
            }
        }

        // 也扫描引用程序集中的 JsonSerializerContext 子类
        foreach (var reference in compilation.References)
        {
            if (reference is not CompilationReference compRef)
                continue;

            foreach (var typeSymbol in compRef.Compilation.GlobalNamespace.GetTypeMembers())
            {
                CollectCoveredTypesFromNamespace(typeSymbol, jsonSerializerContext, jsonSerializableAttr, result);
            }
        }

        return result;
    }

    /// <summary>
    /// 递归扫描命名空间中的 JsonSerializerContext 子类。
    /// </summary>
    private static void CollectCoveredTypesFromNamespace(
        INamedTypeSymbol namespaceSymbol,
        INamedTypeSymbol jsonSerializerContext,
        INamedTypeSymbol jsonSerializableAttr,
        HashSet<INamedTypeSymbol> result)
    {
        // 检查当前类型
        if (InheritsFromJsonSerializerContext(namespaceSymbol, jsonSerializerContext))
        {
            foreach (var attr in namespaceSymbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, jsonSerializableAttr))
                    continue;

                if (attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is INamedTypeSymbol coveredType)
                {
                    result.Add(coveredType);
                }
            }
        }

        // 递归子命名空间
        foreach (var child in namespaceSymbol.GetTypeMembers())
        {
            CollectCoveredTypesFromNamespace(child, jsonSerializerContext, jsonSerializableAttr, result);
        }
    }

    /// <summary>
    /// 检查类型是否继承自 JsonSerializerContext。
    /// </summary>
    private static bool InheritsFromJsonSerializerContext(INamedTypeSymbol type, INamedTypeSymbol contextBase)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, contextBase))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// 检查单个方法的 DTO 覆盖情况。
    /// </summary>
    private static void CheckMethodDtoCoverage(
        Compilation compilation,
        SourceProductionContext context,
        INamedTypeSymbol interfaceSymbol,
        IMethodSymbol method,
        HashSet<INamedTypeSymbol> coveredTypes)
    {
        var bodyAttr = compilation.GetTypeByMetadataName(BodyAttributeFullName);

        // 检查请求体 DTO
        if (bodyAttr != null)
        {
            foreach (var param in method.Parameters)
            {
                var hasBodyAttr = param.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr));
                if (!hasBodyAttr)
                    continue;

                var paramType = param.Type as INamedTypeSymbol;
                if (paramType != null && !IsCovered(paramType, coveredTypes) && !IsPrimitiveOrString(paramType))
                {
                    var location = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                        ?? method.Locations.FirstOrDefault();
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.AotDtoNotCoveredByContext,
                        location,
                        interfaceSymbol.Name,
                        method.Name,
                        paramType.ToDisplayString()));
                }
            }
        }

        // 检查响应 DTO
        var returnType = method.ReturnType as INamedTypeSymbol;
        if (returnType != null)
        {
            // 解包 Task<T>
            var innerType = ExtractTaskInnerType(returnType);
            if (innerType != null && !IsCovered(innerType, coveredTypes) && !IsPrimitiveOrString(innerType))
            {
                var location = method.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.AotDtoNotCoveredByContext,
                    location,
                    interfaceSymbol.Name,
                    method.Name,
                    innerType.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// 检查类型是否被 Context 覆盖（包括集合类型解包）。
    /// </summary>
    private static bool IsCovered(INamedTypeSymbol type, HashSet<INamedTypeSymbol> coveredTypes)
    {
        // 直接匹配
        if (coveredTypes.Contains(type))
            return true;

        // 解包 Nullable<T>
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type.TypeArguments.Length > 0 &&
            type.TypeArguments[0] is INamedTypeSymbol inner)
        {
            return coveredTypes.Contains(inner);
        }

        // 解包集合类型：List<T>, IEnumerable<T>, etc.
        if (type.IsGenericType && type.TypeArguments.Length == 1 &&
            type.TypeArguments[0] is INamedTypeSymbol elementType)
        {
            // 检查 List<UserDto> 是否覆盖 → 检查 UserDto 是否覆盖
            if (coveredTypes.Contains(elementType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 判断是否为基本类型或 string（不需要 Context 覆盖）。
    /// </summary>
    private static bool IsPrimitiveOrString(INamedTypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_String or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_Boolean or
            SpecialType.System_Double or
            SpecialType.System_Single or
            SpecialType.System_Decimal or
            SpecialType.System_DateTime or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or
            SpecialType.System_Char or
            SpecialType.System_Object => true,
            _ => false
        };
    }

    /// <summary>
    /// 解包 Task<T> 的内部类型。
    /// </summary>
    private static INamedTypeSymbol? ExtractTaskInnerType(INamedTypeSymbol returnType)
    {
        if (!returnType.IsGenericType)
            return null;

        var def = returnType.OriginalDefinition;
        if (def.ToDisplayString() == "System.Threading.Tasks.Task<T>" ||
            def.ToDisplayString() == "System.Threading.Tasks.ValueTask<T>")
        {
            if (returnType.TypeArguments.Length > 0 &&
                returnType.TypeArguments[0] is INamedTypeSymbol inner)
                return inner;
        }

        return null;
    }
}
