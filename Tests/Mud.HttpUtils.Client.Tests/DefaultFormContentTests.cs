namespace Mud.HttpUtils.Client.Tests;

public class DefaultFormContentTests
{
    [Fact]
    public void Constructor_NullFormData_ThrowsArgumentNullException()
    {
        var act = () => new DefaultFormContent(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("formData");
    }

    [Fact]
    public void Constructor_ValidFormData_CreatesInstance()
    {
        var formData = new Dictionary<string, string> { ["key"] = "value" };

        var content = new DefaultFormContent(formData);

        content.Should().NotBeNull();
        content.Should().BeAssignableTo<IFormContent>();
    }

    [Fact]
    public void ToHttpContent_ReturnsFormUrlEncodedContent()
    {
        var formData = new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret"
        };
        var content = new DefaultFormContent(formData);

        var httpContent = content.ToHttpContent();

        httpContent.Should().NotBeNull();
        httpContent.Should().BeOfType<FormUrlEncodedContent>();
    }

    [Fact]
    public async Task ToHttpContent_ContainsCorrectContentType()
    {
        var formData = new Dictionary<string, string> { ["key"] = "value" };
        var content = new DefaultFormContent(formData);

        var httpContent = content.ToHttpContent();

        httpContent.Headers.ContentType.Should().NotBeNull();
        httpContent.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
    }

    [Fact]
    public async Task ToHttpContent_EncodesFormDataCorrectly()
    {
        var formData = new Dictionary<string, string>
        {
            ["name"] = "test value",
            ["email"] = "test@example.com"
        };
        var content = new DefaultFormContent(formData);

        var httpContent = content.ToHttpContent();
        var result = await httpContent.ReadAsStringAsync();

        result.Should().Contain("name=test+value");
        result.Should().Contain("email=test%40example.com");
    }

    [Fact]
    public async Task ToHttpContentAsync_ReturnsSameAsSync()
    {
        var formData = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };
        var content = new DefaultFormContent(formData);

        var syncResult = content.ToHttpContent();
        var asyncResult = await content.ToHttpContentAsync();

        asyncResult.Should().NotBeNull();
        asyncResult.Should().BeOfType<FormUrlEncodedContent>();

        var syncString = await syncResult.ReadAsStringAsync();
        var asyncString = await asyncResult.ReadAsStringAsync();

        syncString.Should().Be(asyncString);
    }

    [Fact]
    public async Task ToHttpContentAsync_WithProgress_ReturnsContent()
    {
        var formData = new Dictionary<string, string> { ["key"] = "value" };
        var content = new DefaultFormContent(formData);
        var progress = new Progress<long>(_ => { });

        var httpContent = await content.ToHttpContentAsync(progress, CancellationToken.None);

        httpContent.Should().NotBeNull();
    }

    [Fact]
    public async Task ToHttpContentAsync_WithCancellationToken_ReturnsContent()
    {
        var formData = new Dictionary<string, string> { ["key"] = "value" };
        var content = new DefaultFormContent(formData);

        using var cts = new CancellationTokenSource();
        var httpContent = await content.ToHttpContentAsync(null, cts.Token);

        httpContent.Should().NotBeNull();
    }

    [Fact]
    public void ToHttpContent_EmptyDictionary_ReturnsEmptyContent()
    {
        var formData = new Dictionary<string, string>();
        var content = new DefaultFormContent(formData);

        var httpContent = content.ToHttpContent();

        httpContent.Should().NotBeNull();
        httpContent.Should().BeOfType<FormUrlEncodedContent>();
    }

    [Fact]
    public async Task ToHttpContent_SpecialCharacters_EncodedCorrectly()
    {
        var formData = new Dictionary<string, string>
        {
            ["query"] = "a=b&c=d",
            ["path"] = "/api/test",
            ["unicode"] = "中文测试"
        };
        var content = new DefaultFormContent(formData);

        var httpContent = content.ToHttpContent();
        var result = await httpContent.ReadAsStringAsync();

        result.Should().Contain("query=a%3Db%26c%3Dd");
        result.Should().Contain("path=%2Fapi%2Ftest");
    }
}
