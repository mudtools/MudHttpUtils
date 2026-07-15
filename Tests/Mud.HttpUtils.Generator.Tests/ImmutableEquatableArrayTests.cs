using Mud.HttpUtils.Generator.Models;
using Xunit;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// Phase 2.2 验证：ImmutableEquatableArray&lt;T&gt; 的值相等语义（用于增量生成器缓存判断）。
/// </summary>
public class ImmutableEquatableArrayTests
{
    [Fact]
    public void Equal_WithSameElements_ReturnsTrue()
    {
        var a = new[] { 1, 2, 3 }.ToImmutableEquatableArray();
        var b = new[] { 1, 2, 3 }.ToImmutableEquatableArray();

        Assert.Equal(a, b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equal_WithDifferentLength_ReturnsFalse()
    {
        var a = new[] { 1, 2 }.ToImmutableEquatableArray();
        var b = new[] { 1, 2, 3 }.ToImmutableEquatableArray();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equal_WithDifferentElements_ReturnsFalse()
    {
        var a = new[] { 1, 2, 3 }.ToImmutableEquatableArray();
        var b = new[] { 1, 2, 4 }.ToImmutableEquatableArray();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Indexer_And_Count_Work()
    {
        var a = new[] { "x", "y" }.ToImmutableEquatableArray();

        Assert.Equal(2, a.Count);
        Assert.Equal("x", a[0]);
        Assert.Equal("y", a[1]);
    }

    [Fact]
    public void Null_Other_ReturnsFalse()
    {
        var a = new[] { 1 }.ToImmutableEquatableArray();

        Assert.False(a.Equals(null));
    }
}
