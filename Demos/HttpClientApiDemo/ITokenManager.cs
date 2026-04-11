namespace Mud.HttpUtils.Attributes;


/// <summary>
/// Token管理器实现（示例）
/// </summary>
public class TestTokenManager : ITokenManager
{
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Bearer test-access-token");
    }
}