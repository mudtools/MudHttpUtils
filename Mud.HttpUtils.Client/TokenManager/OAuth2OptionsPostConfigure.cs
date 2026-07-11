// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// OAuth2 选项的后置配置器，在选项绑定后检查 <see cref="OAuth2Options.ClientSecret"/> 
/// 与 <see cref="OAuth2Options.ClientSecretProviderName"/> 的互斥冲突并记录警告日志。
/// </summary>
internal sealed class OAuth2OptionsPostConfigure : IPostConfigureOptions<OAuth2Options>
{
    private readonly ILogger<OAuth2OptionsPostConfigure> _logger;

    public OAuth2OptionsPostConfigure(ILogger<OAuth2OptionsPostConfigure>? logger)
    {
        _logger = logger ?? NullLogger<OAuth2OptionsPostConfigure>.Instance;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, OAuth2Options options)
    {
        if (options == null)
            return;

        var warning = options.GetConflictWarning();
        if (warning != null)
        {
            _logger.LogWarning("{Warning}", warning);
        }
    }
}
