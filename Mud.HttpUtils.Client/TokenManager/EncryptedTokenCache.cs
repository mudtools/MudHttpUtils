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
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
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
            value = JsonSerializer.Deserialize<T>(plain, s_jsonOptions);
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
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        if (value == null)
        {
            _inner.TryRemove(key, out _);
            postEvictionCallback?.Invoke(key);
            return;
        }

        var plain = JsonSerializer.Serialize(value, s_jsonOptions);
        _inner.Set(key, _encryption.Encrypt(plain), absoluteExpirationRelativeToNow, slidingExpiration, postEvictionCallback);
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
        // 密文无值语义：移除按底层结果转发，removed 恒 null（加密包装不还原被移除值）
        var result = _inner.TryRemove(key, out _);
        removed = null;
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
