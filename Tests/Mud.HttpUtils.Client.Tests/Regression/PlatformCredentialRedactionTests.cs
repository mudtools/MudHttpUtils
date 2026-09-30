// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// C-01 回归用例（Mud.Wechat 集成评审 P0-4）：平台凭据参数名的脱敏词表覆盖。
// 背景：企业微信把 corpsecret / suite_access_token / provider_access_token 强制放在 Query（官方契约，非 Header），
// 这些键名不含通用 `token` / `secret` 变体，也不以 `_token` 结尾，故精确匹配词表必须显式列出，
// 否则会随 ApiException.RequestUri、日志与遥测 URL 明文外泄。

using Mud.HttpUtils.Helpers;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 平台（企业微信）凭据参数名必须被脱敏词表覆盖，且不得破坏既有词表语义。
/// </summary>
public class PlatformCredentialRedactionTests
{
    /// <summary>企业微信官方契约中以 Query 承载的凭据参数名（C-01）。</summary>
    public static TheoryData<string> WechatCredentialKeys => new()
    {
        "corpsecret",
        "suite_access_token",
        "provider_access_token",
        "suite_secret",
        "provider_secret",
        "permanent_code",
        "suite_ticket",
    };

    [Theory]
    [MemberData(nameof(WechatCredentialKeys))]
    public void Redact_ShouldMaskWechatCredentialValue(string key)
    {
        var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid=ww-corp&{key}=SUPER-SECRET-VALUE";

        var redacted = SensitiveUrlRedactor.Redact(url);

        redacted.Should().NotContain("SUPER-SECRET-VALUE", $"{key} 是企业微信凭据参数名，必须掩码");
        redacted.Should().Contain("corpid=ww-corp", "非敏感参数保留（排障可用性）");
    }

    [Fact]
    public void Redact_ShouldRespectTelemetrySwitch_ForWordListKeys()
    {
        // 既有语义（M1-#5.3）：RedactUrlInTelemetry = false 时保留完整 URL（已自行治理日志下游的排障逃生门）。
        // 该开关默认 true；平台凭据的脱敏依赖其保持默认（登记 ExtraSensitiveKeys 的键才不受开关约束）。
        var previous = MudHttpObservabilityOptions.RedactUrlInTelemetry;
        MudHttpObservabilityOptions.RedactUrlInTelemetry = false;
        try
        {
            var url = "https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpsecret=SUPER-SECRET-VALUE";

            SensitiveUrlRedactor.Redact(url).Should().Be(url);
        }
        finally
        {
            MudHttpObservabilityOptions.RedactUrlInTelemetry = previous;
        }
    }

    [Fact]
    public void Redact_ShouldNotMaskUnrelatedBusinessParams()
    {
        // 词表扩充不得误伤业务参数（企业微信业务参数多为 snake_case，须逐项确认无碰撞）。
        var url = "https://qyapi.weixin.qq.com/cgi-bin/user/list"
            + "?department_id=1&fetch_child=0&agentid=1001&corp_id=ww-corp&suite_id=ww-suite&template_id=dk-1";

        SensitiveUrlRedactor.Redact(url).Should().Be(url);
    }

    [Fact]
    public void SensitiveFieldNames_ShouldBeSingleSourceForAllThreeConsumers()
    {
        // URL query / 消息体（MessageSanitizer）/ 异常字段（DefaultSensitiveFieldExceptionRedactor）共用本词表。
        SensitiveUrlRedactor.SensitiveFieldNames.Should().Contain(new[]
        {
            "corpsecret", "suite_access_token", "provider_access_token",
            "suite_secret", "provider_secret", "permanent_code", "suite_ticket",
        });
    }
}
