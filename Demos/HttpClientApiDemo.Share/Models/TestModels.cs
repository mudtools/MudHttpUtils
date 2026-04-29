namespace HttpClientApiTest.Models;


/// <summary>
/// 测试请求数据
/// </summary>
public class TestData
{
    /// <summary>
    /// 用户名
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 年龄
    /// </summary>
    public int Age { get; set; }
}


/// <summary>
/// 测试响应数据
/// </summary>
public class TestResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 数据
    /// </summary>
    public object? Data { get; set; }
}


/// <summary>
/// 部门信息
/// </summary>
public class Department
{
    /// <summary>
    /// 部门ID
    /// </summary>
    public long DeptId { get; set; }

    /// <summary>
    /// 部门名称
    /// </summary>
    public string? DeptName { get; set; }

    /// <summary>
    /// 父部门ID
    /// </summary>
    public long? ParentId { get; set; }
}
