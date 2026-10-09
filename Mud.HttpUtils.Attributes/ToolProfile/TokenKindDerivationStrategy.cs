// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 令牌身份推导策略：如何从 SDK 接口名推出 <see cref="SdkTokenKind"/>。
/// </summary>
/// <remarks>
/// 引擎只按本策略 + profile 的 <c>TokenKindMarkers</c> 标记表推导，永不内联任何 SDK 的命名事实。
/// 实测（设计文档 §3 v2.1 修正）：微信同时存在后缀形态
/// （<c>IWechatWorkAccountIdBotService_Provider</c>）与中缀形态
/// （<c>IWechatWorkProviderAccountIdBotService</c>），故策略必须三值，且标记表按形态分列。
/// </remarks>
public enum TokenKindDerivationStrategy
{
    /// <summary>未指定（等价 <see cref="SdkTokenKind.Unspecified"/>，剖面契约守卫报错）。</summary>
    Unspecified = 0,

    /// <summary>前缀标记：接口名以标记串开头（飞书 <c>IFeishuTenant</c>→Tenant、<c>IFeishuUser</c>→User）。</summary>
    NamePrefix = 1,

    /// <summary>后缀标记：接口名以标记串结尾（微信 <c>…_Provider</c>→ThirdParty、<c>…_Internal</c>→Internal）。</summary>
    SuffixMarker = 2,

    /// <summary>中缀标记：接口名在 I + SDK 词后含标记串（微信 <c>IWechatWorkProvider…</c>→ThirdParty）。</summary>
    InfixMarker = 3,
}
