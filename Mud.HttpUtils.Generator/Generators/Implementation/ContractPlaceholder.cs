// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 契约占位实现的共用支持：诊断报告、用户手写实现的探测、unsafe 上下文判定。
/// </summary>
/// <remarks>
/// <para>
/// 占位成员在运行期调用会抛 <see cref="System.NotSupportedException"/>。
/// 因此「发射占位实现」<b>必须</b>有编译期诊断陪跑，否则会把原本的编译期错误
/// （CS0535）降级为运行期故障 —— 这是本缺陷修复过程中刻意避免的安全退化。
/// </para>
/// <para>
/// 调用约定：<b>每次真正发射占位实现时都必须调用 <see cref="ReportUnsupportedMember"/></b>，
/// 即使该成员上已有更具体的诊断（MUD001/MUD002/HTTPCLIENT004/005/008/009/017）。
/// 两害相权取其轻：多报一条同源诊断，优于占位实现完全静默 ——
/// 因为更具体的诊断可能是<b>分析器</b>诊断，而实测「生成器报出 Error + NotConfigurable 诊断时，
/// 本次编译的分析器诊断整体不再呈现」，届时占位实现将没有任何提示。
/// </para>
/// </remarks>
internal static class ContractPlaceholder
{
    /// <summary>
    /// 报告「接口成员未生成实现，已发射占位实现」诊断（HTTPCLIENT024，Error）。
    /// </summary>
    /// <param name="context">生成上下文。</param>
    /// <param name="member">未被实现的接口成员。</param>
    /// <param name="reason">原因描述（用于拼接诊断消息）。</param>
    public static void ReportUnsupportedMember(GeneratorContext context, ISymbol member, string reason)
    {
        var location = member.Locations.FirstOrDefault() ?? context.InterfaceDeclaration.GetLocation();

        context.ProductionContext.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.HttpClientMemberNotGeneratedPlaceholder,
            location,
            context.InterfaceSymbol.Name,
            member.Name,
            reason));
    }

    /// <summary>
    /// 判断接口成员是否<b>已由使用方在 partial 实现类中手写实现</b>。
    /// </summary>
    /// <param name="context">生成上下文。</param>
    /// <param name="member">待发射占位的接口成员。</param>
    /// <returns>已由使用方实现则返回 <c>true</c>（调用方应跳过占位成员）。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么需要</b>：占位实现落地前，生成器对无法处理的成员<b>直接跳过</b>，
    /// 使用方可在同一 partial 实现类中手写补齐（当时唯一可行的写法）。
    /// 占位实现落地后，若仍为该成员发射占位，就会与使用方手写的成员构成重复定义（<c>CS0111</c>）——
    /// 即把「原本可编译的既有代码」变成编译失败。
    /// </para>
    /// <para>
    /// 因此发射占位前先探测实现类是否已有同名同参成员：有则视为使用方自行实现（与 <c>[IgnoreGenerator]</c> 等效），
    /// 生成器让路。若使用方手写的签名与接口不匹配，则回到占位实现落地前的行为（<c>CS0535</c>），不会更糟。
    /// </para>
    /// <para>
    /// 探测范围仅限实现类自身（生成器产出的类），不含 <c>InheritedFrom</c> 基类的成员 ——
    /// 基类实现的场景已由 <c>GeneratorContext.HasInheritedFrom</c> 在调用方先行排除。
    /// </para>
    /// </remarks>
    public static bool IsImplementedByUser(GeneratorContext context, ISymbol member)
    {
        var implementationType = GetImplementationType(context);
        if (implementationType == null)
            return false;

        foreach (var candidate in implementationType.GetMembers(member.Name))
        {
            // 静态与非静态成员分属不同契约面（接口静态抽象成员由实现类的静态成员满足）。
            if (candidate.IsStatic != member.IsStatic)
                continue;

            switch (candidate, member)
            {
                case (IMethodSymbol candidateMethod, IMethodSymbol interfaceMethod):
                    if (candidateMethod.MethodKind != MethodKind.Ordinary)
                        continue;
                    if (candidateMethod.TypeParameters.Length != interfaceMethod.TypeParameters.Length)
                        continue;
                    if (HasSameParameterTypes(candidateMethod.Parameters, interfaceMethod.Parameters))
                        return true;
                    break;

                case (IPropertySymbol candidateProperty, IPropertySymbol interfaceProperty):
                    if (candidateProperty.IsIndexer != interfaceProperty.IsIndexer)
                        continue;
                    if (HasSameParameterTypes(candidateProperty.Parameters, interfaceProperty.Parameters))
                        return true;
                    break;

                case (IEventSymbol, IEventSymbol):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断类型是否含指针/函数指针，因而发射成员时需要 <c>unsafe</c> 修饰符。
    /// </summary>
    /// <param name="type">成员签名中的类型（返回类型或参数类型）。</param>
    /// <returns>需要 unsafe 上下文则返回 <c>true</c>。</returns>
    public static bool RequiresUnsafeContext(ITypeSymbol? type) => type switch
    {
        null => false,
        { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer } => true,
        // 指针数组（如 int*[]）同样需要 unsafe 上下文。
        IArrayTypeSymbol array => RequiresUnsafeContext(array.ElementType),
        _ => false,
    };

    /// <summary>
    /// 解析实现类符号（供探测使用方手写成员）。
    /// </summary>
    private static INamedTypeSymbol? GetImplementationType(GeneratorContext context)
    {
        var arity = context.InterfaceSymbol.TypeParameters.Length;
        var metadataName = arity == 0 ? context.ClassName : $"{context.ClassName}`{arity}";
        var fullName = string.IsNullOrEmpty(context.NamespaceName)
            ? metadataName
            : $"{context.NamespaceName}.{metadataName}";

        return context.Compilation.GetTypeByMetadataName(fullName);
    }

    /// <summary>
    /// 比较两组参数的类型是否逐一相同（含 ref 类别）。
    /// </summary>
    private static bool HasSameParameterTypes(
        ImmutableArray<IParameterSymbol> left,
        ImmutableArray<IParameterSymbol> right)
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].RefKind != right[i].RefKind)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(left[i].Type, right[i].Type))
                return false;
        }

        return true;
    }
}
