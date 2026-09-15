// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

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
/// <b>Mask 方法</b>：字符串脱敏行为与 <see cref="DefaultSensitiveDataMasker.Mask"/> 完全一致。
/// <b>MaskObject 方法</b>：与反射版 <see cref="DefaultSensitiveDataMasker"/> 存在差异——
/// 反射版通过 [SensitiveData] 特性自动发现脱敏规则；本类要求显式 Register&lt;T&gt; 注册，
/// 未注册类型返回 [TypeName] 兜底输出并发出一次性告警（引导补注册）。
/// </para>
/// <para>
/// 此类是线程安全的，可以在多线程环境中安全使用。
/// </para>
/// <para>
/// <b>禁止对含特殊字符的值手工拼接 JSON</b>：字符串插值（如 <c>$"{{\"name\":\"{value}\"}}"</c>）不会转义
/// 引号、反斜杠与控制字符，含特殊字符的字段会产出非法 JSON（下游解析失败），换行/控制字符还会造成日志注入。
/// 正确做法是经 <c>JsonSerializer.SerializeToNode(obj, JsonTypeInfo&lt;T&gt;)</c> 序列化后修改字段再
/// <c>ToJsonString()</c>（AOT 安全）；若确需局部拼接，至少对每个值单独调用 <c>JsonSerializer.Serialize(value)</c>。
/// </para>
/// </remarks>
/// <example>
/// 使用示例：
/// <code>
/// var masker = new AotSafeSensitiveDataMasker();
/// masker.Register&lt;UserDto&gt;(obj =&gt;
/// {
///     var user = (UserDto)obj;
///     // 先以源生成 JsonTypeInfo 序列化为 JsonNode（AOT 安全），再对敏感字段做后处理。
///     var node = JsonSerializer.SerializeToNode(user, AppJsonContext.Default.UserDto)!.AsObject();
///     node["email"] = masker.Mask(user.Email);
///     return node.ToJsonString();
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
    // [T5 修复] 未注册类型一次性告警去重表
    private readonly ConcurrentDictionary<Type, byte> _unregisteredWarned = new();
    private readonly ILogger? _logger;
    // [T5 修复] 基类注册兜底开关（默认关闭，避免改变现有精确匹配契约）
    private readonly bool _enableBaseTypeFallback;

    /// <summary>
    /// 初始化 <see cref="AotSafeSensitiveDataMasker"/> 实例。
    /// </summary>
    /// <param name="logger">日志记录器（可选）。未注册类型命中时发出一次性告警。</param>
    /// <param name="enableBaseTypeFallback">
    /// 是否启用基类注册兜底（默认 false）。
    /// [P0-3 修复] 开启时：未注册但存在可赋值的已注册基类时，降级为类型占位输出（TypeOnly）而非使用基类规则，
    /// 防止派生类新增敏感字段明文输出。告警引导用户注册派生类型专用规则。
    /// </param>
    public AotSafeSensitiveDataMasker(ILogger? logger = null, bool enableBaseTypeFallback = false)
    {
        _logger = logger;
        _enableBaseTypeFallback = enableBaseTypeFallback;
    }

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
    /// <para>
    /// <b>Mask 方法</b>：字符串脱敏行为与 <see cref="DefaultSensitiveDataMasker.Mask"/> 完全一致，
    /// 确保从反射实现切换到 AOT 安全实现时无行为差异。
    /// </para>
    /// </remarks>
    public virtual string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2)
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
    /// <para>
    /// [T5 修复] 未注册命中时发出一次性告警（每个类型仅一次），
    /// 引导消费方补充注册：注入了 <see cref="ILogger"/> 时走日志，否则回退 <see cref="Console.Error"/>——
    /// 脱敏是安全相关路径，禁止静默（"未注册 → 输出 [TypeName]"本身是 fail-safe，
    /// 但静默会让消费方永远发现不了遗漏注册）。
    /// 若 <see cref="_enableBaseTypeFallback"/> 开启，
    /// 命中未注册但存在可赋值的已注册基类时使用基类规则并告警。
    /// </para>
    /// </remarks>
    public virtual string MaskObject(object obj)
    {
        if (obj == null)
            return "null";

        var type = obj.GetType();
        if (_maskers.TryGetValue(type, out var masker))
            return masker(obj);

        // [T5 修复] 基类注册兜底（可选开关）
        // [P0-3 修复] 回退命中时降级为类型占位输出（TypeOnly），而非使用基类规则，
        // 防止派生类新增敏感字段不在基类规则视野内导致明文输出。
        if (_enableBaseTypeFallback)
        {
            var baseType = type.BaseType;
            while (baseType != null)
            {
                if (_maskers.TryGetValue(baseType, out _))
                {
                    WarnUnregistered(
                        $"AotSafeSensitiveDataMasker: 类型 {type} 未注册脱敏规则，检测到基类 {baseType} 已注册，为防漏脱敏已降级为类型占位输出。请调用 Register<{type.Name}>() 注册专用规则。",
                        type,
                        "AotSafeSensitiveDataMasker: 类型 {Type} 未注册脱敏规则，检测到基类 {BaseType} 已注册，为防漏脱敏已降级为类型占位输出。请调用 Register<{TypeName}>() 注册专用规则。",
                        type,
                        baseType,
                        type.Name);
                    return $"[{type.Name}, BaseType={baseType.Name}]";
                }
                baseType = baseType.BaseType;
            }
        }

        // [T5 修复] 未注册类型一次性告警。
        // 注意：结构化日志的消息模板占位符数量必须与参数数量一致——模板出现 3 个占位符而只传 2 个参数时，
        // LogValuesFormatter 会抛 FormatException（原实现即如此，且仅在真正调用 logger 时才暴露）。
        WarnUnregistered(
            $"AotSafeSensitiveDataMasker: 类型 {type} 未注册脱敏规则，已按 [{type.Name}] 兜底输出。请调用 Register<{type.Name}>() 注册。",
            type,
            "AotSafeSensitiveDataMasker: 类型 {Type} 未注册脱敏规则，已按 [{TypeName}] 兜底输出。请调用 Register<T>() 注册该类型。",
            type,
            type.Name);

        // 未注册的类型返回类型信息（不序列化未知类型，避免反射）
        return $"[{type.Name}]";
    }

    /// <summary>
    /// [T5] 未注册类型的一次性告警（每个类型仅一次）：优先走注入的日志，否则回退 <see cref="Console.Error"/>。
    /// </summary>
    /// <param name="fallbackMessage">无 <see cref="ILogger"/> 时输出的完整消息。</param>
    /// <param name="dedupKey">去重键（命中过的类型不再重复告警）。</param>
    /// <param name="messageTemplate">日志消息模板（占位符须与 <paramref name="logArgs"/> 数量一致）。</param>
    /// <param name="logArgs">日志消息参数。</param>
    private void WarnUnregistered(string fallbackMessage, Type dedupKey, string messageTemplate, params object[] logArgs)
    {
        if (!_unregisteredWarned.TryAdd(dedupKey, 0))
            return;

        if (_logger != null)
            _logger.LogWarning(messageTemplate, logArgs);
        else
            Console.Error.WriteLine(fallbackMessage);
    }
}
