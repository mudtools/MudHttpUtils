// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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

    [Fact]
    public void InterfaceOrStructTarget_IsRejectedByCscItself()
    {
        // [PayloadContract] 的 AttributeUsage 为 AttributeTargets.Class，故 struct/interface 目标会被
        // csc 以 CS0592 拒绝、特性不参与绑定 ⇒ 生成器根本看不到该节点（PAYLOAD009 在符号层不可达）。
        // 本用例只钉住「生成器不会因此产生半成品或内部错误」。
        var (diagnostics, generatedTreeCount) = RunGenerator("""
            namespace PayloadTests
            {
                public partial struct StructPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
            """);

        diagnostics.Should().BeEmpty();
        generatedTreeCount.Should().Be(0);
    }
}
