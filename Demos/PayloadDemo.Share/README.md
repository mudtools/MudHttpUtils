# PayloadDemo.Share 共享夹具项目

载荷字段映射生成器（`PayloadFieldMapGenerator` / 诊断 `PAYLOAD001`~`PAYLOAD009`）的功能夹具库。
夹具内容（载荷契约声明、消费方转换器、契约注册表、领域模型、报文样本与演示入口）全部收敛在本共享项目，
由两个消费方工程分别以「本地源码」与「已发布 NuGet 包」两条依赖路径引入：

- `PayloadDemo`：`ProjectReference` 本地源码（Abstractions / Attributes / Generator），验证 HEAD 行为；
- `PayloadDemoPublicDemo`：`PackageReference` 已发布包（Mud.HttpUtils / Mud.HttpUtils.Generator 3.0.7），
  验证包消费方体验（生成代码来自已发布版本的生成器）。

## 项目概述

本演示想说明的事情：把「外部报文 → 强类型载荷」的字段映射从手写声明式 binder 升级为编译期生成 binder 之后——

1. 载荷类只写 `[PayloadContract]` / `[PayloadField]` 声明，`public static IPayloadFieldMap<T> PayloadFieldMap` 由生成器产出；
2. 7 种字段形态（`Text` / `Delimited` / `Items` / `ItemsWithAttributes` / `Object` / `ItemsObject` + 显式 `Method`）
   基本可由属性类型推断，推断失败在编译期报 `PAYLOAD007`，而不是运行期静默返回空值；
3. 嵌套结构（树形单向聚合）由生成器递归 Bind，无需消费方用 `Method` 逃生舱手写遍历；
4. 元素名与属性名的配对、转换方法的存在性与返回类型、形态与附加参数的匹配 —— 三者全部由编译器与生成器保证；
5. 「缺失即默认值」是全链路的统一语义：不抛异常、不产出半成品；
6. 手写链与生成链同型且等价，可逐类渐进迁移、随时回退。

## 目录结构

```
PayloadDemo.Share/
├── Contracts/                          # 载荷契约声明（生成器的输入）
│   ├── Payloads.cs                     # ContactBatchChangedPayload / ScanPushEventPayload / ApprovalResultPayload 等
│   └── HandWrittenContactChangedPayload.cs  # 手写映射链对照物（刻意不标注 [PayloadContract]）
├── Conversion/
│   └── WechatPayloadConverter.cs       # 消费方转换器：生成器与消费方的唯一签名契约
├── Engine/
│   └── PayloadContractRegistry.cs      # 契约注册表：IPayloadContractAccessor 非泛型桥的典型用法
├── Models/
│   ├── ContactExtAttrItem.cs           # ItemsWithAttributes 的 TItem（实现 INamedTextValue）
│   ├── Enums.cs                        # UserGender / ApprovalStatus（枚举必须显式 Method）
│   └── INamedTextValue.cs              # 「具名文本项」形状契约
├── Samples/
│   └── PayloadSamples.cs               # 回调报文样本（直接构造 PayloadNode 树）
├── GlobalUsings.cs                     # 全局 using
├── Program.cs                          # 演示入口（九个区段，逐项对照「声明 → 生成物 → 运行期行为」）
└── README.md                           # 本文档
```

## 编译和运行

`PayloadDemo.Share` 仅有 `.shproj` / `.projitems`（共享项目，**没有 csproj**），`dotnet build` 无法直接构建本目录；
请构建消费方项目：

```powershell
dotnet build Demos/PayloadDemo/PayloadDemo.csproj
dotnet build Demos/PayloadDemoPublicDemo/PayloadDemoPublicDemo.csproj
```

运行演示（两条依赖路径产物等价，可对照输出）：

```powershell
dotnet run --project Demos/PayloadDemo/PayloadDemo.csproj
dotnet run --project Demos/PayloadDemoPublicDemo/PayloadDemoPublicDemo.csproj
```

## 查看生成的代码

两个消费方工程均开启了 `EmitCompilerGeneratedFiles`，生成物落盘在：

```
obj/<配置>/net10.0/generated/Mud.HttpUtils.Generator/Mud.HttpUtils.PayloadFieldMapGenerator/PayloadDemo.Contracts/*.PayloadFieldMap.g.cs
```

可逐个对照「声明 → `*.PayloadFieldMap.g.cs`」：

```powershell
Get-ChildItem -Recurse -Filter *.PayloadFieldMap.g.cs Demos/PayloadDemo/obj
```

## 编译期诊断矩阵（PAYLOAD001~009）

全部诊断都是 Error，且「有错不产出」。触发条件与复现形态（把声明改成括号内形态即可复现）：

| 诊断 | 标题 | 触发条件 |
|------|------|---------|
| PAYLOAD001 | 载荷字段映射生成错误 | 生成器内部/环境类兜底（合法输入不应触发） |
| PAYLOAD002 | 载荷契约类必须为 partial | 去掉载荷类声明的 partial |
| PAYLOAD003 | 未指定 Converter 类型 | 保留 [PayloadField] 但删掉 Converter = typeof(...) |
| PAYLOAD004 | 转换器上找不到指定的转换方法 | 改名/删方法；首参写成非可空 PayloadNode / string；ItemsObject 的 TItem 用非可空 class 约束 |
| PAYLOAD005 | 转换方法返回值不可赋给目标属性 | 让 Method 返回 string 而属性是 List\<long\> |
| PAYLOAD006 | 字段映射声明非法 | 元素名重复；Separator 与 ItemName 并存；Separator 落在非 Delimited 形态；Format = (PayloadFieldFormat)99；索引器/显式接口实现属性；static/只读/init-only 属性 |
| PAYLOAD007 | 无法按属性类型推断字段形态 | 枚举不写 Method；非可空值类型（int）；非可空 string；List\<非契约复杂类型\> + ItemName；泛型约束不满足 |
| PAYLOAD008 | 手写 PayloadFieldMap 与 [PayloadContract] 冲突 | 同一类型（或其基类）上已声明 PayloadFieldMap 成员 |
| PAYLOAD009 | 载荷契约类形态不受支持 | 泛型/嵌套/record/struct/interface/static/abstract/无公共无参构造/基类带 [PayloadField] |

## 相关文档

- [Mud.HttpUtils 主 README](../../README.md)

## 许可证

本项目遵循 MIT 许可证进行分发和使用。
