// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 请求体序列化模式。
/// </summary>
/// <remarks>
/// <para>
/// 控制请求体 JSON 序列化使用同步还是异步路径。
/// <see cref="Buffered"/> 和 <see cref="Streamed"/> 启用 STJ 源生成 fast-path（<c>SerializeToUtf8Bytes</c> / <c>Utf8JsonWriter</c>），
/// <see cref="Default"/> 使用现有行为（<c>Serialize&lt;T&gt;</c> 返回字符串，包装为 <c>StringContent</c>）。
/// </para>
/// </remarks>
public enum RequestBodySerializationMode
{
    /// <summary>
    /// 现有行为：同步 <c>Serialize&lt;T&gt;</c> 返回字符串，包装为 <c>StringContent</c>（向后兼容，非 fast-path）。
    /// </summary>
    Default,

    /// <summary>
    /// 缓冲模式：<c>SerializeToUtf8Bytes</c> 返回 <c>ByteArrayContent</c>（启用 STJ 源生成 fast-path）。
    /// </summary>
    /// <remarks>
    /// 要求配置的 <see cref="IHttpContentSerializer"/> 实现 <see cref="ISynchronousContentSerializer"/>。
    /// </remarks>
    Buffered,

    /// <summary>
    /// 流式模式：<c>Utf8JsonWriter</c> 写入请求流（启用 fast-path，不缓冲全部内容）。
    /// </summary>
    /// <remarks>
    /// 要求配置的 <see cref="IHttpContentSerializer"/> 实现 <see cref="ISynchronousContentSerializer"/>。
    /// 适用于大 payload 上传场景。
    /// </remarks>
    Streamed
}
