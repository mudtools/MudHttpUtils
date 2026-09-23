# Mud.HttpUtils.Xml

Mud HttpUtils 的 XML 序列化器适配包，提供基于 `System.Xml.Serialization.XmlSerializer` 的 `IHttpContentSerializer` 实现（`XmlContentSerializer`）。

## 安装

```bash
dotnet add package Mud.HttpUtils.Xml
```

## 用法

在 DI 中将默认的 JSON 序列化器替换为 XML：

```csharp
// TryAdd 先注册者胜：自定义序列化器必须在 AddMudHttpClient 之前注册，
// 否则 AddMudHttpClient 内部的默认 TryAdd 注册将先生效。
services.TryAddSingleton<IHttpContentSerializer>(sp =>
    new XmlContentSerializer(new XmlContentSerializerSettings
    {
        MediaType = "application/xml",
        WriterSettings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = true,
            OmitXmlDeclaration = false
        }
    }));
services.AddMudHttpClient("catalog", "https://api.example.com/");
```

设置默认值：`MediaType = "application/xml"`、`Indent = false`、`NewLineChars` 固定为 `"\r\n"`（跨平台输出一致），示例中按需覆盖。

也可脱离 DI 独立使用（实现 `IHttpContentSerializer` 的全部方法）：

```csharp
var serializer = new XmlContentSerializer();
string xml = serializer.Serialize(new Order { Id = 1 });
var order = serializer.Deserialize<Order>(xml);
```

### 字段名映射与接口范围

- 实现 `IHttpContentSerializer.GetFieldNameForProperty`，读取属性上 `[XmlElement("...")]` 特性的 `ElementName`，供生成器在查询参数等场景下映射字段名。
- 本包仅实现 `IHttpContentSerializer`，**未**实现 `ISynchronousContentSerializer`——不存在同步/流式请求体路径（`ToHttpContentSynchronous<T>` / `ToStreamingHttpContent<T>` 不可用）。

## Native AOT 限制（重要）

`XmlSerializer` 在运行时生成动态程序集，**不支持 Native AOT 与裁剪**。标注现状：`[RequiresDynamicCode]` 与 `[RequiresUnreferencedCode]` 经 `#if NET7_0_OR_GREATER` / `#elif NET6_0_OR_GREATER` 条件标注于序列化相关成员（`netstandard2.0` 资产不带标注）；`GetFieldNameForProperty`、主构造函数与 `Settings` 属性完全未标注。消费方在启用 AOT 分析器时会在相应调用点获得编译期提示。本包 `IsAotCompatible=false`（非 AOT 路径），并已关闭 AOT/裁剪分析器以消除已知噪声。

**Native AOT 场景请使用 `Mud.HttpUtils.Client` 内置的 `SystemTextJsonContentSerializer` + `JsonSerializerContext`（源生成 JSON 序列化）。**

## 定位说明：与生成客户端内置 XML 路径的关系

| 入口 | 机制 | 适用场景 |
|------|------|----------|
| `XmlContentSerializer`（本包） | `IHttpContentSerializer` DI 抽象 | 请求体序列化引擎替换（如 `[SerializationMethod(SerializationMethod.Xml)]`） |
| 生成客户端内置路径 | `ResponseDescriptor.XmlSerializer` 元数据直通（源生成器发射 `XmlSerializer` 字段） | 生成的客户端方法的 XML 响应反序列化 |

二者互补、不互相替代。注意两条路径均基于 `XmlSerializer`（反射），均不受 Native AOT 支持。
