// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Token注入模式
/// </summary>
public enum TokenInjectionMode
{
    /// <summary>
    /// 写入HTTP Header（默认）
    /// </summary>
    Header = 0,

    /// <summary>
    /// 写入URL查询参数（如 ?access_token=xxx）
    /// </summary>
    Query = 1,

    /// <summary>
    /// 写入URL路径参数（如 /api/{token}/data）
    /// </summary>
    Path = 2,
}
