#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AotVerificationDemo;

// 本文件为 AppJsonContext 的「部分类补充」（手动维护）。
//
// AppJsonContext 的主体（[JsonSourceGenerationOptions] 与标注实体的 [JsonSerializable]）
// 由 Mud.HttpUtils.JsonContextScaffolder 在 pre-build 阶段自动生成，
// 产出位置：obj/<tfm>/GeneratedJsonContext/AppJsonContext.g.cs（见 AotVerificationDemo.csproj 的 GenerateJsonContext 目标）。
//
// 脚手架仅扫描标注 [HttpJsonSerializable] 的实体/DTO。List<UserDto> 是框架开放泛型包装类型，
// 无法（也不应）标注特性，故在此手工补充注册，使 ListUsersAsync / SearchAsync 的反序列化在 AOT 下可用。
// 若新增其它「脚手架无法覆盖」的根类型，请在此处追加对应 [JsonSerializable]。

// [D25] List<UserDto> 等闭合泛型包装类型现在由脚手架自动发现并纳入 AppJsonContext.g.cs。
// 不再需要在此手工补充 [JsonSerializable]，避免 partial class 重复定义导致 STJ 源生成器 hintName 冲突。
// 若新增其它「脚手架无法覆盖」的根类型，请在此处追加对应 [JsonSerializable]。
// 注意：若需手工补充，须确保不与脚手架生成的 AppJsonContext.g.cs 中的 [JsonSerializable] 冲突。

// [场景16修复] EncryptedTokenCache 在 AOT 下要求注入携寄 JsonTypeInfoResolver 的
// JsonSerializerOptions（EncryptedTokenCache 类注释「AOT 注意」/ TMX-11）。场景 16 以
// string 为缓存值类型。注意【不能】把 [JsonSerializable(typeof(string))] 挂到 AppJsonContext
// 上——脚手架生成的 AppJsonContext.g.cs partial 声明与手工标注共存时，STJ 源生成器会因
// hintName 冲突（AppJsonContext.Boolean.g.cs 重复）整体放弃生成（CS8785 → CS0534）。
// 故使用独立 context，仅服务加密令牌缓存的序列化选项注入。
[JsonSerializable(typeof(string))]
internal sealed partial class TokenCacheJsonContext : JsonSerializerContext;
#endif
