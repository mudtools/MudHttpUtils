// -----------------------------------------------------------------------
//  临时探针（验证后删除）
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace Mud.HttpUtils.Generator.Tests;

public class ZZTempProbeTests
{
    private readonly ITestOutputHelper _output;

    public ZZTempProbeTests(ITestOutputHelper output) => _output = output;

    private void Probe(string name, string payloadDeclaration)
    {
        var compilation = CSharpCompilation.Create(
            "PayloadProbe_" + name,
            new[] { CSharpSyntaxTree.ParseText(PayloadTestData.Source(payloadDeclaration)) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new PayloadFieldMapGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id + ": " + d.GetMessage() + " @ " + d.Location.GetLineSpan().StartLinePosition)
            .ToArray();

        _output.WriteLine("=== " + name + " ===");
        _output.WriteLine("generator diagnostics: " + (diagnostics.IsDefaultOrEmpty
            ? "<none>"
            : string.Join(" | ", diagnostics.Select(d => d.Id + " " + d.GetMessage()))));
        _output.WriteLine("generated files: " + (output.SyntaxTrees.Count() - 1));
        foreach (var tree in output.SyntaxTrees.Skip(1))
        {
            _output.WriteLine("--- generated tree: " + tree.FilePath);
            _output.WriteLine(string.Join("\n", tree.ToString().Split('\n').Skip(3)));
        }

        _output.WriteLine("output compilation errors: " + (errors.Length == 0 ? "<none>" : string.Join(" | ", errors)));
    }

    [Fact]
    public void Probe_All()
    {
        Probe("StaticClass", """
            namespace PayloadTests
            {
                [PayloadContract]
                public static partial class StaticPayload
                {
                }
            }
            """);

        Probe("AbstractClass", """
            namespace PayloadTests
            {
                [PayloadContract]
                public abstract partial class AbstractPayload
                {
                }
            }
            """);

        Probe("NoPublicCtor", """
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class NoCtorPayload
                {
                    private NoCtorPayload() { }

                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
            """);

        Probe("CtorWithArgs", """
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class ArgsCtorPayload
                {
                    public ArgsCtorPayload(int x) { }

                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
            }
            """);

        Probe("Indexer", """
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class IndexerPayload
                {
                    [PayloadField("UserID")] public string? this[int i] => null;
                }
            }
            """);

        Probe("WrongHelperFirstParam", """
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(BadConverter))]
                public sealed partial class BadHelperPayload
                {
                    [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();
                }

                public static class BadConverter
                {
                    public static List<T> Delimited<T>(string text, char separator) => new List<T>();
                }
            }
            """);

        Probe("ExplicitSeparatorOnScalar", """
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class SeparatorOnScalarPayload
                {
                    [PayloadField("UserID", Separator = '|')] public string? UserId { get; set; }
                }
            }
            """);

        Probe("ExplicitSeparatorWithExplicitText", """
            namespace PayloadTests
            {
                [PayloadContract(Converter = typeof(PayloadConverter))]
                public sealed partial class SeparatorWithTextPayload
                {
                    [PayloadField("Ids", Format = PayloadFieldFormat.Text, Separator = '|')]
                    public List<long> Ids { get; set; } = new List<long>();
                }
            }
            """);
    }
}
