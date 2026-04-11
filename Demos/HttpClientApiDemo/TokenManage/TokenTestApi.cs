
namespace HttpClientApiTest.Api;

/// <summary>
/// Token特性测试接口 - 使用 AppAccessToken
/// </summary>
[HttpClientApi("https://api.example.com")]
[Token("AppAccessToken")]
public interface IAppTokenService
{
    [Get("/api/data")]
    Task<string> GetDataAsync();
}

/// <summary>
/// Token特性测试接口 - 使用 TenantAccessToken（默认）
/// </summary>
[HttpClientApi("https://api.example.com")]
[Token]
public interface ITenantTokenService
{
    [Get("/api/data")]
    Task<string> GetDataAsync();
}

/// <summary>
/// 无 Token 特性的接口
/// </summary>
[HttpClientApi("https://api.example.com")]
public interface INoTokenService
{
    [Get("/api/data")]
    Task<string> GetDataAsync();
}
