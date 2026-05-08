// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;


/// <summary>
/// 标记属性或参数为敏感数据，在日志记录时进行掩码处理。
/// </summary>
/// <remarks>
/// <para>
/// 应用于属性或参数上，指示该字段包含敏感数据（如密码、令牌、信用卡号等），
/// 在日志记录和监控中应进行掩码处理以防止数据泄露。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class LoginRequest
/// {
///     public string Username { get; set; }
///     
///     [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
///     public string Password { get; set; }
/// }
/// 
/// public class PaymentInfo
/// {
///     [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask, PrefixLength = 4, SuffixLength = 4)]
///     public string CardNumber { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class SensitiveDataAttribute : Attribute
{
    /// <summary>
    /// 获取或设置敏感数据的掩码模式。
    /// </summary>
    /// <value>默认为 <see cref="SensitiveDataMaskMode.Mask"/>。</value>
    public SensitiveDataMaskMode MaskMode { get; set; } = SensitiveDataMaskMode.Mask;

    /// <summary>
    /// 获取或设置掩码模式下保留的前缀字符数量。
    /// </summary>
    /// <value>默认为 2。</value>
    public int PrefixLength { get; set; } = 2;

    /// <summary>
    /// 获取或设置掩码模式下保留的后缀字符数量。
    /// </summary>
    /// <value>默认为 2。</value>
    public int SuffixLength { get; set; } = 2;
}
