# Mud.HttpUtils.Newtonsoft.Json

Mud HttpUtils 的 Newtonsoft.Json 序列化器适配包，提供基于 `Newtonsoft.Json.JsonSerializer` 的 `IHttpContentSerializer` 实现（`NewtonsoftJsonContentSerializer`）。该实现同时实现了同步序列化接口 `ISynchronousContentSerializer`，并支持流式请求体写入。

## 安装

```bash
dotnet add package Mud.HttpUtils.Newtonsoft.Json
```

## 用法

在 DI 中将默认的 JSON 序列化器替换为 Newtonsoft.Json（可传入自定义 `JsonSerializerSettings`）：

```csharp
services.AddMudHttpClient<ICatalogApi>(options =>
{
    options.BaseAddress = new Uri("https://api.example.com/");
    options.ContentSerializer = new NewtonsoftJsonContentSerializer(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None
    });
});
```

不传参数时使用默认的 `JsonSerializerSettings`，也可通过 `Settings` 属性在初始化后读取或自行构造：

```csharp
var serializer = new NewtonsoftJsonContentSerializer();
string json = serializer.Serialize(new Order { Id = 1 });
var order = serializer.Deserialize<Order>(json);
```

### 字段名映射

实现 `IHttpContentSerializer.GetFieldNameForProperty`，识别属性上的 `[JsonProperty("...")]` 特性名称，供生成器在查询参数等场景下映射字段名。

### 同步与流式序列化

该序列化器实现了 `ISynchronousContentSerializer`：

- **同步请求体**：`ToHttpContentSynchronous<T>` 直接通过 `JsonConvert.SerializeObject` 生成内容；
- **流式请求体**：`ToStreamingHttpContent<T>` 返回流式内容，在发送时以 `JsonSerializer` 逐步写入请求流，适用于大对象请求体场景。

## Native AOT 限制（重要）

Newtonsoft.Json 依赖反射与动态代码，**不支持 Native AOT 与裁剪**。本包所有公共成员均已标注 `[RequiresUnreferencedCode]`（复合类型序列化重载额外标注 `[RequiresDynamicCode]`），消费方在启用 AOT/裁剪分析器时会在调用点获得编译期提示。本包自身已设置 `IsAotCompatible=false` 并关闭 AOT/裁剪分析器的"已知噪声"。

**Native AOT 场景请使用 `Mud.HttpUtils.Client` 内置的 `SystemTextJsonContentSerializer` + `JsonSerializerContext`（源生成 JSON 序列化）。**

## 接口成员对照

| 接口 | 方法 | 说明 |
|------|------|------|
| `IHttpContentSerializer` | `ToHttpContent<T>` | 序列化对象为 UTF-8 `application/json` 的 `StringContent` |
| `IHttpContentSerializer` | `FromHttpContentAsync<T>` | 从响应流反序列化为对象 |
| `IHttpContentSerializer` | `Serialize<T>` / `Serialize(object, Type)` | 序列化为 JSON 字符串 |
| `IHttpContentSerializer` | `Deserialize<T>` | 从 JSON 字符串反序列化 |
| `IHttpContentSerializer` | `GetFieldNameForProperty` | 读取 `[JsonProperty]` 字段名 |
| `ISynchronousContentSerializer` | `ToHttpContentSynchronous<T>` | 同步生成请求内容 |
| `ISynchronousContentSerializer` | `ToStreamingHttpContent<T>` | 生成流式请求内容 |

## 依赖

- `Newtonsoft.Json` (13.0.4)
- `Mud.HttpUtils.Abstractions`（`IHttpContentSerializer`、`ISynchronousContentSerializer` 接口定义）