// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Threading;
using Mud.HttpUtils.ToolSurface.Emit;
using Mud.HttpUtils.ToolSurface.Extraction;
using Mud.HttpUtils.ToolSurface.Schema;

namespace Mud.HttpUtils.ToolSurface;

/// <summary>
/// 工具面 Schema 生成引擎入口（设计文档 §5.3）：把 <c>[SdkToolProfile]</c> 剖面驱动的
/// 工具特性接口编译为「模型可调用工具」的全部编译期产物。
/// </summary>
/// <remarks>
/// <para><b>管道（L1 → L2 → L4，结构照搬上游 <c>FeishuToolSchemaGenerator</c>）</b>：</para>
/// <list type="number">
/// <item>L1 抽取（<see cref="ToolSurfaceScanner"/> / <see cref="ToolHandlerScanner"/>）：
/// 接口符号 + <c>Source</c> 源挂钩 → 值模型；</item>
/// <item>L2 渲染（<see cref="SchemaWriter"/> + Emit 家族）：条目 → 描述符 JSON 与全部产物；</item>
/// <item>L4 校验（<see cref="DescriptorValidator"/>）：结构/类型/跨字段一致 → 动态槽位诊断。</item>
/// </list>
/// <para>
/// <b>剖面扇出（§5.3 v2.1）</b>：一个编译可含 0/1/N 个 <c>ISdkToolProfile</c> 实现。
/// 剖面集合经 <c>CompilationProvider</c> 解析并按 <see cref="SdkToolProfileModel.Name"/> 排序；
/// 每条输出路径对每个 profile 各跑一遍，hintName 由 <c>{ProductPrefix}</c> 天然隔离。
/// 工具接口按「哪个剖面的 <c>ToolAttributeName/Namespace</c> 命中」归属唯一剖面，互不串味。
/// </para>
/// <para>
/// <b>空剖面短路（§5.3/§7.2 硬性前提）</b>：纯 HTTP 消费方（无 <c>ISdkToolProfile</c> 实现）下，
/// 候选谓词是 O(1) 语法判定，语义变换在 <see cref="ProfileDiscovery.ResolveProfiles"/> 一次
/// <c>GetTypeByMetadataName</c> 查找后即返回 <see langword="null"/>——不进入任何扫描/发射逻辑，
/// 0 工具诊断、0 工具 AddSource。注：<c>IIncrementalGenerator.Initialize</c> 无法在注册前拿到编译，
/// 故「不注册任何 SyntaxProvider」的表述落地为「注册最廉价的语法谓词 + 语义变换零成本短路」；
/// §10 测试门禁断言的「语法树步进不进 ToolSurface（扫描/发射逻辑）」由此满足。
/// </para>
/// <para>
/// <b>故障隔离</b>：每条注册路径经 <see cref="Guard.WrapUnchecked{T}"/> 兜底；多剖面循环内
/// 再按 profile 逐个 try/catch 上报 <c>{prefix}026</c>——单剖面异常不阻断其余剖面。
/// SDKT002（缺槽剖面）路径无法拼 <c>{prefix}026</c>（缺 <c>DiagnosticPrefix</c> 正是其主题），
/// 由 <see cref="Guard.WrapUnchecked{T}"/> 退化为日志兜底。
/// </para>
/// </remarks>
[Generator]
public sealed class ToolSurfaceSourceGenerator : TransitiveCodeGenerator
{
    /// <inheritdoc />
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Extraction ↔ Schema 层接缝注册（幂等赋值；工厂签名携带 profile，注册点与剖面无关——扇出安全）。
        ToolSurfaceSchemaResolver.Factory = static (compilation, profile) => new TypeSchemaResolver(compilation, profile);

