// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;


/// <summary>
/// 标记属性为敏感数据，在日志记录时进行掩码处理。
/// </summary>
/// <remarks>
/// <para>
/// 指示该属性包含敏感数据（如密码、令牌、信用卡号等），在日志记录和监控中应进行掩码处理以防止数据泄露。
/// </para>
/// <para>
/// <b>CFG-11</b>：本特性<strong>仅对对象属性生效</strong>——<c>DefaultSensitiveDataMasker</c> 通过反射遍历
/// <c>Type.GetProperties()</c> 读取本特性；<c>AotSafeSensitiveDataMasker</c> 则由编译期注册驱动（忽略本特性）。
/// </para>
/// <para>
/// <b>CFG-33：生效前提（三方必须同时满足，且失败时无任何提示）</b>
/// <list type="number">
///   <item>DI 中必须已注册 <c>ISensitiveDataMasker</c>（<c>AddMudHttpClient</c> <b>不会</b>默认注册，
///         未注册时该服务为 <c>null</c> ⇒ 本特性完全无效）；</item>
///   <item>注册的必须是<b>反射式</b>实现 <c>DefaultSensitiveDataMasker</c>
///         （即 <c>services.AddSensitiveDataMasker&lt;DefaultSensitiveDataMasker&gt;()</c>，
///         该实现标注了 <c>[Obsolete]</c>，<b>非 AOT 安全</b>）；
///         <b>注意</b>：无参的 <c>services.AddSensitiveDataMasker()</c> 注册的是
///         <c>AotSafeSensitiveDataMasker</c>，它<b>按设计忽略本特性</b>（只认 <c>Register&lt;T&gt;</c> 的编译期字典），
///         因此该重载<b>不会</b>让本特性生效；</item>
///   <item>标注对象的脱敏入口确实走到该掩码器（如错误响应体脱敏 / <c>ISensitiveDataMasker.MaskObject</c>）。</item>
/// </list>
/// </para>
/// <para>
/// AOT 场景请改用 <c>AddSensitiveDataMasker()</c> + <c>Register&lt;T&gt;(...)</c> 显式登记 DTO 类型，
/// 不要依赖本特性（AOT 下反射元数据可能被裁剪，导致脱敏静默失效）。
/// </para>
/// <para>
/// 因此 <see cref="AttributeUsageAttribute.ValidOn"/> 已收窄为仅 <see cref="AttributeTargets.Property"/>，
/// 应用于方法参数将产生编译错误 <c>CS0592</c>（原先允许标注在参数上但<strong>不会产生任何掩码效果</strong>，属静默失效）。
/// 如需对方法参数脱敏，请在请求 DTO 属性上标注本特性，或自定义 <c>ISensitiveDataMasker</c>。
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
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
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
