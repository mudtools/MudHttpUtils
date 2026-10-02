// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// <c>PayloadFieldMapGenerator</c> 生成形状快照 + 编译断言。
/// </summary>
/// <remarks>
/// 快照只负责「形状」，可编译性由 <see cref="VerifyFixture.VerifyGenerator"/> 内建的编译断言负责
/// （输入 + 生成产物整体无 Error 级诊断，另含 CS0219 形状守卫）。
/// 生成代码的<b>运行期行为</b>（作用域三级判定、元素集合、非泛型桥）由
/// <see cref="PayloadRuntimeContractTests"/> 覆盖。
/// </remarks>
public class PayloadFieldMapGeneratorTests
{
    private static Task VerifyPayloadAsync(string payloadDeclaration) =>
        RunAsync(payloadDeclaration);

    private static async Task RunAsync(string payloadDeclaration)
    {
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(
            PayloadTestData.Source(payloadDeclaration),
            additionalReferences: null,
            generator: new PayloadFieldMapGenerator());

        await VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>最小载荷：Text + Delimited（含自定义分隔符）。</summary>
    [Fact]
    public Task BasicPayload_Snapshot() => VerifyPayloadAsync(PayloadTestData.BasicPayload);

    /// <summary>全部推断形态 + <c>Method</c> 的两种首参绑定（<c>string?</c> ⇒ <c>n?.Value</c>，<c>PayloadNode?</c> ⇒ <c>n</c>）。</summary>
    [Fact]
    public Task AllFormatsPayload_Snapshot() => VerifyPayloadAsync(PayloadTestData.AllFormatsPayload);

    /// <summary>零绑定映射表（未知事件兜底载荷）：无 <c>Converter</c> 也能生成。</summary>
    [Fact]
    public Task ZeroFieldPayload_Snapshot() => VerifyPayloadAsync(PayloadTestData.ZeroFieldPayload);

    /// <summary>全局命名空间载荷（无 <c>namespace</c> 声明）。</summary>
    [Fact]
    public Task GlobalNamespacePayload_Snapshot() => VerifyPayloadAsync(PayloadTestData.GlobalNamespacePayload);

    /// <summary>显式 <c>ContractId</c> 覆盖类名。</summary>
    [Fact]
    public Task CustomContractIdPayload_Snapshot() => VerifyPayloadAsync(PayloadTestData.CustomContractIdPayload);
}
