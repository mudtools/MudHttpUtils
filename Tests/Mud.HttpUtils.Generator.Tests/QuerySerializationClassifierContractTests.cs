// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [P2-3] QuerySerializationClassifier.IsSimple 与 TypeDetectionHelper.IsSimpleType 契约级对拍测试。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QuerySerializationClassifier.IsSimple"/>（基于 <c>INamedTypeSymbol</c>）与
/// <see cref="TypeDetectionHelper.IsSimpleType"/>（基于 <c>string</c> 类型名）必须对同一类型
/// 给出一致的"是否简单类型"判定，否则 AOT005 诊断与生成器输出会分叉。
/// </para>
/// <para>
/// 本测试枚举一组代表性类型符号，断言两套判定的结果一致。
/// 当 <c>TypeDetectionHelper</c> 新增简单类型而分类器未同步时，本测试变红。
/// </para>
/// </remarks>
public class QuerySerializationClassifierContractTests
{
    /// <summary>
    /// 对代表性类型集合，断言 <see cref="QuerySerializationClassifier.IsSimple"/> 与
    /// <see cref="TypeDetectionHelper.IsSimpleType"/> 判定一致。
    /// </summary>
    [Fact]
    public void IsSimple_ParityBetweenClassifierAndTypeDetectionHelper()
    {
        // 测试类型声明：涵盖两套判定逻辑覆盖的所有分支
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace TestNs
            {
                // 简单类型
                public class SimpleTypes
                {
                    public string StringProp { get; set; }
                    public int IntProp { get; set; }
                    public long LongProp { get; set; }
                    public bool BoolProp { get; set; }
                    public double DoubleProp { get; set; }
                    public float FloatProp { get; set; }
                    public decimal DecimalProp { get; set; }
                    public DateTime DateTimeProp { get; set; }
                    public Guid GuidProp { get; set; }
                    public DateTimeOffset DateTimeOffsetProp { get; set; }
                    public TimeSpan TimeSpanProp { get; set; }
                    public byte ByteProp { get; set; }
                    public sbyte SByteProp { get; set; }
                    public short ShortProp { get; set; }
                    public ushort UShortProp { get; set; }
                    public uint UIntProp { get; set; }
                    public ulong ULongProp { get; set; }
                    public char CharProp { get; set; }
                    public object ObjectProp { get; set; }
                    public DayOfWeek EnumProp { get; set; }
                    public DateOnly DateOnlyProp { get; set; }
                    public TimeOnly TimeOnlyProp { get; set; }
                    public int? NullableIntProp { get; set; }
                    public DayOfWeek? NullableEnumProp { get; set; }
                }

                // 非简单类型
                public class CustomDto { public int Id { get; set; } }

                public class ComplexTypes
                {
                    public CustomDto CustomDtoProp { get; set; }
                    public Dictionary<string, string> DictProp { get; set; }
                    public List<int> ListProp { get; set; }
                    public int[] ArrayProp { get; set; }
                    public string[] StringArrayProp { get; set; }
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var simpleTypesClass = compilation.GetTypeByMetadataName("TestNs.SimpleTypes")!;
        var complexTypesClass = compilation.GetTypeByMetadataName("TestNs.ComplexTypes")!;

        // 简单类型集合：(属性名, C# 类型名, 期望是否简单)
        // 注意：枚举（DayOfWeek）和 Nullable<枚举> 仅在 QuerySerializationClassifier（基于符号）中被视为简单类型，
        // TypeDetectionHelper（基于类型名字符串）无法识别自定义枚举——这是两者的设计差异，不属于对拍范围。
        var simpleCases = new[]
        {
            ("StringProp", "string", true),
            ("IntProp", "int", true),
            ("LongProp", "long", true),
            ("BoolProp", "bool", true),
            ("DoubleProp", "double", true),
            ("FloatProp", "float", true),
            ("DecimalProp", "decimal", true),
            ("DateTimeProp", "DateTime", true),
            ("GuidProp", "Guid", true),
            ("DateTimeOffsetProp", "DateTimeOffset", true),
            ("TimeSpanProp", "TimeSpan", true),
            ("ByteProp", "byte", true),
            ("SByteProp", "sbyte", true),
            ("ShortProp", "short", true),
            ("UShortProp", "ushort", true),
            ("UIntProp", "uint", true),
            ("ULongProp", "ulong", true),
            ("CharProp", "char", true),
            ("ObjectProp", "object", true),
            ("DateOnlyProp", "DateOnly", true),
            ("TimeOnlyProp", "TimeOnly", true),
            ("NullableIntProp", "int?", true),
        };

        foreach (var (propName, typeName, expected) in simpleCases)
        {
            var propSymbol = simpleTypesClass.GetMembers(propName).FirstOrDefault() as IPropertySymbol;
            propSymbol.Should().NotBeNull($"属性 {propName} 应存在于 SimpleTypes");
            var namedType = (propSymbol!.Type as INamedTypeSymbol)!;
            namedType.Should().NotBeNull($"属性 {propName} 应为命名类型");

            var classifierResult = QuerySerializationClassifier.IsSimple(namedType);
            var typeDetectionResult = TypeDetectionHelper.IsSimpleType(typeName);

            classifierResult.Should().Be(expected,
                $"[Classifier] {propName} ({typeName}) 应为简单类型={expected}");
            typeDetectionResult.Should().Be(expected,
                $"[TypeDetectionHelper] {propName} ({typeName}) 应为简单类型={expected}");
            classifierResult.Should().Be(typeDetectionResult,
                $"[Parity] {propName} ({typeName}): Classifier={classifierResult}, TypeDetection={typeDetectionResult} 必须一致");
        }

        // 非简单类型集合
        // 注意：数组（int[], string[]）的 ITypeSymbol 是 IArrayTypeSymbol 而非 INamedTypeSymbol，
        // QuerySerializationClassifier.IsSimple 只接受 INamedTypeSymbol，数组由 Classify 方法直接判定为 NotApplicable。
        // TypeDetectionHelper.IsSimpleType 对数组返回 false（走 IsSimpleArrayType 分支检查元素类型）。
        // 数组不参与 IsSimple 对拍，在 Classify 层面由 Classify_Parity 测试覆盖。
        var complexCases = new[]
        {
            ("CustomDtoProp", "CustomDto", false),
            ("DictProp", "Dictionary<string, string>", false),
            ("ListProp", "List<int>", false),
        };

        foreach (var (propName, typeName, expected) in complexCases)
        {
            var propSymbol = complexTypesClass.GetMembers(propName).FirstOrDefault() as IPropertySymbol;
            propSymbol.Should().NotBeNull($"属性 {propName} 应存在于 ComplexTypes");
            var namedType = (propSymbol!.Type as INamedTypeSymbol)!;
            namedType.Should().NotBeNull($"属性 {propName} 应为命名类型");

            var classifierResult = QuerySerializationClassifier.IsSimple(namedType);
            var typeDetectionResult = TypeDetectionHelper.IsSimpleType(typeName);

            classifierResult.Should().Be(expected,
                $"[Classifier] {propName} ({typeName}) 应为简单类型={expected}");
            typeDetectionResult.Should().Be(expected,
                $"[TypeDetectionHelper] {propName} ({typeName}) 应为简单类型={expected}");
            classifierResult.Should().Be(typeDetectionResult,
                $"[Parity] {propName} ({typeName}): Classifier={classifierResult}, TypeDetection={typeDetectionResult} 必须一致");
        }
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "ContractTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 确保编译无错误
        var diagnostics = compilation.GetDiagnostics();
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty($"测试编译不应有错误，但有: {string.Join(", ", errors.Select(e => e.GetMessage()))}");

        return compilation;
    }
}
