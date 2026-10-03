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
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// <c>PayloadFieldMapGenerator</c> 诊断矩阵（<c>PAYLOAD002</c>~<c>PAYLOAD009</c>）。
/// </summary>
/// <remarks>
/// 每个用例同时断言「有 Error 即不产出源文件」——这是本生成器的核心口径：
/// 宁可不生成（消费方立刻看到缺成员），也不产出缺字段的映射表（静默丢字段）。
/// </remarks>
public class PayloadFieldMapDiagnosticTests
{
    private static (ImmutableArray<Diagnostic> Diagnostics, int GeneratedTreeCount) RunGenerator(string payloadDeclaration)
    {
        var compilation = CSharpCompilation.Create(
            "PayloadDiagnostics",
            new[] { CSharpSyntaxTree.ParseText(PayloadTestData.Source(payloadDeclaration)) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new PayloadFieldMapGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (diagnostics, output.SyntaxTrees.Count() - 1);
    }

    private static void AssertSingleError(string payloadDeclaration, string expectedId)
    {
        var (diagnostics, generatedTreeCount) = RunGenerator(payloadDeclaration);

        diagnostics.Should().ContainSingle(
            "用例应恰好报告一条诊断，实际：" + string.Join(" | ", diagnostics.Select(d => d.Id + " " + d.GetMessage())));
        diagnostics[0].Id.Should().Be(expectedId);
        diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Error);
        generatedTreeCount.Should().Be(0, "存在 Error 诊断时不得产出生成文件（避免半成品）");
    }

    /// <summary>
    /// 正向对照通用断言（v2.4）：零诊断 + 恰 1 个生成文件 + <b>生成物零新增错误/告警</b>，并返回生成文本。
    /// </summary>
    /// <param name="payloadDeclaration">载荷声明块（拼在 <see cref="PayloadTestData.Source"/> 之后）。</param>
    /// <param name="nullableProperty"><c>build_property.Nullable</c> 的值。</param>
    /// <returns>生成文件全文（供调用方正向钉住关键渲染片段，防用例空转）。</returns>
    /// <remarks>
    /// <b>为什么必须有这条与形态无关的断言</b>：v2.2/v2.3 的教训是「只看诊断 ID」会漏掉
    /// 「零诊断 + 静默产出无法编译或带告警的代码」这一类缺陷（v2.4 的 <c>@</c> 关键字标识符、
    /// 继承同名成员的 <c>CS0108</c> 就是这样被捕获的）。
    /// </remarks>
    private static string AssertProducesCompilableOutput(string payloadDeclaration, string? nullableProperty = "enable")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "PayloadDiagnostics",
            new[] { CSharpSyntaxTree.ParseText(PayloadTestData.Source(payloadDeclaration), parseOptions) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var buildProperties = new Dictionary<string, string>();
        if (nullableProperty != null)
            buildProperties["build_property.Nullable"] = nullableProperty;

        var driver = CSharpGeneratorDriver.Create(
            new[] { new PayloadFieldMapGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        diagnostics.Should().BeEmpty(
            "正向对照用例的输入必须合法，实际：" + string.Join(" | ", diagnostics.Select(d => d.Id + " " + d.GetMessage())));
        (output.SyntaxTrees.Count() - 1).Should().Be(1, "合法载荷必须产出恰一个映射表文件");

        var baseline = compilation.GetDiagnostics().Select(d => d.Id + "|" + d.GetMessage()).ToHashSet();
        var introduced = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error)
            .Where(d => !baseline.Contains(d.Id + "|" + d.GetMessage()))
            .Select(d => d.Id + ": " + d.GetMessage())
            .ToArray();

        introduced.Should().BeEmpty(
            "生成物不得把任何错误/告警泄漏到消费方构建（CS8604 / CS0108 / CS1001 / CS0260 …）；新增：" +
            string.Join(" | ", introduced));

        return string.Join("\n", output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()));
    }

    [Fact]
    public void ValidPayload_ProducesNoDiagnostics_AndOneFile()
    {
        var (diagnostics, generatedTreeCount) = RunGenerator(PayloadTestData.BasicPayload);

        diagnostics.Should().BeEmpty();
        generatedTreeCount.Should().Be(1);
    }

    [Fact]
    public void ZeroFieldPayload_WithoutConverter_IsValid()
    {
        var (diagnostics, generatedTreeCount) = RunGenerator(PayloadTestData.ZeroFieldPayload);

        diagnostics.Should().BeEmpty();
        generatedTreeCount.Should().Be(1);
    }

    /// <summary>
    /// 正向对照（v2.2）：隐式默认构造函数必须被识别为「公共无参构造」（否则 <c>new()</c> 形态守卫会全线误报）。
    /// </summary>
    [Fact]
    public void ClassWithImplicitDefaultConstructor_IsValid()
    {
        var (diagnostics, generatedTreeCount) = RunGenerator("""
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class ImplicitCtorPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
            """);

        diagnostics.Should().BeEmpty();
        generatedTreeCount.Should().Be(1);
    }

    /// <summary>
    /// 正向对照（v2.2）：基类<b>不含</b> <c>[PayloadField]</c> 时派生载荷合法（只有「继承映射字段」才是形态问题）。
    /// </summary>
    [Fact]
    public void DerivedPayloadWithUnmappedBase_IsValid()
    {
        var (diagnostics, generatedTreeCount) = RunGenerator("""
            namespace PayloadTests
            {
                public class PlainBase
                {
                    public string? Unmapped { get; set; }
                }

                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class DerivedPlainPayload : PlainBase
                {
                    [PayloadField("Name")] public string? Name { get; set; }
                }
            }
            """);

        diagnostics.Should().BeEmpty();
        generatedTreeCount.Should().Be(1);
    }

    /// <summary>
    /// 正向对照（v2.2）：转换器的类型参数约束<b>被满足</b>时不得误报（<c>struct</c> / <c>new()</c> 两种）。
    /// </summary>
    [Fact]
    public void GenericHelperWithSatisfiedConstraint_IsValid()
    {
        var (diagnostics, generatedTreeCount) = RunGenerator("""
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(ConstrainedOkConverter))]
                public sealed partial class ConstraintOkPayload
                {
                    [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();

                    [PayloadField("Flags")] public List<int> Flags { get; set; } = new List<int>();
                }

                public static class ConstrainedOkConverter
                {
                    public static List<T> Delimited<T>(PayloadNode? node, char separator) where T : struct
                        => new List<T>();

                    public static List<T> Items<T>(PayloadNode? node, string itemName) where T : new()
                        => new List<T>();
                }
            }
            """);

        diagnostics.Should().BeEmpty();
        generatedTreeCount.Should().Be(1);
    }

    [Fact]
    public void NonPartialPayload_ReportsPayload002() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed class NotPartialPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD002");

    [Fact]
    public void FieldWithoutConverter_ReportsPayload003() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract]
            public sealed partial class NoConverterPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD003");

