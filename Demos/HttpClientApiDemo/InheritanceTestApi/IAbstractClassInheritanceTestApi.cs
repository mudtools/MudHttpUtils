namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// 抽象类继承测试基础接口
/// 定义抽象方法，不能直接实例化
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 60, IsAbstract = true)]
public interface IAbstractClassBaseTestApi
{
    /// <summary>
    /// 测试：抽象方法获取产品信息
    /// 接口：GET /api/v1/products/{id}
    /// 特点：抽象方法，必须由派生接口实现
    /// </summary>
    [Get("/api/v1/products/{id}")]
    Task<ProductBaseInfo> GetProductAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：抽象方法创建产品
    /// 接口：POST /api/v1/products
    /// 特点：抽象方法，必须由派生接口实现
    /// </summary>
    [Post("/api/v1/products")]
    Task<ProductBaseInfo> CreateProductAsync([Body] ProductBaseInfo product, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：抽象方法更新产品
    /// 接口：PUT /api/v1/products/{id}
    /// 特点：抽象方法，必须由派生接口实现
    /// </summary>
    [Put("/api/v1/products/{id}")]
    Task<bool> UpdateProductAsync([Path] string id, [Body] ProductBaseInfo product, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：抽象方法删除产品
    /// 接口：DELETE /api/v1/products/{id}
    /// 特点：抽象方法，必须由派生接口实现
    /// </summary>
    [Delete("/api/v1/products/{id}")]
    Task<bool> DeleteProductAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 具体产品服务接口
/// 继承自抽象基接口，实现所有抽象方法
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "ConcreteProductService")]
[Header("X-Service-Type", "concrete-product")]
public interface IConcreteProductServiceTestApi : IAbstractClassBaseTestApi
{
    /// <summary>
    /// 测试：获取产品详情（扩展抽象方法）
    /// 接口：GET /api/v1/products/{id}/details
    /// 特点：扩展抽象接口，添加新方法
    /// </summary>
    [Get("/api/v1/products/{id}/details")]
    Task<ProductDetailInfo> GetProductDetailsAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取产品库存信息（扩展抽象方法）
    /// 接口：GET /api/v1/products/{id}/inventory
    /// 特点：扩展抽象接口，添加新方法
    /// </summary>
    [Get("/api/v1/products/{id}/inventory")]
    Task<ProductInventoryInfo> GetProductInventoryAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：搜索产品（扩展抽象方法）
    /// 接口：GET /api/v1/products/search
    /// 特点：扩展抽象接口，添加新方法
    /// </summary>
    [Get("/api/v1/products/search")]
    Task<List<ProductBaseInfo>> SearchProductsAsync([Query] string keyword, [Query] string category = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 高级产品服务接口
/// 继承自具体产品服务接口，进一步扩展功能
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 120, TokenManage = "IFeishuAppManager", RegistryGroupName = "AdvancedProductService")]
[Header("X-Service-Type", "advanced-product")]
[Header("X-Advanced-Features", "enabled")]
public interface IAdvancedProductServiceTestApi : IConcreteProductServiceTestApi
{
    /// <summary>
    /// 测试：批量获取产品信息（高级功能）
    /// 接口：POST /api/v1/products/batch
    /// 特点：高级服务新增方法，批量处理
    /// </summary>
    [Post("/api/v1/products/batch")]
    Task<Dictionary<string, ProductDetailInfo>> BatchGetProductsAsync([Body] List<string> productIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取产品分析报告（高级功能）
    /// 接口：GET /api/v1/products/{id}/analysis
    /// 特点：高级服务新增方法，数据分析
    /// </summary>
    [Get("/api/v1/products/{id}/analysis")]
    Task<ProductAnalysisReport> GetProductAnalysisAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：批量更新产品价格（高级功能）
    /// 接口：POST /api/v1/products/batch-prices
    /// 特点：高级服务新增方法，批量更新
    /// </summary>
    [Post("/api/v1/products/batch-prices")]
    Task<bool> BatchUpdateProductPricesAsync([Body] List<ProductPriceUpdate> priceUpdates, CancellationToken cancellationToken = default);
}

/// <summary>
/// 产品基础信息模型
/// </summary>
public class ProductBaseInfo
{
    /// <summary>
    /// 产品ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 产品名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 产品价格
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// 产品分类
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 产品详情信息模型
/// </summary>
public class ProductDetailInfo : ProductBaseInfo
{
    /// <summary>
    /// 产品描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 产品图片
    /// </summary>
    public List<string> Images { get; set; }

