// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 安全密钥提供程序接口，用于从安全存储中获取敏感配置（如 ClientSecret）。
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// 根据名称获取密钥值。
    /// </summary>
    /// <param name="name">密钥名称。</param>
    /// <returns>密钥值，如果未找到则返回 null。</returns>
    Task<string?> GetSecretAsync(string name);
}
