// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// `SW-15`：<see cref="AppKeySwitchExtensions"/> 的守卫消息必须与**生成代码**逐字一致。
/// </summary>
/// <remarks>
/// <para>
/// 两者由不同机制产生：生成代码由 <c>ConstructorGenerator.GenerateAppKeyGuard</c> 以字符串发射，
/// 扩展方法由 <c>Mud.HttpUtils.Abstractions</c> 直接抛出。它们<b>不能共用常量</b> ——
/// 生成器属 analyzer，按设计**不引用** <c>Mud.HttpUtils.Abstractions</c> 程序集。
/// </para>
/// <para>
/// 因此只能靠本用例做「跨项目一致性」守卫：同一守卫在两条实现路径下的用户可见消息必须相同，
/// 否则调用方会看到两套措辞，误以为是不同问题（历史缺陷模式）。
/// </para>
/// </remarks>
public class AppKeyGuardConsistencyTests
{
    private const string DefaultModeSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IGuardConsistencyProbeApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    [Fact]
    public void GuardMessages_AreVerbatimIdentical_BetweenExtensionAndGeneratedCode()
    {
        var generatedCode = GenerateImplementation(DefaultModeSource);

        var holder = new AsyncLocalAppContextSwitcher();
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("guard-app-a", new TestAppContext("guard-app-a"), isDefault: true);

        var invalidAppKey = Record.Exception(
            () => holder.SwitchToApp("!!! 非法 appKey", manager, new AlwaysAllowAuthorizer()))!;
        var missingAuthorizer = Record.Exception(
            () => holder.SwitchToApp("guard-app-a", manager, authorizer: null))!;
        var denied = Record.Exception(
            () => holder.SwitchToApp("guard-app-a", manager, new AlwaysDenyAuthorizer()))!;

        invalidAppKey.Should().BeOfType<ArgumentException>();
        missingAuthorizer.Should().BeOfType<InvalidOperationException>();
        denied.Should().BeOfType<UnauthorizedAccessException>();

        // ArgumentException.Message 会由 .NET 自动追加 " (Parameter 'appKey')" 后缀；生成代码同样以
        // nameof(appKey) 抛出 ⇒ 后缀一致。此处取「消息本体」与生成代码中的文本比对。
        generatedCode.Should().Contain(StripParameterSuffix(invalidAppKey.Message),
            "格式校验消息必须在扩展方法与生成代码中逐字一致（跨项目不能共用常量，只能靠本条守卫）");
        generatedCode.Should().Contain(missingAuthorizer.Message,
            "『默认拒绝』消息必须逐字一致 —— 它是接线缺陷的唯一提示");
        generatedCode.Should().Contain(denied.Message,
            "业务拒绝消息必须逐字一致");
    }

    /// <summary>移除 <see cref="ArgumentException"/> 自动追加的 <c>" (Parameter '...')"</c> 后缀。</summary>
    /// <remarks>只影响格式校验消息：另两条消息使用的是中文全角括号（<c>（</c>），不会被误伤。</remarks>
    private static string StripParameterSuffix(string message)
    {
        var index = message.LastIndexOf(" (", StringComparison.Ordinal);
        return index > 0 ? message.Substring(0, index) : message;
    }

    /// <summary>
    /// 提取生成器产出的接口实现类文本（跳过 Preserve / Registration 等辅助文件）。
    /// </summary>
    private static string GenerateImplementation(string source)
    {
        var (_, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);

        var implementation = outputCompilation.SyntaxTrees
            .Skip(1)
            .Select(t => (Name: Path.GetFileName(t.FilePath), Text: t.ToString()))
            .FirstOrDefault(x => !x.Name.Contains("Preserve")
                                 && !x.Name.Contains("Registration")
                                 && !x.Name.Contains("EventHandler")
                                 && !x.Name.Contains("FormContent"));

        implementation.Text.Should().NotBeNullOrEmpty("应产出接口实现类");
        return implementation.Text;
    }

    private sealed class AlwaysAllowAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => true;
    }

    private sealed class AlwaysDenyAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => false;
    }

    private sealed class TestAppContext(string appKey) : IMudAppContext
    {
        public string AppKey => appKey;
        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }
}
