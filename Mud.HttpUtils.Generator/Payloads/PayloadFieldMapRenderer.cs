// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text;

namespace Mud.HttpUtils.Models.Payloads;

/// <summary>
/// 载荷字段映射的渲染器（模型 → 生成代码文本）。
/// </summary>
/// <remarks>
/// <para><b>渲染纪律</b>：</para>
/// <list type="number">
///   <item><b>一律 <c>global::</c> 全限定、零 <c>using</c> 依赖</b>：消除「消费方 GlobalUsings / 命名空间差异 ⇒ 生成代码编译失败」；</item>
///   <item><b>不使用 <c>static</c> lambda</b>：<c>static</c> 匿名函数是 C# 9 特性，而
///     <c>netstandard2.0</c> 外部消费工程的<b>默认</b> <c>LangVersion</c> 是 7.3（实测 CS8370）。
///     生成物按「C# 7.3 可编译」基线书写，无捕获性由<b>生成器本身</b>保证
///     （只输出形参 <c>t</c>/<c>n</c> 与转换器静态方法的调用，无任何外层变量引用）；</item>
///   <item>固定 <c>\n</c> 换行、4 空格缩进（FIX-16：跨平台字节一致）；</item>
///   <item>生成成员带 XML 文档注释：<c>// &lt;auto-generated/&gt;</c> 头<b>不</b>抑制 CS1591，
///     消费方启用 <c>GenerateDocumentationFile</c> 时会因公共成员缺注释告警；</item>
///   <item>仅生成 <c>partial</c> 成员（类名与属性由消费方手写，公共 API 的 XML 文档亦由消费方负责）。</item>
/// </list>
/// </remarks>
internal static class PayloadFieldMapRenderer
{
    /// <summary>零 using：全部类型以 <c>global::</c> 全限定书写。</summary>
    private static readonly string[] NoUsingNamespaces = Array.Empty<string>();

    private const string PayloadsNamespace = "global::Mud.HttpUtils.Payloads.";

    /// <summary>
    /// 把标识符渲染为「可安全写进生成文件」的形式：C# 保留字（<c>class</c> / <c>event</c> / <c>string</c> …）
    /// 需补 <c>@</c> 前缀。
    /// </summary>
    /// <remarks>
    /// <b>为什么必需</b>：消费方可以把类名 / 属性名写成 <c>@class</c> / <c>@event</c>（XML 载荷里
    /// <c>event</c>、<c>default</c>、<c>class</c> 都是常见元素名），而 <c>ISymbol.Name</c> <b>不含</b> <c>@</c>
    /// （取到的是 <c>"event"</c>）⇒ 直接拼接会产出 <c>t.event = …</c> 或 <c>partial class class</c>，
    /// 让**生成文件**报一堆语法错误（实测 <c>CS1001</c> / <c>CS1519</c> / <c>CS1513</c> / <c>CS0260</c>，
    /// 且 <c>[PayloadContract]</c> 类会连带报 CS0260「缺少 partial 修饰符」这种指向错误位置的失败）。
    /// </remarks>
    private static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;

    /// <summary>
    /// 渲染生成代码。
    /// </summary>
    /// <param name="model">已解析的契约模型（保证无 Error 诊断）。</param>
    /// <param name="config">配置值快照（决定 <c>#nullable enable</c> 与生成标记的发射）。</param>
    /// <returns>生成文件全文。</returns>
    internal static string Render(PayloadContractModel model, GeneratorConfigSnapshot config)
    {
        var sb = new StringBuilder(1024);
        TransitiveCodeGenerator.GenerateFileHeader(sb, NoUsingNamespaces, config.NullableEnable);
        sb.Append('\n');

        var indent = model.Namespace == null ? string.Empty : "    ";
        if (model.Namespace != null)
        {
            // 注意：Namespace 来自 INamespaceSymbol.ToDisplayString()，**已**自带保留字 `@` 转义
            // （如 `@class.Sub`），此处**不得**再调 EscapeIdentifier（会得到 `@@class`）。
            sb.Append("namespace ").Append(model.Namespace).Append('\n');
            sb.Append("{\n");
        }

        sb.Append(indent).Append("partial class ").Append(EscapeIdentifier(model.TypeName)).Append('\n');
        sb.Append(indent).Append("{\n");

        var memberIndent = indent + "    ";
        sb.Append(memberIndent).Append("/// <summary>载荷字段映射契约（由 [PayloadContract] 声明生成");
        if (model.ScopeFallback != null)
            sb.Append("；作用域回退节点 ").Append(model.ScopeFallback);

        sb.Append("）。</summary>\n");

        if (config.EmitMarkers)
        {
            sb.Append(memberIndent).Append(GeneratedCodeConsts.CompilerGeneratedAttribute).Append('\n');
            sb.Append(memberIndent).Append(GeneratedCodeConsts.HttpGeneratedCodeAttribute).Append('\n');
        }

        sb.Append(memberIndent)
            .Append("public static ")
            .Append(PayloadsNamespace).Append("IPayloadFieldMap<")
            .Append(model.TypeDisplay)
            .Append("> PayloadFieldMap\n");

        sb.Append(memberIndent).Append("{\n");
        sb.Append(memberIndent).Append("    get\n");
        sb.Append(memberIndent).Append("    {\n");

        var bodyIndent = memberIndent + "        ";
        sb.Append(bodyIndent).Append("return ").Append(PayloadsNamespace).Append("PayloadFieldMap<")
            .Append(model.TypeDisplay).Append(">.Create(\"")
            .Append(StringEscapeHelper.EscapeString(model.ContractId ?? model.TypeName)).Append('"');

        if (model.ScopeFallback != null)
        {
            sb.Append(", \"").Append(StringEscapeHelper.EscapeString(model.ScopeFallback)).Append('"');
        }

        if (model.Fields.Length == 0)
        {
            sb.Append(");\n");
        }
        else
        {
            sb.Append(")\n");
            for (var i = 0; i < model.Fields.Length; i++)
            {
                var field = model.Fields[i];
                sb.Append(bodyIndent).Append("    ")
                    .Append(".Map(\"").Append(StringEscapeHelper.EscapeString(field.Element))
                    .Append("\", (t, n) => { t.").Append(EscapeIdentifier(field.PropertyName))
                    .Append(" = ").Append(field.ResolvedCall).Append("; })")
                    .Append(i == model.Fields.Length - 1 ? ";\n" : "\n");
            }
        }

        sb.Append(memberIndent).Append("    }\n");
        sb.Append(memberIndent).Append("}\n");
        sb.Append(indent).Append("}\n");

        if (model.Namespace != null)
        {
            sb.Append("}\n");
        }

        return sb.ToString();
    }
}
