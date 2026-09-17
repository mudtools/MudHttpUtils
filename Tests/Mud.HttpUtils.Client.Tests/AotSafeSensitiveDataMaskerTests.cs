// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// T5 验收：AotSafeSensitiveDataMasker 未注册类型告警 + 派生类型兜底测试。
/// </summary>
public class AotSafeSensitiveDataMaskerTests
{
    [Fact]
    public void Mask_HideMode_ReturnsMaskString()
    {
        var masker = new AotSafeSensitiveDataMasker();
        var result = masker.Mask("HelloWorld", SensitiveDataMaskMode.Hide);

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_MaskMode_ReturnsMaskedValue()
    {
        var masker = new AotSafeSensitiveDataMasker();
        var result = masker.Mask("HelloWorld", SensitiveDataMaskMode.Mask, 2, 3);

        result.Should().Be("He***rld");
    }

    [Fact]
    public void Mask_TypeOnlyMode_ReturnsTypeAndLength()
    {
        var masker = new AotSafeSensitiveDataMasker();
        var result = masker.Mask("HelloWorld", SensitiveDataMaskMode.TypeOnly);

        result.Should().Be("[String, Length=10]");
    }

    [Fact]
    public void MaskObject_Null_ReturnsNullString()
    {
        var masker = new AotSafeSensitiveDataMasker();
        var result = masker.MaskObject(null!);

        result.Should().Be("null");
    }

    [Fact]
    public void MaskObject_RegisteredType_UsesRegisteredRule()
    {
        var masker = new AotSafeSensitiveDataMasker();
        masker.Register<TestPersonDto>(obj =>
        {
            var p = (TestPersonDto)obj;
            return $"{{\"id\":{p.Id},\"name\":\"{p.Name}\"}}";
        });

        var result = masker.MaskObject(new TestPersonDto { Id = 1, Name = "Alice" });

        result.Should().Contain("Alice");
        result.Should().Contain("\"id\":1");
    }

    // === T5 验收：未注册告警仅触发一次 ===

    [Fact]
    public void UnregisteredType_WarnsOnlyOnce()
    {
        var capture = new LogCapture();
        var masker = new AotSafeSensitiveDataMasker(capture);

        // 同一未注册类型调用两次
        masker.MaskObject(new TestPersonDto { Id = 1, Name = "Alice" });
        masker.MaskObject(new TestPersonDto { Id = 2, Name = "Bob" });

        capture.Warnings.Should().HaveCount(1, "同一类型告警应仅触发一次");
        capture.Warnings[0].Should().Contain("TestPersonDto");
    }

    [Fact]
    public void UnregisteredType_DifferentTypes_WarnEachOnce()
    {
        var capture = new LogCapture();
        var masker = new AotSafeSensitiveDataMasker(capture);

        masker.MaskObject(new TestPersonDto { Id = 1, Name = "Alice" });
        masker.MaskObject(new TestOrderDto { OrderId = "ORD-001" });
        masker.MaskObject(new TestPersonDto { Id = 2, Name = "Bob" });
        masker.MaskObject(new TestOrderDto { OrderId = "ORD-002" });

        capture.Warnings.Should().HaveCount(2, "两个不同类型各告警一次");
        capture.Warnings.Should().Contain(s => s.Contains("TestPersonDto"));
        capture.Warnings.Should().Contain(s => s.Contains("TestOrderDto"));
    }

    [Fact]
    public void UnregisteredType_NoLogger_NoException()
    {
        var masker = new AotSafeSensitiveDataMasker(logger: null);

        var act = () => masker.MaskObject(new TestPersonDto { Id = 1, Name = "Alice" });

        act.Should().NotThrow();
    }

    [Fact]
    public void UnregisteredType_ReturnsTypeNameFallback()
    {
        var masker = new AotSafeSensitiveDataMasker();

        var result = masker.MaskObject(new TestPersonDto { Id = 1, Name = "Alice" });

        result.Should().Be("[TestPersonDto]");
    }

    // === T5 验收：派生类型在 fallback 开启时降级为类型占位输出（P0-3 修复） ===

    [Fact]
    public void DerivedType_FallbackEnabled_ReturnsTypeOnlyFallback()
    {
        var capture = new LogCapture();
        var masker = new AotSafeSensitiveDataMasker(capture, enableBaseTypeFallback: true);

        masker.Register<TestBaseDto>(obj =>
        {
            var b = (TestBaseDto)obj;
            return $"{{\"baseId\":{b.BaseId}}}";
        });

        var result = masker.MaskObject(new TestDerivedDto { BaseId = 42, ExtraField = "extra" });

        // [P0-3] 回退命中时降级为类型占位输出，不输出任何字段值
        result.Should().Be("[TestDerivedDto, BaseType=TestBaseDto]");
        result.Should().NotContain("baseId");
        result.Should().NotContain("extra");

        // 派生类型应触发一次性告警（引导补注册专用规则）
        capture.Warnings.Should().HaveCount(1);
        capture.Warnings[0].Should().Contain("TestDerivedDto");
        capture.Warnings[0].Should().Contain("TestBaseDto");
    }

    [Fact]
    public void DerivedType_FallbackDisabled_ReturnsTypeName()
    {
        var masker = new AotSafeSensitiveDataMasker(
            logger: null,
            enableBaseTypeFallback: false);

        masker.Register<TestBaseDto>(obj =>
        {
            var b = (TestBaseDto)obj;
            return $"{{\"baseId\":{b.BaseId}}}";
        });

        var result = masker.MaskObject(new TestDerivedDto { BaseId = 42, ExtraField = "extra" });

        // fallback 关闭时维持 [TypeName] 兜底输出
        result.Should().Be("[TestDerivedDto]");
    }

    [Fact]
    public void DerivedType_FallbackEnabled_DeepHierarchy_ReturnsTypeOnly()
    {
        var masker = new AotSafeSensitiveDataMasker(
            logger: null,
            enableBaseTypeFallback: true);

        masker.Register<TestGrandBaseDto>(obj =>
        {
            var b = (TestGrandBaseDto)obj;
            return $"{{\"grandBaseId\":{b.GrandBaseId}}}";
        });

        var result = masker.MaskObject(
            new TestDeepDerivedDto { GrandBaseId = 99, MidId = 5, LeafId = 1 });

        // [P0-3] 深层继承链同样降级为类型占位输出
        // TestMidDto 未注册，遍历到 TestGrandBaseDto 才命中
        result.Should().Be("[TestDeepDerivedDto, BaseType=TestGrandBaseDto]");
    }

    // === T5 评审补充：已注册类型的精确匹配不受 fallback 影响 ===

    [Fact]
    public void RegisteredExactType_NotAffectedByFallback()
    {
        var masker = new AotSafeSensitiveDataMasker(
            logger: null,
            enableBaseTypeFallback: true);

        masker.Register<TestBaseDto>(obj => "{\"base\":\"base_rule\"}");
        masker.Register<TestDerivedDto>(obj => "{\"derived\":\"derived_rule\"}");

        var result = masker.MaskObject(new TestDerivedDto { BaseId = 1, ExtraField = "x" });

        // 精确匹配优先于基类 fallback
        result.Should().Contain("\"derived\":\"derived_rule\"");
        result.Should().NotContain("base_rule");
    }

    [Fact]
    public void RegisteredType_DoesNotWarn()
    {
        var capture = new LogCapture();
        var masker = new AotSafeSensitiveDataMasker(capture);

        masker.Register<TestPersonDto>(obj =>
        {
            var p = (TestPersonDto)obj;
            return $"{{\"id\":{p.Id}}}";
        });

        masker.MaskObject(new TestPersonDto { Id = 1, Name = "Alice" });

        capture.Warnings.Should().BeEmpty("已注册类型不应产生告警");
    }

    [Fact]
    public void MaskObject_ConcurrentCalls_ThreadSafe()
    {
        var masker = new AotSafeSensitiveDataMasker();
        masker.Register<TestPersonDto>(obj =>
        {
            var p = (TestPersonDto)obj;
            return $"{{\"id\":{p.Id}}}";
        });

        var tasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() =>
                masker.MaskObject(new TestPersonDto { Id = i, Name = $"User{i}" })));

        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

