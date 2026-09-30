// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-04（架构不变式 I1）：<see cref="CompositeKey"/> 的单射性（长度前缀编码）。
/// </summary>
public class CompositeKeyTests
{
    [Fact]
    public void Combine_ShouldBeInjective_ForAdversarialSegments()
    {
        // 对抗集合：分隔符、冒号、空串、前缀关系（"a" vs "ab"）交错分布。
        var samples = new[]
        {
            new[] { "a", "b\u001Fc" },
            new[] { "a\u001Fb", "c" },
            new[] { "a", "b", "c" },
            new[] { "a", "bc" },
            new[] { "ab", "c" },
            new[] { "", "abc" },
            new[] { "abc", "" },
            new[] { "a:b", "c" },
            new[] { "a", ":b" },
            new[] { "1:a", "b" },
            new[] { "a", "b\u001E\u001Fc" },
            new[] { "a\u001Fb", "c", "d" },
            new[] { "a", "b\u001Fc", "d" },
            new[] { "a", "b", "c\u001Fd" },
        };

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var seenBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<string>();

        foreach (var parts in samples)
        {
            var key = parts.Length switch
            {
                2 => CompositeKey.Combine(parts[0], parts[1]),
                3 => CompositeKey.Combine(parts[0], parts[1], parts[2]),
                4 => CompositeKey.Combine(parts[0], parts[1], parts[2], parts[3]),
                _ => throw new InvalidOperationException("示例分段数必须为 2/3/4"),
            };

            var description = string.Join("|", parts.Select(Describe));
            if (!keys.Add(key))
            {
                conflicts.Add($"[{description}] 与 [{seenBy[key]}]");
            }
            else
            {
                seenBy[key] = description;
            }
        }

        conflicts.Should().BeEmpty("长度前缀编码对不同分段组合必须两两不等");
    }

    [Fact]
    public void Combine_ShouldBeInjective_ForRandomSegments()
    {
        // 属性测试风格：1000 组随机分段（含 U+001F / U+001E / 冒号），断言两两不等。
        var random = new Random(20260930);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisions = new List<string>();

        for (var i = 0; i < 1000; i++)
        {
            var count = random.Next(2, 5);
            var parts = new string[count];
            for (var j = 0; j < count; j++)
            {
                parts[j] = RandomSegment(random);
            }

            var key = count switch
            {
                2 => CompositeKey.Combine(parts[0], parts[1]),
                3 => CompositeKey.Combine(parts[0], parts[1], parts[2]),
                _ => CompositeKey.Combine(parts[0], parts[1], parts[2], parts[3]),
            };

            var description = string.Join("\u241F", parts.Select(Describe));
            if (seen.TryGetValue(key, out var previous))
            {
                // 只有"不同输入 → 同一键"才是碰撞；相同输入重复出现是合法的。
                if (!string.Equals(previous, description, StringComparison.Ordinal))
                {
                    collisions.Add($"{previous} == {description}");
                }
            }
            else
            {
                seen[key] = description;
            }
        }

        collisions.Should().BeEmpty("随机分段（含控制字符）不得产生键碰撞");
    }

    [Fact]
    public void Combine_ShouldDistinguishEmptyAndNullSegments()
    {
        CompositeKey.Combine("a", "").Should().NotBe(CompositeKey.Combine("", "a"));
        CompositeKey.Combine(null, "a").Should().Be(CompositeKey.Combine("", "a"), "null 与空串同义（与本库既有口径一致）");
        CompositeKey.Combine("a", null, "b").Should().Be(CompositeKey.Combine("a", "", "b"));
    }

    private static string RandomSegment(Random random)
    {
        var length = random.Next(0, 6);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = random.Next(5) switch
            {
                0 => '\u001F',
                1 => '\u001E',
                2 => ':',
                3 => (char)random.Next('a', 'z' + 1),
                _ => (char)random.Next('0', '9' + 1),
            };
        }
        return new string(chars);
    }

    private static string Describe(string value)
        => value.Replace("\u001F", "<US>").Replace("\u001E", "<RS>");
}
