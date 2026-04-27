// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌主动刷新后台服务配置选项。
/// </summary>
public class TokenRefreshBackgroundOptions
{
    /// <summary>
    /// 配置节的名称。
    /// </summary>
    public const string SectionName = "TokenRefreshBackground";

    /// <summary>
    /// 获取或设置是否启用后台刷新，默认 false。
    /// 需要显式设置为 true 以启用令牌主动刷新后台服务。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 刷新间隔（秒），默认 300 秒（5 分钟）。
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// 令牌过期前提前刷新时间（秒），默认 600 秒（10 分钟）。
    /// 此值用于配合后台服务判断令牌是否即将过期，当前由 TokenManagerBase.ExpireThresholdSeconds 控制。
    /// </summary>
    public int RefreshBeforeExpirySeconds { get; set; } = 600;

    /// <summary>
    /// 刷新失败后重试延迟（秒），默认 60 秒（1 分钟）。
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>
    /// 获取或设置刷新失败时是否停止服务，默认 false。
    /// </summary>
    public bool StopOnError { get; set; } = false;
}