        var profiles = context.CompilationProvider
            .Select(static (compilation, _) => ProfileDiscovery.ResolveProfiles(compilation));
        var assemblyName = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName);
        var additionalTexts = context.AdditionalTextsProvider.Collect();

        // ── L1：工具特性扫描（语法候选 → 剖面归属 → 值模型；空剖面在变换内短路）──
        var scanned = context.SyntaxProvider
            .CreateSyntaxProvider(IsToolCandidate, ScanTool)
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        // ── L1：执行器绑定扫描（handler 特性 → 工具名 + 执行器构造签名）──
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(IsHandlerCandidate, ScanHandler)
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        // ── 诊断出口（与模型同源一次扫描；PendingDiagnostic 自带所属剖面的描述符，混合流无需 factory）──
        context.RegisterSourceOutput(
            scanned.SelectMany(static (result, _) => result.Scan.Diagnostics).Collect(),
            Guard.WrapUnchecked<ImmutableArray<PendingDiagnostic>>("ToolSurfaceDiagnostics(工具扫描)", ReportPendingDiagnostics));

        context.RegisterSourceOutput(
            handlers.SelectMany(static (result, _) => result.Scan.Diagnostics).Collect(),
            Guard.WrapUnchecked<ImmutableArray<PendingDiagnostic>>("ToolSurfaceDiagnostics(执行器扫描)", ReportPendingDiagnostics));

        // ── SDKT002：必填槽缺失的剖面（静态描述符上报，并从一切扇出中排除）──
        context.RegisterSourceOutput(
            profiles,
            Guard.WrapUnchecked<ImmutableArray<SdkToolProfileModel>>("ToolSurfaceProfiles(槽位守卫)", ReportInvalidProfiles));

        // ── 按剖面分组的模型/绑定流（元组元素全为值相等类型，增量缓存安全）──
        var modelsByProfile = scanned
            .Where(static result => result.Scan.Model is not null)
            .Select(static (result, _) => (result.Profile, Model: result.Scan.Model!))
            .Collect();

        var handlersByProfile = handlers
            .Select(static (result, _) => (result.Profile, result.Scan))
            .Collect();

        // ── L2/L4 + golden：Schema 常量 / 名字契约表 / 类型化契约表 ──
        // 注：WrapUnchecked 的 T 必须显式指定——方法组实参不参与泛型推断（CS0411）。
        context.RegisterSourceOutput(
            modelsByProfile.Combine(assemblyName).Combine(profiles).Combine(additionalTexts),
            Guard.WrapUnchecked<(((ImmutableArray<(SdkToolProfileModel, ToolSchemaModel)>, string?), ImmutableArray<SdkToolProfileModel>), ImmutableArray<AdditionalText>)>(
                "ToolSurfaceSchemas", EmitToolSurface));

        // ── L2：参数解包器（{P}Args/{Tool}Args.g.cs，每类型一文件）──
        context.RegisterSourceOutput(
            modelsByProfile.Combine(assemblyName).Combine(profiles),
            Guard.WrapUnchecked<((ImmutableArray<(SdkToolProfileModel, ToolSchemaModel)>, string?), ImmutableArray<SdkToolProfileModel>)>(
                "ToolSurfaceArgs", EmitToolArgs));

        // ── L2：域注册器 + DI 装配 ──
        context.RegisterSourceOutput(
            modelsByProfile.Combine(handlersByProfile).Combine(assemblyName).Combine(profiles),
            Guard.WrapUnchecked<(((ImmutableArray<(SdkToolProfileModel, ToolSchemaModel)>, ImmutableArray<(SdkToolProfileModel, ScannedHandler)>), string?), ImmutableArray<SdkToolProfileModel>)>(
                "ToolSurfaceDomainRegistrars", EmitRegistrars));

        // ── 域级 guidance 资产（{GuidanceDirectory}{domain}.md → {P}Guidance.g.cs）──
        context.RegisterSourceOutput(
            assemblyName.Combine(profiles).Combine(additionalTexts),
            Guard.WrapUnchecked<((string?, ImmutableArray<SdkToolProfileModel>), ImmutableArray<AdditionalText>)>(
                "ToolSurfaceGuidance", EmitGuidance));

        // ── Tier R：能力目录（开关 = build_property.{CapabilityCatalogPropertyName}，剖面槽动态键）──
        // 动态键经拼接读取：组件侧 props 不预注册消费方属性（§5.3 v2.1 消费方注册义务）。
        var catalogFlags = profiles
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) =>
            {
                var flags = ImmutableArray.CreateBuilder<bool>();
                foreach (var profile in pair.Left)
                {
                    var key = profile.CapabilityCatalogPropertyName;
                    flags.Add(key.Length > 0
                        && pair.Right.GlobalOptions.TryGetValue("build_property." + key, out var value)
                        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
                }

                return flags.ToImmutable();
            });

        // 全部关闭时投影为常量 null（照搬上游 R4-9 增量纪律）——增量缓存据此判定「无变化」，
        // 下游不会随每次编辑重跑；任一剖面开启时才让 Compilation 进入输入。
        var catalogCompilation = context.CompilationProvider
            .Combine(catalogFlags)
            .Select(static (pair, _) => pair.Right.Any(static enabled => enabled) ? pair.Left : null);

        context.RegisterSourceOutput(
            catalogCompilation.Combine(modelsByProfile).Combine(profiles).Combine(catalogFlags),
            Guard.WrapUnchecked<(((Compilation?, ImmutableArray<(SdkToolProfileModel, ToolSchemaModel)>), ImmutableArray<SdkToolProfileModel>), ImmutableArray<bool>)>(
                "ToolSurfaceCapabilityCatalog", EmitCapabilityCatalog));
    }

    // ────────── 候选谓词与扫描变换 ──────────

    private static bool IsToolCandidate(SyntaxNode node, CancellationToken _)
        => node is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 };

    private static bool IsHandlerCandidate(SyntaxNode node, CancellationToken _)
        => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };

    /// <summary>
    /// 工具接口扫描：解析剖面 → 找到「工具特性命中」的唯一剖面 → 扫描。
    /// 空剖面 / 无命中 / 缺槽剖面（SDKT002 已接管）一律返回 <see langword="null"/>。
    /// </summary>
    private static ProfiledScan? ScanTool(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            var compilation = context.SemanticModel.Compilation;
            var profiles = ProfileDiscovery.ResolveProfiles(compilation);
            if (profiles.IsDefaultOrEmpty)
            {
                // 空剖面短路：纯 HTTP 消费方的典型路径（一次 GetTypeByMetadataName 查找即返回）。
                return null;
            }

            foreach (var profile in profiles)
            {
                if (profile.MissingRequiredSlots.Length > 0)
                {
                    continue;
                }

                if (Extractors.GetToolAttribute(symbol, profile) is null)
                {
                    continue;
                }

                return new ProfiledScan(profile, ToolSurfaceScanner.Scan(symbol, compilation, profile, ToolSurfaceDiagnostics.For(profile)));
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GeneratorDebugLogger.LogError(nameof(ScanTool), ex);
            return null;
        }
    }

    /// <summary>执行器方法扫描（按 handler 特性槽归属剖面；同 ScanTool 的短路纪律）。</summary>
    private static ProfiledHandler? ScanHandler(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        try
        {
            var compilation = context.SemanticModel.Compilation;
            var profiles = ProfileDiscovery.ResolveProfiles(compilation);
            if (profiles.IsDefaultOrEmpty)
            {
                return null;
            }

            foreach (var profile in profiles)
            {
                if (profile.MissingRequiredSlots.Length > 0)
                {
                    continue;
                }

                var scan = ToolHandlerScanner.Scan(context, profile, ToolSurfaceDiagnostics.For(profile), cancellationToken);
                if (scan is not null)
                {
                    return new ProfiledHandler(profile, scan);
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GeneratorDebugLogger.LogError(nameof(ScanHandler), ex);
            return null;
        }
    }

    // ────────── 诊断出口 ──────────

    private static void ReportPendingDiagnostics(SourceProductionContext context, ImmutableArray<PendingDiagnostic> diagnostics)
    {
        foreach (var pending in diagnostics)
        {
            context.ReportDiagnostic(Diagnostic.Create(pending.Descriptor, Location.None, pending.Arguments));
        }
    }

    private static void ReportInvalidProfiles(SourceProductionContext context, ImmutableArray<SdkToolProfileModel> profiles)
    {
        foreach (var profile in profiles)
        {
            if (profile.MissingRequiredSlots.Length == 0)
            {
                continue;
            }

            var display = profile.Name.Length == 0 ? "(未命名剖面)" : profile.Name;
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.SdkToolProfileMissingRequiredSlots,
                Location.None,
                display,
                string.Join(", ", profile.MissingRequiredSlots)));
        }
    }

    // ────────── 输出路径（每条 = Guard.WrapUnchecked 外层 + ForEachProfile 逐剖面 026 隔离）──────────

    private static void EmitToolSurface(
        SourceProductionContext context,
        (((ImmutableArray<(SdkToolProfileModel Profile, ToolSchemaModel Model)> Models, string? AssemblyName) WithName,
          ImmutableArray<SdkToolProfileModel> Profiles) Grouped,
         ImmutableArray<AdditionalText> Texts) input)
        => ForEachProfile(
            context,
            "ToolSurfaceSchemas",
            input.Grouped.Profiles,
            (input.Grouped.WithName.Models, input.Grouped.WithName.AssemblyName, input.Texts),
            static (ctx, profile, factory, data) =>
            {
                var models = ModelsFor(data.Models, profile);
                if (models.IsEmpty)
                {
                    return;
                }

                // L4 校验：结构 / 类型一致 / 跨字段一致（含工具名唯一 → 槽位 003、派生常量名冲突 → 槽位 027）。
                foreach (var result in DescriptorValidator.ValidateAll(
                             models.Select(static m => m.Entry), profile, factory, SchemaEmitter.BuildNameConstant))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(result.Descriptor, Location.None, result.Arguments));
                }

                ReportOutputSchemaTruncations(ctx, models, factory);

                var goldenText = ReadGolden(data.Texts, profile, ctx.CancellationToken);
                var drift = SchemaEmitter.Emit(ctx, models, profile, data.AssemblyName, goldenText);
                if (drift is not null)
                {
                    // 槽位 014 上报点：描述符静默漂移（golden 快照不一致）。
                    factory.Report(ctx, ToolSurfaceDiagnostics.SlotGoldenDrift, null, drift, profile.GoldenUpdatePropertyName);
                }
            });

    private static void EmitToolArgs(
        SourceProductionContext context,
        ((ImmutableArray<(SdkToolProfileModel Profile, ToolSchemaModel Model)> Models, string? AssemblyName) WithName,
         ImmutableArray<SdkToolProfileModel> Profiles) input)
        => ForEachProfile(
            context,
            "ToolSurfaceArgs",
            input.Profiles,
            (input.WithName.Models, input.WithName.AssemblyName),
            static (ctx, profile, factory, data) =>
                ToolArgsEmitter.Emit(ctx, ModelsFor(data.Models, profile), profile, data.AssemblyName, factory));

    private static void EmitRegistrars(
        SourceProductionContext context,
        (((ImmutableArray<(SdkToolProfileModel Profile, ToolSchemaModel Model)> Models,
           ImmutableArray<(SdkToolProfileModel Profile, ScannedHandler Handler)> Handlers) WithBindings,
          string? AssemblyName) Outer,
         ImmutableArray<SdkToolProfileModel> Profiles) input)
        => ForEachProfile(
            context,
            "ToolSurfaceDomainRegistrars",
            input.Profiles,
            (input.Outer.WithBindings.Models, input.Outer.WithBindings.Handlers, input.Outer.AssemblyName),
            static (ctx, profile, factory, data) =>
            {
                var handlers = ImmutableArray.CreateBuilder<ScannedHandler>();
                foreach (var (p, handler) in data.Handlers)
                {
                    if (p.Equals(profile))
                    {
                        handlers.Add(handler);
                    }
                }

                ToolRegistrarEmitter.Emit(ctx, ModelsFor(data.Models, profile), profile, data.AssemblyName, handlers.ToImmutable(), factory);
            });

    private static void EmitGuidance(
        SourceProductionContext context,
        ((string? AssemblyName, ImmutableArray<SdkToolProfileModel> Profiles) WithName,
         ImmutableArray<AdditionalText> Texts) input)
        => ForEachProfile(
            context,
            "ToolSurfaceGuidance",
            input.WithName.Profiles,
            (input.WithName.AssemblyName, input.Texts),
            static (ctx, profile, _, data) =>
            {
                var files = data.Texts
                    .Where(text => GuidanceEmitter.IsGuidanceFile(text.Path, profile))
                    .ToImmutableArray();
                GuidanceEmitter.Emit(ctx, profile, data.AssemblyName, files, ctx.CancellationToken);
            });

    private static void EmitCapabilityCatalog(
        SourceProductionContext context,
        (((Compilation? Compilation, ImmutableArray<(SdkToolProfileModel Profile, ToolSchemaModel Model)> Models) WithModels,
          ImmutableArray<SdkToolProfileModel> Profiles) Grouped,
         ImmutableArray<bool> Flags) input)
    {
        // 全剖面关闭时上游投影为 null，此处直接返回（增量缓存已判定「无变化」）。
        if (input.Grouped.WithModels.Compilation is not { } compilation)
        {
            return;
        }

        var profiles = input.Grouped.Profiles;
        for (var i = 0; i < profiles.Length; i++)
        {
            var profile = profiles[i];
            if (profile.MissingRequiredSlots.Length > 0)
            {
                continue;
            }

            var factory = ToolSurfaceDiagnostics.For(profile);
            try
            {
                if (!input.Flags[i])
                {
                    // 该剖面开关关闭：不产出（与 flags 的「常量 null」投影同语义）。
                    continue;
                }

                CapabilityCatalogEmitter.Emit(
                    context,
                    compilation,
                    ModelsFor(input.Grouped.WithModels.Models, profile),
                    profile,
                    catalogEnabled: true,
                    factory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                factory.Report(
                    context,
                    ToolSurfaceDiagnostics.SlotGeneratorInternalError,
                    null,
                    profile.Name + ":ToolSurfaceCapabilityCatalog",
                    ex.GetType().Name,
                    ex.Message);
            }
        }
    }

    // ────────── 管线辅助 ──────────

    /// <summary>逐剖面执行发射回调：单剖面异常上报该剖面的 <c>{prefix}026</c>，不阻断其余剖面。</summary>
    private static void ForEachProfile<T>(
        SourceProductionContext context,
        string product,
        ImmutableArray<SdkToolProfileModel> profiles,
        T data,
        Action<SourceProductionContext, SdkToolProfileModel, ToolSurfaceDiagnostics.Factory, T> action)
    {
        foreach (var profile in profiles)
        {
            if (profile.MissingRequiredSlots.Length > 0)
            {
                continue;
            }

            var factory = ToolSurfaceDiagnostics.For(profile);
            try
            {
                action(context, profile, factory, data);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                factory.Report(
                    context,
                    ToolSurfaceDiagnostics.SlotGeneratorInternalError,
                    null,
                    profile.Name + ":" + product,
                    ex.GetType().Name,
                    ex.Message);
            }
        }
    }

    private static ImmutableArray<ToolSchemaModel> ModelsFor(
        ImmutableArray<(SdkToolProfileModel Profile, ToolSchemaModel Model)> all,
        SdkToolProfileModel profile)
    {
        var builder = ImmutableArray.CreateBuilder<ToolSchemaModel>();
        foreach (var (p, model) in all)
        {
            if (p.Equals(profile))
            {
                builder.Add(model);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>读取该剖面的 golden 快照（<see cref="SchemaEmitter.IsGoldenFile"/> 全名匹配，§5.2-3）。</summary>
    private static string? ReadGolden(ImmutableArray<AdditionalText> texts, SdkToolProfileModel profile, CancellationToken cancellationToken)
    {
        foreach (var text in texts)
        {
            if (SchemaEmitter.IsGoldenFile(text.Path, profile) && text.GetText(cancellationToken) is { } sourceText)
            {
                return sourceText.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// 汇总输出 Schema 的截断情况并上报<b>单条</b>槽位 009（照搬上游 AT-B14 聚合纪律：
    /// 逐处上报会被 SDK 中大量深层 DTO 淹没；截断本身不是缺陷，但必须构建期可见）。
    /// </summary>
    private static void ReportOutputSchemaTruncations(
        SourceProductionContext context,
        ImmutableArray<ToolSchemaModel> models,
        ToolSurfaceDiagnostics.Factory factory)
    {
        var affected = new List<string>();
        foreach (var model in models)
        {
            if (model.Entry.OutputSchemaTruncations.Count == 0)
            {
                continue;
            }

            affected.Add($"{model.Entry.ToolName}(×{model.Entry.OutputSchemaTruncations.Count})");
        }

        if (affected.Count == 0)
        {
            return;
        }

        const int MaxListedTools = 5;
        var listed = affected.Count <= MaxListedTools
            ? string.Join(" | ", affected)
            : string.Join(" | ", affected.Take(MaxListedTools)) + $" | …另有 {affected.Count - MaxListedTools} 个工具";

        factory.Report(
            context,
            ToolSurfaceDiagnostics.SlotOutputSchemaTruncation,
            null,
            $"{affected.Count} 个工具的输出 Schema 被截断（深度超限或循环引用）",
            listed);
    }

    /// <summary>扫描结果 + 归属剖面（值相等：剖面与扫描产物均已实现 <see cref="IEquatable{T}"/>）。</summary>
    private sealed class ProfiledScan : IEquatable<ProfiledScan?>
    {
        public ProfiledScan(SdkToolProfileModel profile, ScannedTool scan)
        {
            Profile = profile;
            Scan = scan;
        }

        public SdkToolProfileModel Profile { get; }
        public ScannedTool Scan { get; }

        public bool Equals(ProfiledScan? other)
            => other is not null && Profile.Equals(other.Profile) && Scan.Equals(other.Scan);

        public override bool Equals(object? obj) => Equals(obj as ProfiledScan);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Profile.GetHashCode() * 31) + Scan.GetHashCode();
            }
        }
    }

    /// <summary>执行器扫描结果 + 归属剖面（同 <see cref="ProfiledScan"/> 纪律）。</summary>
    private sealed class ProfiledHandler : IEquatable<ProfiledHandler?>
    {
        public ProfiledHandler(SdkToolProfileModel profile, ScannedHandler scan)
        {
            Profile = profile;
            Scan = scan;
        }

        public SdkToolProfileModel Profile { get; }
        public ScannedHandler Scan { get; }

        public bool Equals(ProfiledHandler? other)
            => other is not null && Profile.Equals(other.Profile) && Scan.Equals(other.Scan);

        public override bool Equals(object? obj) => Equals(obj as ProfiledHandler);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Profile.GetHashCode() * 31) + Scan.GetHashCode();
            }
        }
    }
}
