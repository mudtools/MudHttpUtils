// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.HttpClientApiTestApis;


using HttpClientApiTest.Models;


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
