namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IFormContent"/> 的默认实现：把 <c>application/x-www-form-urlencoded</c> 键值对
/// 包装为 <see cref="HttpContent"/>。
/// </summary>
/// <remarks>
/// 表单内容为纯内存键值对，因此同步与异步构造路径等价（异步路径不产生额外 IO）。
/// </remarks>
public class DefaultFormContent : IFormContent
{
    private readonly Dictionary<string, string> _formData;

    /// <summary>
    /// 初始化 <see cref="DefaultFormContent"/>。
    /// </summary>
    /// <param name="formData">表单键值对。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="formData"/> 为 <c>null</c> 时抛出。</exception>
    public DefaultFormContent(Dictionary<string, string> formData)
    {
        _formData = formData ?? throw new ArgumentNullException(nameof(formData));
    }

    /// <summary>
    /// 创建表单 <see cref="HttpContent"/>。
    /// </summary>
    /// <returns>URL 编码后的表单内容。</returns>
    public HttpContent ToHttpContent()
    {
        return new FormUrlEncodedContent(_formData);
    }

    /// <summary>
    /// 创建表单 <see cref="HttpContent"/>（异步契约对齐；本实现无 IO，直接返回已完成的同步结果）。
    /// </summary>
    /// <param name="progress">上传进度回调（本实现不使用，仅为契约对齐）。</param>
    /// <param name="cancellationToken">取消令牌（本实现不检查）。</param>
    /// <returns>已完成的表单内容任务。</returns>
    public Task<HttpContent> ToHttpContentAsync(IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ToHttpContent());
    }
}
