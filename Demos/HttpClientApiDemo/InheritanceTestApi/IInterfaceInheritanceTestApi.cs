namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// 基础功能接口
/// 定义基本的API操作功能
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", IsAbstract = true)]
public interface IBaseFunctionApi
{
    /// <summary>
    /// 测试：基础功能接口中获取数据
    /// 接口：GET /api/v1/data/{id}
    /// 特点：基础功能方法
    /// </summary>
    [Get("/api/v1/data/{id}")]
    Task<DataItem> GetDataAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：基础功能接口中保存数据
    /// 接口：POST /api/v1/data
    /// 特点：基础功能方法
    /// </summary>
    [Post("/api/v1/data")]
    Task<DataItem> SaveDataAsync([Body] DataItem data, CancellationToken cancellationToken = default);
}

/// <summary>
/// 高级功能接口
/// 定义高级的API操作功能
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", IsAbstract = true)]
public interface IAdvancedFunctionApi
{
    /// <summary>
    /// 测试：高级功能接口中批量处理数据
    /// 接口：POST /api/v1/data/batch
    /// 特点：高级功能方法
    /// </summary>
    [Post("/api/v1/data/batch")]
    Task<BatchResult> BatchProcessDataAsync([Body] List<DataItem> dataItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：高级功能接口中导出数据
    /// 接口：GET /api/v1/data/export
    /// 特点：高级功能方法
    /// </summary>
    [Get("/api/v1/data/export")]
    Task<byte[]> ExportDataAsync([Query] string format, CancellationToken cancellationToken = default);
}

/// <summary>
/// 综合功能接口
/// 同时继承自基础功能接口和高级功能接口，展示接口多继承
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "CompositeInheritance")]
[Header("X-Function-Type", "composite")]
public interface ICompositeFunctionApi : IBaseFunctionApi, IAdvancedFunctionApi
{
    /// <summary>
    /// 测试：综合功能接口中获取数据列表
    /// 接口：GET /api/v1/data
    /// 特点：综合功能接口新增方法
    /// </summary>
    [Get("/api/v1/data")]
    Task<List<DataItem>> GetDataListAsync([Query] int pageSize = 10, [Query] int pageIndex = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：综合功能接口中搜索数据
    /// 接口：GET /api/v1/data/search
    /// 特点：综合功能接口新增方法
    /// </summary>
    [Get("/api/v1/data/search")]
    Task<List<DataItem>> SearchDataAsync([Query] string keyword, CancellationToken cancellationToken = default);
}

/// <summary>
/// 扩展功能接口
/// 继承自综合功能接口，展示多层接口继承
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 120, TokenManage = "IFeishuAppManager", RegistryGroupName = "ExtendedInheritance")]
[Header("X-Function-Type", "extended")]
public interface IExtendedFunctionApi : ICompositeFunctionApi
{
    /// <summary>
    /// 测试：扩展功能接口中导入数据
    /// 接口：POST /api/v1/data/import
    /// 特点：扩展功能接口新增方法
    /// </summary>
    [Post("/api/v1/data/import")]
    Task<ImportResult> ImportDataAsync([Body] ImportDataRequest body, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：扩展功能接口中获取数据统计
    /// 接口：GET /api/v1/data/stats
    /// 特点：扩展功能接口新增方法
    /// </summary>
    [Get("/api/v1/data/stats")]
    Task<DataStats> GetDataStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：扩展功能接口中获取扩展数据
    /// 接口：GET /api/v2/data/{id}
    /// 特点：新增方法，使用不同的路径和返回类型
    /// </summary>
    [Get("/api/v2/data/{id}")]
    Task<ExtendedDataItem> GetExtendedDataAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 数据项模型
/// </summary>
public class DataItem
{
    /// <summary>
    /// 数据ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 数据名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 数据值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 扩展数据项模型
/// </summary>
public class ExtendedDataItem : DataItem
{
    /// <summary>
    /// 数据描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 数据标签
    /// </summary>
    public List<string> Tags { get; set; }

    /// <summary>
    /// 数据版本
    /// </summary>
    public string Version { get; set; }
}

/// <summary>
/// 批量处理结果模型
/// </summary>
public class BatchResult
{
    /// <summary>
    /// 成功处理数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败处理数量
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 处理详情
    /// </summary>
    public List<BatchItemResult> Details { get; set; }
}

/// <summary>
/// 批量处理项结果模型
/// </summary>
public class BatchItemResult
{
    /// <summary>
    /// 数据ID
    /// </summary>
    public string DataId { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; }
}

/// <summary>
/// 导入数据请求模型
/// </summary>
public class ImportDataRequest
{
    /// <summary>
    /// 文件数据
    /// </summary>
    public byte[] FileData { get; set; }

    /// <summary>
    /// 文件格式
    /// </summary>
    public string FileFormat { get; set; }

    /// <summary>
    /// 是否覆盖现有数据
    /// </summary>
    public bool OverwriteExisting { get; set; }
}

/// <summary>
/// 数据统计模型
/// </summary>
public class DataStats
{
    /// <summary>
    /// 总数据量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 今日新增
    /// </summary>
    public int TodayAdded { get; set; }

    /// <summary>
    /// 今日更新
    /// </summary>
    public int TodayUpdated { get; set; }

    /// <summary>
    /// 数据类型分布
    /// </summary>
    public Dictionary<string, int> TypeDistribution { get; set; }
}


