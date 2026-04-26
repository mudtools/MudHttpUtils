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
