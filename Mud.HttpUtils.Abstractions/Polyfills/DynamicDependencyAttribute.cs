// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#if !NET6_0_OR_GREATER
// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Polyfill: 声明动态依赖，供 trimmer 保留被引用成员。</summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class DynamicDependencyAttribute : Attribute
{
    /// <summary>初始化 <see cref="DynamicDependencyAttribute"/> 实例。</summary>
    /// <param name="memberTypes">要保留的成员类型。</param>
    /// <param name="type">拥有被依赖成员的类型。</param>
    public DynamicDependencyAttribute(DynamicallyAccessedMemberTypes memberTypes, Type type)
    {
        MemberTypes = memberTypes;
        Type = type;
    }

    /// <summary>初始化 <see cref="DynamicDependencyAttribute"/> 实例。</summary>
    /// <param name="memberSignature">被依赖成员的签名。</param>
    /// <param name="type">拥有被依赖成员的类型。</param>
    public DynamicDependencyAttribute(string memberSignature, Type type)
    {
        MemberSignature = memberSignature;
        Type = type;
    }

    /// <summary>获取必须保留的成员类型。</summary>
    public DynamicallyAccessedMemberTypes MemberTypes { get; }

    /// <summary>获取拥有被依赖成员的类型。</summary>
    public Type? Type { get; }

    /// <summary>获取被依赖成员的签名。</summary>
    public string? MemberSignature { get; }
}
#endif
