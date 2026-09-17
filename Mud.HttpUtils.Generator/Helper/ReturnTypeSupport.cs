// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 接口方法返回类型形态的判定与解析 —— 生成器与分析器（MUD002）的<b>单一事实来源</b>。
/// </summary>
/// <remarks>
/// <para>
/// 本类同时回答两个问题，二者必须与生成器的实际能力一致：
/// <list type="number">
///   <item>该返回类型是否受支持（<see cref="IsSupported"/>）—— 决定生成器发射真实实现还是占位实现；</item>
///   <item>若是 <c>IAsyncEnumerable&lt;T&gt;</c>，元素类型 <c>T</c> 是什么（<see cref="GetAsyncEnumerableElementType"/>）
///         —— 决定生成器走流式分支。</item>
/// </list>
/// </para>
/// <para>
/// <b>为什么必须共享</b>：生成器与分析器对「返回类型是否受支持」必须给出完全相同的答案，
/// 否则两个方向都会出问题：
/// <list type="bullet">
///   <item>生成器判「不支持」而分析器判「支持」→ 生成器发射占位实现且<b>无任何诊断陪跑</b>，
///         编译期错误被静默降级为运行期 <c>NotSupportedException</c>（最危险的方向）；</item>
///   <item>生成器判「支持」而分析器判「不支持」→ 误报（Error 级）阻断本可正常编译的代码。</item>
/// </list>
/// 故本判定只实现一处，由 <c>MethodAnalyzer</c>（供生成器使用）与
/// <c>MudHttpInterfaceAnalyzer</c>（MUD002）共同引用。
/// </para>
/// <para>
/// <b>判定口径 = 生成器的实际能力（经实测校准）</b>：生成器只为<b>异步形态</b>的返回类型发射 <c>async</c> 关键字
/// （见 <c>MethodGenerator</c>：<c>asyncKeyword = methodInfo.IsAsyncMethod || methodInfo.IsAsyncEnumerableReturn</c>），
/// 而方法体一律包含 <c>await</c>。因此只有下列形态可编译：
/// <list type="bullet">
///   <item><c>Task</c> / <c>Task&lt;T&gt;</c> / <c>ValueTask</c> / <c>ValueTask&lt;T&gt;</c>；
///         <c>T</c> 可为任意响应体类型（含 <c>byte[]</c>/<c>Stream</c>/<c>HttpResponseMessage</c>/<c>Response&lt;T&gt;</c>/自定义类型，
///         它们由生成器在方法体内部分支处理）；</item>
///   <item><c>IAsyncEnumerable&lt;T&gt;</c>（流式返回）。</item>
/// </list>
/// </para>
/// <para>
/// <b>实现上复用生成器的既有判定</b>：<c>Task</c>/<c>ValueTask</c> 直接调用 <see cref="TypeSymbolHelper.IsAsyncType"/>
/// （与 <c>MethodAnalyzer</c> 填充 <c>IsAsyncMethod</c> 所用函数相同）；
/// <c>IAsyncEnumerable&lt;T&gt;</c> 按其 <b>符号名 + 元数</b>判定，与 <c>MethodAnalyzer</c> 填充
/// <c>IsAsyncEnumerableReturn</c> 的结论一致。故本判定与 <c>MethodGenerator</c> 的 <c>asyncKeyword</c> 条件等价，不会漂移。
/// </para>
/// <para>
/// <b>刻意不按命名空间 / 类型显示串判定</b>：在 <c>ForAttributeWithMetadataName</c> 的 transform 上下文中，
/// 符号的命名空间信息可能退化为全局命名空间，同一类型在「生成器 transform 上下文」与「分析器 SyntaxNode 上下文」
/// 下会得到两种不同的显示串。若按显示串判定，就会出现「生成器判不支持、分析器判支持」的分叉 —— 即本类开头描述的最危险方向。
/// 历史上 <c>IAsyncEnumerable</c> 正是踩了这个坑：用「<c>^IAsyncEnumerable&lt;…&gt;$</c>」正则匹配
/// <see cref="TypeSymbolHelper.GetTypeFullName"/> 返回的限定名（<c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c>），
/// 永不匹配 → 流式分支成为死代码，生成结果退化为「非 async 方法体内含 await」→ <c>CS4032</c>。
/// </para>
/// <para>
/// <b>裸（未被 async 形态包裹）的返回类型一律不支持</b>：<c>byte[]</c>、<c>Stream</c>、<c>HttpResponseMessage</c>、
/// <c>Response&lt;T&gt;</c>、<c>string</c>、<c>void</c> 等会产出「非 async 方法体含 await」的不可编译代码
/// （实测 <c>CS4032</c>）。其中 <c>void</c> 尤其隐蔽：非泛型 <c>Task</c> 的内部返回类型会被解析为 <c>void</c>，
/// 生成器据此走「void 分支」，但该分支同样只在方法为 <c>async</c> 时成立。
/// </para>
/// <para>
/// <b>历史缺陷（本判定修正的正是它）</b>：MUD002 原白名单把裸 <c>byte[]</c>/<c>Stream</c>/<c>HttpResponseMessage</c>
/// 视为合法，而生成器对它们会产出 CS4032 —— 属「分析器沉默 + 生成不可编译代码」的漏报方向，已由本判定收紧。
/// </para>
/// <para>
/// <b>新增受支持返回类型时的检查清单（必须同步，缺一即为"三方不一致"）</b>：
/// <list type="number">
///   <item>本类（判定源）—— 扩展 <see cref="IsSupported"/> / 新增解析方法；</item>
///   <item><c>MethodGenerator.GenerateExecutorCall</c> —— 为新形态新增对应的生成分支，
///         并确认 <c>asyncKeyword</c> 条件覆盖该形态（否则产出 CS4032）；</item>
///   <item><c>Diagnostics.MudMethodInvalidReturnType</c>（MUD002）—— 消息文案中的"受支持形态"清单；</item>
///   <item><c>Mud.HttpUtils.Generator/README.md</c> 的 MUD002 行 —— 形态清单段
///         （由 <c>DocumentationContractTests</c> 的机器可解析标记守卫）；</item>
///   <item><see cref="SupportedReturnShapes"/> —— 供上述 README 守卫比对的能力口径清单；</item>
///   <item>阶段三表驱动测试（<c>Tests/Mud.HttpUtils.Generator.Tests/ReturnTypeCapabilityContractTests.cs</c>）
///         —— 新增"生成器是否发射占位 ⟺ MUD002 是否报告"的样本行。</item>
/// </list>
/// </para>
/// </remarks>
internal static class ReturnTypeSupport
{
    /// <summary>
    /// 受支持的返回类型形态清单（<b>能力口径</b>，供 README 守卫比对，不是判定逻辑）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsSupported"/> 是同一能力的两处表达：本清单用于与
    /// <c>README.md</c> 中 MUD002 行的机器可解析标记
    /// （<c>&lt;!-- supported-return-shapes: ... --&gt;</c>）做一致性守卫，
    /// 判定逻辑仍是 <see cref="IsSupported"/>。二者不一致会让"文档承诺的能力"与"实际能力"漂移，
    /// 正是 <c>IAsyncEnumerable</c> 死分支缺陷（README/分析器都认为支持、生成器实际不支持）的成因。
    /// </remarks>
    public static readonly string[] SupportedReturnShapes =
    [
        "Task", "Task<T>", "ValueTask", "ValueTask<T>", "IAsyncEnumerable<T>",
    ];

