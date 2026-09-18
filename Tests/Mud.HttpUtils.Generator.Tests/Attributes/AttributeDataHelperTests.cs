// -----------------------------------------------------------------------
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CFG-28 / 不变量 I-9：<c>AttributeDataHelper.GetIntValuePreferNamed</c> 的「命名参数优先」语义单元测试。
/// </summary>
/// <remarks>
/// <para>
/// <b>为何需要独立单测</b>：该 API 是本轮 5 个特性读取点的唯一口径来源（<c>Cache</c> / <c>Retry</c> /
/// <c>CircuitBreaker</c> / <c>Timeout</c>）。仅靠「生成器端到端 + 快照」覆盖时，
/// 一旦优先级写反，快照只会体现为「某个数字不同」，定位成本高；本文件把语义直接钉死。
/// </para>
/// <para>
/// C# 特性求值顺序：先调用构造函数（位置参数），再依次赋值命名参数 ⇒ <b>命名参数是后写的一方</b>。
/// </para>
/// </remarks>
public class AttributeDataHelperTests
{
    #region 测试夹具

    /// <summary>
    /// 编译一段带特性标注的源码并取回指定特性的 <see cref="AttributeData"/>。
    /// </summary>
    private static AttributeData GetAttributeData(string methodAttributes, string attributeTypeName)
    {
        var source = $$"""
            using System;
            using Mud.HttpUtils.Attributes;

            public interface IApi
            {
                {{methodAttributes}}
                void M();
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "AttributeDataHelperTests",
            [syntaxTree],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodSyntax = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax)
            ?? throw new InvalidOperationException("无法取得方法符号");

        return methodSymbol.GetAttributes()
            .First(a => a.AttributeClass?.Name == attributeTypeName);
    }

    private static AttributeData Retry(string attributeUsage) => GetAttributeData(attributeUsage, "RetryAttribute");

    private static AttributeData Cache(string attributeUsage) => GetAttributeData(attributeUsage, "CacheAttribute");

    private static AttributeData Timeout(string attributeUsage) => GetAttributeData(attributeUsage, "TimeoutAttribute");

    private static AttributeData CircuitBreaker(string attributeUsage)
        => GetAttributeData(attributeUsage, "CircuitBreakerAttribute");

    #endregion

    #region CFG-28：位置参数必须被读取

    [Fact]
    public void Retry_TwoArgConstructor_PositionalDelayMillisecondsIsRead()
    {
        // CFG-28 缺陷本身：[Retry(5, 250)] 的 250 位于 ConstructorArguments[1]，此前从未被读取。
        var attribute = Retry("[Retry(5, 250)]");

        AttributeDataHelper.GetIntValuePreferNamed(attribute, "MaxRetries", 0).Should().Be(5);
        AttributeDataHelper.GetIntValuePreferNamed(attribute, "DelayMilliseconds", 1).Should().Be(250);
    }

    [Fact]
    public void Retry_SingleArgConstructor_PositionalDelayIndexIsAbsent()
    {
        // [Retry(5)] 只传 1 个位置参数 ⇒ index 1 越界，应返回 null（由调用方回退默认 1000），
        // 而不是抛出或误取其他索引的值。
        var attribute = Retry("[Retry(5)]");

        AttributeDataHelper.GetIntValuePreferNamed(attribute, "DelayMilliseconds", 1).Should().BeNull();
    }

    [Fact]
    public void Cache_PositionalDurationSecondsIsRead()
    {
        AttributeDataHelper.GetIntValuePreferNamed(Cache("[Cache(600)]"), "DurationSeconds", 0).Should().Be(600);
    }

    [Fact]
    public void Timeout_PositionalTimeoutMillisecondsIsRead()
    {
        AttributeDataHelper.GetIntValuePreferNamed(Timeout("[Timeout(1500)]"), "TimeoutMilliseconds", 0).Should().Be(1500);
    }

    [Fact]
    public void CircuitBreaker_PositionalFailureThresholdIsRead()
    {
        AttributeDataHelper.GetIntValuePreferNamed(CircuitBreaker("[CircuitBreaker(8)]"), "FailureThreshold", 0)
            .Should().Be(8);
    }

    #endregion

    #region I-9：命名参数优先

    [Fact]
    public void Retry_NamedArgumentWinsOverPositional()
    {
        // C# 语义：构造函数先执行（DelayMilliseconds = 250），随后命名参数赋值（= 700）覆盖。
        var attribute = Retry("[Retry(5, 250, DelayMilliseconds = 700)]");

        AttributeDataHelper.GetIntValuePreferNamed(attribute, "DelayMilliseconds", 1).Should().Be(700);
        AttributeDataHelper.GetIntValuePreferNamed(attribute, "MaxRetries", 0).Should().Be(5);
    }

    [Fact]
    public void Retry_NamedMaxRetriesWinsOverPositional()
    {
        var attribute = Retry("[Retry(5, MaxRetries = 9)]");

        AttributeDataHelper.GetIntValuePreferNamed(attribute, "MaxRetries", 0).Should().Be(9);
    }

    [Fact]
    public void Retry_NamedArgumentOnly_UsesNamedValue()
    {
        var attribute = Retry("[Retry(DelayMilliseconds = 700)]");

        AttributeDataHelper.GetIntValuePreferNamed(attribute, "DelayMilliseconds", 1).Should().Be(700);
        // 未显式指定 MaxRetries ⇒ 构造函数默认值 3（编译期已展开进 ConstructorArguments[0]）
        AttributeDataHelper.GetIntValuePreferNamed(attribute, "MaxRetries", 0).Should().Be(3);
    }

    [Fact]
    public void Cache_NamedArgumentWinsOverPositional()
    {
        AttributeDataHelper.GetIntValuePreferNamed(
                Cache("[Cache(600, DurationSeconds = 100)]"), "DurationSeconds", 0)
            .Should().Be(100);
    }

    [Fact]
    public void Timeout_NamedArgumentWinsOverPositional()
    {
        AttributeDataHelper.GetIntValuePreferNamed(
                Timeout("[Timeout(1000, TimeoutMilliseconds = 2000)]"), "TimeoutMilliseconds", 0)
            .Should().Be(2000);
    }

    [Fact]
    public void CircuitBreaker_NamedArgumentWinsOverPositional()
    {
        AttributeDataHelper.GetIntValuePreferNamed(
                CircuitBreaker("[CircuitBreaker(5, FailureThreshold = 7)]"), "FailureThreshold", 0)
            .Should().Be(7);
    }

    [Fact]
    public void PropertyNameMatchIsCaseInsensitive()
    {
        AttributeDataHelper.GetIntValuePreferNamed(
                Retry("[Retry(5, DelayMilliseconds = 700)]"), "delaymilliseconds", 1)
            .Should().Be(700);
    }

    #endregion

    #region 边界

    [Fact]
    public void NullAttribute_ReturnsNull()
    {
        AttributeDataHelper.GetIntValuePreferNamed(null, "AnyProperty", 0).Should().BeNull();
    }

    [Fact]
    public void MissingNamedArgumentAndOutOfRangeIndex_ReturnsNull()
    {
        AttributeDataHelper.GetIntValuePreferNamed(Cache("[Cache(600)]"), "NotExisting", 5).Should().BeNull();
    }

    [Fact]
    public void NegativeConstructorParameterIndex_SkipsPositionalLookup()
    {
        // -1 = 显式声明「仅命名参数」（CircuitBreaker 的三个单项参数即用此形态）
        AttributeDataHelper.GetIntValuePreferNamed(Cache("[Cache(600)]"), "DurationSeconds", -1).Should().BeNull();
    }

    #endregion
}