    /// <summary>
    /// 产品规格
    /// </summary>
    public List<ProductSpecification> Specifications { get; set; }

    /// <summary>
    /// 产品标签
    /// </summary>
    public List<string> Tags { get; set; }
}

/// <summary>
/// 产品库存信息模型
/// </summary>
public class ProductInventoryInfo
{
    /// <summary>
    /// 产品ID
    /// </summary>
    public string ProductId { get; set; }

    /// <summary>
    /// 当前库存
    /// </summary>
    public int CurrentStock { get; set; }

    /// <summary>
    /// 警戒库存
    /// </summary>
    public int AlertStock { get; set; }

    /// <summary>
    /// 库存更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; }

    /// <summary>
    /// 供应商信息
    /// </summary>
    public SupplierInfo SupplierInfo { get; set; }
}

/// <summary>
/// 产品规格信息模型
/// </summary>
public class ProductSpecification
{
    /// <summary>
    /// 规格名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 规格值
    /// </summary>
    public string Value { get; set; }
}

/// <summary>
/// 产品分析报告模型
/// </summary>
public class ProductAnalysisReport
{
    /// <summary>
    /// 产品ID
    /// </summary>
    public string ProductId { get; set; }

    /// <summary>
    /// 销售统计
    /// </summary>
    public SalesStats SalesStats { get; set; }

    /// <summary>
    /// 库存统计
    /// </summary>
    public InventoryStats InventoryStats { get; set; }

    /// <summary>
    /// 价格趋势
    /// </summary>
    public List<PriceTrendPoint> PriceTrends { get; set; }

    /// <summary>
    /// 分析时间
    /// </summary>
    public DateTime AnalysisTime { get; set; }
}

/// <summary>
/// 产品价格更新模型
/// </summary>
public class ProductPriceUpdate
{
    /// <summary>
    /// 产品ID
    /// </summary>
    public string ProductId { get; set; }

    /// <summary>
    /// 新价格
    /// </summary>
    public double NewPrice { get; set; }

    /// <summary>
    /// 更新原因
    /// </summary>
    public string UpdateReason { get; set; }

    /// <summary>
    /// 生效时间
    /// </summary>
    public DateTime EffectiveTime { get; set; }
}

/// <summary>
/// 销售统计信息模型
/// </summary>
public class SalesStats
{
    /// <summary>
    /// 总销量
    /// </summary>
    public int TotalSales { get; set; }

    /// <summary>
    /// 月销量
    /// </summary>
    public int MonthlySales { get; set; }

    /// <summary>
    /// 日销量
    /// </summary>
    public int DailySales { get; set; }

    /// <summary>
    /// 销售趋势
    /// </summary>
    public string Trend { get; set; }
}

/// <summary>
/// 库存统计信息模型
/// </summary>
public class InventoryStats
{
    /// <summary>
    /// 当前库存
    /// </summary>
    public int CurrentStock { get; set; }

    /// <summary>
    /// 平均库存
    /// </summary>
    public int AverageStock { get; set; }

    /// <summary>
    /// 库存周转率
    /// </summary>
    public double TurnoverRate { get; set; }

    /// <summary>
    /// 断货次数
    /// </summary>
    public int OutOfStockCount { get; set; }
}

/// <summary>
/// 价格趋势点模型
/// </summary>
public class PriceTrendPoint
{
    /// <summary>
    /// 时间点
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 价格
    /// </summary>
    public double Price { get; set; }
}

/// <summary>
/// 供应商信息模型
/// </summary>
public class SupplierInfo
{
    /// <summary>
    /// 供应商ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 供应商名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 联系人
    /// </summary>
    public string ContactPerson { get; set; }

    /// <summary>
    /// 联系电话
    /// </summary>
    public string ContactPhone { get; set; }

    /// <summary>
    /// 响应时间（天）
    /// </summary>
    public int ResponseTime { get; set; }
}


