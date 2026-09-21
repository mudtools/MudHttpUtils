// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Moq;

namespace Mud.HttpUtils.Client.Tests.AppAuth;

/// <summary>
/// G8-11：appKey 的<b>大小写敏感</b>语义与 <see cref="AppKey.ToSafeText"/> 的日志用途契约。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppKey.IsValid"/> 与 <c>AppKeyValidator.Validate</c> 的规则逐字一致且<b>均不做</b>大小写归一；
/// <c>DefaultAppManager&lt;TAppContext&gt;</c> 内部为序数语义字典 ⇒ <c>"Tenant"</c> 与 <c>"tenant"</c> 是两个独立应用。
/// </para>
/// <para>
/// 本用例把「大小写敏感」钉死为<b>有意行为</b>而非偶然实现 —— 若有人为「兼容」加上归一化，
/// 会立刻失败并触发设计讨论（归一化会改变已注册应用的可见性，属破坏性语义变更）。
/// </para>
/// </remarks>
public class AppManagerCaseSensitivityTests
{
    [Fact]
    public void AppKey_IsCaseSensitive_ExactMatchOnly()
    {
        var context = new Mock<IMudAppContext>().Object;
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("Tenant", context, isDefault: true);

        // 完全一致的文本命中
        manager.GetApp("Tenant").Should().BeSameAs(context);

        // 仅大小写不同 → 未找到
        manager.TryGetApp("tenant", out _).Should().BeFalse(
            "G8-11：appKey 区分大小写（序数比较），'tenant' 不得命中已注册的 'Tenant'");
        manager.TryGetApp("TENANT", out _).Should().BeFalse("G8-11：appKey 不做大小写归一化");

        var act = () => manager.GetApp("tenant");
        act.Should().Throw<InvalidOperationException>(
            "G8-11：大小写不同的 appKey 视为未注册应用（异常消息中的 appKey 已经 ToSafeText 过滤）");
    }

    [Fact]
    public void AppKey_IsValid_AcceptsBothCases_WithoutNormalization()
    {
        AppKey.IsValid("Tenant").Should().BeTrue();
        AppKey.IsValid("tenant").Should().BeTrue();

        // 校验只做字符集/长度判定，不含任何大小写归一（故二者是不同键）
        AppKey.IsValid("Tenant").Should().Be(AppKey.IsValid("tenant"));
    }

    /// <summary>
    /// <c>ToSafeText</c> 仅用于日志/异常：截断到 <see cref="AppKey.MaxLength"/> 并把控制字符替换为 <c>_</c>；
    /// <b>不得</b>用于查找（查找必须使用原始 appKey）。
    /// </summary>
    [Fact]
    public void ToSafeText_TruncatesAndSanitizes_ForLoggingOnly()
    {
        AppKey.ToSafeText(null).Should().BeEmpty();
        AppKey.ToSafeText(string.Empty).Should().BeEmpty();

        AppKey.ToSafeText(new string('a', AppKey.MaxLength + 50)).Length
            .Should().Be(AppKey.MaxLength, "G8-11：超长 appKey 必须截断到 128 字符（防日志膨胀）");

        AppKey.ToSafeText("a\r\nb\0c").Should().Be("a__b_c",
            "G8-11：控制字符（含 CR/LF/NUL）替换为 '_'，防日志注入");
    }
}
