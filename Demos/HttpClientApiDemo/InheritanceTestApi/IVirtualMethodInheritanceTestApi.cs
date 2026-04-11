namespace HttpClientApiTest.InheritanceTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// 虚方法继承测试基础接口
/// 定义可被重写的基础方法
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 60, IsAbstract = true)]
public interface IVirtualMethodBaseTestApi
{
    /// <summary>
    /// 测试：虚方法基础接口中获取订单信息
    /// 接口：GET /api/v1/orders/{id}
    /// 特点：虚方法，可被重写
    /// </summary>
    [Get("/api/v1/orders/{id}")]
    Task<OrderInfo> GetOrderAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：虚方法基础接口中创建订单
    /// 接口：POST /api/v1/orders
    /// 特点：虚方法，可被重写
    /// </summary>
    [Post("/api/v1/orders")]
    Task<OrderInfo> CreateOrderAsync([Body] OrderInfo order, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：虚方法基础接口中获取订单列表
    /// 接口：GET /api/v1/orders
    /// 特点：虚方法，可被重写
    /// </summary>
    [Get("/api/v1/orders")]
    Task<List<OrderInfo>> GetOrdersAsync([Query] int pageSize = 10, [Query] int pageIndex = 1, CancellationToken cancellationToken = default);
}

/// <summary>
/// 虚方法继承测试接口
/// 继承自IVirtualMethodBaseTestApi，重写部分方法
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "VirtualMethod")]
[Header("X-Virtual-Method", "true")]
public interface IVirtualMethodInheritanceTestApi : IVirtualMethodBaseTestApi
{
    /// <summary>
    /// 测试：获取订单详情
    /// 接口：GET /api/v2/orders/{id}/details
    /// 特点：新增方法，使用不同的路径和返回类型
    /// </summary>
    [Get("/api/v2/orders/{id}/details")]
    Task<OrderDetailInfo> GetOrderDetailsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：高级创建订单（重写基接口方法）
    /// 接口：POST /api/v2/orders/advanced
    /// 特点：重写基接口方法，使用不同的路径和请求参数
    /// </summary>
    [Post("/api/v2/orders/advanced")]
    Task<OrderInfo> CreateOrderAsync([Body] AdvancedOrderInfo order, [Query("priority")] string priority = "normal", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取订单历史记录（新增方法）
    /// 接口：GET /api/v2/orders/{id}/history
    /// 特点：新增方法，不重写基接口方法
    /// </summary>
    [Get("/api/v2/orders/{id}/history")]
    Task<List<OrderHistoryInfo>> GetOrderHistoryAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 虚方法继承测试子接口
/// 继承自IVirtualMethodInheritanceTestApi，进一步重写方法
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 120, TokenManage = "IFeishuAppManager", RegistryGroupName = "VirtualMethodChild")]
[Header("X-Virtual-Method", "child")]
[Header("X-Child-Header", "child-value")]
public interface IVirtualMethodChildInheritanceTestApi : IVirtualMethodInheritanceTestApi
{
    /// <summary>
    /// 测试：获取订单完整详情
    /// 接口：GET /api/v3/orders/{id}/full-details
    /// 特点：新增方法，使用不同的路径和返回类型
    /// </summary>
    [Get("/api/v3/orders/{id}/full-details")]
    Task<OrderFullDetailInfo> GetOrderFullDetailsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：批量获取订单详情（新增方法）
    /// 接口：POST /api/v3/orders/batch-details
    /// 特点：新增方法，批量处理
    /// </summary>
    [Post("/api/v3/orders/batch-details")]
    Task<Dictionary<string, OrderFullDetailInfo>> BatchGetOrderDetailsAsync([Body] List<string> orderIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// 订单信息模型
/// </summary>
public class OrderInfo
{
    /// <summary>
    /// 订单ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 订单编号
    /// </summary>
    public string OrderNumber { get; set; }

    /// <summary>
    /// 订单金额
    /// </summary>
    public double Amount { get; set; }

    /// <summary>
    /// 订单状态
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 高级订单信息模型
/// </summary>
public class AdvancedOrderInfo : OrderInfo
{
    /// <summary>
    /// 订单优先级
    /// </summary>
    public string Priority { get; set; }

    /// <summary>
    /// 配送方式
    /// </summary>
    public string ShippingMethod { get; set; }

    /// <summary>
    /// 预计送达时间
    /// </summary>
    public DateTime EstimatedDeliveryTime { get; set; }
}

/// <summary>
/// 订单详情信息模型
/// </summary>
public class OrderDetailInfo : OrderInfo
{
    /// <summary>
    /// 订单明细
    /// </summary>
    public List<OrderItemInfo> Items { get; set; }

    /// <summary>
    /// 配送地址
    /// </summary>
    public string ShippingAddress { get; set; }

    /// <summary>
    /// 支付方式
    /// </summary>
    public string PaymentMethod { get; set; }
}

/// <summary>
/// 订单完整详情信息模型
/// </summary>
public class OrderFullDetailInfo : OrderDetailInfo
{
    /// <summary>
    /// 用户信息
    /// </summary>
    public UserInfo UserInfo { get; set; }

    /// <summary>
    /// 订单历史记录
    /// </summary>
    public List<OrderHistoryInfo> History { get; set; }

    /// <summary>
    /// 物流信息
    /// </summary>
    public ShippingInfo ShippingInfo { get; set; }
}

/// <summary>
/// 订单项信息模型
/// </summary>
public class OrderItemInfo
{
    /// <summary>
    /// 订单项ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 产品ID
    /// </summary>
    public string ProductId { get; set; }

    /// <summary>
    /// 产品名称
    /// </summary>
    public string ProductName { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 单价
    /// </summary>
    public double UnitPrice { get; set; }
}

/// <summary>
/// 订单历史记录信息模型
/// </summary>
public class OrderHistoryInfo
{
    /// <summary>
    /// 历史记录ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// 操作时间
    /// </summary>
    public DateTime OperationTime { get; set; }

    /// <summary>
    /// 操作人
    /// </summary>
    public string Operator { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string Remarks { get; set; }
}

/// <summary>
/// 用户信息模型
/// </summary>
public class UserInfo
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// 电话
    /// </summary>
    public string Phone { get; set; }
}

/// <summary>
/// 物流信息模型
/// </summary>
public class ShippingInfo
{
    /// <summary>
    /// 物流公司
    /// </summary>
    public string Carrier { get; set; }

    /// <summary>
    /// 物流单号
    /// </summary>
    public string TrackingNumber { get; set; }

    /// <summary>
    /// 物流状态
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// 最新物流更新
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
}


