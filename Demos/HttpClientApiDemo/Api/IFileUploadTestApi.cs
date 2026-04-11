// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.Api;

using HttpClientApiTest.Models;
using System.Text.Json.Serialization;

/// <summary>
/// 文件上传测试API接口
/// 测试 FormContent 特性支持 multipart/form-data 请求
/// </summary>
[HttpClientApi("https://open.feishu.cn", TokenManage = "IFeishuAppManager", RegistryGroupName = "FileUploadTest")]
[Header("Authorization")]
public interface IFileUploadTestApi
{
    /// <summary>
    /// 上传图片（飞书API示例）
    /// 接口：POST /open-apis/im/v1/images
    /// 特点：使用 multipart/form-data 上传文件和文本数据
    /// </summary>
    /// <param name="uploadImageRequest">上传图片请求对象，包含文件路径和其他表单字段</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/>取消操作令牌对象。</param>
    /// <returns>上传结果</returns>
    [Post("/open-apis/im/v1/images")]
    Task<FeishuApiResult<ImageUpdateResult>?> UploadImageAsync(
        [FormContent] UploadImageRequest uploadImageRequest,
        CancellationToken cancellationToken = default);

    [Post("/open-apis/im/v1/images")]
    Task<FeishuApiResult<ImageUpdateResult>?> UploadImage1Async(
        [FormContent] UploadImageRequest uploadImageRequest);

    [Post("/open-apis/im/v1/images/{imageId}")]
    Task<FeishuApiResult<ImageUpdateResult>?> UploadImageByIdAsync(
      [FormContent] UploadImageRequest uploadImageRequest,
      [Path] string imageId,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// 飞书API通用结果包装类
    /// </summary>
    public class FeishuApiResult<T>
    {
        /// <summary>
        /// 错误码
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        /// <summary>
        /// 返回数据
        /// </summary>
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }




}
