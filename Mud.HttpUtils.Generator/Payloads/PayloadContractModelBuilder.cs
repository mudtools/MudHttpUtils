// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mud.HttpUtils.Helpers;

namespace Mud.HttpUtils.Models.Payloads;

/// <summary>
/// 载荷契约模型构建器：把「带 <c>[PayloadContract]</c> 的类」语义化为 <see cref="PayloadContractModel"/>。
/// </summary>
/// <remarks>
/// <para>
/// 全部语义解析（转换器方法查找、返回值可赋值性、形态推断）均在此完成，使用
/// <see cref="GeneratorAttributeSyntaxContext.SemanticModel"/>（及其 <c>Compilation</c>）——
/// 而<b>不</b>把 <c>CompilationProvider</c> 引入增量管线（避免编译级粒度重执行）。
/// </para>
/// <para>
/// <b>异常纪律</b>：本类的解析路径均为「符号为空即返回诊断」的显式分支，
/// 不做防御性 null 断言；任何未预期异常由生成器执行体的 try/catch 兜底为 <c>PAYLOAD001</c>。
/// </para>
/// </remarks>
internal static class PayloadContractModelBuilder
{
    /// <summary>
    /// <c>[PayloadContract]</c> 的元数据名（生成器不引用特性程序集，按名匹配）。
    /// </summary>
    /// <remarks>
    /// 命名空间与其它全部特性一致（<c>Mud.HttpUtils.Attributes</c>，见 Guards/AttributeNamespaceConsistencyTests 的 G8-13 守卫：
    /// 该程序集的公开面命名空间白名单为空，消费方的 <c>using</c> 组合必须可预期）。
    /// </remarks>
    internal const string ContractAttributeMetadataName = "Mud.HttpUtils.Attributes.PayloadContractAttribute";

    /// <summary><c>[PayloadField]</c> 的元数据名。</summary>
    internal const string FieldAttributeMetadataName = "Mud.HttpUtils.Attributes.PayloadFieldAttribute";

    /// <summary>生成成员名（契约的一部分，非配置项：消费方注册入口直接书写该名）。</summary>
    internal const string GeneratedMemberName = "PayloadFieldMap";

    /// <summary>上游节点类型的元数据名（用于 <c>Method</c> 首参绑定判定）。</summary>
    internal const string PayloadNodeMetadataName = "Mud.HttpUtils.Payloads.PayloadNode";

    // —— 特性参数名 ——
    // 常量化的动机：AttributeParameterContractTests 以「属性名字符串是否出现在生成器源码中」
    // 判定「特性新增了可写属性但生成器未同步读取」这一类回归（CFG-28 根因），故必须保留字面量。
    private const string ContractIdProperty = "ContractId";
    private const string ConverterProperty = "Converter";
    private const string ScopeFallbackProperty = "ScopeFallback";
    private const string ElementProperty = "Element";
    private const string FormatProperty = "Format";
    private const string SeparatorProperty = "Separator";
    private const string ItemNameProperty = "ItemName";
    private const string NameAttributeProperty = "NameAttribute";
    private const string ValueElementProperty = "ValueElement";
    private const string MethodProperty = "Method";

    // —— 形态成员名（PayloadFieldFormat） ——
    private const string AutoFormatName = "Auto";
    private const string TextFormatName = "Text";
    private const string DelimitedFormatName = "Delimited";
    private const string ItemsFormatName = "Items";
    private const string ItemsWithAttributesFormatName = "ItemsWithAttributes";

    // —— 转换器契约方法名 ——
    private const string TextMethodName = "Text";
    private const string NumberMethodName = "Number";
    private const string FlagMethodName = "Flag";
    private const string DelimitedMethodName = "Delimited";
    private const string ItemsMethodName = "Items";
    private const string ItemsWithAttributesMethodName = "ItemsWithAttributes";

    private const string NodeArgument = "n";
    private const string NodeTextArgument = "n?.Value";

    /// <summary><c>List&lt;T&gt;</c> 原始定义的完全限定显示名（Roslyn <c>SpecialType</c> 不含泛型集合）。</summary>
    private const string ListOfTDisplay = "global::System.Collections.Generic.List<T>";

    /// <summary>
    /// 生成代码中的类型显示格式：全限定 + 特殊类型关键字 + <b>可空引用类型标注</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须含 <c>IncludeNullableReferenceTypeModifier</c></b>：<see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>
    /// 会丢弃 <c>?</c> 标注，于是 <c>List&lt;string?&gt;</c> 属性会渲染成
    /// <c>Delimited&lt;string&gt;</c>（返回 <c>List&lt;string&gt;</c>）⇒ 赋给 <c>List&lt;string?&gt;</c> 时报
    /// <c>CS8619</c>（可空性不匹配，错误指向生成文件；消费方开 <c>TreatWarningsAsErrors</c> 即失败）。
    /// 既有生成器（<c>MethodGenerator</c> / <c>ReturnTypeSupport</c> / <c>ParameterSignatureBuilder</c>）统一采用本格式。
    /// </remarks>
    private static readonly SymbolDisplayFormat GeneratedTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>解析结果分类（映射到 <c>PAYLOAD004</c>~<c>PAYLOAD007</c>）。</summary>
    private enum ResolutionFailure
    {
        None = 0,
        MethodNotFound,
        ReturnTypeMismatch,
        DeclarationInvalid,
        NotInferable,
    }

