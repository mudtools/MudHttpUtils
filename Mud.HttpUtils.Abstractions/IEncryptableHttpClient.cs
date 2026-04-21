namespace Mud.HttpUtils;

public interface IEncryptableHttpClient
{
    string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json);
}
