// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F1 回归：TypeConverter.GetDefaultValueLiteral 对「值类型 default」必须输出 default 而非 null。
/// </summary>
public class TypeConverterTests
{
    private const string SourceTemplate = """
        using System;

        namespace TestNamespace
        {
            public struct MyStruct { public int Id; }
            public enum SomeEnum { A, B }

            public interface IApi
            {
                global::System.Threading.Tasks.Task<string> WithDefaults(
                    CancellationToken ct,
                    MyStruct s,
                    string str,
                    int i,
                    SomeEnum e);
            }
        }
        """;

    [Fact]
    public void StructDefault_ShouldReturnDefaultLiteral()
    {
        var literal = GetDefaultValueLiteralForType("TestNamespace.MyStruct");
        literal.Should().Be("default");
    }

    [Fact]
    public void StringDefault_ShouldReturnNullLiteral()
    {
        var literal = GetDefaultValueLiteralForType("string");
        literal.Should().Be("null");
    }

    [Fact]
    public void CancellationTokenDefault_ShouldReturnDefaultLiteral()
    {
        var literal = GetDefaultValueLiteralForType("System.Threading.CancellationToken");
        literal.Should().Be("default");
    }

    [Fact]
    public void NullableIntExplicitNull_ShouldReturnDefaultLiteral()
    {
        // Nullable<int> 的 ExplicitDefaultValue == null，但因 IsValueType 为 true，必须走 default。
        var literal = GetDefaultValueLiteralForType("int?");
        literal.Should().Be("default");
    }

    private static string GetDefaultValueLiteralForType(string typeName)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var tree = CSharpSyntaxTree.ParseText(SourceTemplate);
        var compilation = CSharpCompilation.Create(
            "TypeConverterTests",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var type = ResolveType(compilation, typeName);
        return TypeConverter.GetDefaultValueLiteral(type, defaultValue: null);
    }

    private static ITypeSymbol ResolveType(Compilation compilation, string typeName)
    {
        return typeName switch
        {
            "string" => compilation.GetSpecialType(SpecialType.System_String),
            "int?" => compilation.GetTypeByMetadataName("System.Nullable`1")!
                .Construct(compilation.GetSpecialType(SpecialType.System_Int32)),
            _ => compilation.GetTypeByMetadataName(typeName)
                ?? throw new InvalidOperationException($"无法解析类型 {typeName}"),
        };
    }
}