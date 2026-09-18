namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// GEN-02 / I-17：HeaderMergeMode 端到端回归守卫。
/// </summary>
/// <remarks>
/// 历史缺陷：Roslyn 对枚举 <see cref="TypedConstant.Value"/> 返回底层整数，旧实现
/// <c>ConstructorArguments[0].Value?.ToString()</c> 直出 <c>"1"/"2"</c>，而消费端按成员名
/// （<c>"Replace"/"Ignore"</c>）比较 → 恒失配 → 所有 [HeaderMerge] 都静默退化为 Append。
/// 本类用<strong>真实源码 + 完整生成管线</strong>（GeneratorCompileAssert）验证经
/// <c>AttributeArgumentReader.GetEnumMemberName</c> 反解后的模式确实生效。
/// 旧实现（直接赋字符串的 RequestBuilderTests）无法拦截该回归。
/// </remarks>
public class HeaderMergeModeTests
{
    private static string GetGeneratedCode(string source)
    {
        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "HeaderMerge 端到端用例必须可编译");
        return string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
    }

    /// <summary>
    /// 接口级 [HeaderMerge(HeaderMergeMode.Replace)] → 方法参数级 Header 须发射 Remove + Add。
    /// </summary>
    [Fact]
    public void EndToEnd_Replace_EmitsRemoveAndAdd()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HeaderMerge(HeaderMergeMode.Replace)]
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users")]
                    Task<string> GetAsync([Header("Authorization")] string token);
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().Contain("__httpRequest.Headers.Remove(\"Authorization\")");
        code.Should().Contain("__httpRequest.Headers.Add(\"Authorization\"");
    }

    /// <summary>
    /// 接口级 [HeaderMerge(HeaderMergeMode.Ignore)] → 方法参数级 Header 须被跳过。
    /// </summary>
    [Fact]
    public void EndToEnd_Ignore_SkipsMethodParameterHeader()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HeaderMerge(HeaderMergeMode.Ignore)]
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users")]
                    Task<string> GetAsync([Header("Authorization")] string token);
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().NotContain("__httpRequest.Headers.Add(\"Authorization\"");
        code.Should().NotContain("__httpRequest.Headers.Remove(\"Authorization\")");
    }

    /// <summary>
    /// 接口级 [HeaderMerge(HeaderMergeMode.Append)] → 仅 Add，无 Remove（零漂移基线）。
    /// </summary>
    [Fact]
    public void EndToEnd_Append_Unchanged()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HeaderMerge(HeaderMergeMode.Append)]
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users")]
                    Task<string> GetAsync([Header("Authorization")] string token);
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().Contain("__httpRequest.Headers.Add(\"Authorization\"");
        code.Should().NotContain("__httpRequest.Headers.Remove(\"Authorization\")");
    }

    /// <summary>
    /// 优先级：方法级 [HeaderMerge] 覆盖接口级（方法级 Replace + 接口级 Ignore → 走 Replace）。
    /// </summary>
    [Fact]
    public void EndToEnd_MethodMergeModeOverridesInterface()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HeaderMerge(HeaderMergeMode.Ignore)]
                [HttpClientApi]
                public interface IApi
                {
                    [HeaderMerge(HeaderMergeMode.Replace)]
                    [Get("/users")]
                    Task<string> GetAsync([Header("Authorization")] string token);
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().Contain("__httpRequest.Headers.Remove(\"Authorization\")",
            "方法级 [HeaderMerge] 应覆盖接口级，最高优先级取 Replace");
    }

    /// <summary>
    /// GEN-03 联动：接口级 [HeaderMerge(HeaderMergeMode.Ignore)] → 方法级固定 [Header] 须被整体跳过
    /// （与「只使用接口级头部」语义一致）。
    /// </summary>
    [Fact]
    public void Ignore_SkipsMethodLevelFixedHeader()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HeaderMerge(HeaderMergeMode.Ignore)]
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users")]
                    [Header("Accept", "application/json")]
                    Task<string> GetAsync();
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().NotContain("Headers.Add(\"Accept\", \"application/json\")",
            "Ignore 模式下方法级固定 [Header] 不得发射");
    }
}