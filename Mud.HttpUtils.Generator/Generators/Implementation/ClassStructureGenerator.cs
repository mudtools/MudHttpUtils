// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 类结构生成器，负责生成类的文件头部、命名空间、类声明
/// </summary>
internal class ClassStructureGenerator : ICodeFragmentGenerator
{
    private readonly INamedTypeSymbol _interfaceSymbol;

    public ClassStructureGenerator(INamedTypeSymbol interfaceSymbol)
    {
        _interfaceSymbol = interfaceSymbol;
    }

    private static readonly string[] DefaultUsingNamespaces =
    [
        "System", "System.Net.Http", "System.Text",
        "System.Text.Json", "System.Threading.Tasks",
        "Microsoft.Extensions.Logging", "Microsoft.Extensions.Options", "Mud.HttpUtils"
    ];

    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        TransitiveCodeGenerator.GenerateFileHeader(codeBuilder, DefaultUsingNamespaces);
        codeBuilder.AppendLine();
        GenerateNamespaceDeclaration(codeBuilder, context);
        GenerateClassDeclaration(codeBuilder, context);
    }

    private void GenerateNamespaceDeclaration(StringBuilder codeBuilder, GeneratorContext context)
    {
        // NEW-GEN-04 说明：生成代码使用块作用域命名空间以兼容 netstandard2.0（file-scoped namespace 需 C# 10+）。
        // AGENTS.md 的 file-scoped namespace 规范适用于手写代码，生成代码受目标框架约束豁免。
        codeBuilder.AppendLine($"namespace {context.NamespaceName}".Trim());
        codeBuilder.AppendLine("{");
    }

    private void GenerateClassDeclaration(StringBuilder codeBuilder, GeneratorContext context)
    {
        string classKeyword = context.Configuration.IsAbstract ? "abstract partial class" : "partial class";

        // [v2.4 §3.2] 泛型接口类型参数转发：将接口的类型参数和约束原样转发到实现类。
        var typeParams = _interfaceSymbol.IsGenericType
            ? $"<{string.Join(", ", _interfaceSymbol.TypeParameters.Select(tp => tp.Name))}>"
            : string.Empty;

        // 转发类型约束（where T : class, new() 等）
        var constraints = string.Empty;
        if (_interfaceSymbol.IsGenericType)
        {
            var constraintParts = new List<string>();
            foreach (var tp in _interfaceSymbol.TypeParameters)
            {
                var parts = new List<string>();
                if (tp.HasReferenceTypeConstraint)
                    parts.Add("class");
                if (tp.HasValueTypeConstraint)
                    parts.Add("struct");
                if (tp.HasUnmanagedTypeConstraint)
                    parts.Add("unmanaged");
                if (tp.HasNotNullConstraint)
                    parts.Add("notnull");
                foreach (var constraintType in tp.ConstraintTypes)
                    parts.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes)));
                if (tp.HasConstructorConstraint)
                    parts.Add("new()");

                if (parts.Count > 0)
                    constraintParts.Add($"where {tp.Name} : {string.Join(", ", parts)}");
            }
            if (constraintParts.Count > 0)
                constraints = " " + string.Join(" ", constraintParts);
        }

        string inheritance = string.Empty;
        if (context.HasInheritedFrom)
        {
            inheritance = $" : {context.Configuration.InheritedFrom}, {_interfaceSymbol.Name}{typeParams}";
        }
        else
        {
            inheritance = $" : {_interfaceSymbol.Name}{typeParams}";
        }

        codeBuilder.AppendLine($"    {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
        // T5.4: DynamicDependency 标注，防止 trimmer 在 AOT 下裁剪生成类型及 RestService 成员
        codeBuilder.AppendLine("    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(global::Mud.HttpUtils.RestService))]");
        codeBuilder.AppendLine($"    internal {classKeyword} {context.ClassName}{typeParams}{inheritance}{constraints}");
        codeBuilder.AppendLine("    {");
    }
}
