using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Tests;

public class DirectEnhancedHttpClientTests
{
    private static IEncryptionProvider CreateTestEncryptionProvider()
    {
        var options = new AesEncryptionOptions
        {
            Key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng=="),
            IV = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==")
        };
        return new DefaultAesEncryptionProvider(Options.Create(options));
    }

    private static DirectEnhancedHttpClient CreateClient(IEncryptionProvider? encryptionProvider = null)
    {
        var httpClient = new HttpClient();
        return new DirectEnhancedHttpClient(httpClient, encryptionProvider: encryptionProvider);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new DirectEnhancedHttpClient(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithValidArguments_CreatesInstance()
    {
        var client = CreateClient();

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEncryptionProvider_CreatesInstance()
    {
        var client = CreateClient(CreateTestEncryptionProvider());

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithLogger_CreatesInstance()
    {
        var httpClient = new HttpClient();
        var logger = new Mock<ILogger>().Object;
        var client = new DirectEnhancedHttpClient(httpClient, logger: logger);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithInterceptors_CreatesInstance()
    {
        var httpClient = new HttpClient();
        var requestInterceptor = new Mock<IHttpRequestInterceptor>().Object;
        var responseInterceptor = new Mock<IHttpResponseInterceptor>().Object;

        var client = new DirectEnhancedHttpClient(
            httpClient,
            requestInterceptors: new[] { requestInterceptor },
            responseInterceptors: new[] { responseInterceptor });

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithAllowCustomBaseUrls_CreatesInstance()
    {
        var httpClient = new HttpClient();
        var client = new DirectEnhancedHttpClient(httpClient, allowCustomBaseUrls: true);

        client.Should().NotBeNull();
    }

    #endregion

    #region EncryptContent Tests

    [Fact]
    public void EncryptContent_WithNullContent_ThrowsArgumentNullException()
    {
        var client = CreateClient(CreateTestEncryptionProvider());

        var act = () => client.EncryptContent(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("content");
    }

    [Fact]
    public void EncryptContent_WithEmptyPropertyName_ThrowsArgumentException()
    {
        var client = CreateClient(CreateTestEncryptionProvider());
        var testData = new { Name = "Test" };

        var act = () => client.EncryptContent(testData, "");

        act.Should().Throw<ArgumentException>().WithParameterName("propertyName");
    }

    [Fact]
    public void EncryptContent_WithNullPropertyName_ThrowsArgumentException()
    {
        var client = CreateClient(CreateTestEncryptionProvider());
        var testData = new { Name = "Test" };

        var act = () => client.EncryptContent(testData, null!);

        act.Should().Throw<ArgumentException>().WithParameterName("propertyName");
    }

    [Fact]
    public void EncryptContent_WithoutProvider_ThrowsInvalidOperationException()
    {
        var client = CreateClient();
        var testData = new { Name = "Test" };

        var act = () => client.EncryptContent(testData, "data", SerializeType.Json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*未配置加密提供器*");
    }

    [Fact]
    public void EncryptContent_WithProvider_ReturnsEncryptedString()
    {
        var client = CreateClient(CreateTestEncryptionProvider());
        var testData = new { Name = "Test", Value = 42 };

        var result = client.EncryptContent(testData, "data", SerializeType.Json);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void EncryptContent_WithDefaultPropertyName_UsesData()
    {
        var client = CreateClient(CreateTestEncryptionProvider());
        var testData = new { Name = "Test" };

        var result = client.EncryptContent(testData);

        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region DecryptContent Tests

    [Fact]
    public void DecryptContent_WithEmptyString_ReturnsEmptyString()
    {
        var client = CreateClient(CreateTestEncryptionProvider());

        var result = client.DecryptContent(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecryptContent_WithNull_ReturnsEmptyString()
    {
        var client = CreateClient(CreateTestEncryptionProvider());

        var result = client.DecryptContent(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecryptContent_WithoutProvider_ThrowsInvalidOperationException()
    {
        var client = CreateClient();

        var act = () => client.DecryptContent("somedata");

        act.Should().Throw<InvalidOperationException>().WithMessage("*未配置加密提供器*");
    }

    [Fact]
    public void DecryptContent_WithEncryptedData_ReturnsDecryptedString()
    {
        var provider = CreateTestEncryptionProvider();
        var client = CreateClient(provider);
        var originalData = "Hello, World!";
        var encrypted = provider.Encrypt(originalData);

        var decrypted = client.DecryptContent(encrypted);

        decrypted.Should().Be(originalData);
    }

    [Fact]
    public void EncryptContent_AndDecryptContent_RoundTrip()
    {
        var provider = CreateTestEncryptionProvider();
        var client = CreateClient(provider);
        var originalData = new { Name = "Test", Value = 42 };

        var encrypted = client.EncryptContent(originalData, "data", SerializeType.Json);
        var decrypted = client.DecryptContent(encrypted);

        decrypted.Should().Contain("Test");
        decrypted.Should().Contain("42");
    }

    #endregion

    #region EncryptBytes / DecryptBytes Tests

    [Fact]
    public void EncryptBytes_WithNullData_ThrowsArgumentNullException()
    {
        var client = CreateClient(CreateTestEncryptionProvider());

        var act = () => client.EncryptBytes(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("data");
    }

    [Fact]
    public void EncryptBytes_WithoutProvider_ThrowsInvalidOperationException()
    {
        var client = CreateClient();

        var act = () => client.EncryptBytes(new byte[] { 1, 2, 3 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*未配置加密提供器*");
    }

    [Fact]
    public void EncryptBytes_WithProvider_ReturnsEncryptedBytes()
    {
        var provider = CreateTestEncryptionProvider();
        var client = CreateClient(provider);
        var data = Encoding.UTF8.GetBytes("test data");

        var encrypted = client.EncryptBytes(data);

        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Should().NotEqual(data);
    }

    [Fact]
    public void DecryptBytes_WithNullData_ThrowsArgumentNullException()
    {
        var client = CreateClient(CreateTestEncryptionProvider());

        var act = () => client.DecryptBytes(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("encryptedData");
    }

    [Fact]
    public void DecryptBytes_WithoutProvider_ThrowsInvalidOperationException()
    {
        var client = CreateClient();

        var act = () => client.DecryptBytes(new byte[] { 1, 2, 3 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*未配置加密提供器*");
    }

    [Fact]
    public void EncryptBytes_AndDecryptBytes_RoundTrip()
    {
        var provider = CreateTestEncryptionProvider();
        var client = CreateClient(provider);
        var originalData = Encoding.UTF8.GetBytes("test binary data");

        var encrypted = client.EncryptBytes(originalData);
        var decrypted = client.DecryptBytes(encrypted);

        decrypted.Should().Equal(originalData);
    }

    #endregion
}
