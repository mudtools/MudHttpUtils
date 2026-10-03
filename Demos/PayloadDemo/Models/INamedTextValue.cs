// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace PayloadDemo.Models;

/// <summary>「具名文本项」的形状契约（扩展属性项、字典项等）。</summary>
/// <remarks>
/// <para>
/// <c>ItemsWithAttributes</c> 的属性名与文本元素名是<b>运行期字符串</b>（<c>NameAttribute</c> /
/// <c>ValueElement</c>），因此「<c>TItem</c> 是否真的具备这两个成员」在编译期<b>无法</b>判定
/// —— 生成器既不校验、也不应该校验（那需要一整套成员解析）。
/// </para>
/// <para>
/// 正确的表达方式是<b>用消费方自己的泛型约束把形状写进签名</b>：
/// <c>where TItem : INamedTextValue, new()</c>。这样「形状不匹配」会变成
/// <c>CS0315</c> 这类消费方自己看得懂的错误，而生成器只需校验泛型约束可满足性即可。
/// </para>
/// </remarks>
public interface INamedTextValue
{
    /// <summary>项名（对应 <c>NameAttribute</c> 指定的属性名）。</summary>
    string? Name { get; set; }

    /// <summary>项文本（对应 <c>ValueElement</c> 指定的子元素名）。</summary>
    string? Text { get; set; }
}