    /// <summary>
    /// 构建模型（在 <c>Transform</c> 阶段执行；不做任何 IO）。
    /// </summary>
    /// <param name="context">特性语法上下文（提供目标符号、语法节点、特性数据与语义模型）。</param>
    /// <returns>模型（含全部诊断；<see cref="PayloadContractModel.HasErrors"/> 为真时不产出源文件）。</returns>
    internal static PayloadContractModel Build(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
        {
            var fallbackName = context.TargetSymbol?.Name ?? "<unknown>";
            var fallbackLocation = context.TargetNode?.GetLocation() ?? Location.None;
            var fallbackDiagnostics = ImmutableArray.Create(new PayloadDiagnostic(
                Diagnostics.PayloadGenerationError,
                fallbackLocation,
                fallbackName,
                "无法解析 [PayloadContract] 目标类型符号（语法可能不完整）。"));

            return new PayloadContractModel(
                ns: null,
                typeName: fallbackName,
                typeDisplay: "global::" + fallbackName,
                hintName: fallbackName + ".PayloadFieldMap.g.cs",
                contractId: null,
                scopeFallback: null,
                converterDisplay: null,
                fields: ImmutableArray<PayloadFieldModel>.Empty,
                diagnostics: fallbackDiagnostics,
                location: fallbackLocation,
                fingerprint: "invalid|" + fallbackName);
        }

        var className = typeSymbol.Name;
        var typeDisplay = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // ns 用于**渲染** `namespace` 声明：命名空间段为 C# 保留字时 ToDisplayString() 自带 `@` 转义
        // （如 `namespace @class.Sub`），正是渲染所需，故此处**保持**转义。
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();

        // hintName 是**文件路径**，不得含 `@`（`context.AddSource` 校验 hintName 合法性）：
        // 直接用 ns 会让「命名空间段为保留字」的载荷抛 ArgumentException
        // （实测报文：hintName"@class.Sub.NsPayload.PayloadFieldMap.g.cs"在位置 0 处包含无效字符"@"），
        // 该异常被 Execute 的兜底捕获后表现为 PAYLOAD001「生成器内部错误」—— 用户无法从文案定位到命名空间，
        // 故必须在此剥离标识符转义前缀。
        var hintNamespace = ns?.Replace("@", string.Empty);
        var hintName = (hintNamespace == null ? string.Empty : hintNamespace + ".") + className + ".PayloadFieldMap.g.cs";
        var typeLocation = GetLocation(typeSymbol);
        var diagnostics = new List<PayloadDiagnostic>();

        // ① 形态检查：泛型 / 嵌套 / record / 非 class —— 这些形态与 new() 约束、partial 成员渲染
        //    或 netstandard2.0 消费面互斥，直接判定为不受支持（避免后续渲染出不可编译产物）。
        var shapeProblem = DescribeUnsupportedShape(typeSymbol);
        if (shapeProblem != null)
        {
            diagnostics.Add(new PayloadDiagnostic(
                Diagnostics.PayloadContractTypeShapeUnsupported, typeLocation, className, shapeProblem));

            return CreateModel(ns, className, typeDisplay, hintName, null, null, null,
                ImmutableArray<PayloadFieldModel>.Empty, diagnostics, typeLocation);
        }

        // ② 手写 / 生成互斥（PAYLOAD008）：源码声明的同名成员会让生成物产生 CS0102。
        var handwritten = typeSymbol.GetMembers(GeneratedMemberName)
            .Where(m => !m.IsImplicitlyDeclared && m.DeclaringSyntaxReferences.Length > 0)
            .ToArray();

        if (handwritten.Length > 0)
        {
            diagnostics.Add(new PayloadDiagnostic(
                Diagnostics.PayloadHandwrittenMapConflict,
                GetLocation(handwritten[0]),
                className,
                "本类已声明成员 " + handwritten[0].ToDisplayString() +
                "。生成物是同名 partial 成员，二者并存将导致 CS0102；" +
                "迁移应在同一提交内「删除手写成员 + 添加 [PayloadContract]」。"));

            return CreateModel(ns, className, typeDisplay, hintName, null, null, null,
                ImmutableArray<PayloadFieldModel>.Empty, diagnostics, typeLocation);
        }

        // ②' 继承链上的同名成员（v2.4，PAYLOAD008 扩面）：生成物是 public static 成员，
        //     会**隐藏**基类同名成员 ⇒ 消费方收到 CS0108（开启 TreatWarningsAsErrors 即构建失败；
        //     实测「零诊断 + 1 个生成文件 + CS0108 告警泄漏到消费方」）。与本类同名成员同源（G-ADR-15）。
        var inheritedMapMember = FindInheritedMember(typeSymbol, GeneratedMemberName);
        if (inheritedMapMember != null)
        {
            var inheritedLocation = GetLocation(inheritedMapMember);
            if (inheritedLocation == Location.None)
                inheritedLocation = typeLocation;

            diagnostics.Add(new PayloadDiagnostic(
                Diagnostics.PayloadHandwrittenMapConflict,
                inheritedLocation,
                className,
                "基类 " + inheritedMapMember.ContainingType.ToDisplayString() + " 已声明同名成员 " +
                inheritedMapMember.ToDisplayString() +
                "。生成物是同名 public static 成员，会隐藏继承成员（CS0108）⇒ " +
                "请重命名或移除基类成员，或对该类型改用手写映射表（不标注 [PayloadContract]）。"));

            return CreateModel(ns, className, typeDisplay, hintName, null, null, null,
                ImmutableArray<PayloadFieldModel>.Empty, diagnostics, typeLocation);
        }

        // ③ partial 检查（PAYLOAD002）。
        var declarations = typeSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .ToArray();

        var allPartial = declarations.Length > 0
            && declarations.All(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        if (!allPartial)
        {
            diagnostics.Add(new PayloadDiagnostic(
                Diagnostics.PayloadContractTypeNotPartial, typeLocation, className,
                "请为类声明添加 partial 修饰符。"));

            return CreateModel(ns, className, typeDisplay, hintName, null, null, null,
                ImmutableArray<PayloadFieldModel>.Empty, diagnostics, typeLocation);
        }

        // ④ 读取 [PayloadContract] 参数。
        var contractAttribute = context.Attributes.FirstOrDefault(
            attribute => IsAttribute(attribute, ContractAttributeMetadataName));

        var contractId = ReadString(contractAttribute, ContractIdProperty);
        var scopeFallback = ReadString(contractAttribute, ScopeFallbackProperty);
        var converterType = ReadType(contractAttribute, ConverterProperty);
        var converterDisplay = converterType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // ⑤ 收集带 [PayloadField] 的属性（跨 partial 文件按「文件路径 → 声明位置」稳定排序 ⇒ 声明序）。
        var orderedFields = typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(property => (Property: property, Attribute: FindFieldAttribute(property)))
            .Where(pair => pair.Attribute != null)
            .Select(pair => (pair.Property, Attribute: pair.Attribute!, Location: GetLocation(pair.Property)))
            .OrderBy(entry => entry.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Location.SourceSpan.Start)
            .ToArray();

        if (orderedFields.Length > 0 && converterType == null)
        {
            diagnostics.Add(new PayloadDiagnostic(
                Diagnostics.PayloadConverterNotSpecified, typeLocation, className,
                "请声明 [PayloadContract(Converter = typeof(转换器类型))]。"));

            return CreateModel(ns, className, typeDisplay, hintName, contractId, scopeFallback, null,
                ImmutableArray<PayloadFieldModel>.Empty, diagnostics, typeLocation);
        }

        // ⑥ 逐字段解析（元素名唯一性 + 形态推断 + 转换方法语义校验）。
        var fields = ImmutableArray.CreateBuilder<PayloadFieldModel>(orderedFields.Length);
        var seenElements = new HashSet<string>(StringComparer.Ordinal);
        var compilation = context.SemanticModel?.Compilation;

        foreach (var entry in orderedFields)
        {
            var property = entry.Property;
            var attribute = entry.Attribute;
            var location = entry.Location;
            var element = ReadConstructorString(attribute, 0) ?? string.Empty;

            if (element.Length == 0)
            {
                diagnostics.Add(new PayloadDiagnostic(
                    Diagnostics.PayloadFieldDeclarationInvalid, location, className,
                    "属性 " + property.Name + " 的 [PayloadField] 未给出 " + ElementProperty + " 构造参数（元素名）。"));
                continue;
            }

            if (!seenElements.Add(element))
            {
                diagnostics.Add(new PayloadDiagnostic(
                    Diagnostics.PayloadFieldDeclarationInvalid, location, className,
                    "元素名 \"" + element + "\" 被重复映射（属性 " + property.Name +
                    "）。同一契约内元素名必须唯一，否则后者会覆盖前者。"));
                continue;
            }

            if (property.IsStatic || property.SetMethod == null || property.SetMethod.IsInitOnly)
            {
                diagnostics.Add(new PayloadDiagnostic(
                    Diagnostics.PayloadFieldDeclarationInvalid, location, className,
                    "属性 " + property.Name + " 必须是非 static 的可写属性（init-only 与只读属性无法由生成代码赋值）。"));
                continue;
            }

            // 索引器（this[]）与显式接口实现（IFoo.Bar）的「成员名」都不是可直接书写的属性名，
            // 生成物以 t.<属性名> = … 赋值会产出语法/语义错误（CS1001/CS1061 等）且错误指向生成文件。
            if (property.IsIndexer || property.ExplicitInterfaceImplementations.Length > 0)
            {
                diagnostics.Add(new PayloadDiagnostic(
                    Diagnostics.PayloadFieldDeclarationInvalid, location, className,
                    "属性 " + property.Name + " 是索引器或显式接口实现：生成物以 t.<属性名> = … 的形式赋值，" +
                    "二者都无法被这样引用（请改为普通可写属性）。"));
                continue;
            }

            // Format 显式给出时必须能反解为枚举成员名：`(PayloadFieldFormat)99` 之类会让
            // GetEnumMemberName 返回 null，若不拦截会被静默当作 Auto 走类型推断（看似生效实则丢弃声明）。
            var hasFormatArgument = TryGetNamed(attribute, FormatProperty, out var formatConstant);
            var formatName = hasFormatArgument ? AttributeArgumentReader.GetEnumMemberName(formatConstant) : null;

            if (hasFormatArgument && formatName == null)
            {
                diagnostics.Add(new PayloadDiagnostic(
                    Diagnostics.PayloadFieldDeclarationInvalid, location, className,
                    "属性 " + property.Name + " 的 " + FormatProperty +
                    " 取值无法反解为 PayloadFieldFormat 的成员名（请书写枚举成员名，而非强制转换的数值）。"));
                continue;
            }

            var hasExplicitSeparator = TryGetNamed(attribute, SeparatorProperty, out var separatorConstant);
            var separator = separatorConstant.Value is char separatorChar ? separatorChar : ',';
            var itemName = ReadString(attribute, ItemNameProperty);
            var nameAttribute = ReadString(attribute, NameAttributeProperty);
            var valueElement = ReadString(attribute, ValueElementProperty);
            var methodName = ReadString(attribute, MethodProperty);

            var failure = TryResolveField(
                converterType,
                converterDisplay,
                compilation,
                property,
                formatName ?? AutoFormatName,
                hasExplicitSeparator,
                separator,
                itemName,
                nameAttribute,
                valueElement,
                methodName,
                out var resolvedCall,
                out var detail);

            if (failure != ResolutionFailure.None || resolvedCall == null)
            {
                diagnostics.Add(new PayloadDiagnostic(
                    MapFailureToDescriptor(failure), location, className, detail ?? "无法解析该字段的转换方法。"));

                continue;
            }

            fields.Add(new PayloadFieldModel(
                element,
                property.Name,
                // 用 GeneratedTypeFormat（含可空标注）而非 FullyQualifiedFormat：属性类型的可空标注会改变
                // 渲染产物（如 Delimited<string> 与 Delimited<string?>），必须进入指纹，否则「仅补一个 ?」
                // 的编辑会因指纹相同而命中增量缓存，产出过期（可空性不符）的生成文件。
                property.Type.ToDisplayString(GeneratedTypeFormat),
                resolvedCall,
                location));
        }

        return CreateModel(ns, className, typeDisplay, hintName, contractId, scopeFallback, converterDisplay,
            fields.ToImmutable(), diagnostics, typeLocation);
    }

    /// <summary>
    /// 解析单个字段的转换调用（显式 <c>Method</c> 优先，否则按属性类型推断）。
    /// </summary>
    private static ResolutionFailure TryResolveField(
        INamedTypeSymbol? converterType,
        string? converterDisplay,
        Compilation? compilation,
        IPropertySymbol property,
        string formatName,
        bool hasExplicitSeparator,
        char separator,
        string? itemName,
        string? nameAttribute,
        string? valueElement,
        string? methodName,
        out string? resolvedCall,
        out string? detail)
    {
        resolvedCall = null;
        detail = null;

        if (converterType == null || converterDisplay == null)
        {
            detail = "未声明转换器类型。";
            return ResolutionFailure.MethodNotFound;
        }

        // 组合合法性（PAYLOAD006）：显式 Separator 与 ItemName 互斥 —— 静默忽略会产出
        // 「看似生效实则丢弃配置」的映射，是本生成器要消除的故障模式之一。
        if (hasExplicitSeparator && itemName != null)
        {
            detail = "属性 " + property.Name + " 同时给出了 Separator 与 ItemName：分隔符串与嵌套项是两种互斥的字段形态。";
            return ResolutionFailure.DeclarationInvalid;
        }

        // —— 路径 A：显式 Method（优先于类型推断）——
        if (!string.IsNullOrEmpty(methodName))
        {
            return ResolveExplicitMethod(converterType, converterDisplay, compilation, property, methodName!, out resolvedCall, out detail);
        }

        // —— 路径 B：按声明的 Format 或属性类型推断 ——
        var effectiveFormat = formatName;

        if (string.IsNullOrEmpty(effectiveFormat) || string.Equals(effectiveFormat, AutoFormatName, StringComparison.Ordinal))
        {
            if (!InferFormat(property, itemName, nameAttribute, valueElement, out effectiveFormat, out detail))
                return ResolutionFailure.NotInferable;
        }

        // 形态与附加参数的匹配校验（PAYLOAD006）。
        var combinationValid = ValidateFormatCombination(property, effectiveFormat, itemName, nameAttribute, valueElement, out var combinationDetail);
        if (!combinationValid)
        {
            detail = combinationDetail;
            return ResolutionFailure.DeclarationInvalid;
        }

        // Separator 只对 Delimited 生效（PAYLOAD006）：显式给出却落在其它形态上时，
        // 静默忽略会产出「看似生效实则丢弃配置」的映射（与「Separator + ItemName 互斥」同一口径）。
        if (hasExplicitSeparator && !string.Equals(effectiveFormat, DelimitedFormatName, StringComparison.Ordinal))
        {
            detail = "属性 " + property.Name + " 显式给出了 " + SeparatorProperty + "，但生效形态是 " + effectiveFormat +
                     "：分隔符只对 Format = " + DelimitedFormatName + " 生效，静默忽略会让该声明看似生效实则被丢弃。";
            return ResolutionFailure.DeclarationInvalid;
        }

        // 形态 → 转换器契约方法 + 泛型实参。
        string helperName;
        ITypeSymbol? typeArgument;

        if (string.Equals(effectiveFormat, TextFormatName, StringComparison.Ordinal))
        {
            if (IsStringScalar(property.Type))
            {
                helperName = TextMethodName;
                typeArgument = null;
            }
            else if (IsNullableScalar(property.Type, out var underlying) && IsInferableScalarValueType(underlying))
            {
                typeArgument = underlying;
                helperName = underlying.SpecialType == SpecialType.System_Boolean ? FlagMethodName : NumberMethodName;
            }
            else
            {
                detail = "属性 " + property.Name + " 的 Format = Text 需要可空形态：string?、int?/long?（Number<T>）或 bool?（Flag<bool>），" +
                         "当前为 " + property.Type.ToDisplayString() + "。非空值类型无「缺失 ⇒ null」语义，" +
                         "非空引用类型会在 nullable 启用的消费项目中产生 CS8601；枚举等值域类型请改用 Method。";
                return ResolutionFailure.NotInferable;
            }
        }
        else
        {
            var elementType = GetListElementType(property.Type);
            if (elementType == null)
            {
                detail = "属性 " + property.Name + " 的类型 " + property.Type.ToDisplayString() +
                         " 不是 List<T>，无法按 Format = " + effectiveFormat + " 生成映射。";
                return ResolutionFailure.NotInferable;
            }

            typeArgument = elementType;
            helperName = string.Equals(effectiveFormat, DelimitedFormatName, StringComparison.Ordinal)
                ? DelimitedMethodName
                : (string.Equals(effectiveFormat, ItemsFormatName, StringComparison.Ordinal)
                    ? ItemsMethodName
                    : ItemsWithAttributesMethodName);
        }

        var typeArguments = typeArgument == null
            ? ImmutableArray<ITypeSymbol>.Empty
            : ImmutableArray.Create(typeArgument);

        var parameterCount = string.Equals(effectiveFormat, ItemsWithAttributesFormatName, StringComparison.Ordinal)
            ? 4
            : (string.Equals(effectiveFormat, TextFormatName, StringComparison.Ordinal) ? 1 : 2);

        var candidates = FindContractMethods(converterType, helperName, parameterCount, typeArguments.Length);
        if (candidates.Length == 0)
        {
            detail = "转换器 " + converterType.Name + " 上未找到契约要求的静态方法 " + helperName +
                     "（形参个数 " + parameterCount +
                     (typeArguments.Length == 0 ? "、非泛型" : "、泛型元数 " + typeArguments.Length) + "）。";
            return ResolutionFailure.MethodNotFound;
        }

        // 形参「个数 / 泛型元数」已由 FindContractMethods 筛出候选，此处补齐**类型 + 可空标注**校验：
        // 契约签名恒以 PayloadNode 为首参（Delimited 次参 char、Items/ItemsWithAttributes 次参 string…），
        // 否则生成物会在消费方编译期报 CS1503（错误指向生成文件）。
        //
        // **必须遍历全部候选**：同名重载并存时（如 Delimited<T>(string, char) 与 Delimited<T>(PayloadNode?, char)），
        // GetMembers 的返回顺序不保证契约签名在前 —— 只看首个候选会误报 PAYLOAD004（实测）。
        IMethodSymbol? method = null;
        var anyTypeConforming = false;
        for (var i = 0; i < candidates.Length; i++)
        {
            if (!HasContractParameterTypes(candidates[i], helperName))
                continue;

            anyTypeConforming = true;
            if (HasNullableNodeParameter(candidates[i]))
            {
                method = candidates[i];
                break;
            }
        }

        if (method == null)
        {
            detail = anyTypeConforming
                ? NullableFirstParameterDetail(converterType, helperName)
                : "转换器 " + converterType.Name + " 的 " + helperName + " 形参类型不符合契约签名：" +
                  "首参必须是 " + PayloadNodeMetadataName +
                  (string.Equals(helperName, DelimitedMethodName, StringComparison.Ordinal)
                      ? "、次参必须是 char。"
                      : (string.Equals(helperName, TextMethodName, StringComparison.Ordinal)
                          ? "。"
                          : "、其余形参必须是 string。"));
            return ResolutionFailure.MethodNotFound;
        }

        var constructed = TryConstruct(method, typeArguments);
        if (constructed == null)
        {
            detail = "转换器 " + converterType.Name + " 的 " + helperName + "<" +
                     string.Join(", ", typeArguments.Select(argument => argument.ToDisplayString())) +
                     "> 无法按该类型实参构造（泛型元数不匹配）。";
            return ResolutionFailure.NotInferable;
        }

        if (!AreTypeArgumentsSatisfied(method, typeArguments, compilation, out var constraintDetail))
        {
            detail = "转换器 " + converterType.Name + " 的 " + helperName + "<" +
                     string.Join(", ", typeArguments.Select(argument => argument.ToDisplayString())) +
                     "> 无法按该类型实参构造：" + constraintDetail +
                     "（请调整属性元素类型，或为该字段显式指定 Method）。";
            return ResolutionFailure.NotInferable;
        }

        var assignable = TryCheckAssignable(compilation, constructed.ReturnType, property.Type, out var returnDetail);
        if (assignable == false)
        {
            detail = "转换方法 " + helperName + " 返回 " + constructed.ReturnType.ToDisplayString() +
                     "，无法隐式转换为属性 " + property.Name + "（" + property.Type.ToDisplayString() + "）。" + returnDetail;
            return ResolutionFailure.ReturnTypeMismatch;
        }

        resolvedCall = BuildHelperCall(converterDisplay, helperName, typeArguments, effectiveFormat, separator, itemName, nameAttribute, valueElement);
        return ResolutionFailure.None;
    }

    private static bool ValidateFormatCombination(
        IPropertySymbol property,
        string formatName,
        string? itemName,
        string? nameAttribute,
        string? valueElement,
        out string detail)
    {
        switch (formatName)
        {
            case TextFormatName:
                if (itemName != null || nameAttribute != null || valueElement != null)
                {
                    detail = "属性 " + property.Name + " 的 Format = Text 不接受 ItemName/NameAttribute/ValueElement。";
                    return false;
                }

                break;

            case DelimitedFormatName:
                if (itemName != null || nameAttribute != null || valueElement != null)
                {
                    detail = "属性 " + property.Name + " 的 Format = Delimited 不接受 ItemName/NameAttribute/ValueElement（自带分隔符的标量序列）。";
                    return false;
                }

                break;

            case ItemsFormatName:
                if (itemName == null)
                {
                    detail = "属性 " + property.Name + " 的 Format = Items 必须给出 ItemName（嵌套项元素名）。";
                    return false;
                }

                if (nameAttribute != null || valueElement != null)
                {
                    detail = "属性 " + property.Name + " 的 Format = Items 不接受 NameAttribute/ValueElement（请改用 ItemsWithAttributes）。";
                    return false;
                }

                break;

            case ItemsWithAttributesFormatName:
                if (itemName == null || nameAttribute == null || valueElement == null)
                {
                    detail = "属性 " + property.Name + " 的 Format = ItemsWithAttributes 必须同时给出 ItemName / NameAttribute / ValueElement。";
                    return false;
                }

                break;

            default:
                detail = "属性 " + property.Name + " 声明了未知的 Format 成员名 \"" + formatName + "\"。";
                return false;
        }

        detail = string.Empty;
        return true;
    }

    private static bool InferFormat(
        IPropertySymbol property,
        string? itemName,
        string? nameAttribute,
        string? valueElement,
        out string formatName,
        out string? detail)
    {
        formatName = string.Empty;
        detail = null;

        var propertyType = property.Type;

        if (IsStringScalar(propertyType)
            || (IsNullableScalar(propertyType, out var nullableScalar) && IsInferableScalarValueType(nullableScalar)))
        {
            formatName = TextFormatName;
            return true;
        }

        var elementType = GetListElementType(propertyType);
        if (elementType != null)
        {
            if (itemName != null && nameAttribute != null && valueElement != null)
            {
                formatName = ItemsWithAttributesFormatName;
                return true;
            }

            if (itemName != null)
            {
                formatName = ItemsFormatName;
                return true;
            }

            if (IsSupportedScalar(elementType))
            {
                formatName = DelimitedFormatName;
                return true;
            }

            detail = "属性 " + property.Name + " 的 List 元素类型 " + elementType.ToDisplayString() +
                     " 无法按分隔符串推断（仅支持 string / 数值 / 布尔 / 枚举）";
            return false;
        }

        detail = "属性 " + property.Name + " 的类型 " + propertyType.ToDisplayString() +
                 " 无标准的字段形态（推断支持：string?、可空数值/bool、List<标量>、List<T> + ItemName）";
        return false;
    }

    private static ResolutionFailure ResolveExplicitMethod(
        INamedTypeSymbol converterType,
        string converterDisplay,
        Compilation? compilation,
        IPropertySymbol property,
        string methodName,
        out string? resolvedCall,
        out string? detail)
    {
        resolvedCall = null;
        detail = null;

        // 首参为 PayloadNode 的重载优先（可拿节点结构）；否则退到 string 重载（只拿标量文本）。
        // 同级内**必须**优先取「首参带可空标注」的重载：非可空标注会让生成物在 nullable 启用的
        // 消费工程里报 CS8604（生成物传入的是可能为 null 的 n / n?.Value），详见 HasNullableNodeParameter。
        // CA1508 误报抑制：分析器无法透过 IsPayloadNode 辅助方法推断「第一参数类型命中」这一分支，
        // 因而误判 nodeParameterOverload 恒为 null、resolved == null 恒真。该分支的实际可达性由
        // PayloadFieldMapGeneratorTests.AllFormatsPayload_Snapshot（两种首参重载各一）与
        // PayloadFieldMapDiagnosticTests.ReturnTypeMismatch_ReportsPayload005 覆盖证明。
#pragma warning disable CA1508
        IMethodSymbol? nodeParameterOverload = null;
        IMethodSymbol? textParameterOverload = null;
        IMethodSymbol? nonNullableNodeOverload = null;
        IMethodSymbol? nonNullableTextOverload = null;
        var anyNamedMember = false;

        foreach (var member in converterType.GetMembers(methodName))
        {
            if (member is not IMethodSymbol method)
                continue;

            anyNamedMember = true;

            if (!method.IsStatic || method.IsGenericMethod || method.Parameters.Length != 1)
                continue;

            var firstParameter = method.Parameters[0].Type;
            var nullableAnnotated = HasNullableNodeParameter(method);

            if (IsPayloadNode(firstParameter))
            {
                if (nullableAnnotated)
                {
                    nodeParameterOverload = method;
                    break;
                }

                if (nonNullableNodeOverload == null)
                    nonNullableNodeOverload = method;

                continue;
            }

            if (firstParameter.SpecialType == SpecialType.System_String)
            {
                if (nullableAnnotated)
                {
                    if (textParameterOverload == null)
                        textParameterOverload = method;
                }
                else if (nonNullableTextOverload == null)
                {
                    nonNullableTextOverload = method;
                }
            }
        }

        var resolved = nodeParameterOverload ?? textParameterOverload;

        // CA1508 的误报点落在本判定行（分析器认为 resolved 恒为 null），故 restore 必须置于本 if 之后，
        // 而非 resolved 赋值之后 —— 否则抑制区间不覆盖告警行，构建仍报 CA1508。
        if (resolved == null)
        {
            detail = nonNullableNodeOverload != null || nonNullableTextOverload != null
                ? NullableFirstParameterDetail(converterType, methodName)
                : (anyNamedMember
                    ? "转换器 " + converterType.Name + " 上的方法 " + methodName +
                      " 签名不符合契约：须为 static、非泛型、恰有一个首参为 PayloadNode 或 string 的参数。"
                    : "转换器 " + converterType.Name + " 上不存在方法 " + methodName + "。");
            return ResolutionFailure.MethodNotFound;
        }
#pragma warning restore CA1508

        var assignable = TryCheckAssignable(compilation, resolved.ReturnType, property.Type, out var returnDetail);
        if (assignable == false)
        {
            detail = "转换方法 " + methodName + " 返回 " + resolved.ReturnType.ToDisplayString() +
                     "，无法隐式转换为属性 " + property.Name + "（" + property.Type.ToDisplayString() + "）。" + returnDetail;
            return ResolutionFailure.ReturnTypeMismatch;
        }

        var argument = ReferenceEquals(resolved, nodeParameterOverload) ? NodeArgument : NodeTextArgument;
        resolvedCall = converterDisplay + "." + methodName + "(" + argument + ")";
        return ResolutionFailure.None;
    }

    private static string BuildHelperCall(
        string converterDisplay,
        string helperName,
        ImmutableArray<ITypeSymbol> typeArguments,
        string formatName,
        char separator,
        string? itemName,
        string? nameAttribute,
        string? valueElement)
    {
        var call = new System.Text.StringBuilder(converterDisplay.Length + 32);
        call.Append(converterDisplay).Append('.').Append(helperName);

        if (typeArguments.Length > 0)
        {
            call.Append('<');
            for (var i = 0; i < typeArguments.Length; i++)
            {
                if (i > 0)
                    call.Append(", ");

                call.Append(typeArguments[i].ToDisplayString(GeneratedTypeFormat));
            }

            call.Append('>');
        }

        // CA1834 抑制：NodeArgument 是「单字符常量字符串」，分析器建议改用 Append('n')；
        // 但该字面量与其它渲染点共用同一常量（单一事实源），在此处另写 'n' 会在将来改名时静默分叉。
#pragma warning disable CA1834
        call.Append('(').Append(NodeArgument);
#pragma warning restore CA1834

        if (string.Equals(formatName, DelimitedFormatName, StringComparison.Ordinal))
        {
            call.Append(", '").Append(StringEscapeHelper.EscapeChar(separator)).Append('\'');
        }
        else if (string.Equals(formatName, ItemsFormatName, StringComparison.Ordinal))
        {
            call.Append(", \"").Append(StringEscapeHelper.EscapeString(itemName)).Append('"');
        }
        else if (string.Equals(formatName, ItemsWithAttributesFormatName, StringComparison.Ordinal))
        {
            call.Append(", \"").Append(StringEscapeHelper.EscapeString(itemName)).Append('"');
            call.Append(", \"").Append(StringEscapeHelper.EscapeString(nameAttribute)).Append('"');
            call.Append(", \"").Append(StringEscapeHelper.EscapeString(valueElement)).Append('"');
        }

        call.Append(')');
        return call.ToString();
    }

    /// <summary>
    /// 收集<b>结构匹配</b>的契约方法候选（名称 + <c>static</c> + 形参个数 + 泛型元数）。
    /// </summary>
    /// <remarks>
    /// 返回<b>全部</b>候选而非首个：同名重载并存时（<c>Delimited&lt;T&gt;(string, char)</c> 与
    /// <c>Delimited&lt;T&gt;(PayloadNode?, char)</c>），<c>GetMembers</c> 的顺序不保证契约签名在前，
    /// 取首个会误报 <c>PAYLOAD004</c>（实测）。形参类型由调用方用 <see cref="HasContractParameterTypes"/> 筛选。
    /// </remarks>
    private static ImmutableArray<IMethodSymbol> FindContractMethods(
        INamedTypeSymbol converterType,
        string methodName,
        int parameterCount,
        int typeArgumentCount)
    {
        var builder = ImmutableArray.CreateBuilder<IMethodSymbol>();

        foreach (var member in converterType.GetMembers(methodName))
        {
            if (member is not IMethodSymbol method)
                continue;

            if (!method.IsStatic || method.Parameters.Length != parameterCount)
                continue;

            if (method.TypeParameters.Length != typeArgumentCount)
                continue;

            builder.Add(method);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// 契约方法的首参是否<b>带可空标注</b>（<c>PayloadNode?</c> / <c>string?</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 生成物交给转换器的是<b>可能为 null</b> 的值：辅助方法路径传形参 <c>n</c>（声明类型
    /// <c>PayloadNode?</c>，如元素缺失即为 <see langword="null"/>），<c>Method</c> 路径传 <c>n?.Value</c>
    /// （<c>string?</c>）。若首参被标注为<b>非可空</b>（<c>NullableAnnotation.NotAnnotated</c>），
    /// <c>Nullable=enable</c> 的消费工程会在<b>生成文件</b>里收到 <c>CS8604</c>
    /// （可能传入 null 引用实参；<c>// &lt;auto-generated/&gt;</c> <b>不</b>抑制该告警，
    /// <c>TreatWarningsAsErrors</c> 下即构建失败）⇒ 与 G-ADR-15 同源，改为生成期拦截。
    /// </para>
    /// <para>
    /// <b>未标注（oblivious）仍接受</b>：<c>NullableAnnotation.None</c> 只在 nullable 未启用的声明处出现，
    /// 此时生成文件同样处于 oblivious 上下文、不会有该告警；若强行要求 <c>?</c> 反而会给消费方引入 <c>CS8632</c>。
    /// </para>
    /// </remarks>
    private static bool HasNullableNodeParameter(IMethodSymbol method) =>
        method.Parameters.Length > 0
        && method.Parameters[0].Type.NullableAnnotation != NullableAnnotation.NotAnnotated;

    /// <summary>「首参缺可空标注」的统一诊断文案（辅助方法与 <c>Method</c> 两条路径共用）。</summary>
    private static string NullableFirstParameterDetail(INamedTypeSymbol converterType, string methodName) =>
        "转换器 " + converterType.Name + " 的方法 " + methodName +
        " 首参必须带可空标注（" + PayloadNodeMetadataName + "? 或 string?）：元素缺失时生成物传入的正是 null，" +
        "非可空标注会让 nullable 启用的消费工程在生成文件里收到 CS8604（自动生成头不抑制该告警）。";

    private static IMethodSymbol? TryConstruct(IMethodSymbol method, ImmutableArray<ITypeSymbol> typeArguments)
    {
        if (typeArguments.Length == 0)
            return method;

        try
        {
            return method.Construct(typeArguments.ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 转换器契约方法的<b>形参类型</b>是否与 §5.4 的签名表一致。
    /// </summary>
    /// <remarks>
    /// <c>Text</c>/<c>Number&lt;T&gt;</c>/<c>Flag&lt;T&gt;</c>：<c>(PayloadNode?)</c>；
    /// <c>Delimited&lt;T&gt;</c>：<c>(PayloadNode?, char)</c>；<c>Items&lt;T&gt;</c>：<c>(PayloadNode?, string)</c>；
    /// <c>ItemsWithAttributes&lt;TItem&gt;</c>：<c>(PayloadNode?, string, string, string)</c>。
    /// </remarks>
    private static bool HasContractParameterTypes(IMethodSymbol method, string helperName)
    {
        if (method.Parameters.Length == 0 || !IsPayloadNode(method.Parameters[0].Type))
            return false;

        if (string.Equals(helperName, DelimitedMethodName, StringComparison.Ordinal))
        {
            return method.Parameters.Length == 2
                && method.Parameters[1].Type.SpecialType == SpecialType.System_Char;
        }

        if (string.Equals(helperName, ItemsWithAttributesMethodName, StringComparison.Ordinal))
        {
            return method.Parameters.Length == 4
                && method.Parameters[1].Type.SpecialType == SpecialType.System_String
                && method.Parameters[2].Type.SpecialType == SpecialType.System_String
                && method.Parameters[3].Type.SpecialType == SpecialType.System_String;
        }

        if (string.Equals(helperName, ItemsMethodName, StringComparison.Ordinal))
        {
            return method.Parameters.Length == 2
                && method.Parameters[1].Type.SpecialType == SpecialType.System_String;
        }

        return method.Parameters.Length == 1;
    }

    /// <summary>
    /// 校验「泛型类型实参是否满足契约方法的类型参数约束」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么需要</b>：<see cref="IMethodSymbol.Construct(ITypeSymbol[])"/> <b>不校验约束</b>——
    /// 约束违约时会返回一个「看似可用」的构造方法，生成物里的调用则在消费方编译期报
    /// <c>CS0315</c>/<c>CS0314</c>（错误指向生成文件，可读性极差）。
    /// </para>
    /// <para>
    /// 覆盖范围：<c>struct</c> / <c>class</c> / <c>unmanaged</c> / <c>new()</c> 与
    /// <see cref="ITypeParameterSymbol.ConstraintTypes"/>（逐项按隐式转换判定）。
    /// 约束项本身是类型参数（<c>where T : U</c>）时跳过 —— 该情形在「载荷类非泛型」的前提下不可达。
    /// </para>
    /// </remarks>
    private static bool AreTypeArgumentsSatisfied(
        IMethodSymbol method,
        ImmutableArray<ITypeSymbol> typeArguments,
        Compilation? compilation,
        out string detail)
    {
        detail = string.Empty;
        if (typeArguments.Length == 0)
            return true;

        var count = typeArguments.Length < method.TypeParameters.Length
            ? typeArguments.Length
            : method.TypeParameters.Length;

        for (var i = 0; i < count; i++)
        {
            var typeParameter = method.TypeParameters[i];
            var argument = typeArguments[i];

            if (typeParameter.HasValueTypeConstraint && !IsNonNullableValueType(argument))
            {
                detail = "类型实参 " + argument.ToDisplayString() + " 不满足 struct 约束。";
                return false;
            }

            if (typeParameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
            {
                detail = "类型实参 " + argument.ToDisplayString() + " 不满足 class 约束。";
                return false;
            }

            if (typeParameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
            {
                detail = "类型实参 " + argument.ToDisplayString() + " 不满足 unmanaged 约束。";
                return false;
            }

            if (typeParameter.HasConstructorConstraint && !HasAccessibleParameterlessConstructor(argument))
            {
                detail = "类型实参 " + argument.ToDisplayString() + " 不满足 new() 约束（缺少公共无参构造函数）。";
                return false;
            }

            foreach (var constraintType in typeParameter.ConstraintTypes)
            {
                // 约束项为类型参数（where T : U）时无法用转换判定，且本生成器的类型实参恒为具体类型 ⇒ 跳过。
                if (constraintType.TypeKind == TypeKind.TypeParameter || compilation == null)
                    continue;

                if (!compilation.ClassifyCommonConversion(argument, constraintType).IsImplicit)
                {
                    detail = "类型实参 " + argument.ToDisplayString() +
                             " 不满足约束 " + constraintType.ToDisplayString() + "。";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>是否为「非可空的值类型」（<c>where T : struct</c> 不接受 <c>T?</c>）。</summary>
    private static bool IsNonNullableValueType(ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

    /// <summary>类型实参是否具备 <c>new()</c> 约束所需的条件（具体类 + 公共无参构造函数）。</summary>
    private static bool HasAccessibleParameterlessConstructor(ITypeSymbol type)
    {
        // 值类型（含 T?）恒满足 new()：结构体总有可用的默认构造函数
        // （Roslyn 的 InstanceConstructors 不含隐式结构体默认构造，故必须在此短路，否则误报）。
        if (type.IsValueType)
            return true;

        if (type is not INamedTypeSymbol named || named.IsAbstract)
            return false;

        return HasPublicParameterlessConstructor(named);
    }

    /// <summary>
    /// 校验「转换方法返回值 → 目标属性」是否可隐式转换。
    /// </summary>
    /// <returns><see langword="true"/> 可赋值；<see langword="false"/> 不可赋值；<see langword="null"/> 无法判定（无编译单元，容忍）。</returns>
    private static bool? TryCheckAssignable(Compilation? compilation, ITypeSymbol from, ITypeSymbol to, out string detail)
    {
        detail = string.Empty;
        if (compilation == null)
            return null;

        if (compilation.ClassifyCommonConversion(from, to).IsImplicit)
            return true;

        detail = "（请使返回类型与属性类型一致，或为属性添加 Method 以实现显式转换。）";
        return false;
    }

    private static DiagnosticDescriptor MapFailureToDescriptor(ResolutionFailure failure)
    {
        switch (failure)
        {
            case ResolutionFailure.MethodNotFound:
                return Diagnostics.PayloadConverterMethodNotFound;
            case ResolutionFailure.ReturnTypeMismatch:
                return Diagnostics.PayloadConverterReturnTypeMismatch;
            case ResolutionFailure.DeclarationInvalid:
                return Diagnostics.PayloadFieldDeclarationInvalid;
            default:
                return Diagnostics.PayloadFieldFormatNotInferable;
        }
    }

    private static PayloadContractModel CreateModel(
        string? ns,
        string typeName,
        string typeDisplay,
        string hintName,
        string? contractId,
        string? scopeFallback,
        string? converterDisplay,
        ImmutableArray<PayloadFieldModel> fields,
        List<PayloadDiagnostic> diagnostics,
        Location location)
    {
        var fingerprint = BuildFingerprint(typeDisplay, contractId, scopeFallback, converterDisplay, fields, diagnostics);

        return new PayloadContractModel(
            ns, typeName, typeDisplay, hintName, contractId, scopeFallback, converterDisplay,
            fields, diagnostics.ToImmutableArray(), location, fingerprint);
    }

    /// <summary>
    /// 构建值相等指纹：覆盖渲染产物所依赖的全部输入（含诊断），
    /// 故「指纹相同 ⇒ 渲染结果相同」这一等价性成立（照 <c>InterfaceModel</c> 的既有范式）。
    /// </summary>
    private static string BuildFingerprint(
        string typeDisplay,
        string? contractId,
        string? scopeFallback,
        string? converterDisplay,
        ImmutableArray<PayloadFieldModel> fields,
        List<PayloadDiagnostic> diagnostics)
    {
        var sb = new ValueStringBuilder(512);
        try
        {
            sb.Append(typeDisplay);
            AppendFingerprintPart(ref sb, "Ci:", contractId);
            AppendFingerprintPart(ref sb, "Sf:", scopeFallback);
            AppendFingerprintPart(ref sb, "Cv:", converterDisplay);

            for (var i = 0; i < fields.Length; i++)
            {
                sb.Append("|F:");
                sb.Append(fields[i].Element);
                sb.Append('>');
                sb.Append(fields[i].PropertyName);
                sb.Append('>');
                sb.Append(fields[i].PropertyTypeDisplay);
                sb.Append('>');
                sb.Append(fields[i].ResolvedCall);
            }

            for (var i = 0; i < diagnostics.Count; i++)
            {
                sb.Append("|D:");
                sb.Append(diagnostics[i].Descriptor.Id);
                sb.Append('>');
                sb.Append(diagnostics[i].Message);
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private static void AppendFingerprintPart(ref ValueStringBuilder sb, string prefix, string? value)
    {
        sb.Append('|');
        sb.Append(prefix);
        sb.Append(value ?? string.Empty);
    }

    private static string? DescribeUnsupportedShape(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Class)
            return "当前为 " + typeSymbol.TypeKind + "，仅支持 class。";

        if (typeSymbol.IsRecord)
            return "当前为 record（位置参数属性为 init-only，且 netstandard2.0 消费面无 IsExternalInit）。";

        if (typeSymbol.IsGenericType)
            return "当前为泛型类 " + typeSymbol.ToDisplayString() +
                   "（映射表构造要求 T : class, new()，泛型载荷无法在编译期满足该约束）。";

        if (typeSymbol.ContainingType != null)
            return "当前为嵌套类（嵌套类的 partial 成员需转发容器链，当前不支持）。";

        // —— v2.2 新增：可实例化性（否则生成物必然无法编译，且错误指向生成文件）——
        // 生成物恒以 PayloadFieldMap<T>（约束 T : class, new()）构造映射表，
        // 故 static / abstract / 无公共无参构造函数的类型会让消费方看到 CS0718 / CS0310。
        if (typeSymbol.IsStatic)
            return "当前为 static 类（生成物以 PayloadFieldMap<T>（T : class, new()）构造映射表，静态类型不能用作类型参数 ⇒ CS0718）。";

        if (typeSymbol.IsAbstract)
            return "当前为 abstract 类（生成物以 PayloadFieldMap<T>（T : class, new()）构造映射表，抽象类型无法满足 new() ⇒ CS0310）。";

        if (!HasPublicParameterlessConstructor(typeSymbol))
            return "当前类没有公共无参构造函数（生成物以 PayloadFieldMap<T>（T : class, new()）构造映射表 ⇒ CS0310）。";

        // —— v2.2 新增：继承映射字段（否则基类上的 [PayloadField] 会被静默丢弃）——
        var mappedBase = FindBaseTypeWithMappedFields(typeSymbol);
        if (mappedBase != null)
            return "当前类继承自 " + mappedBase.ToDisplayString() +
                   "，而其上声明了 [PayloadField] 成员 —— 生成器只映射本类声明的属性，" +
                   "继承字段会被静默丢弃（本能力的故障模式正是「静默丢字段」，故直接拒绝）。" +
                   "请把字段声明移到本类，或对派生类改用不标注 [PayloadContract] 的手写映射表。";

        return null;
    }

    /// <summary>类型是否具备可供 <c>new T()</c> 使用的公共无参构造函数（含隐式默认构造）。</summary>
    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol typeSymbol)
    {
        foreach (var constructor in typeSymbol.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 查找继承链上（不含自身）首个声明了 <c>[PayloadField]</c> 成员的类型。
    /// </summary>
    /// <remarks>
    /// <c>[PayloadField]</c> 的 <c>Inherited = false</c> 且 <c>INamedTypeSymbol.GetMembers()</c> 只返回
    /// <b>本类</b>声明的成员 ⇒ 基类上的映射声明不会被收集；若不拦截，消费方会拿到「少几个字段」的映射表
    /// 而编译完全通过（静默丢字段）。故此处 fail-fast。
    /// </remarks>
    private static INamedTypeSymbol? FindBaseTypeWithMappedFields(INamedTypeSymbol typeSymbol)
    {
        for (var baseType = typeSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.SpecialType == SpecialType.System_Object)
                break;

            foreach (var member in baseType.GetMembers())
            {
                if (member is IPropertySymbol property && FindFieldAttribute(property) != null)
                    return baseType;
            }
        }

        return null;
    }

    /// <summary>
    /// 在继承链（不含自身，含元数据类型）上查找指定名称的成员。
    /// </summary>
    /// <remarks>
    /// 用于 <c>PAYLOAD008</c> 的「继承同名成员」判定：生成物是 <c>public static</c> 成员，
    /// 基类（含引用程序集里的基类）已有同名成员时会**隐藏**它 ⇒ 消费方收到 <c>CS0108</c>。
    /// 此处不过滤 <c>DeclaringSyntaxReferences</c>（元数据成员没有语法引用，但同样会触发 CS0108）。
    /// </remarks>
    private static ISymbol? FindInheritedMember(INamedTypeSymbol typeSymbol, string memberName)
    {
        for (var baseType = typeSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.SpecialType == SpecialType.System_Object)
                break;

            foreach (var member in baseType.GetMembers(memberName))
            {
                if (!member.IsImplicitlyDeclared)
                    return member;
            }
        }

        return null;
    }

    private static AttributeData? FindFieldAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (IsAttribute(attribute, FieldAttributeMetadataName))
                return attribute;
        }

        return null;
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName) =>
        string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal);

    /// <summary>
    /// 是否为上游节点类型（按「名称 + 命名空间」判定，<b>不</b>用显示字符串 ——
    /// <c>PayloadNode?</c> 的可空标注是否出现在显示结果里取决于显示格式，字符串比较会漏判）。
    /// </summary>
    private static bool IsPayloadNode(ITypeSymbol type) =>
        string.Equals(type.Name, "PayloadNode", StringComparison.Ordinal)
        && string.Equals(type.ContainingNamespace?.ToDisplayString(), "Mud.HttpUtils.Payloads", StringComparison.Ordinal);

    private static string? ReadConstructorString(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;

    private static string? ReadString(AttributeData? attribute, string propertyName)
    {
        if (attribute == null)
            return null;

        return TryGetNamed(attribute, propertyName, out var constant) && constant.Value is string value && value.Length > 0
            ? value
            : null;
    }

    private static INamedTypeSymbol? ReadType(AttributeData? attribute, string propertyName)
    {
        if (attribute == null)
            return null;

        return TryGetNamed(attribute, propertyName, out var constant) ? constant.Value as INamedTypeSymbol : null;
    }

    private static bool TryGetNamed(AttributeData attribute, string propertyName, out TypedConstant value)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (string.Equals(pair.Key, propertyName, StringComparison.Ordinal))
            {
                value = pair.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Location GetLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
                return location;
        }

        return Location.None;
    }

    /// <summary>
    /// <c>string</c> 是否可安全承载「缺失 ⇒ null」语义。
    /// </summary>
    /// <remarks>
    /// 仅接受<b>可空标注</b>的 <c>string?</c> 与<b>未标注</b>（nullable 未启用 ⇒ 无 CS8601 风险）的 <c>string</c>；
    /// 在 nullable 启用的消费项目中，非空的 <c>string</c> 属性会被判定为「无法推断」（<c>PAYLOAD007</c>），
    /// 从而避免生成代码把 <c>string?</c> 赋给 <c>string</c> 而泄漏 CS8601 到消费方构建
    /// （<c>// &lt;auto-generated/&gt;</c> 头<b>不</b>抑制该编译器告警，消费方开 TreatWarningsAsErrors 即失败）。
    /// </remarks>
    private static bool IsStringScalar(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String
        && type.NullableAnnotation != NullableAnnotation.NotAnnotated;

    private static bool IsNullableScalar(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1)
        {
            underlying = named.TypeArguments[0];
            return true;
        }

        underlying = type;
        return false;
    }

    /// <summary>
    /// 是否可<b>按类型推断</b>为标量转换（<c>Number&lt;T&gt;</c> / <c>Flag&lt;bool&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// <b>刻意排除枚举</b>：枚举的值域语义（如 <c>"1"/"2"</c> 的性别码、未知码的兜底）属消费方领域知识，
    /// 静默走数值解析会把「未知码」变成非法枚举值。故枚举必须显式给出 <c>Method</c>（报 <c>PAYLOAD007</c>），
    /// 而枚举仍可作为 <c>Delimited</c> / <c>Items</c> 的<b>元素</b>类型（元素级由消费方转换器统一处理）。
    /// </remarks>
    private static bool IsInferableScalarValueType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
                return true;
            default:
                return false;
        }
    }

    /// <summary>分隔符串 / 嵌套项的元素类型是否受支持（string / 数值 / 布尔 / 枚举）。</summary>
    private static bool IsSupportedScalar(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String
        || type.TypeKind == TypeKind.Enum
        || IsInferableScalarValueType(type);

    private static ITypeSymbol? GetListElementType(ITypeSymbol type)
    {
        // Roslyn 的 SpecialType 不含 List<T>，故按「原始定义的完全限定显示名」判定
        // （不引入 Compilation 查询，保持本方法可被推断路径直接调用）。
        if (type is INamedTypeSymbol named
            && named.TypeArguments.Length == 1
            && string.Equals(
                named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ListOfTDisplay,
                StringComparison.Ordinal))
        {
            return named.TypeArguments[0];
        }

        return null;
    }
}
