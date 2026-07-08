// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 表单内容接口，定义了将表单数据转换为 HTTP 内容的标准方法。
/// </summary>
/// <remarks>
/// 该接口用于封装各种类型的表单数据（如 application/x-www-form-urlencoded、multipart/form-data 等），
/// 并提供同步和异步两种方式将其转换为 <see cref="HttpContent"/> 对象。
/// <para>
/// 实现此接口的类可以支持进度报告功能，适用于需要上传大文件或显示上传进度的场景。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 实现一个简单的表单内容
/// public class SimpleFormContent : IFormContent
/// {
///     private readonly Dictionary&lt;string, string&gt; _formData;
///     
///     public SimpleFormContent(Dictionary&lt;string, string&gt; formData)
///     {
///         _formData = formData;
///     }
///     
///     public HttpContent ToHttpContent()
///     {
///         return new FormUrlEncodedContent(_formData);
///     }
///     
///     public Task&lt;HttpContent&gt; ToHttpContentAsync(IProgress&lt;long&gt;? progress = null, CancellationToken cancellationToken = default)
///     {
///         // 对于简单表单，同步和异步实现相同
///         return Task.FromResult(ToHttpContent());
///     }
/// }
/// 
/// // 使用示例
/// var formData = new Dictionary&lt;string, string&gt;
/// {
///     { "username", "john" },
///     { "email", "john@example.com" }
/// };
/// var formContent = new SimpleFormContent(formData);
/// var httpContent = formContent.ToHttpContent();
/// </code>
/// </example>
/// <seealso cref="HttpContent"/>
public interface IFormContent
{
    /// <summary>
    /// 将表单数据转换为 HTTP 内容对象（同步方法）。
    /// </summary>
    /// <returns>表示表单数据的 <see cref="HttpContent"/> 实例。</returns>
    /// <remarks>
    /// 此方法适用于小型表单数据的快速转换。对于大型数据或需要进度报告的场景，建议使用异步方法 <see cref="ToHttpContentAsync"/>。
    /// <para>
    /// 实现应确保返回的 <see cref="HttpContent"/> 对象具有正确的 Content-Type 头（如 "application/x-www-form-urlencoded" 或 "multipart/form-data"）。
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">当表单数据格式不正确或无法转换时抛出。</exception>
    HttpContent ToHttpContent();

    /// <summary>
    /// 将表单数据异步转换为 HTTP 内容对象，支持进度报告。
    /// </summary>
    /// <param name="progress">可选的进度报告器，用于报告转换过程的进度（以字节为单位）。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>表示表单数据的 <see cref="HttpContent"/> 实例的异步任务。</returns>
    /// <remarks>
    /// 此方法适用于大型表单数据或文件上传场景，支持进度报告以便用户界面能够显示上传进度。
    /// <para>
    /// 如果不需要进度报告，可以将 <paramref name="progress"/> 参数设置为 <c>null</c>。
    /// </para>
    /// <para>
    /// 实现应考虑取消令牌的状态，在操作被取消时抛出 <see cref="OperationCanceledException"/>。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 带进度报告的异步转换
    /// var progress = new Progress&lt;long&gt;(bytesProcessed =>
    /// {
    ///     Console.WriteLine($"已处理: {bytesProcessed} 字节");
    /// });
    /// 
    /// var httpContent = await formContent.ToHttpContentAsync(progress, cancellationToken);
    /// </code>
    /// </example>
    /// <exception cref="System.OperationCanceledException">当操作被取消时抛出。</exception>
    /// <exception cref="System.InvalidOperationException">当表单数据格式不正确或无法转换时抛出。</exception>
    Task<HttpContent> ToHttpContentAsync(IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
