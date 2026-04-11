// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.HttpClientApiTestApis;


using HttpClientApiTest.Models;


/// <summary>
/// ContentType 使用方式测试接口
/// 测试场景：
/// 1. HTTP方法特性的ContentType属性
/// 2. 各种常见的内容类型
/// </summary>
[HttpClientApi("https://api.mudtools.cn/")]
public interface IContentTypeUsageTestApi
{
    /// <summary>
    /// 测试1：application/json
    /// </summary>
    [Post("/api/usage/json", ContentType = "application/json")]
    Task<TestResponse> TestApplicationJsonAsync([Body] TestData data);

    /// <summary>
    /// 测试2：application/xml
    /// </summary>
    [Post("/api/usage/xml", ContentType = "application/xml")]
    Task<TestResponse> TestApplicationXmlAsync([Body] TestData data);

    /// <summary>
    /// 测试3：application/x-www-form-urlencoded
    /// </summary>
    [Post("/api/usage/form", ContentType = "application/x-www-form-urlencoded")]
    Task<TestResponse> TestFormUrlEncodedAsync([Body] TestData data);

    /// <summary>
    /// 测试4：multipart/form-data
    /// </summary>
    [Post("/api/usage/multipart", ContentType = "multipart/form-data")]
    Task<TestResponse> TestMultipartFormDataAsync([Body] TestData data);

    /// <summary>
    /// 测试5：text/plain
    /// </summary>
    [Post("/api/usage/text", ContentType = "text/plain")]
    Task<TestResponse> TestTextPlainAsync([Body] TestData data);

    /// <summary>
    /// 测试6：text/html
    /// </summary>
    [Post("/api/usage/html", ContentType = "text/html")]
    Task<TestResponse> TestTextHtmlAsync([Body] TestData data);

    /// <summary>
    /// 测试7：application/yaml
    /// </summary>
    [Post("/api/usage/yaml", ContentType = "application/yaml")]
    Task<TestResponse> TestApplicationYamlAsync([Body] TestData data);

    /// <summary>
    /// 测试8：application/protobuf
    /// </summary>
    [Post("/api/usage/protobuf", ContentType = "application/protobuf")]
    Task<TestResponse> TestProtobufAsync([Body] TestData data);
}
