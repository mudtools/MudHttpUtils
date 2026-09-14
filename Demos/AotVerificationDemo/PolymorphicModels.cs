using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

namespace AotVerificationDemo;

// ─────────────────────────────────────────────────────────────
// [T2] 多态序列化场景模型（验证 AOT003 的正确修复姿势）。
//
// 关键语义：STJ 源生成下，以【基类静态类型】序列化/反序列化派生实例时，
// 派生类型映射必须由**基类元数据**携带 [JsonDerivedType]——
// 该特性只能标注在用户类型声明上，脚手架生成的 .g.cs 无法替用户类型附加特性，
// 因此 --auto-derived-types（仅把派生类型注册为独立根）不能替代它。
//
// 本场景两个类型都标注 [HttpJsonSerializable]（都成为 AppJsonContext 的根），
// 且基类声明 [JsonDerivedType(typeof(PolyCircle), "circle")]，故：
//   - 无 AOT003 告警（脚手架已识别基类上的多态映射声明）；
//   - 以 PolyShape 静态类型 round-trip PolyCircle 实例可在 Native AOT 下成功。
// ─────────────────────────────────────────────────────────────

/// <summary>多态基类（几何形状）。</summary>
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
[JsonDerivedType(typeof(PolyCircle), "circle")]
public class PolyShape
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>圆形（<see cref="PolyShape"/> 的派生类型）。</summary>
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class PolyCircle : PolyShape
{
    public double Radius { get; set; }
}
