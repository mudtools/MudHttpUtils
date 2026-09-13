// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// <see cref="ITokenManager"/> 生命周期诊断分析器（P3.5 / C5，TK-24）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITokenManager"/> 的实现类内部维护令牌缓存与并发锁（如 <see cref="TokenManagerBase"/>），
/// 必须注册为 <b>Singleton</b> 以全局共享缓存、避免并发安全机制失效与冗余刷新。
/// </para>
/// <para>
/// 本分析器检查 <c>IServiceCollection</c> 的 DI 注册调用（<c>AddScoped</c>/<c>AddTransient</c>/
/// <c>TryAddScoped</c>/<c>TryAddTransient</c>），当被注册的类型为 <see cref="ITokenManager"/> 或其实现时，
/// 发出 <b>MUD004</b> Warning。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TokenManagerLifetimeAnalyzer : DiagnosticAnalyzer
{
    private const string ITokenManagerFullName = "Mud.HttpUtils.ITokenManager";

    // IServiceCollection 上定义 AddScoped/AddTransient 扩展方法的类型名（用于精确命中 DI 注册而非任意同名方法）。
    private static readonly HashSet<string> DiExtensionContainingTypes = new(StringComparer.Ordinal)
    {
        "ServiceCollectionServiceExtensions",
        "PublicServiceCollectionServiceExtensions",
        "ServiceCollectionDescriptorExtensions",
    };

    private static readonly HashSet<string> NonSingletonMethods = new(StringComparer.Ordinal)
    {
        "AddScoped", "AddTransient", "TryAddScoped", "TryAddTransient",
    };

    public static readonly DiagnosticDescriptor MUD004_NonSingletonTokenManager = new(
        id: "MUD004",
        title: "ITokenManager 实现应注册为 Singleton",
        messageFormat: "令牌管理器类型 '{0}' 应在 IServiceCollection 中注册为 Singleton（当前使用 '{1}'）。“ITokenManager”的实现内部维护令牌缓存与并发锁，Scoped/Transient 注册会使每个请求持有独立缓存实例，导致并发安全机制失效与重复刷新令牌。请改用 AddSingleton/TryAddSingleton。",
        category: "Mud.HttpUtils.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ITokenManager 实现应注册为 Singleton，以避免并发安全机制失效与冗余令牌刷新。");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MUD004_NonSingletonTokenManager);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return;

        // 仅命中 IServiceCollection 的 DI 生命周期扩展方法。
        if (!IsDiLifetimeMethod(methodSymbol))
            return;

        // 提取本次注册的服务类型与实现类型。
        // 若为 null 表示无法静态解析（例如基于非泛型工厂且无法推断返回类型），跳过以避免误报。
        if (!TryResolveRegisteredTypes(context, methodSymbol, invocation, out var serviceType, out var implementationType))
            return;

        // 需要判定"被注册的（服务或实现）类型是 ITokenManager 或实现了 ITokenManager"。
        var iTokenManager = context.Compilation.GetTypeByMetadataName(ITokenManagerFullName);
        if (iTokenManager == null)
            return;

        var registered = serviceType ?? implementationType;
        if (registered == null)
            return;

        // 服务类型为具体实现且隐式实现 ITokenManager，或实现类型（区别于服务类型时）实现了 ITokenManager。
        var twoPartRegistration = serviceType != null && implementationType != null
                                  && !SymbolEqualityComparer.Default.Equals(serviceType, implementationType);
        var isTokenManager = IsImplementationOf(registered, iTokenManager)
                             || (twoPartRegistration && IsImplementationOf(implementationType!, iTokenManager));

        if (!isTokenManager)
            return;

        var reportedTypeName = (implementationType ?? serviceType!)?.ToDisplayString()
                               ?? registered.ToDisplayString();
        context.ReportDiagnostic(Diagnostic.Create(
            MUD004_NonSingletonTokenManager,
            invocation.GetLocation(),
            reportedTypeName,
            methodSymbol.Name));
    }

    private static bool IsDiLifetimeMethod(IMethodSymbol method)
    {
        // 仅处理非 Singleton 注册方法（AddScoped/AddTransient/TryAddScoped/TryAddTransient）。
        if (!NonSingletonMethods.Contains(method.Name))
            return false;

        return IsServiceCollectionExtension(method);
    }

    private static bool IsServiceCollectionExtension(IMethodSymbol method)
    {
        // 校验该方法定义在知名的 IServiceCollection 扩展类型上。
        var containingTypeName = method.ContainingType?.Name;
        if (containingTypeName == null || !DiExtensionContainingTypes.Contains(containingTypeName))
            return false;

        // 扩展方法符号可能是"已化简"（reduced）形式：调用时的 this 接收者为 ReceiverType，
        // 不包含在 Parameters 中；原始未化简方法位于 ReducedFrom.ReducedFrom 或自身 Parameters。
        if (method.IsExtensionMethod)
        {
            // 已化简形式：接收类型即 IServiceCollection。
            if (string.Equals(method.ReceiverType?.Name, "IServiceCollection", StringComparison.Ordinal))
                return true;

            // 未化简形式：第一个参数必须是 IServiceCollection。
            return method.Parameters.Length > 0
                   && string.Equals(method.Parameters[0].Type?.Name, "IServiceCollection", StringComparison.Ordinal);
        }

        // 非扩展方法：第一个参数必须是 IServiceCollection，确保确实是 DI 生命周期注册而非同名普通方法。
        if (method.Parameters.Length == 0)
            return false;
        return string.Equals(method.Parameters[0].Type?.Name, "IServiceCollection", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 DI 注册调用中解析服务类型与实现类型。
    /// 返回 <c>true</c> 表示成功解析出至少一个可判定的类型。
    /// </summary>
    private static bool TryResolveRegisteredTypes(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        out ITypeSymbol? serviceType,
        out ITypeSymbol? implementationType)
    {
        serviceType = null;
        implementationType = null;

        // 泛型形式：
        //   AddScoped<TService>()                       → 单类型参数，服务=实现=TService
        //   AddScoped<TService, TImpl>()                → 双类型参数，服务+实现
        if (method.TypeArguments.Length >= 1)
        {
            var firstType = method.TypeArguments[0];
            if (method.TypeArguments.Length >= 2)
            {
                var secondType = method.TypeArguments[1];
                serviceType = firstType;
                implementationType = secondType;
            }
            else
            {
                // 单类型参数：既作为服务也作为实现（自注册）。
                serviceType = firstType;
                implementationType = firstType;
            }
            return true;
        }

        // 非泛型形式：AddScoped(sp => ...) 或 AddScoped((Func<IServiceProvider,TInterface>)x => ...)。
        // 从第一个参数表达式的类型推断被注册类型。
        var arg = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (arg == null)
            return false;

        var argType = context.SemanticModel.GetTypeInfo(arg.Expression, context.CancellationToken).ConvertedType
                      ?? context.SemanticModel.GetTypeInfo(arg.Expression, context.CancellationToken).Type;
        if (argType == null)
            return false;

        // 若参数是一个 Func<IServiceProvider, T> 或 factory，提取其返回类型。
        if (argType is INamedTypeSymbol namedArg && namedArg.DelegateInvokeMethod != null)
        {
            var returnType = namedArg.DelegateInvokeMethod.ReturnType;
            // 仅当返回类型可判定为非 object/void 时才采用（避免误报）。
            if (returnType != null && returnType.SpecialType != SpecialType.System_Object && returnType.SpecialType != SpecialType.System_Void)
            {
                serviceType = returnType;
                implementationType = returnType;
                return true;
            }
        }

        // 直接传入类型（少见），保守起见以参数静态类型作为服务类型。
        serviceType = argType;
        implementationType = argType;
        return true;
    }

    private static bool IsImplementationOf(ITypeSymbol type, INamedTypeSymbol target)
    {
        // 自身即是目标接口。
        if (SymbolEqualityComparer.Default.Equals(type, target))
            return true;

        // AllInterfaces 涵盖继承链上所有基类实现的接口，无需自行遍历基类。
        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, target));
    }
}