        foreach (var result in results)
        {
            result.Should().Contain("\"id\":");
        }
    }

    // === P1-8 Golden 测试：AotSafeSensitiveDataMasker.Mask 与 DefaultSensitiveDataMasker.Mask 逐字节对拍 ===
    // 安全契约锁定：两实现的 Mask 方法必须产出完全一致的字符串，防止行为漂移。
    // 任一侧 Mask 逻辑改动后必须同步对拍期望值，否则此测试变红。

#pragma warning disable CS0618 // DefaultSensitiveDataMasker 标注了 [Obsolete]，golden 对拍需使用
    private static readonly DefaultSensitiveDataMasker s_reflectionMasker = new();
#pragma warning restore CS0618

    [Theory]
    [InlineData("secret123", SensitiveDataMaskMode.Mask, 2, 2, "se***23")]
    [InlineData("ab", SensitiveDataMaskMode.Mask, 2, 2, "***")]
    [InlineData("", SensitiveDataMaskMode.Hide, 2, 2, "***")]
    [InlineData("abcdef", SensitiveDataMaskMode.TypeOnly, 2, 2, "[String, Length=6]")]
    [InlineData("HelloWorld", SensitiveDataMaskMode.Mask, 2, 3, "He***rld")]
    [InlineData("HelloWorld", SensitiveDataMaskMode.Hide, 2, 2, "***")]
    [InlineData("HelloWorld", SensitiveDataMaskMode.TypeOnly, 2, 2, "[String, Length=10]")]
    [InlineData("x", SensitiveDataMaskMode.Mask, 2, 2, "***")]
    [InlineData("1234567890", SensitiveDataMaskMode.Mask, 3, 4, "123***7890")]
    [InlineData("", SensitiveDataMaskMode.Mask, 2, 2, "***")]
    [InlineData("LongEnoughString", SensitiveDataMaskMode.Mask, 0, 0, "***")]
    [InlineData("LongEnoughString", SensitiveDataMaskMode.Mask, 4, 4, "Long***ring")]
    public void Mask_GoldenParity_AotVsReflection_ByteForByte(
        string value, SensitiveDataMaskMode mode, int prefix, int suffix, string expected)
    {
        var aotMasker = new AotSafeSensitiveDataMasker();
        var aotResult = aotMasker.Mask(value, mode, prefix, suffix);
        var reflectionResult = s_reflectionMasker.Mask(value, mode, prefix, suffix);

        aotResult.Should().Be(expected, "AOT 实现应匹配 golden 期望值");
        reflectionResult.Should().Be(expected, "反射实现应匹配 golden 期望值");
        aotResult.Should().Be(reflectionResult, "两实现必须逐字节一致");
    }

    [Theory]
    [InlineData(null, SensitiveDataMaskMode.Mask, 2, 2, "***")]
    [InlineData(null, SensitiveDataMaskMode.Hide, 2, 2, "***")]
    [InlineData(null, SensitiveDataMaskMode.TypeOnly, 2, 2, "***")]
    public void Mask_GoldenParity_NullValue_BothReturnMaskString(
        string? value, SensitiveDataMaskMode mode, int prefix, int suffix, string expected)
    {
        var aotMasker = new AotSafeSensitiveDataMasker();
        var aotResult = aotMasker.Mask(value!, mode, prefix, suffix);
        var reflectionResult = s_reflectionMasker.Mask(value!, mode, prefix, suffix);

        aotResult.Should().Be(expected);
        reflectionResult.Should().Be(expected);
        aotResult.Should().Be(reflectionResult);
    }

    [Theory]
    [InlineData("ab", SensitiveDataMaskMode.Mask, 1, 1, "***")]              // length == prefix+suffix → 全掩码
    [InlineData("abc", SensitiveDataMaskMode.Mask, 1, 2, "***")]             // length == prefix+suffix → 全掩码
    [InlineData("abcd", SensitiveDataMaskMode.Mask, 2, 2, "***")]            // length == prefix+suffix → 全掩码
    [InlineData("abcde", SensitiveDataMaskMode.Mask, 2, 2, "ab***de")]       // length 5 > 4 → 部分保留
    public void Mask_GoldenParity_BoundaryConditions(
        string value, SensitiveDataMaskMode mode, int prefix, int suffix, string expected)
    {
        var aotMasker = new AotSafeSensitiveDataMasker();
        var aotResult = aotMasker.Mask(value, mode, prefix, suffix);
        var reflectionResult = s_reflectionMasker.Mask(value, mode, prefix, suffix);

        aotResult.Should().Be(expected, "边界条件：长度 <= prefix+suffix 时应全掩码");
        reflectionResult.Should().Be(expected);
        aotResult.Should().Be(reflectionResult);
    }

    // === 测试 DTO ===

    private class TestPersonDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class TestOrderDto
    {
        public string OrderId { get; set; } = "";
    }

    private class TestBaseDto
    {
        public int BaseId { get; set; }
    }

    private class TestDerivedDto : TestBaseDto
    {
        public string ExtraField { get; set; } = "";
    }

    private class TestGrandBaseDto
    {
        public int GrandBaseId { get; set; }
    }

    private class TestMidDto : TestGrandBaseDto
    {
        public int MidId { get; set; }
    }

    private class TestDeepDerivedDto : TestMidDto
    {
        public int LeafId { get; set; }
    }

    // === 轻量日志捕获器 ===

    /// <summary>
    /// 轻量级 ILogger 实现，捕获 Warning 级别日志用于断言。
    /// </summary>
    private sealed class LogCapture : ILogger
    {
        private readonly object _lock = new();
        private readonly List<string> _warnings = new();

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_lock)
                {
                    return _warnings.ToList();
                }
            }
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
                return;

            var message = formatter(state, exception);
            lock (_lock)
            {
                _warnings.Add(message);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
