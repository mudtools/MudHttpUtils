// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 接口契约补全生成器：为「生成器未实现」的接口成员发射占位实现，保证实现类满足接口契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>缺陷背景</b>：生成器对无法处理的成员此前直接跳过（不发射成员），生成的实现类因而缺失接口成员
/// → 编译报 <c>CS0535</c>。该错误不说明根因，且会掩盖同编译中真正有价值的诊断。
/// </para>
/// <para>
/// <b>职责划分</b>：方法成员的补全由 <see cref="MethodGenerator"/> 就地完成（它掌握方法级跳过判定，
/// 无需跨生成器共享「已发射成员」状态）；本生成器负责非方法成员：
/// 未标注 <c>[Query]</c>/<c>[Path]</c>/<c>[Header]</c> 的属性（这类属性不被
/// <c>ConstructorGenerator.GenerateInterfaceProperties</c> 覆盖）、索引器与事件。
/// </para>
/// <para>
/// <b>不补位的情形</b>：
/// <list type="bullet">
///   <item>标注 <c>[IgnoreGenerator]</c> 的成员 —— 语义为使用方自行实现，补位会与其实现冲突；</item>
///   <item><c>static virtual</c> 成员 —— 接口已提供默认实现，补位会以抛异常的成员覆盖该默认行为
///         （<c>static abstract</c> 无默认实现，属实现类契约，仍需补位）；</item>
///   <item>使用方已在 partial 实现类中手写的同名同参成员 —— 补位会构成重复定义（CS0111），
///         见 <see cref="ContractPlaceholder.IsImplementedByUser"/>；</item>
///   <item>使用基类（<c>[HttpClientApi(InheritedFrom = ...)]</c>）时基接口上的成员 —— 由基类负责实现。</item>
/// </list>
/// </para>
/// <para>
/// <b>签名保真</b>：补位成员须与接口签名一致，故需按需补齐 <c>static</c>/<c>unsafe</c> 修饰符与
/// <c>ref</c>/<c>ref readonly</c> 返回形态（<c>ref</c> 返回须用语句体访问器，<c>throw</c> 表达式不可用作 ref 返回值）。
/// </para>
/// </remarks>
internal class InterfaceContractCompletionGenerator : ICodeFragmentGenerator
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>占位实现的原因描述（用于诊断消息）。</summary>
    private const string UnsupportedMemberReason =
        "生成器不支持该成员形态（受支持的接口成员为 HTTP 方法与 [Query]/[Path]/[Header] 属性）";

    /// <inheritdoc />
    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        // 已被 ConstructorGenerator 发射的属性（[Query]/[Path]/[Header]）不得重复发射。
        var handledPropertyNames = new HashSet<string>(
            context.InterfaceProperties.Select(p => p.Name), StringComparer.Ordinal);

        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var emitted = false;

        foreach (var member in EnumerateContractMembers(context))
        {
            if (!visited.Add(member))
                continue;

            // [IgnoreGenerator] 成员由使用方自行实现，补位会与其实现冲突。
            if (GeneratorAttributeFilters.HasIgnoreGenerator(member))
                continue;

            // 静态成员：仅 static abstract（无默认实现）属于实现类的契约面；
            // static virtual 有默认实现，发射占位反而会以抛异常的成员覆盖该默认行为。
            if (member.IsStatic && !member.IsAbstract)
                continue;

            // 其它片段生成器（AppContext / 令牌辅助等）已按模式无条件发射同名成员时不得重复发射
            // （重复会产生 CS0111/CS0102；显式实现还会抢占接口分派，破坏既有运行期行为）。
            // 该登记表只含实例成员，故仅对实例成员生效。
            if (!member.IsStatic && context.ProvidedMemberNames.Contains(member.Name))
                continue;

            // 使用方已在 partial 实现类中手写该成员（占位实现落地前的可用写法）时让路，
            // 否则构成重复定义（CS0111）。
            if (ContractPlaceholder.IsImplementedByUser(context, member))
                continue;

            switch (member)
            {
                case IPropertySymbol property:
                    if (handledPropertyNames.Contains(property.Name))
                        continue;
                    if (!EmitPropertyStub(codeBuilder, property))
                        continue;
                    ContractPlaceholder.ReportUnsupportedMember(context, property, UnsupportedMemberReason);
                    emitted = true;
                    break;

                case IEventSymbol @event:
                    if (!EmitEventStub(codeBuilder, @event))
                        continue;
                    ContractPlaceholder.ReportUnsupportedMember(context, @event, UnsupportedMemberReason);
                    emitted = true;
                    break;
            }
        }

        if (emitted)
        {
            codeBuilder.AppendLine();
        }
    }

    /// <summary>
    /// 枚举实现类需要满足契约的成员（当前接口 + 无基类时的全部基接口）。
    /// </summary>
    private static IEnumerable<ISymbol> EnumerateContractMembers(GeneratorContext context)
    {
        foreach (var member in context.InterfaceSymbol.GetMembers())
            yield return member;

        // 使用基类（InheritedFrom）时，基接口成员的实现由基类负责，不补位
        // （与 GeneratorContext.AllMethods 的选择规则保持一致）。
        if (context.HasInheritedFrom)
            yield break;

        foreach (var baseInterface in context.InterfaceSymbol.AllInterfaces)
        {
            foreach (var member in baseInterface.GetMembers())
                yield return member;
        }
    }

    /// <summary>
    /// 发射属性/索引器占位实现。返回值表示是否实际发射了成员。
    /// </summary>
    /// <remarks>
    /// 需按需补齐修饰符以保证生成的成员与接口签名一致（否则编译器仍报 CS0535）：
    /// <c>static</c>（接口静态抽象成员）、<c>unsafe</c>（指针类型）、<c>ref</c>/<c>ref readonly</c> 返回。
    /// <c>ref</c> 返回的属性无法用 <c>throw</c> 表达式实现，须改用语句体访问器。
    /// </remarks>
    private static bool EmitPropertyStub(StringBuilder codeBuilder, IPropertySymbol property)
    {
        var getter = property.GetMethod;
        var setter = property.SetMethod;
        if (getter == null && setter == null)
            return false;

        var propertyType = property.Type.ToDisplayString(TypeFormat);
        var returnsByRefReadonly = property.ReturnsByRefReadonly;
        var returnsByRef = property.ReturnsByRef || returnsByRefReadonly;
        var refPrefix = returnsByRefReadonly ? "ref readonly " : returnsByRef ? "ref " : string.Empty;
        var modifiers = BuildModifiers(property.IsStatic, needsUnsafe: ContractPlaceholder.RequiresUnsafeContext(property.Type));
        var declaration = property.IsIndexer
            ? $"public {modifiers}{refPrefix}{propertyType} this[{ParameterSignatureBuilder.Build(property.Parameters)}]"
            : $"public {modifiers}{refPrefix}{propertyType} {property.Name}";

        WriteMemberDocumentation(codeBuilder, "属性");
        codeBuilder.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
        codeBuilder.AppendLine($"        {declaration}");
        codeBuilder.AppendLine("        {");
        if (getter != null)
            WritePropertyAccessor(codeBuilder, "get", "属性", property.Name, returnsByRef);
        if (setter != null)
        {
            var accessor = setter.IsInitOnly ? "init" : "set";
            WritePropertyAccessor(codeBuilder, accessor, "属性", property.Name, returnsByRef: false);
        }
        codeBuilder.AppendLine("        }");

        return true;
    }

    /// <summary>
    /// 写入属性访问器：普通情况用 <c>throw</c> 表达式，<c>ref</c> 返回的 getter 用语句体。
    /// </summary>
    private static void WritePropertyAccessor(
        StringBuilder codeBuilder, string accessor, string memberKind, string memberName, bool returnsByRef)
    {
        var message = BuildMessage(memberKind, memberName);
        if (returnsByRef)
        {
            codeBuilder.AppendLine($"            {accessor}");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                throw new global::System.NotSupportedException(\"{message}\");");
            codeBuilder.AppendLine("            }");
            return;
        }

        codeBuilder.AppendLine($"            {accessor} => throw new global::System.NotSupportedException(\"{message}\");");
    }

    /// <summary>
    /// 发射事件占位实现。返回值表示是否实际发射了成员。
    /// </summary>
    private static bool EmitEventStub(StringBuilder codeBuilder, IEventSymbol @event)
    {
        var delegateType = @event.Type.ToDisplayString(TypeFormat);
        var modifiers = BuildModifiers(
            @event.IsStatic,
            needsUnsafe: ContractPlaceholder.RequiresUnsafeContext(@event.Type));

        WriteMemberDocumentation(codeBuilder, "事件");
        codeBuilder.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
        codeBuilder.AppendLine($"        public {modifiers}event {delegateType} {@event.Name}");
        codeBuilder.AppendLine("        {");
        // 使用自定义访问器（而非字段式事件）以避免 CS0067「事件从未使用」告警。
        codeBuilder.AppendLine($"            add => throw new global::System.NotSupportedException(\"{BuildMessage("事件", @event.Name)}\");");
        codeBuilder.AppendLine($"            remove => throw new global::System.NotSupportedException(\"{BuildMessage("事件", @event.Name)}\");");
        codeBuilder.AppendLine("        }");

        return true;
    }

    /// <summary>
    /// 构造成员修饰符前缀（<c>static</c> / <c>unsafe</c>）。
    /// </summary>
    private static string BuildModifiers(bool isStatic, bool needsUnsafe)
        => (isStatic ? "static " : string.Empty) + (needsUnsafe ? "unsafe " : string.Empty);

    private static void WriteMemberDocumentation(StringBuilder codeBuilder, string memberKind)
    {
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// <inheritdoc />");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <remarks>");
        codeBuilder.AppendLine($"        /// 占位实现：生成器不支持该{memberKind}形态（受支持的接口成员为 HTTP 方法与");
        codeBuilder.AppendLine("        /// [Query]/[Path]/[Header] 属性）。保留此成员以保证实现类满足接口契约");
        codeBuilder.AppendLine("        /// （避免 CS0535 掩盖真正的编译诊断）。若确需自行实现，请标注 [IgnoreGenerator]。");
        codeBuilder.AppendLine("        /// </remarks>");
    }

    private static string BuildMessage(string memberKind, string memberName)
        => $"接口{memberKind} '{memberName}' 未生成实现：生成器不支持该{memberKind}形态，"
         + "请改用受支持的接口成员形态，或标注 [IgnoreGenerator] 自行实现。";
}
