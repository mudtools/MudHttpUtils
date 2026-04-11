// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// HTTP 声明式Token参数特性，用于自动注入Token到HTTP请求
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TokenAttribute : Attribute
{
    /// <summary>
    /// <inheritdoc cref="TokenAttribute" />
    /// </summary>
    /// <param name="tokenType">Token类型</param>
    public TokenAttribute(string tokenType = "TenantAccessToken")
    {
        TokenType = tokenType;
    }

    /// <summary>
    /// Token类型
    /// </summary>
    public string TokenType { get; set; } = "TenantAccessToken";

    /// <summary>
    /// Token注入模式（Header/Query/Path）
    /// <para>默认: <see cref="TokenInjectionMode.Header"/></para>
    /// </summary>
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;

    /// <summary>
    /// Token在Header或Query中的名称
    /// <para>Header模式默认: "Authorization"</para>
    /// <para>Query模式默认: "access_token"</para>
    /// <para>Path模式: 路径中的占位符名称，如 "{token}" 中的 "token"</para>
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 是否替换已存在的同名Header/Query参数
    /// </summary>
    public bool Replace { get; set; } = true;
}
