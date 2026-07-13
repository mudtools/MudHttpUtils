#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;

namespace AotPackageRefDemo;

// 手动 JsonSerializerContext — 为 AOT 序列化声明类型元数据。
//
// 在 Phase 11（脚手架随包分发）落地后，消费方可选择：
//   1. 标注 [HttpJsonSerializable] + 运行 mud-jsonctx 工具自动生成（推荐）
//   2. 手动编写 JsonSerializerContext（本文件方式）
//
// 本 Demo 使用手动方式，验证 NuGet 包消费下的 AOT 序列化路径。
// AotDtoCoverageAnalyzer（AOT004）会扫描当前编译单元中的 JsonSerializerContext 子类，
// 确认 [HttpClientApi] 方法的 DTO 已被覆盖。

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(CreateUserRequest))]
internal partial class DemoJsonContext : JsonSerializerContext;
#endif
