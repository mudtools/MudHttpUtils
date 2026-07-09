// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 支持令牌恢复的 EnhancedHttpClient 实现。
/// </summary>
/// <remarks>
/// 此类在 <see cref="HttpClientFactoryEnhancedClient"/> 基础上，通过重写
/// <see cref="EnhancedHttpClient.SendCoreAsync"/> 方法，在 HTTP 请求收到 401 响应时
/// 自动刷新令牌并重试。
/// <para>
/// 相比通过 <c>AddHttpMessageHandler</c> 注册 <see cref="TokenRecoveryDelegatingHandler"/>，
/// 此方案将恢复逻辑从 Handler 管道层提升到 EnhancedHttpClient 层，
/// 避免了 IHttpClientFactory Handler 管道构建时的循环依赖问题。
/// </para>
/// <para>
/// 主要用于多应用/多租户场景（如 Mud.Feishu），其中 HttpClient 的创建与 TokenManager 的创建
/// 存在循环依赖，无法在 Handler 管道构建阶段解析 TokenManager。
/// </para>
/// </remarks>
public sealed class TokenRecoveryEnhancedClient : HttpClientFactoryEnhancedClient
{
    private readonly TokenRecoveryExecutor _recoveryExecutor;

    /// <summary>
    /// 初始化支持令牌恢复的 EnhancedHttpClient 实例。
    /// </summary>
    /// <param name="factory">IHttpClientFactory 实例</param>
    /// <param name="clientName">Named HttpClient 名称</param>
    /// <param name="recoveryExecutor">令牌恢复执行器</param>
    /// <param name="encryptionProvider">加密提供器（可选）</param>
    /// <param name="options">配置选项（可选）</param>
    /// <exception cref="ArgumentNullException">factory、clientName 或 recoveryExecutor 为 null</exception>
    public TokenRecoveryEnhancedClient(
        IHttpClientFactory factory,
        string clientName,
        TokenRecoveryExecutor recoveryExecutor,
        IEncryptionProvider? encryptionProvider = null,
        EnhancedHttpClientOptions? options = null)
        : base(factory, clientName, encryptionProvider, options)
    {
        _recoveryExecutor = recoveryExecutor
            ?? throw new ArgumentNullException(nameof(recoveryExecutor));
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        // 委托给 TokenRecoveryExecutor，传入 base.SendCoreAsync 作为实际发送函数
        return _recoveryExecutor.ExecuteAsync(
            request,
            (req, ct) => base.SendCoreAsync(req, completionOption, ct),
            cancellationToken);
    }
}
