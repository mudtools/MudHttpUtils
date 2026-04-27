// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Mud.HttpUtils;

/// <summary>
/// 默认的敏感数据掩码器实现，提供高效的字符串掩码和对象敏感字段掩码功能。
/// </summary>
/// <remarks>
/// <para>
/// 该类实现了 <see cref="ISensitiveDataMasker"/> 接口，支持多种掩码模式：
/// 完全隐藏、部分掩码和仅显示类型信息。
/// </para>
/// <para>
/// 对于对象掩码，该类使用反射遍历对象属性，并识别标记有 <c>SensitiveDataAttribute</c> 的属性进行掩码处理。
/// 实现了属性信息缓存机制（使用 <see cref="ConcurrentDictionary{TKey,TValue}"/>）以提高性能，
/// 避免重复反射开销。同时支持递归处理嵌套对象，并具备循环引用检测和深度限制保护。
/// </para>
/// <para>
/// 该类是线程安全的，可以在多线程环境中安全使用。
/// </para>
/// </remarks>
/// <example>
/// 使用示例：
/// <code>
/// var masker = new DefaultSensitiveDataMasker();
/// 
/// // 字符串掩码
/// var masked = masker.Mask("1234567890", SensitiveDataMaskMode.Mask, 2, 2);
/// // 输出: "12***90"
/// 
/// // 对象掩码（属性标记了 [SensitiveData] 的会被自动掩码）
/// var user = new { Username = "john", Password = "secret123" };
/// var maskedObj = masker.MaskObject(user);
/// // 输出: JSON 格式，其中标记为敏感的字段会被掩码处理
/// </code>
/// </example>
public class DefaultSensitiveDataMasker : ISensitiveDataMasker
{
    /// <summary>
    /// 掩码字符串常量，用于替换敏感数据。
    /// </summary>
    private const string MaskString = "***";

    /// <summary>
    /// 敏感数据特性名称常量，用于反射识别敏感属性。
    /// </summary>
    private const string SensitiveDataAttributeName = "SensitiveDataAttribute";

    /// <summary>
    /// 最大掩码深度，防止递归处理嵌套对象时发生栈溢出。
    /// </summary>
    private const int MaxMaskDepth = 5;

    /// <summary>
    /// 属性掩码信息缓存，使用类型作为键，避免重复反射开销。
    /// </summary>
    /// <remarks>
    /// 该缓存在应用程序生命周期内持续存在，使用弱引用的对象不会导致内存泄漏，
    /// 因为缓存的键是 Type 对象，而 Type 对象在应用程序域中是单例的。
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, PropertyMaskInfo[]> s_propertyCache = new();

    private sealed class PropertyMaskInfo
    {
        public PropertyInfo Property { get; set; } = null!;
        public bool IsSensitive { get; set; }
        public SensitiveDataMaskMode MaskMode { get; set; }
        public int PrefixLength { get; set; }
        public int SuffixLength { get; set; }
        public bool IsComplexType { get; set; }
    }

