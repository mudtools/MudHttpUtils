// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// F-2（TK-1）：<see cref="TokenScopeKey"/> 的编码单射性（复用 CompositeKeyTests 断言风格）、
/// 三形态区分、工厂守卫与值相等性。
/// </summary>
public class TokenScopeKeyTests
{
    [Fact]
    public void ToCompositeKey_ShouldBeInjective_ForAdversarialSegments()
    {
        // 对抗集合：分隔符、冒号、空段边界、前缀关系（"a" vs "ab"）跨三种形态交错分布。
        var samples = new (string Description, string Key)[]
        {
            ("ForScope(a,b,c\u001Fd)", TokenScopeKey.ForScope("a", "b", "c\u001Fd").ToCompositeKey()),
            ("ForScope(a\u001Fb,c,d)", TokenScopeKey.ForScope("a\u001Fb", "c", "d").ToCompositeKey()),
            ("ForScope(a,b,c)", TokenScopeKey.ForScope("a", "b", "c").ToCompositeKey()),
            ("ForScope(ab,c,d)", TokenScopeKey.ForScope("ab", "c", "d").ToCompositeKey()),
            ("ForScope(a,bc,d)", TokenScopeKey.ForScope("a", "bc", "d").ToCompositeKey()),
            ("ForScope(a,b,cd)", TokenScopeKey.ForScope("a", "b", "cd").ToCompositeKey()),
            ("ForScope(a:b,c,d)", TokenScopeKey.ForScope("a:b", "c", "d").ToCompositeKey()),
            ("ForScope(a,:b,c)", TokenScopeKey.ForScope("a", ":b", "c").ToCompositeKey()),
            ("ForScope(a,b,:c)", TokenScopeKey.ForScope("a", "b", ":c").ToCompositeKey()),
            ("ForScope(1:a,b,c)", TokenScopeKey.ForScope("1:a", "b", "c").ToCompositeKey()),
            ("ForScope(a,b\u001E\u001Fc,d)", TokenScopeKey.ForScope("a", "b\u001E\u001Fc", "d").ToCompositeKey()),
            ("ForApp(a,b)", TokenScopeKey.ForApp("a", "b").ToCompositeKey()),
            ("ForApp(ab,c)", TokenScopeKey.ForApp("ab", "c").ToCompositeKey()),
            ("ForApp(a,bc)", TokenScopeKey.ForApp("a", "bc").ToCompositeKey()),
            ("ForShared(a)", TokenScopeKey.ForShared("a").ToCompositeKey()),
            ("ForShared(ab)", TokenScopeKey.ForShared("ab").ToCompositeKey()),
            ("ForShared(a\u001Fb)", TokenScopeKey.ForShared("a\u001Fb").ToCompositeKey()),
        };

        var seenBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<string>();

        foreach (var (description, key) in samples)
        {
            if (seenBy.TryGetValue(key, out var previous))
                conflicts.Add($"[{description}] 与 [{previous}]");
            else
                seenBy[key] = description;
        }

        conflicts.Should().BeEmpty("长度前缀编码对不同段组合必须两两不等（含跨形态）");
    }

    [Fact]
    public void ToCompositeKey_ShouldBeInjective_ForRandomSegments()
    {
        // 属性测试风格：随机段（含 U+001F / U+001E / 冒号）经三种形态构造，断言不同段组合两两不碰撞。
        var random = new Random(20261013);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisions = new List<string>();

        for (var i = 0; i < 1000; i++)
        {
            var tokenType = RandomSegment(random);
            var appKey = RandomSegment(random);
            var scopeKey = RandomSegment(random);

            TokenScopeKey key;
            string description;
            switch (random.Next(3))
            {
                case 0:
                    key = TokenScopeKey.ForScope(tokenType, appKey, scopeKey);
                    description = $"S[{Describe(tokenType)}|{Describe(appKey)}|{Describe(scopeKey)}]";
                    break;
                case 1:
                    key = TokenScopeKey.ForApp(tokenType, appKey);
                    description = $"A[{Describe(tokenType)}|{Describe(appKey)}]";
                    break;
                default:
                    key = TokenScopeKey.ForShared(tokenType);
                    description = $"H[{Describe(tokenType)}]";
                    break;
            }

            var encoded = key.ToCompositeKey();
            if (seen.TryGetValue(encoded, out var previous))
            {
                // 只有"不同输入 → 同一键"才是碰撞；相同输入重复出现是合法的。
                if (!string.Equals(previous, description, StringComparison.Ordinal))
                    collisions.Add($"{previous} == {description}");
            }
            else
            {
                seen[encoded] = description;
            }
        }

        collisions.Should().BeEmpty("随机段（含控制字符）不得产生键碰撞");
    }

    [Fact]
    public void ToCompositeKey_ShouldEncodeThreeSegmentLengthPrefixedText()
    {
        // 编码键形态 = CompositeKey 风格长度前缀三段式；缺失段按空串编码，形态之间不混淆。
        TokenScopeKey.ForScope("t", "a", "s").ToCompositeKey()
            .Should().Be(CompositeKey.Combine("t", "a", "s"));
        TokenScopeKey.ForApp("t", "a").ToCompositeKey()
            .Should().Be(CompositeKey.Combine("t", "a", string.Empty));
        TokenScopeKey.ForShared("t").ToCompositeKey()
            .Should().Be(CompositeKey.Combine("t", string.Empty, string.Empty));

        TokenScopeKey.ForApp("t", "a").ToCompositeKey()
            .Should().NotBe(TokenScopeKey.ForShared("t").ToCompositeKey(), "缺失段不得与空串段混淆到同一形态");
    }

