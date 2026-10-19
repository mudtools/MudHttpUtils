// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 异步 HTTP 内容构造扩展点：签名 / 取凭据需在内容就绪前完成的场景（如支付报文签名）。
/// </summary>
/// <remarks>
/// <para>
/// 同步契约 <see cref="IHttpContentSerializer.ToHttpContent{T}"/> 无法表达"签名在报文体内、取密钥异步"的
/// 构造顺序；实现本接口后，运行期执行器在发送前探测并优先走异步路径（生成代码不感知，内容构造在
/// 运行期完成）。未注册时逐字节保持既有同步行为。
/// </para>
/// <para>
/// 继承 <see cref="IHttpContentSerializer"/>：既有实现零改动，仅新实现需额外提供异步成员。
/// </para>
/// </remarks>
public interface IAsyncHttpContentSerializer : IHttpContentSerializer
{
    /// <summary>将对象异步序列化为 <see cref="System.Net.Http.HttpContent"/>。</summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象。</param>
    /// <param name="options">序列化选项（实现特定）。可为 null，使用默认选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示序列化内容的 <see cref="System.Net.Http.HttpContent"/> 实例；item 为 null 时返回 null。</returns>
    ValueTask<System.Net.Http.HttpContent?> ToHttpContentAsync<T>(
        T item, object? options = null, CancellationToken cancellationToken = default);
}
