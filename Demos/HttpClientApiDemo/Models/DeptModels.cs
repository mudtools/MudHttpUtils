namespace HttpClientApiTest.WebApi;

/// <summary>
/// 系统部门信息输出模型
/// </summary>
public class SysDeptInfoOutput
{
    /// <summary>
    /// 部门ID
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// 部门名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 父部门ID
    /// </summary>
    public string? ParentId { get; set; }
}

/// <summary>
/// 系统部门列表输出模型
/// </summary>
public class SysDeptListOutput
{
    /// <summary>
    /// 部门ID
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// 部门名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 部门层级
    /// </summary>
    public int Level { get; set; }
}

/// <summary>
/// 系统部门创建输入模型
/// </summary>
public class SysDeptCrInput
{
    /// <summary>
    /// 部门名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 父部门ID
    /// </summary>
    public string? ParentId { get; set; }
}

/// <summary>
/// 系统部门更新输入模型
/// </summary>
public class SysDeptUpInput
{
    /// <summary>
    /// 部门名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 部门ID
    /// </summary>
    public string? Id { get; set; }
}

/// <summary>
/// 项目查询输入模型
/// </summary>
public class ProjectQueryInput
{
    /// <summary>
    /// 项目名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 项目状态
    /// </summary>
    public string? Status { get; set; }
}

/// <summary>
/// 数据查询输入模型
/// </summary>
public class DataQueryInput
{
    /// <summary>
    /// 关键字
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 分页大小
    /// </summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// 页码
    /// </summary>
    public int PageIndex { get; set; } = 1;
}