    /// <summary>元素类型显示格式：全限定 + 特殊类型关键字，保证生成的类型名在任意上下文中可用。</summary>
    private static readonly SymbolDisplayFormat ElementTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// 判断接口方法的返回类型是否为生成器支持的类型形态。
    /// </summary>
    /// <param name="returnType">接口方法的返回类型符号（非方法体的内部响应类型）。</param>
    /// <returns>受支持返回 <c>true</c>。</returns>
    public static bool IsSupported(ITypeSymbol returnType) =>
        // Task / Task<T> / ValueTask / ValueTask<T>：复用生成器判定（名称 + 元数）。
        TypeSymbolHelper.IsAsyncType(returnType) ||
        // IAsyncEnumerable<T>（流式返回）。
        IsAsyncEnumerable(returnType);

    /// <summary>
    /// 判断返回类型是否为 <c>IAsyncEnumerable&lt;T&gt;</c>。
    /// </summary>
    public static bool IsAsyncEnumerable(ITypeSymbol returnType) =>
        returnType is INamedTypeSymbol { Name: "IAsyncEnumerable" } named && named.TypeArguments.Length == 1;

    /// <summary>
    /// 提取 <c>IAsyncEnumerable&lt;T&gt;</c> 的元素类型 <c>T</c> 的显示名（非该方法参数时返回 <c>null</c>）。
    /// </summary>
    public static string? GetAsyncEnumerableElementType(ITypeSymbol returnType) =>
        IsAsyncEnumerable(returnType)
            ? ((INamedTypeSymbol)returnType).TypeArguments[0].ToDisplayString(ElementTypeFormat)
            : null;
}
