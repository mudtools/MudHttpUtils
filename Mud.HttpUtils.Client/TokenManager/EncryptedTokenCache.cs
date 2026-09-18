// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mud.HttpUtils;

/// <summary>
/// SR-M8（P3.4，D12）令牌缓存的加密包装：值经 <see cref="IEncryptionProvider"/> 加密后
/// 以 Base64 密文字符串写入底层 <see cref="ITokenCache{String}"/>，内存转储/抓取不再暴露明文凭据。
/// </summary>
/// <typeparam name="T">缓存值类型。</typeparam>
/// <remarks>
/// <para>加密引擎复用 <see cref="DefaultAesEncryptionProvider"/>（AEAD / CBC+HMAC 信封已就绪）。</para>
/// <para>
/// 密文损坏（密钥轮换 / <see cref="CryptographicException"/>）或反序列化失败（<see cref="JsonException"/>）
/// 时按 miss 处理（返回 false + Warning 日志），触发上层重新获取令牌，绝不抛出（§0.3-V3 修订）。
/// </para>
/// <para>
/// <b>AOT 注意</b>：默认反射序列化在 net10 AOT 严格模式下受限——AOT 用户应传入基于预生成
/// JsonSerializerContext 的自定义包装（见 AotVerificationDemo）；类库侧默认实现不强制。
/// </para>
/// </remarks>
public sealed class EncryptedTokenCache<T> : ITokenCache<T> where T : class
{
    private readonly ITokenCache<string> _inner;
    private readonly IEncryptionProvider _encryption;
    // TMX-11：实例级序列化选项（可注入携寄 JsonTypeInfoResolver 的选项以支持 AOT/裁剪）
    private readonly JsonSerializerOptions _jsonOptions;
    // MT-25：解密失败此前完全静默（类注释却承诺"返回 false + Warning 日志"），排障时无从下手。
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 初始化加密令牌缓存包装。
    /// </summary>
    /// <param name="inner">底层字符串缓存（如 <see cref="MemoryCacheTokenCache{String}"/>）。</param>
    /// <param name="encryption">加密提供程序。</param>
    public EncryptedTokenCache(ITokenCache<string> inner, IEncryptionProvider encryption)
        : this(inner, encryption, null)
    {
    }

    /// <summary>
    /// MT-25：初始化加密令牌缓存包装（带日志记录器，使密文损坏 / 反序列化失败可观测）。
    /// </summary>
    /// <param name="inner">底层字符串缓存（如 <see cref="MemoryCacheTokenCache{String}"/>）。</param>
    /// <param name="encryption">加密提供程序。</param>
    /// <param name="logger">日志记录器（可选）。为 null 时静默（与历史行为一致）。</param>
    public EncryptedTokenCache(ITokenCache<string> inner, IEncryptionProvider encryption, ILogger? logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _jsonOptions = s_jsonOptions;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// TMX-11：允许注入携寄 JsonTypeInfoResolver 的序列化选项（AOT/裁剪场景）与诊断日志。
    /// 序列化失败时降级为"不缓存"（与 TryGet 对称），绝不打断令牌流水线。
    /// </summary>
    public EncryptedTokenCache(ITokenCache<string> inner, IEncryptionProvider encryption,
        JsonSerializerOptions? serializerOptions, ILogger? logger = null)
        : this(inner, encryption, logger)
    {
        _jsonOptions = serializerOptions ?? s_jsonOptions;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "令牌缓存值经注入的 JsonSerializerOptions 序列化；AOT/裁剪场景由调用方注入携寄 JsonTypeInfoResolver（源生成上下文）的选项（TMX-11，见类注释「AOT 注意」）。默认选项的反射路径仅服务非 AOT 宿主。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：反射反序列化仅在调用方未注入 JsonTypeInfoResolver 的非 AOT 场景下发生。")]
    public bool TryGet(string key, out T? value)
    {
        if (!_inner.TryGet(key, out var cipher) || cipher == null)
        {
            value = null;
            return false;
        }

        try
        {
            var plain = _encryption.Decrypt(cipher);
            value = JsonSerializer.Deserialize<T>(plain, _jsonOptions);
            return value != null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // TMR-08：放宽异常白名单——FormatException / ObjectDisposedException / 其他非取消异常均按 miss 处理
            // MT-25：按 miss 处理的同时记录 Warning，使"密钥轮换导致密文不可解"等场景可被观测
            // （此前完全静默，与类注释承诺的"返回 false + Warning 日志"不符）。
            _logger.LogWarning(ex, "加密令牌缓存条目解密/反序列化失败，按缓存未命中处理（Key={Key}）", key);
            value = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Set(string key, T? value)
        => Set(key, value, null, null, null);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "与 TryGet 对称：反射序列化仅在调用方未注入携寄 JsonTypeInfoResolver 的 JsonSerializerOptions 时发生（TMX-11）。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 宿主须注入 JsonTypeInfoResolver，届时不会走到反射重载。")]
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        if (value == null)
        {
            _inner.TryRemove(key, out _);
            postEvictionCallback?.Invoke(key);
            return;
        }

        string plain;
        try { plain = JsonSerializer.Serialize(value, _jsonOptions); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // TMX-11：与 TryGet 对称——序列化不可用时降级为"不缓存"，绝不打断令牌流水线
            MudHttpClientLog.TokenCacheSerializationFailed(_logger, typeof(T).Name, ex.Message, ex);
            return;
        }
        _inner.Set(key, _encryption.Encrypt(plain), absoluteExpirationRelativeToNow, slidingExpiration, postEvictionCallback);
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
        // TMX-15-1 (B8)：先 TryGet 解密得到 removed，使 InvalidateTokenAsync 能返回失效前令牌
        T? value = default;
        var hasValue = TryGet(key, out value);
        var result = _inner.TryRemove(key, out _);
        removed = hasValue ? value : null;
        return result;
    }

    /// <inheritdoc />
    public int Count => _inner.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _inner.Keys;

    /// <inheritdoc />
    public void Clear() => _inner.Clear();

    /// <inheritdoc />
    public void Compact(double percentage) => _inner.Compact(percentage);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
