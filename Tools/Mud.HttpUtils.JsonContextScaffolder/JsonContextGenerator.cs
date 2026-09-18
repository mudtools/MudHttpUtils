// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.JsonContextScaffolder;

/// <summary>
/// JSON Context 生成结果。
/// </summary>
/// <param name="FileName">输出文件名（不含路径）。</param>
/// <param name="SourceCode">生成的 C# 源代码。</param>
/// <param name="ContextClassName">Context 类名。</param>
/// <param name="TypeCount">包含的类型数量。</param>
public record JsonContextFile(string FileName, string SourceCode, string ContextClassName, int TypeCount);

/// <summary>
/// 诊断严重级别。
/// </summary>
public enum ScaffolderDiagnosticSeverity
{
    /// <summary>信息。</summary>
    Info,
    /// <summary>警告。</summary>
    Warning,
    /// <summary>错误。</summary>
    Error
}

/// <summary>
/// Scaffolder 诊断信息（对应 AOT001-AOT005 诊断约定）。
/// </summary>
/// <param name="Id">诊断 ID（如 AOT001）。</param>
/// <param name="Severity">严重级别。</param>
/// <param name="Message">诊断消息。</param>
/// <param name="Location">相关位置（类型全名，可选）。</param>
public record ScaffolderDiagnostic(string Id, ScaffolderDiagnosticSeverity Severity, string Message, string? Location = null);

/// <summary>
/// 类型分组信息。
/// </summary>
internal record TypeGroup
{
    public required string SerializerClassName { get; init; }
    public required JsonNamingPolicyHint NamingPolicy { get; init; }
    public required string TargetNamespace { get; init; }

    /// <summary>
    /// 本组需注册为 <c>[JsonSerializable]</c> 根的类型。
    /// </summary>
    /// <remarks>
    /// [T1 修复] 元素类型放宽为 <see cref="ITypeSymbol"/>：数组型根（如 <c>UserDto[]</c>）是
    /// <see cref="IArrayTypeSymbol"/>，**不继承** <see cref="INamedTypeSymbol"/>，
    /// 原 <c>List&lt;INamedTypeSymbol&gt;</c> + 强制转型会在数组根上抛 <c>InvalidCastException</c>。
    /// </remarks>
    public required List<ITypeSymbol> Types { get; init; }
}

/// <summary>
/// JSON Context 源文件生成器核心逻辑。
/// </summary>
/// <remarks>
/// 此类仅依赖 Roslyn <see cref="Compilation"/>，不依赖 MSBuild Workspace，便于单元测试。
/// 调用方负责加载项目/解决方案获取 <see cref="Compilation"/>，然后调用 <see cref="Generate"/>。
/// </remarks>
public class JsonContextGenerator
{
    private const string AttributeFullName = "Mud.HttpUtils.Attributes.HttpJsonSerializableAttribute";
    private const string JsonDerivedTypeAttributeFullName = "System.Text.Json.Serialization.JsonDerivedTypeAttribute";
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";
    private const string BodyAttributeFullName = "Mud.HttpUtils.Attributes.BodyAttribute";
    private const string SerializationMethodAttributeFullName = "Mud.HttpUtils.Attributes.SerializationMethodAttribute";

    /// <summary>
    /// 本次生成过程中产生的诊断信息。
    /// </summary>
    public List<ScaffolderDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// 生成 Context 源文件。
    /// </summary>
    /// <param name="compilation">Roslyn 编译单元。</param>
    /// <param name="defaultNamespace">默认命名空间（当类型无命名空间时使用）。</param>
    /// <param name="autoDerivedTypes">是否自动检测同程序集内的派生类并生成 [JsonDerivedType]。</param>
    /// <param name="scanHttpClientApi">是否扫描 [HttpClientApi] 接口，自动发现返回类型和 [Body] 参数类型中的闭合泛型。</param>
    /// <param name="targetFrameworks">
    /// [T12] 项目目标框架列表（如 <c>["net8.0","net10.0"]</c>），由调用方从项目文件读取后传入。
    /// 为 null/空时按"未知"处理（AOT002 保持保守告警）。
    /// 仅用于 AOT002 门控：源生成对开放泛型的支持自 net8.0 起才具备，
    /// 故仅当项目确实包含 net8.0 以下 TFM 时才告警——纯 net8+ 项目不再产生噪音告警。
    /// </param>
    public List<JsonContextFile> Generate(
        Compilation compilation,
        string defaultNamespace = "Generated",
        bool autoDerivedTypes = false,
        bool scanHttpClientApi = true,
        IReadOnlyList<string>? targetFrameworks = null)
    {
        Diagnostics.Clear();

        var attributeSymbol = compilation.GetTypeByMetadataName(AttributeFullName);

        // 1. 扫描 [HttpJsonSerializable] 标注类型
        var annotatedTypes = attributeSymbol != null
            ? ScanAnnotatedTypes(compilation, attributeSymbol)
            : new List<AnnotatedType>();

        // 2. 扫描 [HttpClientApi] 接口返回类型和 [Body] 参数类型
        List<ITypeSymbol> discoveredTypes = [];
        if (scanHttpClientApi)
        {
            var annotatedSet = new HashSet<INamedTypeSymbol>(
                annotatedTypes.Select(a => a.Symbol),
                SymbolEqualityComparer.Default);
            discoveredTypes = ScanHttpClientApiTypes(compilation, annotatedSet);
        }

        // 3. 无标注类型且无发现类型时返回空
        if (annotatedTypes.Count == 0 && discoveredTypes.Count == 0)
            return [];

        // 4. 诊断检查（AOT001-AOT003，仅针对标注类型）
        if (annotatedTypes.Count > 0)
            CheckDiagnostics(annotatedTypes, compilation, autoDerivedTypes, targetFrameworks);

        // 5. 分组并生成
        var files = new List<JsonContextFile>();

        // 5a. 标注类型分组
        List<TypeGroup> groups = [];
        if (annotatedTypes.Count > 0)
            groups = GroupTypes(annotatedTypes, defaultNamespace);

        // 5b. [D25] [HttpClientApi] 发现类型（闭合泛型等）合并到第一个标注类型分组中。
        // 避免创建单独的 JsonSerializerContext，防止 STJ 源生成器因 partial class 重复定义导致 hintName 冲突。
        if (discoveredTypes.Count > 0)
        {
            if (groups.Count > 0)
            {
                // 将发现类型添加到第一个标注类型分组
                groups[0].Types.AddRange(discoveredTypes);
            }
            else
            {
                // 无标注类型时，创建独立分组（回退逻辑）
                groups.Add(CreateDiscoveredTypeGroup(discoveredTypes, compilation, defaultNamespace));
            }

            Diagnostics.Add(new ScaffolderDiagnostic(
                "AOT104",
                ScaffolderDiagnosticSeverity.Info,
                $"[HttpClientApi] 接口扫描发现 {discoveredTypes.Count} 个类型（含闭合泛型），已自动纳入 {groups[0].SerializerClassName}JsonContext。",
                null));
        }

        foreach (var group in groups)
        {
            var file = GenerateContextFile(group, compilation, autoDerivedTypes);
            files.Add(file);
        }

        return files;
    }

