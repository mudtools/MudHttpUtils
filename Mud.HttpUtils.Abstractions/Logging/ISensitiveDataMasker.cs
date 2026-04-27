namespace Mud.HttpUtils;

/// <summary>
/// 敏感数据掩码模式枚举，定义了在日志中如何处理敏感数据的显示方式。
/// </summary>
public enum SensitiveDataMaskMode
{
    /// <summary>
    /// 隐藏模式：完全隐藏敏感数据，通常显示为固定占位符（如 "***" 或 "[REDACTED]"）。
    /// 适用于高度敏感的信息，如密码、密钥等。
    /// </summary>
    Hide,

    /// <summary>
    /// 掩码模式：部分显示敏感数据，保留前缀和后缀，中间部分用掩码字符替换。
    /// 例如：信用卡号 "1234567890123456" 可能显示为 "12**********3456"。
    /// 这是默认模式，平衡了安全性和调试便利性。
    /// </summary>
    Mask,

    /// <summary>
    /// 仅类型模式：不显示任何实际数据，只显示数据类型信息。
    /// 例如：可能显示为 "[String: 16 chars]" 或 "[CreditCard]"。
    /// 适用于需要知道数据类型但完全不需要查看实际值的场景。
    /// </summary>
    TypeOnly
}

/// <summary>
/// 敏感数据掩码器接口，用于在日志记录和监控中保护敏感信息。
/// 实现此接口的类可以对字符串值或对象中的敏感字段进行掩码处理，
/// 防止敏感数据（如密码、令牌、信用卡号等）泄露到日志文件中。
/// </summary>
/// <remarks>
/// <para>
/// 该接口通常与日志系统集成，在记录 HTTP 请求/响应、认证信息等场景中使用。
/// 可以通过 <see cref="SensitiveDataAttribute"/> 特性标记需要掩码的字段或属性。
/// </para>
/// <para>
/// 实现者应该考虑性能影响，因为掩码操作可能在高频日志记录中被调用。
/// </para>
/// </remarks>
/// <example>
/// 使用示例：
/// <code>
/// public class MySensitiveDataMasker : ISensitiveDataMasker
/// {
///     public string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2)
///     {
///         if (string.IsNullOrEmpty(value)) return value;
///         
///         return mode switch
///         {
///             SensitiveDataMaskMode.Hide => "***",
///             SensitiveDataMaskMode.Mask => value.Length > prefixLength + suffixLength 
///                 ? value.Substring(0, prefixLength) + new string('*', value.Length - prefixLength - suffixLength) + value.Substring(value.Length - suffixLength)
///                 : new string('*', value.Length),
///             SensitiveDataMaskMode.TypeOnly => $"[String: {value.Length} chars]",
///             _ => value
///         };
///     }
///     
///     public string MaskObject(object obj)
///     {
///         // 实现对象掩码逻辑...
///     }
/// }
/// </code>
/// </example>
public interface ISensitiveDataMasker
{
    /// <summary>
    /// 对字符串值进行敏感数据掩码处理。
    /// </summary>
    /// <param name="value">需要进行掩码处理的原始字符串值。</param>
    /// <param name="mode">掩码模式，控制如何显示掩码后的数据。默认为 <see cref="SensitiveDataMaskMode.Mask"/>。</param>
    /// <param name="prefixLength">在掩码模式下，保留的前缀字符数量。默认为 2。</param>
    /// <param name="suffixLength">在掩码模式下，保留的后缀字符数量。默认为 2。</param>
    /// <returns>经过掩码处理后的字符串。如果输入为 null 或空字符串，则返回原值。</returns>
    /// <remarks>
    /// <para>
    /// 当 <paramref name="mode"/> 为 <see cref="SensitiveDataMaskMode.Hide"/> 时，
    /// <paramref name="prefixLength"/> 和 <paramref name="suffixLength"/> 参数将被忽略。
    /// </para>
    /// <para>
    /// 当 <paramref name="mode"/> 为 <see cref="SensitiveDataMaskMode.TypeOnly"/> 时，
    /// 所有参数都将被忽略，只返回类型信息。
    /// </para>
    /// </remarks>
    string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2);

    /// <summary>
    /// 对对象进行敏感数据掩码处理，通常用于序列化前的对象处理。
    /// </summary>
    /// <param name="obj">需要进行掩码处理的对象实例。</param>
    /// <returns>经过掩码处理后的对象表示形式（通常为 JSON 字符串或其他序列化格式）。</returns>
    /// <remarks>
    /// <para>
    /// 此方法会递归检查对象的所有属性，并对标记为敏感数据的属性进行掩码处理。
    /// 通常与反射和序列化机制配合使用。
    /// </para>
    /// <para>
    /// 对于复杂对象，实现者应该注意循环引用和性能问题。
    /// 建议在实现中使用缓存机制来提高重复对象的处理效率。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">当 <paramref name="obj"/> 为 null 时可能抛出。</exception>
    string MaskObject(object obj);
}
