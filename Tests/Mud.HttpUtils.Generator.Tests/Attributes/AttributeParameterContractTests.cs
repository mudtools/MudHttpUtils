// -----------------------------------------------------------------------
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Reflection;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F-1：特性参数 ↔ 生成器读取点 一致性守卫（CFG-28 根因的机器化封堵）。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机</b>：CFG-28 的根因是「特性新增了可写属性，生成器侧读取逻辑未同步」
/// （<c>RetryAttribute(int, int)</c> 的位置参数 <c>delayMilliseconds</c> 自诞生起从未被读取）。
/// 该类缺陷在编译期与运行期均无任何提示，只能靠人工核对 —— 本守卫将其转为测试红灯。
/// </para>
/// <para>
/// <b>实现手法</b>：与 <c>DocumentationContractTests</c> 保持一致 —— 反射枚举
/// <c>Mud.HttpUtils.Attributes</c> 的公共可写属性，再对生成器源码目录做文本扫描，
/// 断言属性名以字符串字面量形式出现（或命中豁免清单）。不引入 Roslyn 语义分析，避免守卫自身成为脆弱点。
/// </para>
/// <para>
/// <b>已知局限（有意接受）</b>：文本扫描按「属性名字符串是否出现」判定，
/// 对 <c>Name</c> / <c>ContentType</c> 这类在生成器中被他处复用的名字会「假通过」。
/// 本守卫的定位是<b>漏检兜底</b>而非完备证明 —— 它拦住的是「新增属性后生成器完全不知情」这一类高发回归。
/// </para>
/// </remarks>
public class AttributeParameterContractTests
{
    private const string GeneratorRoot = @"..\..\..\..\Mud.HttpUtils.Generator";

    /// <summary>
    /// 显式豁免：由生成器<strong>之外</strong>的消费方读取，或语义上不属于生成器的配置面。
    /// 每条豁免必须写明消费方，防止「用豁免清单掩盖真缺陷」。
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["SensitiveDataAttribute.MaskMode"] =
            "运行时消费：Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs（反射遍历属性读 [SensitiveData]）",
        ["SensitiveDataAttribute.PrefixLength"] =
            "运行时消费：Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs（反射阅读，同 MaskMode）",
        ["SensitiveDataAttribute.SuffixLength"] =
            "运行时消费：Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs（反射阅读，同 MaskMode）",
        ["HttpJsonSerializableAttribute.SerializerClassName"] =
            "工具消费：Tools/Mud.HttpUtils.JsonContextScaffolder/JsonContextGenerator.cs:380",
        ["HttpJsonSerializableAttribute.NamingPolicy"] =
            "工具消费：Tools/Mud.HttpUtils.JsonContextScaffolder/JsonContextGenerator.cs:382",
        ["HttpMethodAttribute.HttpMethod"] =
            "生成器经派生特性类名推断 HTTP 动词（MethodAnalyzer.ExtractHttpMethodName），不经属性名读取",
        ["HttpMethodAttribute.RequestUri"] =
            "生成器经构造函数位置参数 index 0 读取 URL 模板（MethodAnalyzer.cs:60），不经属性名读取",
    };

    /// <summary>
    /// 枚举 <c>Mud.HttpUtils.Attributes</c> 中全部公共具体特性类的公共实例可写属性（每个属性只计一次）。
    /// </summary>
    private static List<(string Key, PropertyInfo Property)> EnumerateWritableAttributeProperties()
    {
        var attributeAssembly = typeof(RetryAttribute).Assembly;

        var attributeTypes = attributeAssembly.GetTypes()
            .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && typeof(Attribute).IsAssignableFrom(t))
            .ToArray();

        var attributeTypeFullNames = new HashSet<string>(
            attributeTypes.Select(t => t.FullName!),
            StringComparer.Ordinal);

        var result = new List<(string, PropertyInfo)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in attributeTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod is null || !property.SetMethod.IsPublic)
                    continue;

                // 只统计「声明在特性类上」的属性，避免派生特性重复计入基类属性
                if (property.DeclaringType is null || !attributeTypeFullNames.Contains(property.DeclaringType.FullName!))
                    continue;

                var key = $"{property.DeclaringType.Name}.{property.Name}";
                if (seen.Add(key))
                    result.Add((key, property));
            }
        }

        return result;
    }

    private static string ReadGeneratorSourceText()
    {
        var root = Path.GetFullPath(GeneratorRoot);
        Directory.Exists(root).Should().BeTrue($"找不到生成器源码目录：{root}");

        return string.Concat(
            Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
    }

    [Fact]
    public void EveryAttributeWritableProperty_IsReadByGeneratorOrExempted()
    {
        var properties = EnumerateWritableAttributeProperties();
        properties.Should().NotBeEmpty("守卫自身必须能枚举到特性属性，否则扫描逻辑失效");

        var generatorText = ReadGeneratorSourceText();

        var unread = new List<string>();
        foreach (var (key, property) in properties)
        {
            if (Exempt.ContainsKey(key))
                continue;

            // 生成器以字符串字面量读取命名参数（含常量化定义，如 CacheDurationSecondsProperty = "DurationSeconds"）
            if (!generatorText.Contains($"\"{property.Name}\"", StringComparison.Ordinal))
                unread.Add($"{key}（声明于 {property.DeclaringType?.FullName}）");
        }

        unread.Should().BeEmpty(
            "每个特性可写属性都必须被生成器读取，或登记进 Exempt 并写明消费方。" +
            "未覆盖项（请核对是否属 CFG-28 同类回归）：\n  - " + string.Join("\n  - ", unread));
    }

    [Fact]
    public void ExemptEntries_AllStillExistOnAttributes()
    {
        var existingKeys = EnumerateWritableAttributeProperties()
            .Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in Exempt.Keys)
        {
            existingKeys.Should().Contain(key,
                $"豁免清单中的 {key} 已不存在于特性定义上，请删除该条豁免（防止清单陈旧掩盖真缺陷）");
        }
    }

    [Fact]
    public void ExemptEntries_AllDeclareTheirConsumer()
    {
        foreach (var (key, reason) in Exempt)
        {
            reason.Should().NotBeNullOrWhiteSpace($"豁免 {key} 必须写明消费方/理由");
            reason.Length.Should().BeGreaterThan(8, $"豁免 {key} 的理由过于简略，无法评审");
        }
    }
}
