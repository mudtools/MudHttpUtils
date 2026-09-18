// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

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

    /// <summary>
    /// [COMP-2 修复] 供 <see cref="CreateWithBaseAddress"/> 构造同类型克隆使用的内部构造。
    /// </summary>
    /// <remarks>
    /// 刻意声明为 <c>internal</c> 而非 <c>public</c>：本类可被 DI 解析，新增 public 构造重载会引入
    /// 构造歧义（见 CHANGELOG 的 BC-30/31/32：TMX-19 正是为消除
    /// "The following constructors are ambiguous" 而把非主构造降级为 internal）。
    /// </remarks>
    /// <param name="factory">IHttpClientFactory 实例</param>
    /// <param name="clientName">Named HttpClient 名称</param>
    /// <param name="recoveryExecutor">令牌恢复执行器</param>
    /// <param name="encryptionProvider">加密提供器（可选）</param>
    /// <param name="options">配置选项</param>
    /// <param name="overrideBaseAddress">覆盖的基地址</param>
    /// <param name="jsonOptions">JSON 序列化选项（可选）</param>
    /// <param name="contentSerializer">HTTP 内容序列化器（可选）</param>
    internal TokenRecoveryEnhancedClient(
        IHttpClientFactory factory,
        string clientName,
        TokenRecoveryExecutor recoveryExecutor,
        IEncryptionProvider? encryptionProvider,
        EnhancedHttpClientOptions options,
        Uri overrideBaseAddress,
        IOptions<JsonSerializerOptions>? jsonOptions,
        IHttpContentSerializer? contentSerializer)
        : base(factory, clientName, encryptionProvider, options, overrideBaseAddress, jsonOptions, contentSerializer)
    {
        _recoveryExecutor = recoveryExecutor
            ?? throw new ArgumentNullException(nameof(recoveryExecutor));
    }

    /// <summary>
    /// 以新的基地址构造同类型克隆，保留 401 令牌恢复能力。
    /// </summary>
    /// <remarks>
    /// [COMP-2 修复] 若不重写，<c>WithBaseAddress</c> 会走到基类实现并返回普通
    /// <see cref="HttpClientFactoryEnhancedClient"/>，使令牌恢复能力被静默丢弃。
    /// </remarks>
    /// <param name="baseAddress">新的基地址。</param>
    /// <returns>仍具备令牌恢复能力、且指向新基地址的新实例。</returns>
    internal override HttpClientFactoryEnhancedClient CreateWithBaseAddress(Uri baseAddress)
        => new TokenRecoveryEnhancedClient(
            Factory,
            ClientName!,
            _recoveryExecutor,
            EncryptionProvider,
            ClientOptions,
            baseAddress,
            JsonOptions,
            // 基类 ContentSerializer 永不返回 null（未注入时回退默认实现），故直接传递即可。
            ContentSerializer);
}
