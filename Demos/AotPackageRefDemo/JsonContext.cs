#if NET8_0_OR_GREATER
namespace AotPackageRefDemo;

// 本 Demo 的 DemoJsonContext 由 JsonContextScaffolder（DotNetToolReference）在 pre-build
// 生成至 obj/<tfm>/GeneratedJsonContext/DemoJsonContext.g.cs（因 Models 标注
// [HttpJsonSerializable(SerializerClassName = "Demo")]）。
//
// 不得再在此手工声明 partial DemoJsonContext / [JsonSourceGenerationOptions] /
// [JsonSerializable]——与脚手架产物重复会导致 CS0579（特性重复）与
// STJ 源生成器 hintName 冲突（CS8785 → CS0534）。
//
// 机制见 AotVerificationDemo.csproj 的 GenerateJsonContext 目标（同为 pre-build 产出真实 .g.cs）。
#endif
