namespace HttpClientApiTest.Api;
/// <summary>
/// 用于测试 ContentType 的优先级功能
/// 测试场景：
/// 1. 接口级特性 vs 方法级特性（方法级优先）
/// 2. 默认值回退
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", ContentType = "application/xml")]
public interface IContentTypeTestApi
{
    /// <summary>
    /// 方法1：接口使用 application/xml，方法覆盖为 application/json
    /// 预期结果：使用 application/json
    /// </summary>
    [Post("/api/test1", ContentType = "application/json")]
    Task<string> TestMethod1Async([Body] string data);

    /// <summary>
    /// 方法2：接口使用 application/xml，方法覆盖为 text/plain
    /// 预期结果：使用 text/plain
    /// </summary>
    [Post("/api/test2", ContentType = "text/plain")]
    Task<string> TestMethod2Async([Body] string data);

    /// <summary>
    /// 方法3：接口使用 application/xml，方法未指定
    /// 预期结果：使用 application/xml（从接口继承）
    /// </summary>
    [Post("/api/test3")]
    Task<string> TestMethod3Async([Body] string data);

    /// <summary>
    /// 方法4：使用命名参数方式指定内容类型
    /// 预期结果：使用 application/x-www-form-urlencoded
    /// </summary>
    [Post("/api/test4", ContentType = "application/x-www-form-urlencoded")]
    Task<string> TestMethod4Async([Body] string data);

    /// <summary>
    /// 方法5：使用命名参数方式指定内容类型
    /// 预期结果：使用 multipart/form-data
    /// </summary>
    [Post("/api/test5", ContentType = "multipart/form-data")]
    Task<string> TestMethod5Async([Body] string data);
}


/// <summary>
/// 用于测试 ContentType 的默认值回退功能
/// 测试场景：
/// 接口和方法均未定义 ContentType
/// </summary>
[HttpClientApi("https://api.example2.com")]
public interface IContentTypeWebApiDefaultTestApi
{
    /// <summary>
    /// 方法1：接口和方法均未指定内容类型
    /// 预期结果：使用默认值 application/json
    /// </summary>
    [Post("/api/default")]
    Task<string> TestDefaultAsync([Body] string data);
}


/// <summary>
/// 用于测试 ContentType 与 Body 参数 ContentType 的优先级
/// 测试场景：
/// Body 参数的 ContentType 优先级最高
/// </summary>
[HttpClientApi("https://api.example3.com", ContentType = "application/xml")]
public interface IContentTypeBodyPriorityTestApi
{
    /// <summary>
    /// 方法1：接口使用 application/xml，Body 参数使用 text/html
    /// 预期结果：使用 text/html（Body 参数优先级最高）
    /// </summary>
    [Post("/api/priority1")]
    Task<string> TestPriority1Async([Body(ContentType = "text/html")] string data);

    /// <summary>
    /// 方法2：接口使用 application/xml，Body 参数使用 application/json
    /// 预期结果：使用 application/json（Body 参数优先级最高）
    /// </summary>
    [Post("/api/priority2")]
    Task<string> TestPriority2Async([Body(ContentType = "application/json")] string data);
}
