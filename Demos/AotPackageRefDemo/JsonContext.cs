#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;

namespace AotPackageRefDemo;

// 手动 JsonSerializerContext — 为 AOT 序列化声明类型元数据。
//
// 本 Demo 有意使用手动 context，不依赖 JsonContextScaffolder：
//   CI（aot-packageref 作业）在 publish --no-restore 下 DotNetToolReference
//   解析出的 mud-jsonctx 不在 PATH，脚手架退出码 127 并清理产物，导致
//   DemoJsonContext 缺失（CS0103）与 AOT006。
//
// 已在 AotPackageRefDemo.csproj 显式设置 MudEnableJsonContextScaffolder=false
// （PublishAot=true 默认会启用脚手架），避免与本文件的 partial 定义产生
// CS0579（JsonSourceGenerationOptions 重复）与 STJ hintName 冲突（CS8785）。
//
// [HttpJsonSerializable] 标注仅用于 AOT006 覆盖检查 / 工具扫描示意；
// 类型元数据由本文件的 [JsonSerializable] 提供。

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(CreateUserRequest))]
internal partial class DemoJsonContext : JsonSerializerContext;
#endif
