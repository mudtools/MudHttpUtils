// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// TransitiveCodeGenerator 的单元测试，验证 NEW-GEN-13 修复（默认命名空间列表扩展）。
/// </summary>
public class TransitiveCodeGeneratorTests
{
    /// <summary>
    /// 测试用子类，用于暴露 protected 的 GetFileUsingNameSpaces 方法。
    /// 不重写该方法，以验证基类的默认命名空间列表。
    /// </summary>
    private sealed class TestTransitiveCodeGenerator : TransitiveCodeGenerator
    {
        public override void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 测试用空实现，无需任何源生成逻辑
        }

        public IReadOnlyList<string> ExposeDefaultNamespaces()
            => GetFileUsingNameSpaces().ToList();
    }

    private static IReadOnlyList<string> GetDefaultNamespaces()
    {
        var generator = new TestTransitiveCodeGenerator();
        return generator.ExposeDefaultNamespaces();
    }

    // ============================================================
    // NEW-GEN-13：默认命名空间列表扩展
    // ============================================================

    [Fact]
    public void TransitiveCodeGenerator_DefaultNamespaces_ShouldIncludeAll14Namespaces()
    {
        // Arrange & Act：通过测试子类获取 TransitiveCodeGenerator 基类的默认命名空间列表
        var defaultNamespaces = GetDefaultNamespaces();

        // Assert：验证包含全部 14 个命名空间
        defaultNamespaces.Should().Contain("System");
        defaultNamespaces.Should().Contain("System.Collections.Generic");
        defaultNamespaces.Should().Contain("System.ComponentModel.DataAnnotations");
        defaultNamespaces.Should().Contain("System.Linq");
        defaultNamespaces.Should().Contain("System.Linq.Expressions");
        defaultNamespaces.Should().Contain("System.Net.Http");
        defaultNamespaces.Should().Contain("System.Runtime.CompilerServices");
        defaultNamespaces.Should().Contain("System.Text.Json");
        defaultNamespaces.Should().Contain("System.Text.Json.Serialization");
        defaultNamespaces.Should().Contain("System.Threading");
        defaultNamespaces.Should().Contain("System.Threading.Tasks");
        defaultNamespaces.Should().Contain("Microsoft.Extensions.Logging");
        defaultNamespaces.Should().Contain("Mud.HttpUtils");
        defaultNamespaces.Should().Contain("Mud.HttpUtils.Observability");

        defaultNamespaces.Count.Should().BeGreaterOrEqualTo(14,
            "NEW-GEN-13：默认命名空间列表应至少包含 14 个命名空间");
    }
}
