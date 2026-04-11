namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;
using CodeBaseTest.Interface;

/// <summary>
/// 密封类继承测试基础接口
/// 定义可被密封类实现的基础方法
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 60)]
public interface ISealedClassBaseTestApi
{
    /// <summary>
    /// 测试：获取用户信息
    /// 接口：GET /api/v1/users/{id}
    /// 特点：基本API方法，可被密封类实现
    /// </summary>
    [Get("/api/v1/users/{id}")]
    Task<UserBaseInfo> GetUserAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：创建用户
    /// 接口：POST /api/v1/users
    /// 特点：基本API方法，可被密封类实现
    /// </summary>
    [Post("/api/v1/users")]
    Task<UserBaseInfo> CreateUserAsync([Body] UserBaseInfo user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：更新用户信息
    /// 接口：PUT /api/v1/users/{id}
    /// 特点：基本API方法，可被密封类实现
    /// </summary>
    [Put("/api/v1/users/{id}")]
    Task<bool> UpdateUserAsync([Path] string id, [Body] UserBaseInfo user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：删除用户
    /// 接口：DELETE /api/v1/users/{id}
    /// 特点：基本API方法，可被密封类实现
    /// </summary>
    [Delete("/api/v1/users/{id}")]
    Task<bool> DeleteUserAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 密封类继承测试扩展接口
/// 扩展自ISealedClassBaseTestApi，添加更多功能
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90)]
public interface ISealedClassExtendedTestApi : ISealedClassBaseTestApi
{
    /// <summary>
    /// 测试：获取用户详细信息
    /// 接口：GET /api/v1/users/{id}/details
    /// 特点：扩展方法，可被密封类实现
    /// </summary>
    [Get("/api/v1/users/{id}/details")]
    Task<UserDetailInfo> GetUserDetailsAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户角色
    /// 接口：GET /api/v1/users/{id}/roles
    /// 特点：扩展方法，可被密封类实现
    /// </summary>
    [Get("/api/v1/users/{id}/roles")]
    Task<List<string>> GetUserRolesAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：搜索用户
    /// 接口：GET /api/v1/users/search
    /// 特点：扩展方法，可被密封类实现
    /// </summary>
    [Get("/api/v1/users/search")]
    Task<List<UserBaseInfo>> SearchUsersAsync([Query] string keyword, [Query] string role = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 密封类实现基础接口
/// 标记为密封类，不能被继承
/// </summary>
public sealed class SealedClassBaseTestApi : ISealedClassBaseTestApi
{
    /// <summary>
    /// 获取用户信息
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户信息</returns>
    public async Task<UserBaseInfo> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new UserBaseInfo { Id = id, Name = "Test User", Email = "test@example.com" };
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的用户信息</returns>
    public async Task<UserBaseInfo> CreateUserAsync(UserBaseInfo user, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        user.Id = Guid.NewGuid().ToString();
        user.CreatedAt = DateTime.UtcNow;
        return user;
    }

    /// <summary>
    /// 更新用户信息
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="user">用户信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否更新成功</returns>
    public async Task<bool> UpdateUserAsync(string id, UserBaseInfo user, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return true;
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否删除成功</returns>
    public async Task<bool> DeleteUserAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return true;
    }
}

/// <summary>
/// 密封类实现扩展接口
/// 标记为密封类，不能被继承
/// </summary>
public sealed class SealedClassExtendedTestApi : ISealedClassExtendedTestApi
{
    /// <summary>
    /// 获取用户信息
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户信息</returns>
    public async Task<UserBaseInfo> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new UserBaseInfo { Id = id, Name = "Test User", Email = "test@example.com" };
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的用户信息</returns>
    public async Task<UserBaseInfo> CreateUserAsync(UserBaseInfo user, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        user.Id = Guid.NewGuid().ToString();
        user.CreatedAt = DateTime.UtcNow;
        return user;
    }

    /// <summary>
    /// 更新用户信息
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="user">用户信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否更新成功</returns>
    public async Task<bool> UpdateUserAsync(string id, UserBaseInfo user, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return true;
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否删除成功</returns>
    public async Task<bool> DeleteUserAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return true;
    }

    /// <summary>
    /// 获取用户详细信息
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户详细信息</returns>
    public async Task<UserDetailInfo> GetUserDetailsAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new UserDetailInfo
        {
            Id = id,
            Name = "Test User",
            Email = "test@example.com",
            Phone = "1234567890",
            Address = "Test Address",
            BirthDate = new DateTime(1990, 1, 1)
        };
    }

    /// <summary>
    /// 获取用户角色
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户角色列表</returns>
    public async Task<List<string>> GetUserRolesAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new List<string> { "User", "Admin" };
    }

    /// <summary>
    /// 搜索用户
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="role">用户角色</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户列表</returns>
    public async Task<List<UserBaseInfo>> SearchUsersAsync(string keyword, string role = null, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new List<UserBaseInfo>
        {
            new UserBaseInfo { Id = "1", Name = "Test User 1", Email = "test1@example.com" },
            new UserBaseInfo { Id = "2", Name = "Test User 2", Email = "test2@example.com" }
        };
    }
}

/// <summary>
/// 用户基础信息模型
/// </summary>
public class UserBaseInfo
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 用户名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 用户邮箱
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 用户详细信息模型
/// </summary>
public class UserDetailInfo : UserBaseInfo
{
    /// <summary>
    /// 用户电话
    /// </summary>
    public string Phone { get; set; }

    /// <summary>
    /// 用户地址
    /// </summary>
    public string Address { get; set; }

    /// <summary>
    /// 用户生日
    /// </summary>
    public DateTime BirthDate { get; set; }

    /// <summary>
    /// 用户注册时间
    /// </summary>
    public DateTime RegistrationDate { get; set; }

    /// <summary>
    /// 用户最后登录时间
    /// </summary>
    public DateTime LastLoginDate { get; set; }
}


