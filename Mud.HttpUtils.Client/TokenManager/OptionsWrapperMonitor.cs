// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// TMX-10：将 <see cref="IOptions{T}"/> 的快照值包装为 <see cref="IOptionsMonitor{T}"/>，
/// 供向后兼容的 IOptions 重载使用。CurrentValue 恒返回构造时传入的值（不支持热更新）。
/// </summary>
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "本类型只包装调用方已构造的实例（CurrentValue 直接返回同一引用），从不通过 IOptionsMonitor 的 PublicParameterlessConstructor 约束实例化 T，故该约束在本实现中不适用。")]
internal sealed class OptionsWrapperMonitor<T> : IOptionsMonitor<T> where T : class
{
    private readonly T _value;

    public OptionsWrapperMonitor(T value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
