// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;

namespace Mud.HttpUtils;

/// <summary>
/// 提供源生成 API 客户端的静态工厂方法（AOT 安全，无反射回退）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ForGenerated{T}(IServiceProvider)"/> 从依赖注入容器解析源生成的 API 客户端实现。
/// 与 <c>AddMudHttpClient</c> + <c>AddWebApiHttpClient</c> 的组合相比，此入口专为 AOT 场景设计：
/// 不标注 <c>[RequiresUnreferencedCode]</c>，不使用反射回退。
/// </para>
/// <para>
/// <b>使用方式</b>：
/// <code>
/// // 1. 注册基础设施 + 源生成的 API 接口
/// services.AddMudHttpGeneratedClient&lt;IUserApi&gt;("default", c => c.BaseAddress = new Uri("https://api.example.com"));
/// services.AddWebApiHttpClient(); // 源生成器自动生成的注册方法
///
/// // 2. 解析 API 客户端
/// var userApi = RestService.ForGenerated&lt;IUserApi&gt;(serviceProvider);
/// </code>
/// </para>
/// <para>
/// 若接口未标记 <c>[HttpClientApi]</c> 特性或未注册源生成实现，<see cref="ForGenerated{T}"/>
/// 将抛出 <see cref="InvalidOperationException"/> 指回生成输出。
/// </para>
/// </remarks>
public static class RestService
{
    /// <summary>
    /// 从依赖注入容器解析源生成的 API 客户端实现。
    /// </summary>
    /// <typeparam name="T">标记了 <c>[HttpClientApi]</c> 特性的接口类型。</typeparam>
    /// <param name="serviceProvider">依赖注入服务提供者。</param>
    /// <returns>源生成的 API 客户端实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 当 <typeparamref name="T"/> 未在容器中注册时抛出。
    /// 请确保：
    /// (1) 接口标记了 <c>[HttpClientApi]</c> 特性；
    /// (2) 已调用 <c>AddMudHttpGeneratedClient&lt;T&gt;()</c> 或 <c>AddWebApiHttpClient()</c> 注册。
    /// </exception>
    public static T ForGenerated<T>(IServiceProvider serviceProvider) where T : class
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        var service = serviceProvider.GetService(typeof(T)) as T;
        if (service == null)
        {
            throw new InvalidOperationException(
                $"无法解析类型 {typeof(T).FullName}。请确保：" +
                "(1) 接口标记了 [HttpClientApi] 特性；" +
                "(2) 已调用 AddMudHttpGeneratedClient<T>() 或 AddWebApiHttpClient() 注册源生成实现。" +
                "若接口为泛型接口，当前不支持代码生成（HTTPCLIENT012）。");
        }

        return service;
    }
}
