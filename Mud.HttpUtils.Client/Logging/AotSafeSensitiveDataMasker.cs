// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// AOT 安全的敏感数据掩码器（编译期字典式实现）。
/// </summary>
/// <remarks>
/// <para>
/// 替代 <see cref="DefaultSensitiveDataMasker"/>（使用反射，AOT 下不安全）。
/// 通过预注册的 {类型 → 属性脱敏规则} 字典在编译期已知所有脱敏目标，
/// 无运行时反射，Native AOT 裁剪后仍可正确工作。
/// </para>
/// <para>
/// 消费方须通过 <see cref="Register{T}"/> 方法注册需要脱敏的类型规则。
/// <b>未注册的类型将返回 <c>[TypeName]</c> 而非真正脱敏</b>，请确保所有需要脱敏的 DTO 均已注册。
/// </para>
/// <para>
/// 此类是线程安全的，可以在多线程环境中安全使用。
/// </para>
/// </remarks>
/// <example>
/// 使用示例：
/// <code>
/// var masker = new AotSafeSensitiveDataMasker();
/// masker.Register&lt;UserDto&gt;(obj =>
/// {
///     var user = (UserDto)obj;
///     return $"{{\"id\":{user.Id},\"name\":\"{user.Name}\",\"email\":\"{masker.Mask(user.Email)}\"}}";
/// });
///
/// // 注册为默认脱敏器
/// services.AddSensitiveDataMasker();
/// </code>
/// </example>
public class AotSafeSensitiveDataMasker : ISensitiveDataMasker
{
    /// <summary>
    /// 掩码字符串常量，用于替换敏感数据。
    /// </summary>
    private const string MaskString = "***";

    private readonly ConcurrentDictionary<Type, Func<object, string>> _maskers = new();

    /// <summary>
    /// 注册类型的脱敏规则。
    /// </summary>
    /// <typeparam name="T">需要脱敏的类型。</typeparam>
    /// <param name="maskFunc">脱敏函数，接收类型实例并返回脱敏后的字符串表示。</param>
    public void Register<T>(Func<T, string> maskFunc) where T : class
    {
        if (maskFunc == null)
            throw new ArgumentNullException(nameof(maskFunc));
        _maskers[typeof(T)] = obj => maskFunc((T)obj);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 此方法的行为与 <see cref="DefaultSensitiveDataMasker.Mask"/> 完全一致，
    /// 确保从反射实现切换到 AOT 安全实现时无行为差异。
    /// </remarks>
    public string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2)
    {
        if (string.IsNullOrEmpty(value))
            return MaskString;

        switch (mode)
        {
            case SensitiveDataMaskMode.Hide:
                return MaskString;

            case SensitiveDataMaskMode.Mask:
                if (value.Length <= prefixLength + suffixLength)
                    return MaskString;

                var prefix = value.Substring(0, prefixLength);
                var suffix = value.Substring(value.Length - suffixLength);
                return $"{prefix}{MaskString}{suffix}";

            case SensitiveDataMaskMode.TypeOnly:
                return $"[String, Length={value.Length}]";

            default:
                return MaskString;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// 使用预注册的字典查找类型的脱敏规则。无运行时反射。
    /// </para>
    /// <para>
    /// <b>注意</b>：未注册的类型将返回 <c>[TypeName]</c>，不会进行脱敏。
    /// 请确保所有需要脱敏的 DTO 均已通过 <see cref="Register{T}"/> 注册。
    /// </para>
    /// </remarks>
    public string MaskObject(object obj)
    {
        if (obj == null)
            return "null";

        var type = obj.GetType();
        if (_maskers.TryGetValue(type, out var masker))
            return masker(obj);

        // 未注册的类型返回类型信息（不序列化未知类型，避免反射）
        return $"[{type.Name}]";
    }
}
