using FluentAssertions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mud.HttpUtils.Client.Tests;

public class DefaultHmacSignatureProviderTests
{
    private readonly DefaultHmacSignatureProvider _provider = new();
    private const string TestSecretKey = "test-secret-key-12345";

    /// <summary>16 位小写十六进制（8 字节随机值的十六进制表示）。</summary>
    private static readonly Regex NonceHexPattern = new("^[0-9a-f]{16}$", RegexOptions.Compiled);

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

    // ============================================================
    // M6-HC-25：HMAC 防重放入签（时间戳 + 随机数）
    // ============================================================

    /// <summary>默认构造（requireAntiReplay: false）→ <see cref="DefaultHmacSignatureProvider.RequireAntiReplay"/> 为 false。</summary>
    [Fact]
    public void RequireAntiReplay_DefaultConstructor_IsFalse()
    {
        new DefaultHmacSignatureProvider().RequireAntiReplay.Should().BeFalse();
    }

    /// <summary>
    /// 默认（关闭防重放）路径签名确定性不回归，且不向请求写入任何防重放头
    ///（确定性本身已由 <c>GenerateSignatureAsync_SameRequestSameKey_ReturnsSameSignature</c> 覆盖，此处只补头写入面）。
    /// </summary>
    [Fact]
    public async Task GenerateSignatureAsync_Default_DoesNotAddAntiReplayHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        await _provider.GenerateSignatureAsync(request, TestSecretKey);

        request.Headers.Contains("X-Timestamp").Should().BeFalse();
        request.Headers.Contains("X-Nonce").Should().BeFalse();
    }

    /// <summary>开启防重放 → 签名后请求头中出现 X-Timestamp / X-Nonce，且 nonce 为 16 位小写 hex。</summary>
    [Fact]
    public async Task GenerateSignatureAsync_WithAntiReplay_AddsTimestampAndNonceHeaders()
    {
        var provider = new DefaultHmacSignatureProvider(requireAntiReplay: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        await provider.GenerateSignatureAsync(request, TestSecretKey);

        request.Headers.GetValues("X-Timestamp").Should().ContainSingle();
        var nonce = request.Headers.GetValues("X-Nonce").Should().ContainSingle().Subject;
        NonceHexPattern.IsMatch(nonce).Should().BeTrue($"nonce 应为 16 位小写 hex，实际为 {nonce}");
    }

    /// <summary>开启防重放 → 生成签名后立即在同一请求上校验必须通过（复用已写入的头值复算签名）。</summary>
    [Fact]
    public async Task GenerateSignatureAsync_WithAntiReplay_VerifyImmediatelySucceeds()
    {
        var provider = new DefaultHmacSignatureProvider(requireAntiReplay: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var signature = await provider.GenerateSignatureAsync(request, TestSecretKey);
        var verified = await provider.VerifySignatureAsync(request, signature, TestSecretKey);

        verified.Should().BeTrue();
    }

    /// <summary>签名串快照：开启防重放时签名串以 <c>X-Timestamp: </c> 开头，第二行为 <c>X-Nonce: </c>。</summary>
    [Fact]
    public async Task GenerateSignatureAsync_WithAntiReplay_PrependsTimestampAndNonceLines()
    {
        var provider = new DefaultHmacSignatureProvider(requireAntiReplay: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        var signatureString = await InvokeBuildSignatureStringAsync(provider, request);
        var lines = signatureString.Split('\n');

        signatureString.Should().StartWith("X-Timestamp: ");
        lines[1].Should().StartWith("X-Nonce: ");
        NonceHexPattern.IsMatch(lines[1].Substring("X-Nonce: ".Length)).Should().BeTrue();
    }

    /// <summary>复用已有头值：预先设置固定的 X-Timestamp / X-Nonce，两次签名的签名串完全一致（证明复用了已有值）。</summary>
    [Fact]
    public async Task GenerateSignatureAsync_WithAntiReplay_ReusesExistingHeaderValues()
    {
        var provider = new DefaultHmacSignatureProvider(requireAntiReplay: true);
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request1.Headers.Add("X-Timestamp", "1700000000");
        request1.Headers.Add("X-Nonce", "0123456789abcdef");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request2.Headers.Add("X-Timestamp", "1700000000");
        request2.Headers.Add("X-Nonce", "0123456789abcdef");

        var sigString1 = await InvokeBuildSignatureStringAsync(provider, request1);
        var sigString2 = await InvokeBuildSignatureStringAsync(provider, request2);

        sigString1.Should().Be(sigString2);
        sigString1.Should().StartWith("X-Timestamp: 1700000000\nX-Nonce: 0123456789abcdef\n");
    }

    /// <summary>经反射调用私有 <c>BuildSignatureStringAsync</c> 以对签名串做快照断言。</summary>
    private static async Task<string> InvokeBuildSignatureStringAsync(
        DefaultHmacSignatureProvider provider, HttpRequestMessage request)
    {
        var method = typeof(DefaultHmacSignatureProvider).GetMethod(
            "BuildSignatureStringAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task<string>)method!.Invoke(provider, new object[] { request, CancellationToken.None })!;
        return await task;
    }
}
