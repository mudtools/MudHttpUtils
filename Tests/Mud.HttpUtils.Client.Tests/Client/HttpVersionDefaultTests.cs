// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// CFG-30（v3.1 复核修订）/ 不变量 I-14：HTTP 版本配置的默认值与生效语义。
/// </summary>
/// <remarks>
/// <para>
/// <b>复核结论（与 v3.0 方案的差异）</b>：<c>HttpClient.DefaultRequestVersion</c> /
/// <c>DefaultVersionPolicy</c> 官方文档明确<b>不适用于 <c>SendAsync(HttpRequestMessage)</c></b>
/// （仅作用于 <c>GetAsync</c>/<c>PostAsync</c> 等由 <c>HttpClient</c> 内部创建请求的便捷重载）。
/// 本库两条路径（<c>EnhancedHttpClient</c> / <c>DefaultHttpRequestExecutor</c>）均自建
/// <c>HttpRequestMessage</c> 后调用 <c>SendAsync</c>，因此：
/// <list type="number">
///   <item>不存在「<c>EnhancedHttpClientOptions.HttpVersion</c> 覆盖了 <c>DefaultRequestVersion</c>」这一冲突；</item>
///   <item>默认值由 <c>Version11</c> 改为 <c>null</c> <b>不产生可观察行为差异</b> ——
///         <c>HttpRequestMessage.Version</c> 的构造默认值本就是 1.1，<c>VersionPolicy</c> 本就是
///         <c>RequestVersionOrLower</c>。</item>
/// </list>
/// 因此本文件<b>不</b>断言「<c>DefaultRequestVersion</c> 生效」（该断言在任何实现下都不可能成立），
/// 只断言「未配置 ⇒ 不干预（保持 1.1）」与「已配置 ⇒ 原样应用」。
/// </para>
/// </remarks>
public class HttpVersionDefaultTests
{
    private static async Task<HttpRequestMessage> CaptureRequestAsync(DefaultHttpRequestExecutor executor)
    {
        HttpRequestMessage? captured = null;

        var mockClient = new Mock<IBaseHttpClient>();
        mockClient
            .Setup(c => c.SendAsAsyncEnumerable<int>(
                It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequestMessage, object?, CancellationToken>((req, _, _) => captured = req)
            .Returns(EmptySequence());

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/x");

        // null 需显式转型 object?，否则重载决策会绑定 JsonTypeInfo<T> 重载
        await foreach (var _ in executor.SendAsAsyncEnumerable<int>(request, mockClient.Object, (object?)null))
        {
        }

        captured.Should().NotBeNull();
        return captured!;
    }

    private static async IAsyncEnumerable<int> EmptySequence()
    {
        await Task.CompletedTask;
        yield break;
    }

    [Fact]
    public async Task T6_DefaultOptions_DoNotSetRequestVersion()
    {
        // httpVersion / httpVersionPolicy 均缺省（=== EnhancedHttpClientOptions 的新默认值 null）
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var request = await CaptureRequestAsync(executor);

        request.Version.Should().Be(HttpVersion.Version11,
            "未配置 HttpVersion 时不得干预请求版本（保持 HttpRequestMessage 的构造默认值 1.1）");
        request.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionOrLower);
    }

    [Fact]
    public async Task T6b_ConfiguredHttpVersion_IsAppliedToRequest()
    {
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            httpVersion: new Version(2, 0),
            httpVersionPolicy: HttpVersionPolicy.RequestVersionExact);

        var request = await CaptureRequestAsync(executor);

        request.Version.Should().Be(new Version(2, 0));
        request.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionExact);
    }

    [Fact]
    public void EnhancedHttpClientOptions_HttpVersionDefaultsToNull()
    {
        // CFG-30：与无 DI 路径的 GeneratedClientOptions.HttpVersion（默认 null）对齐
        var options = new EnhancedHttpClientOptions();

        options.HttpVersion.Should().BeNull();
        options.HttpVersionPolicy.Should().BeNull();
    }

    [Fact]
    public void GeneratedClientOptions_HttpVersionDefaultsToNull()
    {
        // 对照：无 DI 路径的共享契约既有默认值（CFG-30 的「对齐目标」）
        var options = new GeneratedClientOptions();

        options.HttpVersion.Should().BeNull();
        options.HttpVersionPolicy.Should().BeNull();
    }
}
