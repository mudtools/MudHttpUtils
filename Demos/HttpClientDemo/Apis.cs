using HttpClientDemo.Models;
using Mud.HttpUtils.Attributes;

namespace HttpClientDemo.Apis;

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/api/users/{id}")]
    Task<UserInfo?> GetUserAsync([Path] int id);

    [Get("/api/users")]
    Task<List<UserInfo>?> SearchUsersAsync([Query] string keyword, [Query] int page = 1, [Query] int pageSize = 20);

    [Post("/api/users")]
    Task<UserInfo?> CreateUserAsync([Body] CreateUserRequest request);

    [Put("/api/users/{id}")]
    Task<UserInfo?> UpdateUserAsync([Path] int id, [Body] CreateUserRequest request);

    [Delete("/api/users/{id}")]
    Task<bool> DeleteUserAsync([Path] int id);

    [Post("/api/users/encrypted")]
    Task<UserInfo?> CreateEncryptedUserAsync([Body(EnableEncrypt = true)] CreateUserRequest request);
}

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IOrderApi
{
    [Get("/api/orders/{orderId}")]
    Task<OrderInfo?> GetOrderAsync([Path] string orderId);

    [Get("/api/orders")]
    Task<List<OrderInfo>?> ListOrdersAsync([Query] string? status = null);
}
