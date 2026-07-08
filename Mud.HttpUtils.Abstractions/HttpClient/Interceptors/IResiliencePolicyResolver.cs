// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 弹性策略解析器接口，解耦执行器与具体弹性策略实现。
/// 由 Mud.HttpUtils.Resilience 项目提供实现。
/// </summary>
public interface IResiliencePolicyResolver
{
    /// <summary>
    /// 根据执行配置获取弹性策略包装函数。
    /// 返回 null 表示无需弹性策略包装。
    /// </summary>
    /// <remarks>
    /// 返回的包装函数负责在每次重试时克隆请求（HttpRequestMessage 不可重用），
    /// 克隆逻辑封装在实现内部，执行器无需感知。
    /// </remarks>
    /// <param name="options">弹性策略执行配置。</param>
    /// <param name="requestTemplate">请求模板，重试时由实现内部克隆后传给 coreExecute。</param>
    /// <returns>包装函数；为 null 表示无需弹性策略。</returns>
    Func<Func<HttpRequestMessage, CancellationToken, Task<TResult>>, CancellationToken, Task<TResult>>? ResolvePolicyWrapper<TResult>(
        ResilienceExecutionOptions options,
        HttpRequestMessage requestTemplate);
}
