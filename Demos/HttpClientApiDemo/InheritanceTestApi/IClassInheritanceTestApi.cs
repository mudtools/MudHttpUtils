namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;
using CodeBaseTest.Interface;

/// <summary>
/// 类继承测试接口
/// 用于测试类实现接口的场景
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 60)]
public interface IClassInheritanceTestApi
{
    /// <summary>
    /// 测试：获取产品信息
    /// 接口：GET /api/v1/products/{id}
    /// 特点：基本API方法
    /// </summary>
    [Get("/api/v1/products/{id}")]
    Task<ProductInfo> GetProductAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：创建产品
    /// 接口：POST /api/v1/products
    /// 特点：基本API方法，包含请求体
    /// </summary>
    [Post("/api/v1/products")]
    Task<ProductInfo> CreateProductAsync([Body] ProductInfo product, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：更新产品
    /// 接口：PUT /api/v1/products/{id}
    /// 特点：基本API方法，包含路径参数和请求体
    /// </summary>
    [Put("/api/v1/products/{id}")]
    Task<bool> UpdateProductAsync([Path] string id, [Body] ProductInfo product, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：删除产品
    /// 接口：DELETE /api/v1/products/{id}
    /// 特点：基本API方法，仅包含路径参数
    /// </summary>
    [Delete("/api/v1/products/{id}")]
    Task<bool> DeleteProductAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 类继承测试实现类
/// 实现IClassInheritanceTestApi接口，展示类继承接口的场景
/// </summary>
public class ClassInheritanceTestApi : IClassInheritanceTestApi
{
    /// <summary>
    /// 获取产品信息
    /// </summary>
    /// <param name="id">产品ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>产品信息</returns>
    public async Task<ProductInfo> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new ProductInfo { Id = id, Name = "Test Product", Price = 100.0, Category = "Test Category" };
    }

    /// <summary>
    /// 创建产品
    /// </summary>
    /// <param name="product">产品信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的产品信息</returns>
    public async Task<ProductInfo> CreateProductAsync(ProductInfo product, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        product.Id = Guid.NewGuid().ToString();
        product.CreatedAt = DateTime.UtcNow;
        return product;
    }

    /// <summary>
    /// 更新产品
    /// </summary>
    /// <param name="id">产品ID</param>
    /// <param name="product">产品信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否更新成功</returns>
    public async Task<bool> UpdateProductAsync(string id, ProductInfo product, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return true;
    }

    /// <summary>
    /// 删除产品
    /// </summary>
    /// <param name="id">产品ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否删除成功</returns>
    public async Task<bool> DeleteProductAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return true;
    }
}

/// <summary>
/// 扩展类继承测试实现类
/// 继承自ClassInheritanceTestApi，展示类继承类的场景
/// </summary>
public class ExtendedClassInheritanceTestApi : ClassInheritanceTestApi
{
    /// <summary>
    /// 测试：获取产品列表（扩展方法）
    /// 接口：GET /api/v1/products
    /// 特点：扩展类新增方法
    /// </summary>
    [Get("/api/v1/products")]
    public async Task<List<ProductInfo>> GetProductsAsync([Query] int pageSize = 10, [Query] int pageIndex = 1, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new List<ProductInfo>
        {
            new ProductInfo { Id = "1", Name = "Product 1", Price = 100.0, Category = "Category A" },
            new ProductInfo { Id = "2", Name = "Product 2", Price = 200.0, Category = "Category B" },
            new ProductInfo { Id = "3", Name = "Product 3", Price = 300.0, Category = "Category A" }
        };
    }

    /// <summary>
    /// 测试：搜索产品（扩展方法）
    /// 接口：GET /api/v1/products/search
    /// 特点：扩展类新增方法
    /// </summary>
    [Get("/api/v1/products/search")]
    public async Task<List<ProductInfo>> SearchProductsAsync([Query] string keyword, [Query] string category = null, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        return new List<ProductInfo>
        {
            new ProductInfo { Id = "1", Name = "Test Product 1", Price = 100.0, Category = "Test Category" },
            new ProductInfo { Id = "2", Name = "Test Product 2", Price = 200.0, Category = "Test Category" }
        };
    }

    /// <summary>
    /// 重写获取产品信息方法
    /// 展示方法重写的场景
    /// </summary>
    /// <param name="id">产品ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>产品信息</returns>
    public new async Task<ProductInfo> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        // 模拟实现，实际应调用HTTP客户端
        // 重写方法可以添加额外的逻辑
        var product = await base.GetProductAsync(id, cancellationToken);
        product.LastAccessed = DateTime.UtcNow;
        return product;
    }
}

/// <summary>
/// 产品信息模型
/// </summary>
public class ProductInfo
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

    /// <summary>
    /// 最后访问时间
    /// </summary>
    public DateTime LastAccessed { get; set; }
}


