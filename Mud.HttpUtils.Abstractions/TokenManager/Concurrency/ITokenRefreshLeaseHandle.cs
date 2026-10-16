// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 刷新租约句柄 —— 携带所有权凭据（实现方内部持有，如 Redis 租约值 GUID），
/// 释放时按所有权校验删除（compare-and-del），防止迟到释放误删他人的租约。
/// </summary>
public interface ITokenRefreshLeaseHandle
{
    /// <summary>租约键（与获取时传入一致）。</summary>
    string LeaseKey { get; }

    /// <summary>
    /// 释放租约（按所有权校验；重复释放幂等 —— 所有权已被过期/转手后为 no-op）。
    /// 实现不得抛出异常阻断主路径（存储不可用时吞异常并按 no-op 处置）。
    /// </summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
