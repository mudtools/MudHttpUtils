// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-15：接口声明了「生成器按模式**无条件发射**」的同名成员时，契约补全必须避让（不得重复发射）。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景</b>：<c>GeneratorContext.ProvidedMemberNames</c> 登记「与接口是否声明无关、由生成器无条件发射」的成员名，
/// 供 <c>MethodGenerator.EmitContractCompletionStub</c> 与 <c>InterfaceContractCompletionGenerator</c> 避让。
/// 该登记表此前<b>漏登记 <c>UseAppScope</c></b>（发射点见 <c>ConstructorGenerator.GenerateUseAppMethod</c>），
/// 使接口一旦声明同名成员即产生重复成员（CS0111/CS0102）。
/// </para>
/// <para>
/// <b>本用例的价值</b>：它不是「针对 UseAppScope 的一次性断言」，而是<b>结构一致性守卫</b> ——
/// 未来任何「新增无条件发射成员却忘记登记」的改动都会在此用例上失败（接口声明了全部已知无条件成员，
/// 只要漏登记其中之一就会产生重复成员或占位诊断）。
/// </para>
/// </remarks>
public class UnconditionalMemberAvoidanceTests
{
    /// <summary>声明全部「AppContext 模式无条件成员」的接口（方法与属性均**不带** HTTP 方法特性）。</summary>
    private const string Source = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IAllUnconditionalMembersApi
            {
                IMudAppContext? Current { get; set; }

                void SwitchTo(IMudAppContext? context);

                IDisposable BeginScope(IMudAppContext context);

                IDisposable BeginScope(string appKey);

                IMudAppContext UseApp(string appKey);

                IDisposable UseAppScope(string appKey);

                IMudAppContext UseDefaultApp();

                IDisposable UseDefaultAppScope();
            }
        }
        """;

    /// <summary>
    /// 接口声明全部无条件成员 ⇒ 必须无 CS0111/CS0102（重复成员），且不得为其发射占位实现（HTTPCLIENT024）。
    /// </summary>
    [Fact]
    public void DeclaringAllUnconditionalMembers_CompilesWithoutDuplicates()
    {
        // RunAndAssertNoErrors 断言「输入 + 生成产物」无 Error 级诊断 ——
        // 重复成员会以 CS0111/CS0102 立即失败。
        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            Source, description: "声明全部无条件成员的接口");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().NotContain("NotSupportedException",
            "无 HTTP 方法特性的成员若被发射占位实现，说明避让表漏登记（占位成员会与生成器无条件成员重名）");
    }

    /// <summary>
    /// 反向守卫：无条件成员**不得**被报告 HTTPCLIENT024（占位诊断）——
    /// 它们由生成器自身实现，属正常可用成员。
    /// </summary>
    [Fact]
    public void DeclaringAllUnconditionalMembers_ReportsNoPlaceholderDiagnostic()
    {
        var compilation = CSharpCompilation.Create(
            "UnconditionalMemberAvoidanceTests",
            [CSharpSyntaxTree.ParseText(Source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT024",
            "无条件成员由生成器实现，不应被当作「未生成实现」的占位成员");
    }
}
