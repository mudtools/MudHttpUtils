using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class HttpRequestMessageClonerTests
{
    [Fact]
    public async Task CloneAsync_WithGetRequest_ShouldCloneMethodAndUri()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        clone.Should().NotBeNull();
        clone.Should().NotBeSameAs(original);
        clone.Method.Should().Be(HttpMethod.Get);
        clone.RequestUri.Should().Be(original.RequestUri);
    }

    [Fact]
    public async Task CloneAsync_WithPostRequest_ShouldCloneContent()
    {
        var content = """{"name":"test"}""";
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        clone.Should().NotBeNull();
        clone.Content.Should().NotBeNull();
        var clonedContent = await clone.Content!.ReadAsStringAsync();
        clonedContent.Should().Be(content);
    }

    [Fact]
    public async Task CloneAsync_WithHeaders_ShouldCloneHeaders()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        original.Headers.Add("X-Custom-Header", "test-value");
        original.Headers.Add("Authorization", "Bearer token123");

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        clone.Headers.GetValues("X-Custom-Header").Should().ContainSingle().Which.Should().Be("test-value");
        clone.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer token123");
    }

    [Fact]
    public async Task CloneAsync_WithContentHeaders_ShouldCloneContentHeaders()
    {
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent("test", Encoding.UTF8, "application/json")
        };

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        clone.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CloneAsync_WithNoContent_ShouldHaveNullContent()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        clone.Content.Should().BeNull();
    }

    [Fact]
    public async Task CloneAsync_WithVersion_ShouldCloneVersion()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test")
        {
            Version = new Version(2, 0)
        };

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        clone.Version.Should().Be(new Version(2, 0));
    }

    [Fact]
    public async Task CloneAsync_ShouldNotModifyOriginal()
    {
        var content = """{"name":"test"}""";
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        original.Headers.Add("X-Test", "value");

        var clone = await HttpRequestMessageCloner.CloneAsync(original);

        original.Method.Should().Be(HttpMethod.Post);
        original.RequestUri!.ToString().Should().Be("https://api.example.com/test");
        original.Headers.GetValues("X-Test").Should().ContainSingle().Which.Should().Be("value");
        var originalContent = await original.Content!.ReadAsStringAsync();
        originalContent.Should().Be(content);
    }

    [Fact]
    public async Task CloneAsync_WithMaxContentSize_ExceedsLimit_Throws()
    {
        var largeData = new byte[1000];
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new ByteArrayContent(largeData)
        };

        var act = async () => await HttpRequestMessageCloner.CloneAsync(original, 500);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CloneAsync_WithMaxContentSize_WithinLimit_Succeeds()
    {
        var smallData = new byte[100];
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new ByteArrayContent(smallData)
        };

        var clone = await HttpRequestMessageCloner.CloneAsync(original, 500);

        clone.Should().NotBeNull();
        clone.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task CloneAsync_WithNoContent_AndMaxContentSize_Succeeds()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var clone = await HttpRequestMessageCloner.CloneAsync(original, 500);

        clone.Should().NotBeNull();
        clone.Content.Should().BeNull();
    }

    // M4-C/H-7：TryCloneAsync 透传 CancellationToken —— 克隆阶段被取消时 OperationCanceledException
    // 即时上抛（不被内部 InvalidOperationException 捕获），而非静默返回 null 或继续克隆。
    [Fact]
    public async Task TryCloneAsync_WithCancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent("""{"name":"test"}""", Encoding.UTF8, "application/json")
        };

        var act = async () => await HttpRequestMessageCloner.TryCloneAsync(original, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // M4-C/H-7：未取消的 token 不改变既有语义（克隆成功）。
    [Fact]
    public async Task TryCloneAsync_WithActiveToken_StillSucceeds()
    {
        using var cts = new CancellationTokenSource();
        var original = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent("body", Encoding.UTF8, "text/plain")
        };

        var clone = await HttpRequestMessageCloner.TryCloneAsync(original, cancellationToken: cts.Token);

        clone.Should().NotBeNull();
        (await clone!.Content!.ReadAsStringAsync()).Should().Be("body");
    }
}