    /// <summary>
    /// 运行 AOT 诊断检查。
    /// </summary>
    private void CheckDiagnostics(
        List<AnnotatedType> annotatedTypes,
        Compilation compilation,
        bool autoDerivedTypes,
        IReadOnlyList<string>? targetFrameworks)
    {
        // AOT001：同一 SerializerClassName 出现冲突的 NamingPolicy
        CheckDuplicateSerializerClassNameConflicts(annotatedTypes);

        // AOT002：开放泛型类型在低版本 TFM 上不可用（仅当项目确实包含 net8.0 以下 TFM）
        CheckOpenGenericOnLegacyTfm(annotatedTypes, targetFrameworks);

        // AOT003：多态类型缺少 [JsonDerivedType]
        if (!autoDerivedTypes)
            CheckPolymorphismWithoutJsonDerivedType(annotatedTypes, compilation);
    }

    /// <summary>
    /// AOT001：检测同一 SerializerClassName 下存在冲突的 NamingPolicy 配置。
    /// </summary>
    private void CheckDuplicateSerializerClassNameConflicts(List<AnnotatedType> annotatedTypes)
    {
        var conflictGroups = annotatedTypes
            .Where(t => !string.IsNullOrEmpty(t.SerializerClassName))
            .GroupBy(t => t.SerializerClassName!)
            .Where(g => g.Select(t => t.NamingPolicy).Distinct().Count() > 1
                        && g.Any(t => t.NamingPolicy != JsonNamingPolicyHint.Default));
        ;

        foreach (var group in conflictGroups)
        {
            var policies = string.Join(", ", group.Select(t => $"{t.Symbol.ToDisplayString()}={t.NamingPolicy}"));
            var aot001 = ScaffolderAotDiagnostics.AotDuplicateSerializerClassName;
            Diagnostics.Add(new ScaffolderDiagnostic(
                aot001.Id,
                ScaffolderAotDiagnostics.ToScaffolderSeverity(aot001.DefaultSeverity),
                $"SerializerClassName '{group.Key}' 存在冲突的 NamingPolicy 配置：{policies}。同一 Context 内只能使用一个命名策略，当前采用第一个非 Default 值。建议统一配置或拆分为不同分组。",
                group.Key));
        }
    }

