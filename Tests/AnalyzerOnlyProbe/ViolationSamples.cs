// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// 「仅分析器」CI 探针的违规样例（方案 §3.5）：
// 三类样例分别触发 MUD001 / MUD002 / MUD004，用于验证「关闭源生成器后分析器诊断确实可见」。
// 本文件刻意保留违规写法 —— 若为了让构建通过而"修好"它们，CI 的 grep 断言会立即失败。
// 详见 AnalyzerOnlyProbe.csproj 顶部说明。

using Microsoft.Extensions.DependencyInjection;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.AnalyzerOnlyProbe;

/// <summary>MUD001 样例：<c>[HttpClientApi]</c> 接口方法缺少 HTTP 方法特性。</summary>
[HttpClientApi]
public interface IMissingHttpMethodProbeApi
{
    Task<string> MissingHttpMethodAsync();
}

/// <summary>MUD002 样例：返回类型不是异步形态（裸 <c>string</c>）。</summary>
[HttpClientApi]
public interface IInvalidReturnTypeProbeApi
{
    [Get("/probe/invalid-return-type")]
    string GetString();
}

/// <summary>用于 MUD004 样例的 <see cref="ITokenManager"/> 实现。</summary>
public sealed class ProbeTokenManager : ITokenManager
{
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("probe-token");

    public Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
        => Task.FromResult("probe-token");

    public Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("probe-token");

    public Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
        => Task.FromResult("probe-token");

    public Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
        => Task.FromResult(TokenResult.Empty);

    public bool SupportsBackgroundRefresh => false;

    public void Dispose()
    {
    }
}

/// <summary>MUD004 样例：<see cref="ITokenManager"/> 实现以 <c>AddScoped</c> 注册（应为 Singleton）。</summary>
public static class ProbeRegistrations
{
    public static void Register(IServiceCollection services)
        => services.AddScoped<ITokenManager, ProbeTokenManager>();
}