    [Fact]
    public void UnknownMethod_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class UnknownMethodPayload
            {
                [PayloadField("UserID", Method = "Nope")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD004");

    [Fact]
    public void InstanceMethod_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(NonStaticConverter))]
            public sealed partial class InstanceMethodPayload
            {
                [PayloadField("UserID", Method = "InstanceConverter")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD004");

    [Fact]
    public void MissingInferredHelper_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(OnlyTextConverter))]
            public sealed partial class MissingHelperPayload
            {
                [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();
            }

            public static class OnlyTextConverter
            {
                public static string? Text(PayloadNode? node) => node?.Value;
            }
        }
        """, "PAYLOAD004");

    [Fact]
    public void ReturnTypeMismatch_ReportsPayload005() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class ReturnMismatchPayload
            {
                [PayloadField("Ids", Method = "NotAList")] public List<long> Ids { get; set; } = new List<long>();
            }
        }
        """, "PAYLOAD005");

    [Fact]
    public void DuplicateElement_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class DuplicateElementPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }

                [PayloadField("UserID")] public string? OtherUserId { get; set; }
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void SeparatorWithItemName_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class ConflictingShapePayload
            {
                [PayloadField("Names", Separator = '|', ItemName = "Name")]
                public List<string> Names { get; set; } = new List<string>();
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void ItemsWithoutItemName_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class ItemsWithoutItemNamePayload
            {
                [PayloadField("Names", Format = PayloadFieldFormat.Items)]
                public List<string> Names { get; set; } = new List<string>();
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void StaticProperty_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class StaticPropertyPayload
            {
                [PayloadField("UserID")] public static string? UserId { get; set; }
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void InitOnlyProperty_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class InitOnlyPayload
            {
                [PayloadField("UserID")] public string? UserId { get; init; }
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void EnumWithoutMethod_ReportsPayload007() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class EnumPayload
            {
                [PayloadField("Gender")] public WechatUserGender? Gender { get; set; }
            }
        }
        """, "PAYLOAD007");

    [Fact]
    public void NonNullableValueType_ReportsPayload007() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NonNullableValuePayload
            {
                [PayloadField("Count")] public int Count { get; set; }
            }
        }
        """, "PAYLOAD007");

    [Fact]
    public void NonNullableStringInNullableContext_ReportsPayload007() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NonNullableStringPayload
            {
                [PayloadField("UserID")] public string UserId { get; set; } = string.Empty;
            }
        }
        """, "PAYLOAD007");

    [Fact]
    public void HandwrittenMapConflict_ReportsPayload008() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class ConflictPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }

                public static Mud.HttpUtils.Payloads.IPayloadFieldMap<ConflictPayload> PayloadFieldMap { get; } = null!;
            }
        }
        """, "PAYLOAD008");

    [Fact]
    public void GenericPayload_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class GenericPayload<T>
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD009");

    [Fact]
    public void NestedPayload_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            public sealed class Container
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class NestedPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
        }
        """, "PAYLOAD009");

    [Fact]
    public void RecordPayload_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial record RecordPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD009");

    // —— v2.2 新增：可实例化性（否则生成物必然无法编译，且错误指向生成文件）——

    [Fact]
    public void StaticClassPayload_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract]
            public static partial class StaticPayload
            {
            }
        }
        """, "PAYLOAD009");

    [Fact]
    public void AbstractClassPayload_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract]
            public abstract partial class AbstractPayload
            {
            }
        }
        """, "PAYLOAD009");

    [Fact]
    public void ClassWithoutPublicParameterlessConstructor_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NoCtorPayload
            {
                private NoCtorPayload() { }

                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD009");

    [Fact]
    public void ClassWithOnlyParameterizedConstructor_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class ArgsCtorPayload
            {
                public ArgsCtorPayload(int seed) { }

                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD009");

    // —— v2.2 新增：继承映射字段（否则基类的 [PayloadField] 被静默丢弃）——

    [Fact]
    public void InheritedFieldDeclaration_ReportsPayload009() => AssertSingleError("""
        namespace PayloadTests
        {
            public class BasePayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class DerivedPayload : BasePayload
            {
                [PayloadField("Name")] public string? Name { get; set; }
            }
        }
        """, "PAYLOAD009");

    // —— v2.2 新增：属性形态（索引器 / 显式接口实现无法以 t.<名> = … 引用）——

    [Fact]
    public void IndexerProperty_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class IndexerPayload
            {
                [PayloadField("UserID")] public string? this[int index] { get { return null; } set { } }
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void ExplicitInterfaceImplementationProperty_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            public interface IHasUserId { string? UserId { get; set; } }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class ExplicitImplPayload : IHasUserId
            {
                [PayloadField("UserID")] string? IHasUserId.UserId { get; set; }
            }
        }
        """, "PAYLOAD006");

    // —— v2.2 新增：声明静默失效类（Format 取值无效 / Separator 落空）——

    [Fact]
    public void InvalidFormatEnumValue_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class InvalidFormatPayload
            {
                [PayloadField("UserID", Format = (PayloadFieldFormat)99)] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void ExplicitSeparatorOnNonDelimitedShape_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class SeparatorOnScalarPayload
            {
                [PayloadField("UserID", Separator = '|')] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD006");

    [Fact]
    public void ExplicitSeparatorWithItemsShape_ReportsPayload006() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class SeparatorWithItemsPayload
            {
                [PayloadField("Names", ItemName = "Name", Separator = '|')]
                public List<string> Names { get; set; } = new List<string>();
            }
        }
        """, "PAYLOAD006");

    // —— v2.2 新增：转换器契约方法的形参类型与泛型约束（否则生成物报 CS1503/CS0315）——

    [Fact]
    public void HelperWithNonNodeFirstParameter_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(BadFirstParameterConverter))]
            public sealed partial class BadFirstParameterPayload
            {
                [PayloadField("Count")] public int? Count { get; set; }
            }

            public static class BadFirstParameterConverter
            {
                public static T? Number<T>(string text) where T : struct => null;
            }
        }
        """, "PAYLOAD004");

    [Fact]
    public void HelperWithWrongSeparatorParameter_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(BadSeparatorConverter))]
            public sealed partial class BadSeparatorPayload
            {
                [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();
            }

            public static class BadSeparatorConverter
            {
                public static List<T> Delimited<T>(PayloadNode? node, string separator) => new List<T>();
            }
        }
        """, "PAYLOAD004");

    [Fact]
    public void HelperWithUnsatisfiedGenericConstraint_ReportsPayload007() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(ConstrainedConverter))]
            public sealed partial class UnsatisfiedConstraintPayload
            {
                [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();
            }

            public interface IShape { }

            public static class ConstrainedConverter
            {
                public static List<T> Delimited<T>(PayloadNode? node, char separator) where T : IShape
                    => new List<T>();
            }
        }
        """, "PAYLOAD007");

    /// <summary>
    /// 形态守卫（v2.3 修正）：<c>[PayloadContract]</c> 的 <c>AttributeUsage</c> 为 <c>AttributeTargets.Class</c>，
    /// 标在 <c>struct</c> 上时 csc 会报 <c>CS0592</c>，但<b>特性仍会被语义模型绑定</b> ⇒ 生成器照样命中该节点，
    /// 由形态守卫报 <c>PAYLOAD009</c> 且不产出文件。
    /// </summary>
    /// <remarks>
    /// 原断言「零诊断 + 零产出」建立在「csc 拒绝后生成器不可达」的误判上：实测生成器确实命中并报
    /// <c>PAYLOAD009</c>，故此处按真实行为钉住（并同时覆盖 <c>interface</c> 目标）。
    /// </remarks>
    [Fact]
    public void StructTarget_ReportsPayload009_AndProducesNothing() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public partial struct StructPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD009");

    /// <summary>形态守卫（v2.3 修正）：<c>interface</c> 目标同理，生成器报 <c>PAYLOAD009</c> 且不产出文件。</summary>
    [Fact]
    public void InterfaceTarget_ReportsPayload009_AndProducesNothing() => AssertSingleError("""
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public partial interface InterfacePayload
            {
                [PayloadField("UserID")] string? UserId { get; set; }
            }
        }
        """, "PAYLOAD009");

    // —— v2.4 新增：契约方法首参的可空标注（否则生成物报 CS8604）——

    /// <summary>
    /// 契约方法首参<b>非可空</b>标注 ⇒ <c>PAYLOAD004</c>。
    /// </summary>
    /// <remarks>
    /// 修复前实测：零诊断 + 1 个生成文件 + 生成文件报
    /// <c>CS8604</c>「"string StrictConverter.Text(PayloadNode node)" 中的形参 "node" 可能传入 null 引用实参」。
    /// 非可空标注与契约语义冲突（元素缺失时传入的就是 null），故按 G-ADR-15 生成期拦截。
    /// </remarks>
    [Fact]
    public void HelperWithNonNullableNodeParameter_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            public static class StrictConverter
            {
                public static string Text(PayloadNode node) => node.Value;
            }

            [PayloadContract(Converter = typeof(StrictConverter))]
            public sealed partial class StrictPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD004");

    /// <summary><c>Method</c> 路径同理：首参声明为非可空 <c>string</c> ⇒ <c>PAYLOAD004</c>（生成物传的是 <c>n?.Value</c>）。</summary>
    [Fact]
    public void ExplicitMethodWithNonNullableParameter_ReportsPayload004() => AssertSingleError("""
        namespace PayloadTests
        {
            public static class StrictTextConverter
            {
                public static string Parse(string text) => text;
            }

            [PayloadContract(Converter = typeof(StrictTextConverter))]
            public sealed partial class StrictMethodPayload
            {
                [PayloadField("UserID", Method = "Parse")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD004");

    // —— v2.4 新增：继承链同名成员（否则生成物隐藏基类成员 ⇒ CS0108）——

    /// <summary>
    /// 基类已声明同名 <c>PayloadFieldMap</c> ⇒ <c>PAYLOAD008</c>（不产出）。
    /// </summary>
    /// <remarks>
    /// 修复前实测：零诊断 + 1 个生成文件 + 生成文件报
    /// <c>CS0108</c>「"DerivedWithBaseMap.PayloadFieldMap" 隐藏继承的成员"MapBase.PayloadFieldMap"」。
    /// </remarks>
    [Fact]
    public void InheritedHandwrittenMapMember_ReportsPayload008() => AssertSingleError("""
        namespace PayloadTests
        {
            public class MapBase
            {
                public static Mud.HttpUtils.Payloads.IPayloadFieldMap<MapBase> PayloadFieldMap { get; } = null!;
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class DerivedWithBaseMap : MapBase
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """, "PAYLOAD008");

    // —— v2.4 新增：正向对照（防「新拦截」与「新渲染」误伤合法写法 / 静默产出坏代码）——

    /// <summary>
    /// 正向对照（v2.4）：<c>@</c> 关键字**属性名**必须被 <c>@</c> 转义渲染，且生成物零新增诊断。
    /// </summary>
    /// <remarks>
    /// 修复前实测：<c>[PayloadField("event")] public string? @event</c> 渲染为 <c>t.event = …</c> ⇒
    /// 生成文件报 <c>CS1001</c>/<c>CS1519</c>/<c>CS1026</c>/<c>CS1513</c> 等一串语法错误（错误指向生成文件）。
    /// </remarks>
    [Fact]
    public void KeywordPropertyName_ProducesCompilableOutput()
    {
        var generated = AssertProducesCompilableOutput("""
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class KeywordPropertyPayload
                {
                    [PayloadField("event")] public string? @event { get; set; }
                }
            }
            """);

        generated.Should().Contain("t.@event = ", "关键字属性名必须以 @ 前缀渲染（否则生成文件语法错误）");
    }

    /// <summary>
    /// 正向对照（v2.4）：<c>@</c> 关键字**类名**必须被 <c>@</c> 转义渲染，且生成物零新增诊断。
    /// </summary>
    /// <remarks>
    /// 修复前实测：<c>public sealed partial class @class</c> 渲染为 <c>partial class class</c> ⇒
    /// 生成文件报 <c>CS1001</c>，且消费方的类被报 <c>CS0260</c>「缺少 partial 修饰符」（指向完全错误的位置）。
    /// </remarks>
    [Fact]
    public void KeywordClassName_ProducesCompilableOutput()
    {
        var generated = AssertProducesCompilableOutput("""
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class @class
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
            """);

        generated.Should().Contain("partial class @class", "关键字类名必须以 @ 前缀渲染（否则生成文件语法错误）");
    }

    /// <summary>
    /// 正向对照（v2.4）：<b>命名空间段</b>是 C# 保留字（<c>namespace @class.Sub</c>）时必须正常产出。
    /// </summary>
    /// <remarks>
    /// 两侧行为不同，容易改错任一侧：
    /// ① **渲染**侧：`INamespaceSymbol.ToDisplayString()` **自带** `@` 转义，渲染成 `namespace @class.Sub` 合法
    ///    （若再调一次 `EscapeIdentifier` 会得到 `@@class`）；
    /// ② **hintName** 侧是**文件路径**，不得含 `@` —— 修复前实测 `context.AddSource` 抛
    ///    `ArgumentException`「hintName"@class.Sub.NsPayload.PayloadFieldMap.g.cs"在位置 0 处包含无效字符"@"」，
    ///    被 `Execute` 兜底捕获后表现为 **`PAYLOAD001`「生成器内部错误」**（用户无法从该文案定位到命名空间）。
    /// </remarks>
    [Fact]
    public void KeywordNamespace_ProducesCompilableOutput()
    {
        var generated = AssertProducesCompilableOutput("""
            namespace @class.Sub
            {
                [PayloadContract(Converter = typeof(PayloadTests.PayloadConverter))]
                public sealed partial class NsPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
            """);

        generated.Should().Contain("namespace @class.Sub", "命名空间声明必须保留（且只保留一层）@ 转义");
    }

    /// <summary>
    /// 正向对照（v2.4）：转换器提供<b>多个</b>同名同元数重载时必须优选「签名符合契约」的那个。
    /// </summary>
    /// <remarks>
    /// 修复前实测：<c>Delimited&lt;T&gt;(string, char)</c> 先声明、<c>Delimited&lt;T&gt;(PayloadNode?, char)</c> 后声明时，
    /// 生成器只看首个结构候选 ⇒ 误报 <c>PAYLOAD004</c>（合法声明被判非法）。
    /// </remarks>
    [Fact]
    public void DuplicateHelperOverloads_PrefersContractSignature()
    {
        var generated = AssertProducesCompilableOutput("""
            namespace PayloadTests
            {
                public static class OverloadConverter
                {
                    public static List<T> Delimited<T>(string text, char separator) => new List<T>();

                    public static List<T> Delimited<T>(PayloadNode? node, char separator) => new List<T>();
                }

                [PayloadContract(Converter = typeof(OverloadConverter))]
                public sealed partial class OverloadPayload
                {
                    [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();
                }
            }
            """);

        generated.Should().Contain(
            "OverloadConverter.Delimited<long>(n, ',')",
            "必须绑定形参类型符合契约的重载（而非 GetMembers 顺序里的首个）");
    }

    /// <summary>
    /// 正向对照（v2.4）：nullable <b>未启用</b>（oblivious）时，非可空书写的 <c>PayloadNode</c> 首参仍<b>必须</b>被接受。
    /// </summary>
    /// <remarks>
    /// 「首参必须可空标注」的新拦截只看 <c>NullableAnnotation</c>：oblivious（<c>None</c>）表示声明处
    /// nullable 未启用，此时生成文件同样处于 oblivious 上下文、<b>不会</b>产生 <c>CS8604</c>；
    /// 若一并拒绝，会给 netstandard2.0 且未声明 <c>LangVersion</c>/`Nullable` 的既有消费方造成误杀。
    /// </remarks>
    [Fact]
    public void ObliviousParameterInNullableDisabledSource_IsValid()
    {
        var generated = AssertProducesCompilableOutput("""
            #nullable disable
            namespace PayloadTests
            {
                public static class ObliviousConverter
                {
                    public static string Text(PayloadNode node) => node == null ? null : node.Value;
                }

                [PayloadContract(Converter = typeof(ObliviousConverter))]
                public sealed partial class ObliviousPayload
                {
                    [PayloadField("UserID")] public string UserId { get; set; }
                }
            }
            """, nullableProperty: "disable");

        generated.Should().Contain("ObliviousConverter.Text(n)", "oblivious 首参不得被误判为「缺可空标注」");
    }
}