    /// <summary>
    /// 获取或创建类型的属性掩码信息数组，使用缓存机制提高性能。
    /// </summary>
    /// <param name="type">要获取属性信息的类型。</param>
    /// <returns>该类型的属性掩码信息数组。</returns>
    /// <remarks>
    /// <para>
    /// 该方法使用 <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd"/> 实现线程安全的缓存。
    /// 首次访问某个类型时会进行反射分析，后续访问将直接从缓存中获取，避免重复反射开销。
    /// </para>
    /// <para>
    /// 对于每个属性，会检测：
    /// <list type="bullet">
    /// <item><description>是否标记了 SensitiveDataAttribute 特性</description></item>
    /// <item><description>是否为字符串类型（只有字符串类型可以标记为敏感）</description></item>
    /// <item><description>是否为复杂类型（需要递归处理）</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private static PropertyMaskInfo[] GetOrCreatePropertyInfos(Type type)
    {
        return s_propertyCache.GetOrAdd(type, t =>
        {
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var infos = new List<PropertyMaskInfo>(properties.Length);

            foreach (var property in properties)
            {
                if (!property.CanRead)
                    continue;

                var sensitiveAttr = property.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType.Name == SensitiveDataAttributeName);

                var isSensitive = sensitiveAttr != null && property.PropertyType == typeof(string);
                var isComplex = property.PropertyType != typeof(string) &&
                                !property.PropertyType.IsPrimitive;

                infos.Add(new PropertyMaskInfo
                {
                    Property = property,
                    IsSensitive = isSensitive,
                    MaskMode = isSensitive ? GetNamedArgument(sensitiveAttr!, "MaskMode", SensitiveDataMaskMode.Mask) : SensitiveDataMaskMode.Mask,
                    PrefixLength = isSensitive ? GetNamedArgument(sensitiveAttr!, "PrefixLength", 2) : 2,
                    SuffixLength = isSensitive ? GetNamedArgument(sensitiveAttr!, "SuffixLength", 2) : 2,
                    IsComplexType = isComplex
                });
            }

            return infos.ToArray();
        });
    }

    /// <summary>
    /// 对字符串值进行敏感数据掩码处理。
    /// </summary>
    /// <param name="value">需要进行掩码处理的原始字符串值。</param>
    /// <param name="mode">掩码模式，控制如何显示掩码后的数据。默认为 <see cref="SensitiveDataMaskMode.Mask"/>。</param>
    /// <param name="prefixLength">在掩码模式下，保留的前缀字符数量。默认为 2。</param>
    /// <param name="suffixLength">在掩码模式下，保留的后缀字符数量。默认为 2。</param>
    /// <returns>经过掩码处理后的字符串。如果输入为 null 或空字符串，则返回掩码常量。</returns>
    /// <remarks>
    /// <para>
    /// 当 <paramref name="mode"/> 为 <see cref="SensitiveDataMaskMode.Hide"/> 时，返回固定的掩码字符串。
    /// </para>
    /// <para>
    /// 当 <paramref name="mode"/> 为 <see cref="SensitiveDataMaskMode.Mask"/> 时，保留指定长度的前缀和后缀，
    /// 中间部分替换为掩码字符串。如果字符串长度不足以保留前缀和后缀，则返回完整掩码。
    /// </para>
    /// <para>
    /// 当 <paramref name="mode"/> 为 <see cref="SensitiveDataMaskMode.TypeOnly"/> 时，返回包含类型和长度信息的字符串。
    /// </para>
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

    /// <summary>
    /// 对对象进行敏感数据掩码处理，返回 JSON 格式的掩码后对象表示。
    /// </summary>
    /// <param name="obj">需要进行掩码处理的对象实例。</param>
    /// <returns>经过掩码处理后的对象 JSON 字符串表示。</returns>
    /// <remarks>
    /// 该方法会递归遍历对象的所有公共属性，对标记有 <c>SensitiveDataAttribute</c> 的字符串属性进行掩码处理。
    /// 使用引用相等性检测来避免循环引用问题，并限制最大递归深度以防止栈溢出。
    /// </remarks>
    public string MaskObject(object obj)
    {
        return MaskObjectInternal(obj, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// 内部递归方法，用于执行对象掩码处理。
    /// </summary>
    /// <param name="obj">当前要处理的对象。</param>
    /// <param name="depth">当前递归深度，用于防止无限递归。</param>
    /// <param name="visited">已访问对象集合，用于检测循环引用。</param>
    /// <returns>掩码处理后的对象 JSON 字符串表示。</returns>
    /// <remarks>
    /// 该方法处理以下情况：
    /// <list type="bullet">
    /// <item><description>null 对象：返回 "null" 字符串</description></item>
    /// <item><description>超过最大深度：返回 "[深度超限]" 标记</description></item>
    /// <item><description>基本类型：直接转换为字符串</description></item>
    /// <item><description>循环引用：返回 "[循环引用]" 标记</description></item>
    /// <item><description>复杂对象：递归处理所有公共属性</description></item>
    /// </list>
    /// </remarks>
    private string MaskObjectInternal(object obj, int depth, HashSet<object> visited)
    {
        if (obj == null)
            return "null";

        if (depth > MaxMaskDepth)
            return "\"[深度超限]\"";

        var type = obj.GetType();

        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal))
        {
            return obj.ToString() ?? "null";
        }

        if (!visited.Add(obj))
            return "\"[循环引用]\"";

        try
        {
            var maskedObject = new Dictionary<string, object?>();
            var propertyInfos = GetOrCreatePropertyInfos(type);

            foreach (var info in propertyInfos)
            {
                object? value;
                try
                {
                    value = info.Property.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (info.IsSensitive && value is string stringValue)
                {
                    maskedObject[info.Property.Name] = Mask(stringValue, info.MaskMode, info.PrefixLength, info.SuffixLength);
                }
                else if (value != null && info.IsComplexType)
                {
                    maskedObject[info.Property.Name] = MaskObjectInternal(value, depth + 1, visited);
                }
                else
                {
                    maskedObject[info.Property.Name] = value;
                }
            }

            return JsonSerializer.Serialize(maskedObject);
        }
        finally
        {
            visited.Remove(obj);
        }
    }

    /// <summary>
    /// 从自定义特性数据中获取命名参数值。
    /// </summary>
    /// <typeparam name="T">参数值的类型。</typeparam>
    /// <param name="attr">自定义特性数据。</param>
    /// <param name="name">参数名称。</param>
    /// <param name="defaultValue">默认值，当参数不存在或值为 null 时使用。</param>
    /// <returns>特性参数的值，如果不存在则返回默认值。</returns>
    private static T GetNamedArgument<T>(CustomAttributeData attr, string name, T defaultValue)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.MemberName == name);
        if (arg.TypedValue.Value != null)
        {
            return (T)arg.TypedValue.Value;
        }
        return defaultValue;
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// 引用相等性比较器，用于检测对象循环引用。
    /// 基于 <see cref="ReferenceEqualityComparer"/> 实现。
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly ReferenceEqualityComparer Instance = new();
        
        /// <summary>
        /// 比较两个对象是否引用相等。
        /// </summary>
        /// <param name="x">第一个对象。</param>
        /// <param name="y">第二个对象。</param>
        /// <returns>如果两个对象引用相同则返回 true，否则返回 false。</returns>
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        
        /// <summary>
        /// 获取对象的哈希码，基于运行时引用。
        /// </summary>
        /// <param name="obj">要获取哈希码的对象。</param>
        /// <returns>对象的运行时哈希码。</returns>
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
#else
    /// <summary>
    /// 引用相等性比较器，用于检测对象循环引用。
    /// 兼容 .NET Standard 2.0 的实现。
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly ReferenceEqualityComparer Instance = new();

        /// <summary>
        /// 比较两个对象是否引用相等。
        /// </summary>
        /// <param name="x">第一个对象。</param>
        /// <param name="y">第二个对象。</param>
        /// <returns>如果两个对象引用相同则返回 true，否则返回 false。</returns>
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        /// <summary>
        /// 获取对象的哈希码，基于运行时引用。
        /// </summary>
        /// <param name="obj">要获取哈希码的对象。</param>
        /// <returns>对象的运行时哈希码。</returns>
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
#endif
}
