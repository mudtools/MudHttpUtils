namespace HttpClientApiTest.WebApi;

/// <summary>
/// 系统用户信息输出模型
/// </summary>
public class SysUserInfoOutput
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 部门ID
    /// </summary>
    public string? DeptId { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    public int? Status { get; set; }
}

/// <summary>
/// 受保护数据模型
/// </summary>
public class ProtectedData
{
    /// <summary>
    /// 数据内容
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 用户搜索条件
/// </summary>
public class UserSearchCriteria
{
    /// <summary>
    /// 用户名
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 年龄
    /// </summary>
    public int? Age { get; set; }

    /// <summary>
    /// 部门
    /// </summary>
    public string? Department { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string? Phone { get; set; }
}
