namespace Mud.HttpUtils;

/// <summary>
/// 轻量级 HTTP 发送器接口，仅组合基础 HTTP 操作和 JSON 序列化能力。
/// 适用于不需要 XML 处理和加密功能的场景，遵循接口隔离原则。
/// </summary>
public interface IHttpSender : IBaseHttpClient, IJsonHttpClient
{
}
