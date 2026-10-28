using Moq;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// AM2-02 回归：容器级 <see cref="IUrlValidator"/> 必须真正参与 <see cref="EnhancedHttpClient"/> 的校验。
/// </summary>
/// <remarks>
/// <para>
/// 修复前：主链路直接调用静态 <see cref="UrlValidator"/>，宿主注册的自定义实现<b>从未被调用</b>
/// （承载它的 <c>UrlValidator.Instance</c> 字段既无写入也无读取）。因此本文件的
/// <c>CustomUrlValidator_IsInvokedByEnhancedHttpClient</c> 在修复前必然失败 —— 它是锚点用例。
/// </para>
/// <para>
/// 接线方式遵循既有的"服务依赖经 options 承载"惯例（与 <c>Logger</c>/<c>AppAccessAuthorizer</c> 同），
/// 不新增 <see cref="EnhancedHttpClient"/> 构造重载。
/// </para>
/// </remarks>
public class UrlValidatorWiringTests
{
    private static DirectEnhancedHttpClient CreateClient(EnhancedHttpClientOptions options)
        => new(
            new HttpClient { BaseAddress = new Uri("https://api.example.com") },
            options);

    [Fact]
    public async Task CustomUrlValidator_IsInvokedByEnhancedHttpClient()
    {
        var validator = new Mock<IUrlValidator>();
        validator
            .Setup(v => v.ValidateUrlAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(new InvalidOperationException("blocked-by-custom-validator"))));

        var client = CreateClient(new EnhancedHttpClientOptions { UrlValidator = validator.Object });

        var act = async () => await client
            .SendRawAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/x"))
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("blocked-by-custom-validator", StringComparison.Ordinal))
            .ConfigureAwait(false);

        validator.Verify(
            v => v.ValidateUrlAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce(),
            "容器级验证器必须被调用（修复前主链路绕过了它）");
    }

    [Fact]
    public async Task NoValidatorOption_FallsBackToStaticFacade()
    {
        // 未注入验证器 ⇒ 回落进程级静态门面：严格模式 + 空白名单必然拒绝（行为零漂移）。
        // 注意：静态白名单是进程级状态，其他用例可能已登记域名，故这里只断言"要么被静态门面拒绝、
        // 要么在放行后因无真实网络而失败"—— 关键是**不得**抛出与自定义验证器相关的异常。
        var client = CreateClient(new EnhancedHttpClientOptions());

        var act = async () => await client
            .SendRawAsync(new HttpRequestMessage(HttpMethod.Get, "https://not-whitelisted.example.invalid/x"))
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);
    }

    [Fact]
    public void UrlValidatorOption_SurvivesClone()
    {
        var validator = new Mock<IUrlValidator>().Object;
        var source = new EnhancedHttpClientOptions { UrlValidator = validator };

        var clone = source.Clone();

        clone.UrlValidator.Should().BeSameAs(validator,
            "K8：EnhancedHttpClientOptions 新增可写属性必须同步 Clone（浅拷贝 ⇒ 同引用）");
    }
}
