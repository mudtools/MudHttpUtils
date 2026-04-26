// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.HttpClientApiTestApis;


using HttpClientApiTest.Models;


/// <summary>
/// ContentType 默认值测试接口
/// 测试默认值回退机制
/// </summary>
[HttpClientApi()]
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
