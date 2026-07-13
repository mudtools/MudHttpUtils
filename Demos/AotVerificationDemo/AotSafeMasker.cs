using System.Collections.Concurrent;
using System.Text.Json;
using Mud.HttpUtils;

namespace AotVerificationDemo;

/// <summary>
/// AOT 安全的敏感数据脱敏器（编译期字典式实现）。
/// <para>
/// 替代 <see cref="DefaultSensitiveDataMasker"/>（使用反射，AOT 下不安全）。
/// 通过预注册的 {类型 → 属性脱敏规则} 字典在编译期已知所有脱敏目标，
/// 无运行时反射，Native AOT 裁剪后仍可正确工作。
/// </para>
/// </summary>
public class AotSafeSensitiveDataMasker : ISensitiveDataMasker
{
    private readonly ConcurrentDictionary<Type, Func<object, string>> _maskers = new();

    public AotSafeSensitiveDataMasker()
    {
        // 在构造函数中注册所有需要脱敏的类型规则（编译期已知）
        Register<UserDto>(obj =>
        {
            var user = (UserDto)obj;
            var masked = new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = Mask(user.Email, SensitiveDataMaskMode.Mask, 2, 2)
            };
            return JsonSerializer.Serialize(masked, AppJsonContext.Default.UserDto);
        });

        Register<CreateUserRequest>(obj =>
        {
            var req = (CreateUserRequest)obj;
            var masked = new CreateUserRequest
            {
                Name = req.Name,
                Email = "***" // Hide 模式
            };
            return JsonSerializer.Serialize(masked, AppJsonContext.Default.CreateUserRequest);
        });

        Register<LoginResult>(obj =>
        {
            var result = (LoginResult)obj;
            var masked = new LoginResult
            {
                Token = "***",
                ExpiresAt = result.ExpiresAt
            };
            return JsonSerializer.Serialize(masked, AppJsonContext.Default.LoginResult);
        });

        Register<LoginForm>(obj =>
        {
            var form = (LoginForm)obj;
            // LoginForm 用于 FormUrlEncoded Body，不以 JSON 序列化。
            // 返回脱敏后的字符串表示（避免无 JsonSerializerContext 的 JSON 序列化触发 IL3050）
            return $"{{\"username\":\"{form.Username}\",\"password\":\"***\",\"rememberMe\":{form.RememberMe.ToString().ToLowerInvariant()}}}";
        });
    }

    /// <summary>
    /// 注册类型的脱敏规则
    /// </summary>
    public void Register<T>(Func<T, string> maskFunc) where T : class
    {
        _maskers[typeof(T)] = obj => maskFunc((T)obj);
    }

    /// <inheritdoc/>
    public string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask,
        int prefixLength = 2, int suffixLength = 2)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return mode switch
        {
            SensitiveDataMaskMode.Hide => "***",
            SensitiveDataMaskMode.Mask => value.Length > prefixLength + suffixLength
                ? value[..prefixLength] + new string('*', value.Length - prefixLength - suffixLength)
                  + value[^suffixLength..]
                : new string('*', value.Length),
            SensitiveDataMaskMode.TypeOnly => $"[String: {value.Length} chars]",
            _ => value
        };
    }

    /// <inheritdoc/>
    public string MaskObject(object obj)
    {
        if (obj == null)
            return "null";

        var type = obj.GetType();
        if (_maskers.TryGetValue(type, out var masker))
            return masker(obj);

        // 未注册的类型返回类型信息（不序列化未知类型，避免反射）
        return $"[{type.Name}]";
    }
}
