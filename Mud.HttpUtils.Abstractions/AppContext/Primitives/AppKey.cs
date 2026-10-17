// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// MT-18：应用标识（appKey）校验与安全文本化的公开门面。
/// </summary>
/// <remarks>
/// 内部实现为 <c>AppKeyValidator</c>（<c>internal</c>，零正则手写字符判断，AOT 安全），本类逐成员委托，
/// <b>不</b>另行实现规则。由于源生成器产物位于消费方程序集，生成代码无法访问 <c>internal</c> 成员，
/// 故在此暴露只读门面供调用方在授权前进行格式校验与日志安全回显。
/// <para>本类型为纯函数式工具，无状态、无分配（除 <see cref="ToSafeText"/> 的必要输出外）。</para>
/// <para><b>大小写语义</b>：appKey <b>区分大小写</b>，且本类<b>不做</b>任何大小写归一化 ——
/// <c>"Tenant"</c> 与 <c>"tenant"</c> 是两个不同的应用。宿主注册与查询必须使用完全一致的文本
/// （应用管理器内部以序数语义的字典存储）。</para>
/// </remarks>
public static class AppKey
{
    /// <summary>应用标识允许的最大长度（128）。</summary>
    public const int MaxLength = AppKeyValidator.MaxLength;

    /// <summary>
    /// 判断应用标识是否合法：非空、长度不超过 <see cref="MaxLength"/>，
    /// 且仅由字母、数字、<c>'.'</c>、<c>'_'</c>、<c>'-'</c> 组成（首字符必须是字母或数字）。
    /// </summary>
    /// <remarks>大小写敏感，不做归一化。</remarks>
    /// <param name="value">待校验的应用标识。</param>
    /// <returns>合法返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool IsValid(string? value) => AppKeyValidator.IsValid(value);

    /// <summary>
    /// 把应用标识转换为可安全写入日志 / 异常消息的文本（截断到 <see cref="MaxLength"/> 并过滤控制字符）。
    /// </summary>
    /// <remarks>
    /// G9-02：本方法实现通用（截断 + 控制字符过滤），不依赖输入语义——亦适用于 userId 等其他
    /// 外部标识的安全回显（如 <c>DefaultTokenProvider</c> 的用户身份不一致异常），避免后来者误以为仅限 appKey。
    /// </remarks>
    /// <param name="value">原始应用标识（可含不可信输入）。</param>
    /// <returns>安全文本；输入为 null / 空时返回空字符串。</returns>
    public static string ToSafeText(string? value) => AppKeyValidator.ToSafeText(value);
}
