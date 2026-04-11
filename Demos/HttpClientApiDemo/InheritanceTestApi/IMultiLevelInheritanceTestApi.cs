namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// 多层继承测试接口
/// 继承自ISingleInheritanceTestApi，测试多层继承关系
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 120, RegistryGroupName = "MultiLevelInheritance")]
[Header("X-Application-Id", "multi-level-app")]
public interface IMultiLevelInheritanceTestApi : ISingleInheritanceTestApi
{
    /// <summary>
    /// 测试：批量获取实体详情（多层继承新增方法）
    /// 接口：POST /api/v1/entities/batch-details
    /// 特点：新增方法，使用多层继承的配置
    /// </summary>
    [Post("/api/v1/entities/batch-details")]
    Task<Dictionary<string, EntityDetailInfo>> BatchGetEntityDetailsAsync([Body] BatchEntityRequest body, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：导出实体数据（多层继承新增方法）
    /// 接口：GET /api/v1/entities/export
    /// 特点：新增方法，使用多层继承的配置
    /// </summary>
    [Get("/api/v1/entities/export")]
    Task<byte[]> ExportEntitiesAsync([Query] string format = "excel", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：导入实体数据（多层继承新增方法）
    /// 接口：POST /api/v1/entities/import
    /// 特点：新增方法，使用多层继承的配置
    /// </summary>
    [Post("/api/v1/entities/import")]
    Task<ImportResult> ImportEntitiesAsync([Body] ImportRequest body, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取实体统计信息（多层继承新增方法）
    /// 接口：GET /api/v1/entities/stats
    /// 特点：新增方法，使用多层继承的配置
    /// </summary>
    [Get("/api/v1/entities/stats")]
    Task<EntityStats> GetEntityStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：高级搜索实体
    /// 接口：POST /api/v1/entities/advanced-search
    /// 特点：新增方法，使用不同的HTTP方法和参数
    /// </summary>
    [Post("/api/v1/entities/advanced-search")]
    Task<List<EntityDetailInfo>> AdvancedSearchEntitiesAsync([Body] AdvancedSearchRequest body, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取实体历史记录（多层继承新增方法）
    /// 接口：GET /api/v1/entities/{id}/history
    /// 特点：新增方法，使用多层继承的配置
    /// </summary>
    [Get("/api/v1/entities/{id}/history")]
    Task<List<EntityHistory>> GetEntityHistoryAsync([Path] string id, [Query] int limit = 10, CancellationToken cancellationToken = default);
}

/// <summary>
/// 批量实体请求模型
/// </summary>
public class BatchEntityRequest
{
    /// <summary>
    /// 实体ID列表
    /// </summary>
    public List<string> Ids { get; set; }
}

/// <summary>
/// 导入请求模型
/// </summary>
public class ImportRequest
{
    /// <summary>
    /// 文件数据
    /// </summary>
    public byte[] FileData { get; set; }

    /// <summary>
    /// 文件格式
    /// </summary>
    public string Format { get; set; }

    /// <summary>
    /// 是否覆盖现有数据
    /// </summary>
    public bool Overwrite { get; set; }
}

/// <summary>
/// 导入结果模型
/// </summary>
public class ImportResult
{
    /// <summary>
    /// 成功导入数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败导入数量
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 失败详情
    /// </summary>
    public List<ImportFailure> Failures { get; set; }
}

/// <summary>
/// 导入失败详情模型
/// </summary>
public class ImportFailure
{
    /// <summary>
    /// 行号
    /// </summary>
    public int RowNumber { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; }
}

/// <summary>
/// 实体统计信息模型
/// </summary>
public class EntityStats
{
    /// <summary>
    /// 总实体数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 活跃实体数量
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// 今日新增数量
    /// </summary>
    public int TodayAddedCount { get; set; }

    /// <summary>
    /// 今日更新数量
    /// </summary>
    public int TodayUpdatedCount { get; set; }
}

/// <summary>
/// 高级搜索请求模型
/// </summary>
public class AdvancedSearchRequest
{
    /// <summary>
    /// 搜索关键词
    /// </summary>
    public string Keyword { get; set; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public List<string> Tags { get; set; }

    /// <summary>
    /// 创建时间范围开始
    /// </summary>
    public DateTime? CreatedAtStart { get; set; }

    /// <summary>
    /// 创建时间范围结束
    /// </summary>
    public DateTime? CreatedAtEnd { get; set; }
}

/// <summary>
/// 实体历史记录模型
/// </summary>
public class EntityHistory
{
    /// <summary>
    /// 历史记录ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public string OperationType { get; set; }

    /// <summary>
    /// 操作时间
    /// </summary>
    public DateTime OperationTime { get; set; }

    /// <summary>
    /// 操作人
    /// </summary>
    public string Operator { get; set; }

    /// <summary>
    /// 变更内容
    /// </summary>
    public string ChangeContent { get; set; }
}


