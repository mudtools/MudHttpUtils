namespace HttpClientApiTest.Models;

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class SecureDataRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
}

public class SecureDataResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
}

public class XmlRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class XmlResponse
{
    public string Status { get; set; } = string.Empty;
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class JsonData
{
    public string Id { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SecurePayload
{
    public string EncryptedData { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}