    [Fact]
    public void ToString_ShouldEqualToCompositeKey()
    {
        var key = TokenScopeKey.ForScope("corp.token", "app-a", "auth-corp-1");

        key.ToString().Should().Be(key.ToCompositeKey());
    }

    [Fact]
    public void Factories_ShouldRejectNullAndEmptySegments()
    {
        var nullTokenType = () => TokenScopeKey.ForShared(null!);
        nullTokenType.Should().Throw<ArgumentNullException>().WithParameterName("tokenType");

        var emptyTokenType = () => TokenScopeKey.ForShared(string.Empty);
        emptyTokenType.Should().Throw<ArgumentException>().WithParameterName("tokenType");

        var nullAppKey = () => TokenScopeKey.ForApp("t", null!);
        nullAppKey.Should().Throw<ArgumentNullException>().WithParameterName("appKey");

        var emptyAppKey = () => TokenScopeKey.ForScope("t", string.Empty, "s");
        emptyAppKey.Should().Throw<ArgumentException>().WithParameterName("appKey");

        var nullScopeKey = () => TokenScopeKey.ForScope("t", "a", null!);
        nullScopeKey.Should().Throw<ArgumentNullException>().WithParameterName("scopeKey");

        var emptyScopeKey = () => TokenScopeKey.ForScope("t", "a", string.Empty);
        emptyScopeKey.Should().Throw<ArgumentException>().WithParameterName("scopeKey");
    }

    [Fact]
    public void Factories_ShouldExposeSegments()
    {
        var scope = TokenScopeKey.ForScope("t", "a", "s");
        scope.TokenType.Should().Be("t");
        scope.AppKey.Should().Be("a");
        scope.ScopeKey.Should().Be("s");

        var app = TokenScopeKey.ForApp("t", "a");
        app.TokenType.Should().Be("t");
        app.AppKey.Should().Be("a");
        app.ScopeKey.Should().BeNull();

        var shared = TokenScopeKey.ForShared("t");
        shared.TokenType.Should().Be("t");
        shared.AppKey.Should().BeNull();
        shared.ScopeKey.Should().BeNull();
    }

    [Fact]
    public void Equals_ShouldCompareAllSegmentsOrdinally()
    {
        TokenScopeKey.ForScope("t", "a", "s").Equals(TokenScopeKey.ForScope("t", "a", "s")).Should().BeTrue();
        (TokenScopeKey.ForScope("t", "a", "s") == TokenScopeKey.ForScope("t", "a", "s")).Should().BeTrue();
        (TokenScopeKey.ForScope("t", "a", "s") != TokenScopeKey.ForScope("t", "a", "s")).Should().BeFalse();

        TokenScopeKey.ForScope("t", "a", "s").Equals(TokenScopeKey.ForScope("t", "a", "S")).Should().BeFalse("序数比较，大小写敏感");
        TokenScopeKey.ForScope("t", "a", "s").Equals(TokenScopeKey.ForApp("t", "a")).Should().BeFalse("形态不同不得相等");
        TokenScopeKey.ForApp("t", "a").Equals(TokenScopeKey.ForShared("t")).Should().BeFalse();

        TokenScopeKey.ForScope("t", "a", "s").GetHashCode()
            .Should().Be(TokenScopeKey.ForScope("t", "a", "s").GetHashCode());
    }

    [Fact]
    public void ToScopes_ShouldMapToLegacyScopeRouting()
    {
        // 定位符形态 → 既有 string[] scopes 路径的段映射（互通性由 TokenScopeKeyLocatorTests 端到端钉死）。
        TokenScopeKey.ForScope("t", "a", "s").ToScopes().Should().Equal("s");
        TokenScopeKey.ForApp("t", "a").ToScopes().Should().BeNull("ForApp 映射到默认作用域（scopes = null）");
        TokenScopeKey.ForShared("t").ToScopes().Should().BeNull("ForShared 映射到默认作用域（scopes = null）");
    }

    private static string RandomSegment(Random random)
    {
        // 工厂拒绝空段，随机段取长度 1..6。
        var length = random.Next(1, 7);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = random.Next(6) switch
            {
                0 => 'ࠀ',       // 非 ASCII 干扰
                1 => '\u001F',   // CompositeKey 段分隔符干扰
                2 => '\u001E',   // ScopeKeyBuilder 转义字符干扰
                3 => ':',        // 长度前缀分隔符干扰
                4 => 'x',
                _ => '1',        // 长度前缀数字干扰
            };
        }

        return new string(chars);
    }

    private static string Describe(string segment)
    {
        var builder = new System.Text.StringBuilder(segment.Length * 2);
        foreach (var ch in segment)
        {
            builder.Append(char.IsControl(ch) ? $"\\u{(int)ch:X4}" : ch.ToString());
        }

        return builder.ToString();
    }
}
