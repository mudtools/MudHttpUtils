// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="AesEncryptionOptions"/> 的校验器，在选项绑定时验证密钥长度有效性。
/// </summary>
/// <remarks>
/// 将原 <see cref="AesEncryptionOptions.Validate"/> 的校验逻辑提前到配置绑定阶段，
/// 使无效密钥在应用启动时即被检测，而非延迟到 <see cref="IEncryptionProvider"/> 首次解析时。
/// </remarks>
public class AesEncryptionOptionsValidator : IValidateOptions<AesEncryptionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AesEncryptionOptions? options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        // 仅在已设置 Key 时校验长度；未设置 Key 时由 DefaultAesEncryptionProvider 在解析时校验
        var key = options.Key;
        if (key.Length == 0)
            return ValidateOptionsResult.Success;

        if (key.Length != 16 && key.Length != 24 && key.Length != 32)
        {
            return ValidateOptionsResult.Fail(
                $"AesEncryptionOptions: Key 长度必须为 16、24 或 32 字节，当前为 {key.Length} 字节。");
        }

        return ValidateOptionsResult.Success;
    }
}
