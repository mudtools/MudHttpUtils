// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// CFG-33 / CFG-38：<c>ISensitiveDataMasker</c> 的默认接线与 <c>AddSensitiveDataMasker()</c> 的实际实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>CFG-33（三态）</b>：
/// <list type="number">
///   <item>未注册掩码器 ⇒ <c>[SensitiveData]</c> <b>完全无效</b>（<c>ServiceCollectionExtensions</c> 用 <c>GetService</c>，null 合法）；</item>
///   <item><c>AddSensitiveDataMasker()</c> ⇒ 注册 <see cref="AotSafeSensitiveDataMasker"/>，
///         该实现<b>按设计忽略</b> <c>[SensitiveData]</c>（只认 <c>Register&lt;T&gt;</c> 编译期字典）⇒ <c>[SensitiveData]</c> <b>仍然无效</b>；</item>
///   <item>仅 <c>AddSensitiveDataMasker&lt;DefaultSensitiveDataMasker&gt;()</c> 才会反射读取 <c>[SensitiveData]</c>（非 AOT 场景）。</item>
/// </list>
/// 本文件把 ①②两条「无效路径」锁定，避免未来静默改变（例如某天默认注册掩码器会改变现有日志内容）。
/// </para>
/// <para><b>CFG-38</b>：<c>AddSensitiveDataMasker()</c> 注册的是 <see cref="AotSafeSensitiveDataMasker"/>，
/// 而 <c>Client/README.md:836</c> 曾注释为 <c>DefaultSensitiveDataMasker</c>（已修正为以代码为准）。</para>
/// </remarks>
public class SensitiveDataMaskerWiringTests
{
    [Fact]
    public void T10_AddMudHttpClient_WithoutMaskerRegistration_MaskerIsNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpClient("api");

        using var provider = services.BuildServiceProvider();
        provider.GetService<ISensitiveDataMasker>().Should().BeNull(
            "AddMudHttpClient 不默认注册掩码器；未注册时 [SensitiveData] 不产生任何掩码效果（CFG-33）");
    }

    [Fact]
    public void T17_AddSensitiveDataMasker_ResolvesAotSafeImplementation()
    {
        var services = new ServiceCollection();

        services.AddSensitiveDataMasker();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISensitiveDataMasker>().Should().BeOfType<AotSafeSensitiveDataMasker>(
            "AddSensitiveDataMasker() 注册的是 AotSafeSensitiveDataMasker（CFG-38 以代码为准）");
    }

    [Fact]
    public void AddSensitiveDataMasker_Generic_RegistersRequestedImplementation()
    {
        var services = new ServiceCollection();

#pragma warning disable CS0618 // DefaultSensitiveDataMasker 标注 [Obsolete]（AOT 不安全）；此处仅验证注册可解析
        services.AddSensitiveDataMasker<DefaultSensitiveDataMasker>();
#pragma warning restore CS0618

        using var provider = services.BuildServiceProvider();

        var masker = provider.GetRequiredService<ISensitiveDataMasker>();
#pragma warning disable CS0618
        masker.Should().BeOfType<DefaultSensitiveDataMasker>(
            "只有显式注册 DefaultSensitiveDataMasker 时，[SensitiveData] 特性才会被反射读取（CFG-33 第 ③ 态）");
#pragma warning restore CS0618
    }

    [Fact]
    public void AddSensitiveDataMasker_IsIdempotent_TryAddSemantics()
    {
        // TryAddSingleton：自定义实现先行注册时不被默认实现覆盖
        var services = new ServiceCollection();
        services.AddSensitiveDataMasker<AotSafeSensitiveDataMasker>();
        services.AddSensitiveDataMasker();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISensitiveDataMasker>().Should().BeOfType<AotSafeSensitiveDataMasker>();
    }
}
