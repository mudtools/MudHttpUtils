# Mud.HttpUtils.Xml

Mud HttpUtils 的 XML 序列化器适配包，提供基于 `System.Xml.Serialization.XmlSerializer` 的 `IHttpContentSerializer` 实现（`XmlContentSerializer`）。

## 安装

```bash
dotnet add package Mud.HttpUtils.Xml
```

## 用法

在 DI 中将默认的 JSON 序列化器替换为 XML：

```csharp
services.AddMudHttpClient<ICatalogApi>(options =>
{
    options.BaseAddress = new Uri("https://api.example.com/");
    options.ContentSerializer = new XmlContentSerializer(new XmlContentSerializerSettings
    {
        MediaType = "application/xml",
        WriterSettings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = true,
            OmitXmlDeclaration = false
        }
    });
});
```

也可脱离 DI 独立使用（实现 `IHttpContentSerializer` 的全部方法）：

```csharp
var serializer = new XmlContentSerializer();
string xml = serializer.Serialize(new Order { Id = 1 });
var order = serializer.Deserialize<Order>(xml);
```

## Native AOT 限制（重要）

`XmlSerializer` 在运行时生成动态程序集，**不支持 Native AOT 与裁剪**。本包所有公共成员均已标注 `[RequiresDynamicCode]` 与 `[RequiresUnreferencedCode]`，消费方在启用 AOT 分析器时会在调用点获得编译期提示。

**Native AOT 场景请使用 `Mud.HttpUtils.Client` 内置的 `SystemTextJsonContentSerializer` + `JsonSerializerContext`（源生成 JSON 序列化）。**

## 定位说明：与生成客户端内置 XML 路径的关系

| 入口 | 机制 | 适用场景 |
|------|------|----------|
| `XmlContentSerializer`（本包） | `IHttpContentSerializer` DI 抽象 | 请求体序列化引擎替换（如 `[SerializationMethod(SerializationMethod.Xml)]`） |
| 生成客户端内置路径 | `ResponseDescriptor.XmlSerializer` 元数据直通（源生成器发射 `XmlSerializer` 字段） | 生成的客户端方法的 XML 响应反序列化 |

二者互补、不互相替代。注意两条路径均基于 `XmlSerializer`（反射），均不受 Native AOT 支持。
