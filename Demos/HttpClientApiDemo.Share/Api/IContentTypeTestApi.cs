// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.Api;
/// <summary>
/// 用于测试 ContentType 的优先级功能
/// 测试场景：
/// 1. 接口级特性 vs 方法级特性（方法级优先）
/// 2. 默认值回退
/// </summary>
[HttpClientApi(ContentType = "application/xml")]
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
[HttpClientApi()]
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
[HttpClientApi(ContentType = "application/xml")]
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


    [Get("/api/priority2")]
    Task<string> TestPriority2Async([Query("query1")] string[] array);

    /// <summary>
    /// 测试 [Query] 数组默认行为（与 Separator = null 相同）
    /// 预期结果：query1=val1&query1=val2&query1=val3
    /// </summary>
    [Get("/api/priority3")]
    Task<string> TestPriority3Async([Query("query1", Separator = null)] string[] array);

    /// <summary>
    /// 测试 [Query] 数组使用自定义分隔符
    /// 预期结果：query1=val1,val2,val3
    /// </summary>
    [Get("/api/priority4")]
    Task<string> TestPriority4Async([Query("query1", Separator = ",")] string[] array);

    /// <summary>
    /// 测试 [ArrayQuery] 使用 Separator = null 生成重复参数模式
    /// 预期结果：tags=val1&tags=val2&tags=val3
    /// </summary>
    [Get("/api/priority5")]
    Task<string> TestPriority5Async([ArrayQuery("tags", Separator = null)] string[] array);
}
