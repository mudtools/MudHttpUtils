namespace HttpClientApiTest.HttpClientApiTestApis;


using HttpClientApiTest.Models;
using Mud.Common.CodeGenerator;


/// <summary>
/// ContentType 优先级测试接口
/// 测试场景：
/// 1. 方法级特性 > 接口级特性
/// 2. Body参数ContentType > 方法级/接口级特性
/// 3. 默认值回退机制
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", ContentType = "application/xml")]
public interface IContentTypePriorityTestApi
{
    /// <summary>
    /// 测试1：方法级别覆盖接口级别
    /// 接口：application/xml，方法：application/json
    /// 预期：使用 application/json
    /// </summary>
    [Post("/api/test/priority1", ContentType = "application/json")]
    Task<TestResponse> TestMethodOverrideInterfaceAsync([Body] TestData data);

    /// <summary>
    /// 测试2：继承接口级别
    /// 接口：application/xml，方法：未指定
    /// 预期：使用 application/xml
    /// </summary>
    [Post("/api/test/priority2")]
    Task<TestResponse> TestInheritInterfaceAsync([Body] TestData data);

    /// <summary>
    /// 测试3：Body参数优先级最高
    /// 接口：application/xml，方法：application/json，Body：text/html
    /// 预期：使用 text/html
    /// </summary>
    [Post("/api/test/priority3", ContentType = "application/json")]
    Task<TestResponse> TestBodyParameterPriorityAsync([Body(ContentType = "text/html")] TestData data);

    /// <summary>
    /// 测试4：方法未指定，继承接口级别
    /// 接口：application/xml
    /// 方法：未指定
    /// Body：未指定
    /// 预期：使用 application/xml（继承接口级别）
    /// </summary>
    [Post("/api/test/priority4")]
    Task<TestResponse> TestDefaultFallbackAsync([Body] TestData data);
}
