// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G9-06：<c>DetectICurrentUserId</c> 收紧为「元数据名精确匹配」的行为钉子。
/// </summary>
/// <remarks>
/// 历史实现按简单名（<c>i.Name == "ICurrentUserId"</c>）匹配——消费方自定义同名异源接口
/// （不同命名空间、不同成员）会被误判为库契约实现，触发 <c>CurrentUserId</c> 成员发射。
/// G9-06 对齐 <c>ValidateTokenManager</c> 的 <c>GetTypeByMetadataName + SymbolEqualityComparer</c> 规范口径。
/// </remarks>
public class CurrentUserIdDetectionPrecisionTests
{
    /// <summary>正例：继承<b>库契约</b> ICurrentUserId 的接口必须保持 CurrentUserId 发射（防矫枉过正）。</summary>
    [Fact]
    public void LibraryICurrentUserId_StillEmitsCurrentUserIdMember()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [Token("UserAccessToken", RequiresUserId = true)]
                [HttpClientApi(TokenManage = "ITestTokenManager")]
                public interface IUserApi : ICurrentUserId
                {
                    [Get("/me")]
                    Task<string> GetMeAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "继承库契约 ICurrentUserId 的正例（G9-06 防矫枉过正）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain("CurrentUserId",
            "继承库契约 Mud.HttpUtils.ICurrentUserId 的接口必须照常发射 CurrentUserId 成员");
        generated.Should().Contain("set => _currentUserContext.SetUserId(value);",
            "库契约实现形态为 get/set 双向穿透 ICurrentUserContext（G9-06 正例的精确形态）");
    }

    /// <summary>反例：消费方自定义<b>同名异源</b> ICurrentUserId 不得按库契约形态发射 CurrentUserId。</summary>
    [Fact]
    public void UserDefinedSameNameICurrentUserId_DoesNotEmitCurrentUserIdMember()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;
            using MyCompany.Lib;

            namespace MyCompany.Lib
            {
                // 与库契约同名但异源异构（成员为 Id 而非 CurrentUserId）
                public interface ICurrentUserId
                {
                    string Id { get; }
                }
            }

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [Token("UserAccessToken", RequiresUserId = true)]
                [HttpClientApi(TokenManage = "ITestTokenManager")]
                public interface IUserApi : MyCompany.Lib.ICurrentUserId
                {
                    [Get("/me")]
                    Task<string> GetMeAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "自定义同名异源 ICurrentUserId 不得被误判（G9-06）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        // 语义澄清（实施验证修正）：AnyMethodRequiresUserId 场景下 CurrentUserId 成员总会发射——
        // 差异在形态：误判为「库契约实现」时发射 get/set 双向穿透 ICurrentUserContext 的实现形态
        //（旧缺陷：对同名异源接口 set 会穿透写入宿主的 ICurrentUserContext）；正确判定为「非库契约」时
        // 走表达式体只读回退形态。断言「实现形态」不出现即钉住判定收敛。
        generated.Should().NotContain("（ICurrentUserId 实现）",
            "同名异源接口不是库契约实现，不得按实现形态发射（G9-06 修复前此处会误发射穿透形态）");
        generated.Should().NotContain("set => _currentUserContext.SetUserId(value);",
            "非库契约实现不得发射对宿主 ICurrentUserContext 的写穿透 setter");
    }
}
