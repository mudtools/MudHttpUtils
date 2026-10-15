// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// U-8：<see cref="DefaultAppManager{TAppContext}.AppInstantiated"/> 事件参数 ——
/// 惰性工厂<b>首滚实例化成功</b>时发布（同一次实例化恰好一次；重注册同名键的新 Lazy 首滚同样发布）。
/// </summary>
public sealed class AppInstantiatedEventArgs : EventArgs
{
    /// <summary>创建事件参数。</summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="context">刚实例化的应用上下文（以 <see cref="IMudAppContext"/> 承载，消费方可按需向下转型）。</param>
    public AppInstantiatedEventArgs(string appKey, IMudAppContext context)
    {
        AppKey = appKey;
        Context = context;
    }

    /// <summary>应用标识。</summary>
    public string AppKey { get; }

    /// <summary>刚实例化的应用上下文（发布时机严格后于注册表并入，处理器内可见）。</summary>
    public IMudAppContext Context { get; }
}
