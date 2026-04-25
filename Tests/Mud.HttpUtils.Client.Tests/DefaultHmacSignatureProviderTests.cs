using FluentAssertions;

namespace Mud.HttpUtils.Client.Tests;

public class DefaultHmacSignatureProviderTests
{
    private readonly DefaultHmacSignatureProvider _provider = new();
    private const string TestSecretKey = "test-secret-key-12345";

    [Fact]
    public async Task GenerateSignatureAsync_NullRequest_Throws()
    {
        var act = async () => await _provider.GenerateSignatureAsync(null!, TestSecretKey);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task GenerateSignatureAsync_EmptySecretKey_Throws()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var act = async () => await _provider.GenerateSignatureAsync(request, "");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("secretKey");
    }

    [Fact]
    public async Task GenerateSignatureAsync_NullSecretKey_Throws()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var act = async () => await _provider.GenerateSignatureAsync(request, null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("secretKey");
    }

    [Fact]
    public async Task GenerateSignatureAsync_GetRequest_ReturnsSignature()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var signature = await _provider.GenerateSignatureAsync(request, TestSecretKey);

        signature.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateSignatureAsync_SameRequestSameKey_ReturnsSameSignature()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var sig1 = await _provider.GenerateSignatureAsync(request1, TestSecretKey);
        var sig2 = await _provider.GenerateSignatureAsync(request2, TestSecretKey);

        sig1.Should().Be(sig2);
    }

    [Fact]
    public async Task GenerateSignatureAsync_DifferentMethods_ReturnsDifferentSignatures()
    {
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test");

        var getSig = await _provider.GenerateSignatureAsync(getRequest, TestSecretKey);
        var postSig = await _provider.GenerateSignatureAsync(postRequest, TestSecretKey);

        getSig.Should().NotBe(postSig);
    }

    [Fact]
    public async Task GenerateSignatureAsync_DifferentKeys_ReturnsDifferentSignatures()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var sig1 = await _provider.GenerateSignatureAsync(request, TestSecretKey);
        var sig2 = await _provider.GenerateSignatureAsync(request, "another-secret-key");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public async Task GenerateSignatureAsync_WithContent_IncludesContentInSignature()
    {
        var requestNoContent = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test");
        var requestWithContent = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent("test body")
        };

        var sigNoContent = await _provider.GenerateSignatureAsync(requestNoContent, TestSecretKey);
        var sigWithContent = await _provider.GenerateSignatureAsync(requestWithContent, TestSecretKey);

        sigNoContent.Should().NotBe(sigWithContent);
    }

    [Fact]
    public async Task VerifySignatureAsync_ValidSignature_ReturnsTrue()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var signature = await _provider.GenerateSignatureAsync(request, TestSecretKey);

        var result = await _provider.VerifySignatureAsync(request, signature, TestSecretKey);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifySignatureAsync_InvalidSignature_ReturnsFalse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var result = await _provider.VerifySignatureAsync(request, "invalid-signature", TestSecretKey);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifySignatureAsync_EmptySignature_ReturnsFalse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var result = await _provider.VerifySignatureAsync(request, "", TestSecretKey);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifySignatureAsync_NullSignature_ReturnsFalse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var result = await _provider.VerifySignatureAsync(request, null!, TestSecretKey);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateSignatureAsync_WithQueryString_SortsParameters()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test?b=2&a=1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test?a=1&b=2");

        var sig1 = await _provider.GenerateSignatureAsync(request1, TestSecretKey);
        var sig2 = await _provider.GenerateSignatureAsync(request2, TestSecretKey);

        sig1.Should().Be(sig2);
    }
}
