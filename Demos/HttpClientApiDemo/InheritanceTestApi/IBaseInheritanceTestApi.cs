namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// 基础继承测试接口
/// 作为所有继承测试的基类接口
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 60, IsAbstract = true)]
public interface IBaseInheritanceTestApi
{
    /// <summary>
    /// 测试：基础接口中获取实体信息
    /// 接口：GET /api/v1/entities/{id}
    /// 特点：基类方法，使用默认配置
    /// </summary>
    [Get("/api/v1/entities/{id}")]
    Task<EntityInfo> GetEntityAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：基础接口中创建实体
    /// 接口：POST /api/v1/entities
    /// 特点：基类方法，使用默认配置
    /// </summary>
    [Post("/api/v1/entities")]
    Task<EntityInfo> CreateEntityAsync([Body] EntityInfo entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：基础接口中更新实体
    /// 接口：PUT /api/v1/entities/{id}
    /// 特点：基类方法，使用默认配置
    /// </summary>
    [Put("/api/v1/entities/{id}")]
    Task<bool> UpdateEntityAsync([Path] string id, [Body] EntityInfo entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：基础接口中删除实体
    /// 接口：DELETE /api/v1/entities/{id}
    /// 特点：基类方法，使用默认配置
    /// </summary>
    [Delete("/api/v1/entities/{id}")]
    Task<bool> DeleteEntityAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 实体信息模型
/// </summary>
public class EntityInfo
{
    /// <summary>
    /// 实体ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 实体名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 实体描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