    /// <summary>
    /// AOT002：开放泛型类型在 net8.0 以下不支持源生成。
    /// </summary>
    /// <param name="annotatedTypes">已标注类型。</param>
    /// <param name="targetFrameworks">
    /// 项目目标框架列表；为 null/空表示未知（保持保守告警），
    /// 非空且全部 ≥ net8.0 时不再告警（生成文件已由 <c>#if NET8_0_OR_GREATER</c> 包裹，
    /// net8+ 项目完全支持 <c>typeof(Generic&lt;&gt;)</c> 的源生成）。
    /// </param>
    /// <remarks>
    /// [T12 修复] 原实现对开放泛型无条件告警——即使项目只面向 net8.0/net10.0（完全支持源生成开放泛型）
    /// 也会产生噪音告警，且消息自称"net8.0 以下不支持"与实际 TFM 无关。
    /// </remarks>
    private void CheckOpenGenericOnLegacyTfm(List<AnnotatedType> annotatedTypes, IReadOnlyList<string>? targetFrameworks)
    {
        // 已知 TFM 且全部 ≥ net8.0 → 开放泛型走源生成，无需告警。
        if (targetFrameworks is { Count: > 0 } && !targetFrameworks.Any(IsTfmBelowNet8))
            return;

        foreach (var type in annotatedTypes)
        {
            if (type.Symbol.IsGenericType && type.Symbol.TypeParameters.Length > 0)
            {
                var aot002 = ScaffolderAotDiagnostics.AotOpenGenericOnLegacyTfm;
                Diagnostics.Add(new ScaffolderDiagnostic(
                    aot002.Id,
                    ScaffolderAotDiagnostics.ToScaffolderSeverity(aot002.DefaultSeverity),
                    $"类型 '{type.Symbol.ToDisplayString()}' 是开放泛型，且项目包含 net8.0 以下 TFM——开放泛型在该 TFM 下不参与源生成（走反射兜底），Native AOT 下不可用。若所有目标 TFM 均为 net8.0+，可忽略本告警。",
                    type.Symbol.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// 判断单个 TFM 字符串是否低于 net8.0（net6.0 / netstandard2.0 / net48 / netcoreapp3.1 …）。
    /// </summary>
    /// <remarks>
    /// 规则：取出 TFM 中的主版本号（<c>net6.0</c>→6、<c>netstandard2.0</c>→2、<c>net48</c>→4），
    /// 主版本号 &lt; 8 视为低版本。无法解析的 TFM（含 MSBuild 变量）按"低版本"处理（保守告警）。
    /// </remarks>
    private static bool IsTfmBelowNet8(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm))
            return true;

        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return true;

        // 形如 net8.0 / net10.0 / netstandard2.0 / netcoreapp3.1 / net48
        var versionPart = tfm.Substring(3);
        if (versionPart.Length == 0)
            return true;

        if (!char.IsDigit(versionPart[0]))
            return true; // netstandard / netcoreapp 之外的未知前缀

        // .NET 5+ 的 TFM 一律带小数点（net8.0 / net10.0）；
        // 无小数点者为 net48 / net472 等旧式紧凑写法 → 必然低于 net8.0。
        if (!versionPart.Contains('.'))
            return true;

        var digits = new string(versionPart.TakeWhile(char.IsDigit).ToArray());
        return !int.TryParse(digits, out var major) || major < 8;
    }

    /// <summary>
    /// AOT003：类型存在基类（多态）但其多态映射未声明。
    /// </summary>
    /// <remarks>
    /// [T2 修复] 满足以下任一条件即视为"多态映射已声明"，不再告警：
    /// <list type="number">
    ///   <item>类型自身标注了 <c>[JsonDerivedType]</c>（类型作为基类时声明其派生类型）；</item>
    ///   <item>其任一基类上标注了指向本类型的 <c>[JsonDerivedType(typeof(本类型))]</c>——
    ///        这才是"以基类静态类型序列化/反序列化派生实例"的正确修复位置，
    ///        原实现只看类型自身，会导致用户按提示修好基类后仍被持续告警。</item>
    /// </list>
    /// </remarks>
    private void CheckPolymorphismWithoutJsonDerivedType(List<AnnotatedType> annotatedTypes, Compilation compilation)
    {
        var jsonDerivedTypeAttr = compilation.GetTypeByMetadataName(JsonDerivedTypeAttributeFullName);

        foreach (var type in annotatedTypes)
        {
            // 跳过值类型和没有基类（仅 object）的类型
            if (type.Symbol.BaseType == null ||
                type.Symbol.BaseType.SpecialType == SpecialType.System_Object)
                continue;

            // 检查是否有 [JsonDerivedType] 标注（自身声明派生类型）
            var hasJsonDerivedType = jsonDerivedTypeAttr != null &&
                type.Symbol.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, jsonDerivedTypeAttr));

            // 检查基类是否已声明指向本类型的多态映射（正确修复位置）
            if (!hasJsonDerivedType && jsonDerivedTypeAttr != null)
                hasJsonDerivedType = IsDeclaredByBaseJsonDerivedType(type.Symbol, jsonDerivedTypeAttr);

            if (!hasJsonDerivedType)
            {
                var aot003 = ScaffolderAotDiagnostics.AotPolymorphismWithoutJsonDerivedType;
                Diagnostics.Add(new ScaffolderDiagnostic(
                    aot003.Id,
                    ScaffolderAotDiagnostics.ToScaffolderSeverity(aot003.DefaultSeverity),
                    $"类型 '{type.Symbol.ToDisplayString()}' 存在基类 '{type.Symbol.BaseType.ToDisplayString()}'（多态序列化），但未标注 [JsonDerivedType]。以基类反序列化/序列化派生类时源生成不含派生类型映射。修复：在基类声明上标注 [JsonDerivedType(typeof(派生类型))]（派生类型较多时使用 JsonDerivedTypeAttribute 的 typeDiscriminator 形式）；--auto-derived-types 仅能额外注册派生类型为独立根，不能替代本特性。",
                    type.Symbol.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// 判断类型的继承链上是否存在标注了 <c>[JsonDerivedType(typeof(该类型))]</c> 的基类。
    /// </summary>
    private static bool IsDeclaredByBaseJsonDerivedType(INamedTypeSymbol type, INamedTypeSymbol jsonDerivedTypeAttr)
    {
        var baseType = type.BaseType;
        while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
        {
            foreach (var attr in baseType.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, jsonDerivedTypeAttr))
                    continue;

                if (attr.ConstructorArguments.Length > 0 &&
                    SymbolEqualityComparer.Default.Equals(attr.ConstructorArguments[0].Value as ITypeSymbol, type))
                {
                    return true;
                }
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// 扫描编译单元中所有标注 <c>[HttpJsonSerializable]</c> 的类型。
    /// </summary>
    private List<AnnotatedType> ScanAnnotatedTypes(Compilation compilation, INamedTypeSymbol attributeSymbol)
    {
        var result = new List<AnnotatedType>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            // 遍历所有类型声明节点
            foreach (var node in root.DescendantNodes())
            {
                var symbol = semanticModel.GetDeclaredSymbol(node) as INamedTypeSymbol;
                if (symbol == null)
                    continue;

                var attr = symbol.GetAttributes().FirstOrDefault(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeSymbol));

                if (attr == null)
                    continue;

                string? serializerClassName = null;
                var namingPolicy = JsonNamingPolicyHint.Default;

                foreach (var arg in attr.NamedArguments)
                {
                    if (arg.Key == "SerializerClassName" && arg.Value.Value is string s)
                        serializerClassName = s;
                    else if (arg.Key == "NamingPolicy" && arg.Value.Value != null)
                    {
                        var val = arg.Value.Value;
                        if (val is JsonNamingPolicyHint hint)
                            namingPolicy = hint;
                        else
                            namingPolicy = (JsonNamingPolicyHint)System.Convert.ToInt32(val);
                    }
                }

                result.Add(new AnnotatedType
                {
                    Symbol = symbol,
                    SerializerClassName = string.IsNullOrWhiteSpace(serializerClassName) ? null : serializerClassName,
                    NamingPolicy = namingPolicy
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 扫描编译单元中所有标注 <c>[HttpClientApi]</c> 的接口，提取方法返回类型和 <c>[Body]</c> 参数类型。
    /// </summary>
    /// <remarks>
    /// 自动发现闭合泛型（如 <c>FeishuApiResult&lt;T&gt;</c>）和非标注的自定义类型，
    /// 将其纳入 JSON 源生成上下文，确保 AOT 下类型元数据完整。
    /// </remarks>
    /// <param name="compilation">Roslyn 编译单元。</param>
    /// <param name="annotatedSet">已通过 [HttpJsonSerializable] 标注的类型集合（用于去重）。</param>
    /// <returns>发现的需注册类型列表（含数组根，故为 <see cref="ITypeSymbol"/>）。</returns>
    private List<ITypeSymbol> ScanHttpClientApiTypes(
        Compilation compilation,
        HashSet<INamedTypeSymbol> annotatedSet)
    {
        var result = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return [];

        var bodyAttr = compilation.GetTypeByMetadataName(BodyAttributeFullName);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                if (semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
                    continue;

                if (typeSymbol.TypeKind != TypeKind.Interface)
                    continue;

                var hasHttpClientApi = typeSymbol.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr));
                if (!hasHttpClientApi)
                    continue;

                // 扫描接口自身声明的方法 + 继承链上所有基接口的方法
                // [审查修复] 原 GetMembers() 只返回当前接口声明的方法，遗漏继承的基接口方法，
                // 导致基接口中定义的返回类型/Body 参数类型未注册到 JsonSerializerContext，AOT 下反序列化失败。
                var methods = GetAllInterfaceMethods(typeSymbol);
                foreach (var method in methods)
                {
                    // 跳过属性访问器和事件访问器
                    if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
                        continue;

                    // [D25] 检查方法的 SerializationMethod：FormUrlEncoded/Xml 方法不走 JSON 序列化，
                    // 其 [Body] 参数和返回类型不应纳入 JsonSerializerContext（与 Phase 20.1 AOT004 误报修正同理）。
                    var serializationMethod = GetMethodSerializationMethod(method, compilation);
                    var isJsonMethod = serializationMethod is null or "Json";

                    // 返回类型（仅 JSON 方法）
                    if (isJsonMethod)
                    {
                        var returnType = UnwrapTaskType(method.ReturnType);
                        // [T1 修复] 数组返回类型（如 Task<UserDto[]>）：注册数组根本身 + 递归元素类型。
                        // 注意：数组是 IArrayTypeSymbol（非 INamedTypeSymbol），必须以 ITypeSymbol 入集合，
                        // 否则强制转型会在运行期抛 InvalidCastException。
                        if (returnType is IArrayTypeSymbol arrayReturn)
                        {
                            if (arrayReturn.ElementType is INamedTypeSymbol arrayElem && !IsFrameworkType(arrayElem))
                                result.Add(arrayReturn);
                            if (arrayReturn.ElementType is INamedTypeSymbol namedElem)
                                CollectSerializableTypes(namedElem, result, annotatedSet, compilation.Assembly);
                        }
                        else if (returnType is INamedTypeSymbol namedReturn)
                            CollectSerializableTypes(namedReturn, result, annotatedSet, compilation.Assembly);
                    }

                    // [Body] 参数类型（仅 JSON 方法；FormUrlEncoded Body 不走 JSON 序列化，Xml Body 走 XmlSerializer）
                    if (!isJsonMethod)
                        continue;

                    foreach (var param in method.Parameters)
                    {
                        var hasBody = bodyAttr != null && param.GetAttributes().Any(a =>
                            SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr));
                        if (!hasBody)
                            continue;

                        // [T1 修复] 数组 [Body] 参数（如 [Body] UserDto[]）：注册数组根本身 + 递归元素类型
                        if (param.Type is IArrayTypeSymbol arrayParam)
                        {
                            if (arrayParam.ElementType is INamedTypeSymbol arrayElem && !IsFrameworkType(arrayElem))
                                result.Add(arrayParam);
                            if (arrayParam.ElementType is INamedTypeSymbol namedElem)
                                CollectSerializableTypes(namedElem, result, annotatedSet, compilation.Assembly);
                        }
                        else if (param.Type is INamedTypeSymbol namedParam)
                            CollectSerializableTypes(namedParam, result, annotatedSet, compilation.Assembly);
                    }
                }
            }
        }

        return result.ToList();
    }

    /// <summary>
    /// 递归获取接口及其所有父接口的所有方法（去重）。
    /// </summary>
    /// <param name="interfaceSymbol">接口符号。</param>
    /// <returns>去重后的方法列表（含父接口方法）。</returns>
    private static List<IMethodSymbol> GetAllInterfaceMethods(INamedTypeSymbol interfaceSymbol)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var results = new List<IMethodSymbol>();

        void Collect(INamedTypeSymbol iface)
        {
            if (iface == null || visited.Contains(iface))
                return;
            visited.Add(iface);

            foreach (var method in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
                    continue;
                results.Add(method);
            }

            foreach (var baseInterface in iface.Interfaces)
                Collect(baseInterface);
        }

        Collect(interfaceSymbol);
        return results;
    }

    /// <summary>
    /// 获取方法的 SerializationMethod（检查方法自身和接口级标注）。
    /// </summary>
    /// <returns>序列化方法名称（"Json"/"Xml"/"FormUrlEncoded"）；无标注时返回 null（默认 JSON）。</returns>
    private static string? GetMethodSerializationMethod(IMethodSymbol method, Compilation compilation)
    {
        var attrSymbol = compilation.GetTypeByMetadataName(SerializationMethodAttributeFullName);
        if (attrSymbol == null)
            return null;

        // 优先检查方法自身的 [SerializationMethod]
        var attr = method.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSymbol));
        // 回退到接口级 [SerializationMethod]
        attr ??= method.ContainingType.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSymbol));

        if (attr == null || attr.ConstructorArguments.Length == 0)
            return null;

        // 枚举值在 Roslyn 中可能以 int 或 TypedConstant 形式存储
        var value = attr.ConstructorArguments[0].Value;
        return value switch
        {
            int n => n switch
            {
                0 => "Json",
                1 => "Xml",
                2 => "FormUrlEncoded",
                _ => null
            },
            _ => value?.ToString()
        };
    }

    /// <summary>
    /// 解包 <see cref="Task{T}"/> / <see cref="ValueTask{T}"/> 的类型参数。
    /// </summary>
    /// <param name="type">方法返回类型符号。</param>
    /// <returns>解包后的类型参数；若为无返回值的 Task 则返回 null；若非 Task 类型则原样返回。</returns>
    private static ITypeSymbol? UnwrapTaskType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return null;

        // Task<T> / ValueTask<T>
        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            if (IsTaskOrValueTask(named.OriginalDefinition))
                return named.TypeArguments[0];
        }

