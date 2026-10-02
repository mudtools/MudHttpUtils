// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Mud.HttpUtils.Payloads;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 上游运行时契约（<see cref="PayloadNode"/> / <see cref="PayloadFieldMap{T}"/> / <see cref="IPayloadContractAccessor"/>）行为钉。
/// </summary>
/// <remarks>
/// 这些用例即「消费方手写链」的等价性保障：手写链与生成链产出<b>同一类型</b>的运行时对象，
/// 故本类的断言同时覆盖两条路径的行为语义（作用域三级判定、缺失即 null、非泛型桥透传）。
/// </remarks>
public class PayloadRuntimeContractTests
{
    private sealed class Target
    {
        public string? Text { get; set; }

        public List<long> Ids { get; set; } = new List<long>();
    }

    private static PayloadFieldMap<Target> CreateMap(string? scopeFallback = null) =>
        PayloadFieldMap<Target>.Create("Target", scopeFallback)
            .Map("UserID", (t, n) => t.Text = n?.Value)
            .Map("Ids", (t, n) => t.Ids = n == null ? new List<long>() : new List<long> { n.Value?.Length ?? 0 });

    [Fact]
    public void PayloadNode_CopiesChildrenAndAttributes_AndIsImmutable()
    {
        var children = new List<PayloadNode> { PayloadNode.Leaf("A", "1") };
        var attributes = new Dictionary<string, string> { ["Name"] = "x" };

        var node = new PayloadNode("Root", "v", children, attributes);

        // 构造后再修改源集合不得影响节点（构造期冻结 ⇒ 可安全多线程共享）
        children.Add(PayloadNode.Leaf("B", "2"));
        attributes["Name"] = "changed";

        node.Children.Should().HaveCount(1);
        node.Attributes["Name"].Should().Be("x");
        node.Child("B").Should().BeNull();
        node.Has("A").Should().BeTrue();
        node.Child("missing").Should().BeNull();
    }

    [Fact]
    public void PayloadNode_EmptyState_UsesSharedEmptyCollections()
    {
        var leaf = PayloadNode.Leaf("L");

        leaf.Children.Should().BeEmpty();
        leaf.Attributes.Should().BeEmpty();
        leaf.Value.Should().BeNull();
    }

    [Fact]
    public void PayloadNode_NullName_Throws()
    {
        var act = () => new PayloadNode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveScope_RootHit_ReturnsRoot()
    {
        var root = new PayloadNode("Root", null, new[]
        {
            PayloadNode.Leaf("UserID", "u1"),
            PayloadNode.Leaf("Ids", "1,2"),
        });

        CreateMap("BatchJob").ResolveScope(root).Should().BeSameAs(root);
    }

    [Fact]
    public void ResolveScope_RootMiss_FallsBackToContainer()
    {
        var inner = new PayloadNode("BatchJob", null, new[] { PayloadNode.Leaf("UserID", "u1") });
        var root = new PayloadNode("Root", null, new[] { inner });

        CreateMap("BatchJob").ResolveScope(root).Should().BeSameAs(inner);
    }

    [Fact]
    public void ResolveScope_BothMiss_ReturnsRoot_SoAllFieldsBecomeNull()
    {
        var root = new PayloadNode("Root", null, new[] { PayloadNode.Leaf("Unrelated", "x") });
        var map = CreateMap("BatchJob");

        map.ResolveScope(root).Should().BeSameAs(root);

        var target = new Target();
        map.Bind(root, target);
        target.Text.Should().BeNull();
        target.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Bind_PopulatesMappedFields_InDeclarationOrder()
    {
        var map = CreateMap();
        var root = new PayloadNode("Root", null, new[]
        {
            PayloadNode.Leaf("UserID", "u1"),
            PayloadNode.Leaf("Ids", "12"),
        });

        var target = new Target();
        map.Bind(root, target);

        target.Text.Should().Be("u1");
        target.Ids.Should().ContainSingle().Which.Should().Be(2);
    }

    [Fact]
    public void Elements_PreservesDeclarationOrder()
    {
        CreateMap().Elements.Should().Equal("UserID", "Ids");
    }

    [Fact]
    public void Map_DuplicateOrEmptyElement_ThrowsFast()
    {
        var duplicate = () => PayloadFieldMap<Target>.Create("T")
            .Map("A", (t, n) => { })
            .Map("A", (t, n) => { });

        duplicate.Should().Throw<ArgumentException>();

        var empty = () => PayloadFieldMap<Target>.Create("T").Map(string.Empty, (t, n) => { });
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyContractId_Throws()
    {
        var act = () => PayloadFieldMap<Target>.Create(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Accessor_TransparentlyBridgesGenericContract()
    {
        IPayloadContractAccessor accessor = CreateMap("BatchJob");

        accessor.PayloadType.Should().Be(typeof(Target));
        accessor.ContractId.Should().Be("Target");
        accessor.ScopeFallback.Should().Be("BatchJob");
        accessor.Elements.Should().Equal("UserID", "Ids");

        var instance = accessor.CreateInstance();
        instance.Should().BeOfType<Target>();

        var root = new PayloadNode("Root", null, new[]
        {
            new PayloadNode("BatchJob", null, new[] { PayloadNode.Leaf("UserID", "u2") }),
        });

        accessor.Bind(root, instance).Should().BeSameAs(instance);
        ((Target)instance).Text.Should().Be("u2");
    }

    [Fact]
    public void Accessor_BindWithWrongTargetType_Throws()
    {
        IPayloadContractAccessor accessor = CreateMap();
        var act = () => accessor.Bind(PayloadNode.Leaf("Root"), new object());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ZeroBindingMap_IsValid_AndProducesEmptyPayload()
    {
        var map = PayloadFieldMap<Target>.Create("Unknown");

        map.Elements.Should().BeEmpty();
        map.ResolveScope(PayloadNode.Leaf("Root")).Should().NotBeNull();

        var target = new Target();
        map.Bind(PayloadNode.Leaf("Root", "x"), target);
        target.Text.Should().BeNull();
    }
}
