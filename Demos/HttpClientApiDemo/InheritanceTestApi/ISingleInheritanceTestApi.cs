namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// 单层继承测试接口
/// 继承自IBaseInheritanceTestApi，测试单层继承关系
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "SingleInheritance")]
public interface ISingleInheritanceTestApi : IBaseInheritanceTestApi
{
    /// <summary>
    /// 测试：获取实体列表（单层继承新增方法）
    /// 接口：GET /api/v1/entities
    /// 特点：新增方法，使用继承的配置
    /// </summary>
    [Get("/api/v1/entities")]
    Task<List<EntityInfo>> GetEntitiesAsync([Query] int pageSize = 10, [Query] int pageIndex = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：搜索实体（单层继承新增方法）
    /// 接口：GET /api/v1/entities/search
    /// 特点：新增方法，使用继承的配置
    /// </summary>
    [Get("/api/v1/entities/search")]
    Task<List<EntityInfo>> SearchEntitiesAsync([Query] string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取实体详情
    /// 接口：GET /api/v1/entities/{id}/details
    /// 特点：新增方法，不重写基接口方法
    /// </summary>
    [Get("/api/v1/entities/{id}/details")]
    Task<EntityInfo> GetEntityDetailsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：批量删除实体（单层继承新增方法）
    /// 接口：POST /api/v1/entities/batch-delete
    /// 特点：新增方法，使用继承的配置
    /// </summary>
    [Post("/api/v1/entities/batch-delete")]
    Task<bool> BatchDeleteEntitiesAsync([Body] BatchDeleteRequest body, CancellationToken cancellationToken = default);
}

/// <summary>
/// 实体详情信息模型
/// </summary>
public class EntityDetailInfo : EntityInfo
{
    /// <summary>
    /// 实体详情
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// 实体标签
    /// </summary>
    public List<string> Tags { get; set; }

    /// <summary>
    /// 实体元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }
}

/// <summary>
/// 批量删除请求模型
/// </summary>
public class BatchDeleteRequest
{
    /// <summary>
    /// 要删除的实体ID列表
    /// </summary>
    public List<string> Ids { get; set; }
}


