// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 工具面令牌身份枚举——<c>Mud.HttpUtils.Generator</c> 工具面引擎唯一消费的令牌形态抽象。
/// </summary>
/// <remarks>
/// <para>
/// 核心不变量（设计文档 §3）：<b>令牌身份是枚举槽而非命名前缀</b>。飞书 <c>[Tenant|User]</c> 与
/// 微信 <c>_Provider/_ThirdParty/_Internal</c> 的差异由各 SDK profile 的「令牌身份推导策略」吸收，
/// 引擎内部只消费本枚举，永远不看任何 SDK 的命名前缀/后缀字符串。
/// </para>
/// <para>
/// <b>序列化契约</b>：成员的渲染字面量（<c>ToLowerInvariant()</c>：
/// <c>unspecified</c>/<c>tenant</c>/<c>user</c>/<c>third_party</c>/<c>internal</c>/<c>both</c>）
/// 进入工具描述符 JSON 与 golden 快照，是逐字节契约的一部分——成员改名 = golden 漂移。
/// 其中 <c>ThirdParty</c> 渲染为 <c>third_party</c>（snake_case），由引擎的映射表固定，
/// 与 <c>Enum.ToString().ToLowerInvariant()</c>（<c>thirdparty</c>）不同，属有意的契约字面量。
/// </para>
/// </remarks>
public enum SdkTokenKind
{
    /// <summary>无令牌形态的元工具（capability_lookup 等）。</summary>
    Unspecified = 0,

    /// <summary>租户/企业级令牌（飞书 TenantAccessToken、微信 corp）。</summary>
    Tenant = 1,

    /// <summary>用户级令牌（飞书 UserAccessToken、微信成员）。</summary>
    User = 2,

    /// <summary>第三方/服务商令牌（微信第三方应用 / _ThirdParty / _Provider）。</summary>
    ThirdParty = 3,

    /// <summary>自建/内部应用令牌（微信自建应用 / _Internal）。</summary>
    Internal = 4,

    /// <summary>
    /// 双令牌基接口（飞书 <c>IFeishuV*</c>，不直接产工具）。
    /// 上游 <c>ToolIdentity.Both</c> 以 <c>"both"</c> 渲染进 golden 与契约表，缺本成员即无法零漂移复现。
    /// </summary>
    Both = 5,
}

/// <summary>
/// <see cref="SdkTokenKind"/> 的契约字面量映射（引擎与消费方共用，保证 golden 逐字节稳定）。
/// </summary>
public static class SdkTokenKindContract
{
    /// <summary>
    /// 求 <paramref name="kind"/> 的描述符渲染字面量（进 JSON 与 golden，改名即漂移）。
    /// </summary>
    /// <param name="kind">令牌身份。</param>
    /// <returns>契约字面量：unspecified/tenant/user/third_party/internal/both。</returns>
    public static string ToLiteral(SdkTokenKind kind) => kind switch
    {
        SdkTokenKind.Tenant => "tenant",
        SdkTokenKind.User => "user",
        SdkTokenKind.ThirdParty => "third_party",
        SdkTokenKind.Internal => "internal",
        SdkTokenKind.Both => "both",
        _ => "unspecified",
    };
}