        // Task / ValueTask（无返回值）
        if (IsTaskOrValueTask(named))
            return null;

        // 非 Task 类型，原样返回
        return type;
    }

    /// <summary>
    /// 判断类型是否为 <see cref="Task"/> / <see cref="ValueTask"/>（含泛型定义）。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="ISymbol.Name"/> + 命名空间判断，
    /// 比 <c>ToDisplayString</c> 更健壮——不受 Roslyn 版本、
    /// MSBuildWorkspace 加载状态影响（加载不完全时 ToDisplayString 可能返回非标准格式）。
    /// 注意：不能使用 <c>MetadataName</c>，因为它对泛型类型返回带 arity 后缀的名称（如 "Task`1"）。
    /// </remarks>
    private static bool IsTaskOrValueTask(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        // 使用 Name（不含 arity 后缀）而非 MetadataName（含 "`1" 后缀）。
        // MetadataName 对于 Task<T> 返回 "Task`1"，Name 返回 "Task"。
        return (ns, type.Name) is
            ("System.Threading.Tasks", "Task") or
            ("System.Threading.Tasks", "ValueTask");
    }

    /// <summary>
    /// 递归收集需要纳入 JSON 源生成的类型。
    /// </summary>
    /// <remarks>
    /// 处理规则：
    /// <list type="bullet">
    ///   <item>框架类型（System.* 命名空间、基元类型）跳过自身，但递归处理其类型参数</item>
    ///   <item>闭合泛型（如 <c>FeishuApiResult&lt;X&gt;</c>）始终注册，并递归处理类型参数</item>
    ///   <item>开放泛型定义跳过（无法直接注册）</item>
    ///   <item>非泛型类型：仅当来自当前程序集且未标注 <c>[HttpJsonSerializable]</c> 时注册</item>
    ///   <item><see cref="Nullable{T}"/> 自动解包内层类型</item>
    /// </list>
    /// </remarks>
    /// <param name="type">待收集的类型符号。</param>
    /// <param name="result">收集结果集合（[T1] 放宽为 <see cref="ITypeSymbol"/>，以容纳数组型根）。</param>
    /// <param name="annotatedSet">已通过 [HttpJsonSerializable] 标注的类型集合（用于去重）。</param>
    /// <param name="currentAssembly">当前编译的程序集符号（用于判断类型来源）。</param>
    private static void CollectSerializableTypes(
        INamedTypeSymbol type,
        HashSet<ITypeSymbol> result,
        HashSet<INamedTypeSymbol> annotatedSet,
        IAssemblySymbol currentAssembly)
    {
        // 解包 Nullable<T>
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            if (type.TypeArguments.FirstOrDefault() is INamedTypeSymbol inner)
                CollectSerializableTypes(inner, result, annotatedSet, currentAssembly);
            return;
        }

        // [D25] 闭合泛型框架类型（如 List<UserDto>、IEnumerable<UserDto>）：注册自身 + 递归处理类型参数。
        // 这些类型需要注册到 JsonSerializerContext 才能在 AOT 下序列化/反序列化。
        if (IsFrameworkType(type) && type.IsGenericType && !type.IsDefinition)
        {
            // 仅注册包含非框架类型参数的闭合泛型（如 List<UserDto>，不注册 List<string>）
            var hasUserTypeArg = type.TypeArguments.Any(a =>
                a is INamedTypeSymbol namedArg && !IsFrameworkType(namedArg));
            if (hasUserTypeArg)
            {
                result.Add(type);
                foreach (var arg in type.TypeArguments)
                {
                    if (arg is INamedTypeSymbol namedArg)
                        CollectSerializableTypes(namedArg, result, annotatedSet, currentAssembly);
                }
            }
            return;
        }

        // 框架类型：跳过自身，但递归处理类型参数
        if (IsFrameworkType(type))
        {
            if (type.IsGenericType)
            {
                foreach (var arg in type.TypeArguments)
                {
                    if (arg is INamedTypeSymbol namedArg)
                        CollectSerializableTypes(namedArg, result, annotatedSet, currentAssembly);
                }
            }
            return;
        }

        // 跳过开放泛型定义
        if (type.IsGenericType && type.IsDefinition)
            return;

        // 闭合泛型：始终注册（无论来自哪个程序集），并递归处理类型参数
        if (type.IsGenericType && !type.IsDefinition)
        {
            result.Add(type);
            foreach (var arg in type.TypeArguments)
            {
                if (arg is INamedTypeSymbol namedArg)
                    CollectSerializableTypes(namedArg, result, annotatedSet, currentAssembly);
            }
            return;
        }

        // 非泛型类型：仅当来自当前程序集且未标注时注册
        // 来自引用程序集的类型应已由其自身的 [HttpJsonSerializable] Context 覆盖
        if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, currentAssembly))
            return;
        if (annotatedSet.Contains(type))
            return;

        result.Add(type);
    }

    /// <summary>
    /// 判断类型是否为框架类型（基元类型、System.* 命名空间下的类型）。
    /// </summary>
    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        // 基元类型（int, string, bool, DateTime, etc.）
        if (type.OriginalDefinition.SpecialType != SpecialType.None)
            return true;

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is null or "System" || ns.StartsWith("System."))
            return true;

        return false;
    }

    /// <summary>
    /// 为 [HttpClientApi] 发现的类型创建分组。
    /// </summary>
    private static TypeGroup CreateDiscoveredTypeGroup(
        List<ITypeSymbol> types,
        Compilation compilation,
        string defaultNamespace)
    {
        var assemblyName = compilation.AssemblyName ?? "App";
        var shortName = assemblyName.Split('.').Last();
        var serializerClassName = $"{shortName}HttpClientApi";

        return new TypeGroup
        {
            SerializerClassName = serializerClassName,
            NamingPolicy = JsonNamingPolicyHint.Default, // 自动推导
            TargetNamespace = defaultNamespace,
            Types = types
        };
    }

    /// <summary>
    /// 按 SerializerClassName 分组。留空的自动派生名称。
    /// </summary>
    private List<TypeGroup> GroupTypes(List<AnnotatedType> types, string defaultNamespace)
    {
        var groups = new Dictionary<string, TypeGroup>();

        foreach (var type in types)
        {
            var groupName = type.SerializerClassName ?? DeriveSerializerClassName(type.Symbol);

            if (!groups.TryGetValue(groupName, out var group))
            {
                var ns = type.Symbol.ContainingNamespace?.IsGlobalNamespace == true
                    ? defaultNamespace
                    : type.Symbol.ContainingNamespace?.ToDisplayString() ?? defaultNamespace;

                group = new TypeGroup
                {
                    SerializerClassName = groupName,
                    NamingPolicy = type.NamingPolicy,
                    TargetNamespace = ns,
                    Types = []
                };
                groups[groupName] = group;
            }
            else
            {
            // 已有分组：如果已有组为 Default 且当前类型显式指定了策略，则采用当前类型的
            if (group.NamingPolicy == JsonNamingPolicyHint.Default && type.NamingPolicy != JsonNamingPolicyHint.Default)
            {
                group = group with { NamingPolicy = type.NamingPolicy };
                groups[groupName] = group;
            }
            }

            group.Types.Add(type.Symbol);
        }

        return groups.Values.ToList();
    }

    /// <summary>
    /// 自动派生 SerializerClassName：{程序集简称}{顶层命名空间}。
    /// </summary>
    private static string DeriveSerializerClassName(INamedTypeSymbol symbol)
    {
        var assemblyName = symbol.ContainingAssembly?.Name ?? "App";
        // 取程序集名中第一个点前的部分作为简称
        var shortName = assemblyName.Split('.').Last();

        var ns = symbol.ContainingNamespace;
        var topNs = ns?.IsGlobalNamespace == true ? "" : ns?.Name ?? "";

        return string.IsNullOrEmpty(topNs) ? shortName : $"{shortName}{topNs}";
    }

    /// <summary>
    /// 为一个分组生成 JsonSerializerContext 源文件。
    /// </summary>
    private JsonContextFile GenerateContextFile(TypeGroup group, Compilation compilation, bool autoDerivedTypes)
    {
        var className = group.SerializerClassName + "JsonContext";
        var fileName = className + ".g.cs";

        var namingPolicy = group.NamingPolicy == JsonNamingPolicyHint.Default
            ? AutoDeriveNamingPolicy(group.Types)
            : group.NamingPolicy;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> 由 Mud.HttpUtils.JsonContextScaffolder 生成。请勿手动修改。");
        sb.AppendLine("// 生成时间请通过 git blame 查看。重新生成：dotnet mud-jsonctx --project <path> </auto-generated>");
        sb.AppendLine("#if NET8_0_OR_GREATER");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {group.TargetNamespace};");
        sb.AppendLine();
        sb.AppendLine("[JsonSourceGenerationOptions(");
        sb.AppendLine("    PropertyNameCaseInsensitive = true,");
        sb.AppendLine($"    PropertyNamingPolicy = {GetNamingPolicyString(namingPolicy)},");
        sb.AppendLine("    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,");
        sb.AppendLine("    WriteIndented = false)]");

        // 组装本 Context 的全部注册根（主类型 + 可选的自动派生类型），统一做 SYSLIB1031 重名防护
        var roots = new List<(ITypeSymbol Type, string Comment)>();
        foreach (var type in group.Types)
        {
            roots.Add((type, string.Empty));
        }

        // P2.2: 自动检测同程序集内的派生类并生成 [JsonSerializable(typeof(derived))]。
        // 注意：--auto-derived-types 注册派生类型为独立 [JsonSerializable] root，
        // 仅覆盖 Serialize<Derived> 静态调用，不能替代基类上的 [JsonDerivedType] 特性。
        // 多态序列化（以基类类型序列化派生实例）仍需用户在基类声明上标注 [JsonDerivedType]。
        if (autoDerivedTypes)
        {
            foreach (var type in group.Types)
            {
                foreach (var derived in FindDerivedTypes(compilation, type))
                {
                    roots.Add((derived, " // 派生类型已注册为独立根；多态（以基类类型序列化）仍需在基类上标注 [JsonDerivedType]"));
                }
            }
        }

        // SYSLIB1031 防护：STJ 源生成器以「默认 TypeInfo 属性名」（具名类型=简单名；数组=元素默认名+"Array"；
        // 闭包泛型=定义名+按序拼接类型参数默认名）作为 Context 上的属性名，同名即冲突——
        // 仅第一个生成元数据并告警 SYSLIB1031。对冲突组内除首个出现外的类型显式指定
        // TypeInfoPropertyName（完整名转合法标识符，确定性可复现）；首个保留默认名以兼容既有引用。
        var defaultNames = roots.Select(r => GetDefaultTypeInfoPropertyName(r.Type)).ToList();
        var duplicateNames = defaultNames
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (root, index) in roots.Select((r, i) => (r, i)))
        {
            var typeofExpr = GetTypeOfExpression(root.Type);
            var args = $"typeof({typeofExpr})";
            var comment = root.Comment;

            if (duplicateNames.Contains(defaultNames[index]) && !usedPropertyNames.Add(defaultNames[index]))
            {
                var baseIdentifier = ToTypeInfoPropertyIdentifier(root.Type);
                var candidate = baseIdentifier;
                for (var suffix = 1; !usedPropertyNames.Add(candidate); suffix++)
                {
                    candidate = $"{baseIdentifier}_{suffix}";
                }
                args += $", TypeInfoPropertyName = \"{candidate}\"";
                comment = " // SYSLIB1031 防护：默认 TypeInfo 属性名与本 Context 内其他根冲突，已显式重命名" + comment;
            }

            sb.AppendLine($"[JsonSerializable({args})]{comment}");
        }

        sb.AppendLine($"internal partial class {className} : JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
        sb.AppendLine("#endif");

        return new JsonContextFile(fileName, sb.ToString(), className, group.Types.Count);
    }

    /// <summary>
    /// 计算 STJ 源生成器为注册根分配的「默认 TypeInfo 属性名」：
    /// 具名类型取简单名（去元数）；数组取「元素默认名 + Array」；闭包泛型取「定义名 + 按序拼接类型参数默认名」。
    /// 用于提前识别 <see href="https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib1031">SYSLIB1031</see>
    /// 的重名冲突并显式指定 TypeInfoPropertyName。
    /// </summary>
    private static string GetDefaultTypeInfoPropertyName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol arrayType:
                return GetDefaultTypeInfoPropertyName(arrayType.ElementType) + "Array";
            case INamedTypeSymbol { IsDefinition: false, TypeArguments.Length: > 0 } constructed:
            {
                var name = new StringBuilder(constructed.OriginalDefinition.Name);
                foreach (var arg in constructed.TypeArguments)
                {
                    name.Append(GetDefaultTypeInfoPropertyName(arg));
                }

                return name.ToString();
            }
            case INamedTypeSymbol named:
                return named.Name;
            case ITypeParameterSymbol typeParameter:
                return typeParameter.Name;
            default:
                return type.Name;
        }
    }

    /// <summary>
    /// 将类型的完整名（含命名空间与泛型参数）转换为合法且确定性的 C# 标识符，用作 TypeInfoPropertyName。
    /// </summary>
    private static string ToTypeInfoPropertyIdentifier(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var builder = new StringBuilder(display.Length);
        foreach (var ch in display)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        var collapsed = Regex.Replace(builder.ToString(), "_{2,}", "_").Trim('_');
        if (collapsed.StartsWith("global_", StringComparison.Ordinal))
        {
            collapsed = collapsed["global_".Length..];
        }

        return collapsed.Length == 0 ? "Type" : collapsed;
    }

    /// <summary>
    /// 在同程序集内递归查找继承自指定基类的所有派生类型（用于 --auto-derived-types 自动 [JsonDerivedType]）。
    /// 采用广度优先遍历完整继承链，覆盖多层派生（如 Base → Mid → Leaf）。
    /// </summary>
    /// <param name="compilation">Roslyn 编译单元。</param>
    /// <param name="baseType">基类型。数组等非具名类型无继承链，直接返回空。</param>
    private static List<INamedTypeSymbol> FindDerivedTypes(Compilation compilation, ITypeSymbol baseType)
    {
        var result = new List<INamedTypeSymbol>();
        if (baseType is not INamedTypeSymbol baseNamedType)
            return result;

        // 收集编译单元内声明的所有具名类型（仅遍历一次语法树）
        var allTypes = new List<INamedTypeSymbol>();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (semanticModel.GetDeclaredSymbol(node) is INamedTypeSymbol candidate)
                    allTypes.Add(candidate);
            }
        }

        // 从基类出发，逐层找出所有直接/间接派生类
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<INamedTypeSymbol>();
        queue.Enqueue(baseNamedType);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var candidate in allTypes)
            {
                if (candidate.BaseType != null &&
                    SymbolEqualityComparer.Default.Equals(candidate.BaseType, current) &&
                    visited.Add(candidate))
                {
                    result.Add(candidate);
                    queue.Enqueue(candidate);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 自动推导命名策略：检测实体上的 [JsonPropertyName] 模式。
    /// 超过 50% 的实体使用 snake_case_lower → 返回 SnakeCaseLower，否则 CamelCase。
    /// </summary>
    private static JsonNamingPolicyHint AutoDeriveNamingPolicy(List<ITypeSymbol> types)
    {
        var snakeCaseRegex = new Regex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)+$", RegexOptions.Compiled);
        int totalProps = 0;
        int snakeCaseProps = 0;

        foreach (var type in types)
        {
            if (type is not INamedTypeSymbol namedType)
                continue; // 数组等非具名根不参与命名策略推导

            foreach (var prop in namedType.GetMembers().OfType<IPropertySymbol>())
            {
                // 检查是否有 [JsonPropertyName] 特性
                var jsonPropNameAttr = prop.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonPropertyNameAttribute");

                if (jsonPropNameAttr != null)
                {
                    var name = jsonPropNameAttr.ConstructorArguments.FirstOrDefault().Value as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        totalProps++;
                        if (snakeCaseRegex.IsMatch(name))
                            snakeCaseProps++;
                    }
                }
            }
        }

        if (totalProps > 0 && (double)snakeCaseProps / totalProps > 0.5)
            return JsonNamingPolicyHint.SnakeCaseLower;

        return JsonNamingPolicyHint.CamelCase;
    }

    /// <summary>
    /// 获取类型的 typeof() 表达式，处理开放泛型（&lt;T&gt; → &lt;&gt;）。
    /// </summary>
    /// <remarks>
    /// [T1 修复] 参数放宽为 <see cref="ITypeSymbol"/> 以支持数组根：
    /// <c>IArrayTypeSymbol</c> 的 <c>ToDisplayString(FullyQualifiedFormat)</c> 直接产出
    /// <c>global::Ns.UserDto[]</c>，无需任何改写。
    /// </remarks>
    private static string GetTypeOfExpression(ITypeSymbol type)
    {
        var displayString = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

        // 开放泛型定义：将 <T>, <TKey, TValue> 等改写为 <>
        // 闭合泛型（如 FeishuApiResult<X>）保持原样，STJ 支持闭合泛型的 [JsonSerializable]
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.IsDefinition)
        {
            // 匹配 <...> 中的类型参数名并替换为空
            var genericMatch = Regex.Match(displayString, @"<[^>]+>");
            if (genericMatch.Success)
                displayString = displayString.Replace(genericMatch.Value, "<>");
        }

        return displayString;
    }

    /// <summary>
    /// 将 <see cref="JsonNamingPolicyHint"/> 转为 STJ 的 <c>JsonKnownNamingPolicy</c> 字符串。
    /// </summary>
    private static string GetNamingPolicyString(JsonNamingPolicyHint hint) => hint switch
    {
        JsonNamingPolicyHint.CamelCase => "JsonKnownNamingPolicy.CamelCase",
        JsonNamingPolicyHint.SnakeCaseLower => "JsonKnownNamingPolicy.SnakeCaseLower",
        JsonNamingPolicyHint.SnakeCaseUpper => "JsonKnownNamingPolicy.SnakeCaseUpper",
        JsonNamingPolicyHint.KebabCaseLower => "JsonKnownNamingPolicy.KebabCaseLower",
        JsonNamingPolicyHint.KebabCaseUpper => "JsonKnownNamingPolicy.KebabCaseUpper",
        _ => "JsonKnownNamingPolicy.CamelCase"
    };

    /// <summary>
    /// 标注类型信息。
    /// </summary>
    private record AnnotatedType
    {
        public required INamedTypeSymbol Symbol { get; init; }
        public required string? SerializerClassName { get; init; }
        public required JsonNamingPolicyHint NamingPolicy { get; init; }
    }
}
