namespace HttpClientApiTest.HttpClientApiTestApis;


using HttpClientApiTest.Models;
using Mud.Common.CodeGenerator;


/// <summary>
/// ContentType 默认值测试接口
/// 测试默认值回退机制
/// </summary>
[HttpClientApi("https://api.mudtools.cn/")]
public interface IContentTypeDefaultTestApi
{
    /// <summary>
    /// 测试：无任何指定，使用默认值
    /// 接口：未指定
    /// 方法：未指定
    /// Body：未指定
    /// 预期：使用 application/json（默认值）
    /// </summary>
    [Post("/api/default")]
    Task<TestResponse> TestDefaultFallbackAsync([Body] TestData data);
}
