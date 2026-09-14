using System.Text.Json;
using Mud.HttpUtils;

namespace AotVerificationDemo;

/// <summary>
/// Demo 专属脱敏器，子类化库内 <see cref="AotSafeSensitiveDataMasker"/>，
/// 在构造函数中注册 Demo DTO 的脱敏规则。
/// </summary>
/// <remarks>
/// <para>
/// v2.4 重构：原实现为 shadowing 重写（同名类直接实现 <see cref="ISensitiveDataMasker"/>），
/// 导致 Mask 行为与库版本不一致（null/empty 返回原值、用 <c>*</c> 而非 <c>***</c>）。
/// 现改为子类化库内 <see cref="AotSafeSensitiveDataMasker"/>，复用其 AOT 安全的字典式实现，
/// 仅在构造函数中注册 Demo 专属 DTO 规则。
/// </para>
/// <para>
/// 注册方式：先调用 <c>AddSensitiveDataMasker()</c> 注册库内默认实现，
/// 再以 <c>AddSingleton&lt;ISensitiveDataMasker, DemoSensitiveDataMasker&gt;()</c> 覆盖注册子类。
/// </para>
/// </remarks>
public class DemoSensitiveDataMasker : AotSafeSensitiveDataMasker
{
    public DemoSensitiveDataMasker()
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
}
