global using Xunit;
global using FluentAssertions;
global using Microsoft.Extensions.DependencyInjection;
global using System.Net;
global using System.Text;
global using System.Text.Json;
global using Mud.HttpUtils;
global using Mud.HttpUtils.Attributes;
global using Moq;

// 观测性断言依赖进程级静态 Meter（MudHttpMeter）与 MeterListener：若各测试类并行执行，
// 其他类发出的请求会被本类的 listener 捕获，导致计数断言随机失败
// （曾观测到 NormalRequest_Increments_RequestCounter_Metric 期望 1 条却收到 6 条）。
// 集成测试规模很小（全部 < 1s），串行执行以换取确定性。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
