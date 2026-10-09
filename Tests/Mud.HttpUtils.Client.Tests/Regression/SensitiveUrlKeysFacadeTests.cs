// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 3.0.2：敏感 URL 键公开登记门面（<see cref="SensitiveUrlKeys"/>）回归。
/// 验证门面对 internal <c>SensitiveUrlRedactor.RegisterExtraSensitiveKey</c> 的纯转发语义
/// 与强制掩码契约（R-P1-05① 的下游可达形态）。
/// </summary>
/// <remarks>
/// 进程级登记是全局可变状态：本用例统一使用**专用探针键**（不与其它用例的 URL 相交），
/// 隔离依赖 Client.Tests 程序集级测试串行（HealthChecksTests 声明 DisableTestParallelization=true）。
/// </remarks>
public class SensitiveUrlKeysFacadeTests
{
    private const string ProbeKey = "facade_key_probe_310";
    private const string ProbeKey2 = "facade_key_probe_310_batch";

    [Fact]
    public void SensitiveUrlKeys_Register_ShouldForceMask_EvenWhenGlobalSwitchOff()
    {
        SensitiveUrlKeys.Register(ProbeKey);

        MudHttpObservabilityOptions.RedactUrlInTelemetry = false;
        try
        {
            var url = $"https://example.com/api?{ProbeKey}=SECRET&other=plain";
            var redacted = Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url);

            redacted.Should().Contain($"{ProbeKey}=***REDACTED***",
                "经公开门面登记的键为强制掩码项，不受全局开关约束");
            redacted.Should().Contain("other=plain", "未登记的参数不得被误伤");
        }
        finally
        {
            MudHttpObservabilityOptions.RedactUrlInTelemetry = true;
        }
    }

    [Fact]
    public void SensitiveUrlKeys_Register_ShouldBeIdempotent_AndIgnoreEmpty()
    {
        // 重复登记、空串、null、空白项均不抛不炸
        var act = () =>
        {
            SensitiveUrlKeys.Register(ProbeKey);
            SensitiveUrlKeys.Register(ProbeKey);
            SensitiveUrlKeys.Register(null);
            SensitiveUrlKeys.Register(string.Empty);
            SensitiveUrlKeys.Register("   ");
        };

        act.Should().NotThrow();

        // 空/空白键名不得进入进程级集合（否则会把"匹配一切"变成脱敏规则）：
        // 未登记键的普通 URL 在开关关闭时必须原样保留。
        MudHttpObservabilityOptions.RedactUrlInTelemetry = false;
        try
        {
            var url = "https://example.com/v1/data?page=2";
            Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url).Should().Be(url);
        }
        finally
        {
            MudHttpObservabilityOptions.RedactUrlInTelemetry = true;
        }
    }

    [Fact]
    public void SensitiveUrlKeys_Register_BatchOverload_ShouldHandleNullEmptyAndItems()
    {
        // null 集合、空集合、含 null/空串/空白项的集合均不抛
        var act = () =>
        {
            SensitiveUrlKeys.RegisterAll(null!);
            SensitiveUrlKeys.RegisterAll(Array.Empty<string?>());
            SensitiveUrlKeys.RegisterAll(new string?[] { null, string.Empty, "  " });
        };
        act.Should().NotThrow();

        // 有效项逐项登记生效（与 null/空白项混排）
        SensitiveUrlKeys.RegisterAll(new string?[] { ProbeKey2, null, "  " });

        MudHttpObservabilityOptions.RedactUrlInTelemetry = false;
        try
        {
            var url = $"https://example.com/api?{ProbeKey2}=SECRET&other=plain";
            var redacted = Mud.HttpUtils.Helpers.SensitiveUrlRedactor.Redact(url);

            redacted.Should().Contain($"{ProbeKey2}=***REDACTED***",
                "批量重载逐项转发单参重载，强制掩码语义一致");
            redacted.Should().Contain("other=plain", "未登记的参数不得被误伤");
        }
        finally
        {
            MudHttpObservabilityOptions.RedactUrlInTelemetry = true;
        }
    }
}
