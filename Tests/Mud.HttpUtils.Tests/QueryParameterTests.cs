// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud.HttpUtils 2025   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Tests;

/// <summary>
/// IQueryParameter 接口单元测试
/// </summary>
public class QueryParameterTests
{
    [Fact]
    public void IQueryParameter_ToQueryParameters_ShouldReturnAllProperties()
    {
        var criteria = new TestSearchCriteria
        {
            Keyword = "test",
            PageIndex = 1,
            PageSize = 10
        };

        var parameters = criteria.ToQueryParameters().ToList();

        parameters.Should().HaveCount(3);
        parameters.Should().Contain(p => p.Key == "keyword" && p.Value == "test");
        parameters.Should().Contain(p => p.Key == "pageIndex" && p.Value == "1");
        parameters.Should().Contain(p => p.Key == "pageSize" && p.Value == "10");
    }

    [Fact]
    public void IQueryParameter_WithNullValues_ShouldReturnNullValue()
    {
        var criteria = new TestSearchCriteria
        {
            Keyword = null,
            PageIndex = 0,
            PageSize = 10
        };

        var parameters = criteria.ToQueryParameters().ToList();

        parameters.Should().HaveCount(3);
        parameters.First(p => p.Key == "keyword").Value.Should().BeNull();
    }

    [Fact]
    public void IQueryParameter_EmptyInstance_ShouldReturnDefaultValues()
    {
        var criteria = new TestSearchCriteria();

        var parameters = criteria.ToQueryParameters().ToList();

        parameters.Should().HaveCount(3);
        parameters.First(p => p.Key == "keyword").Value.Should().BeNull();
        parameters.First(p => p.Key == "pageIndex").Value.Should().Be("0");
    }

    private class TestSearchCriteria : IQueryParameter
    {
        public string? Keyword { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }

        public IEnumerable<KeyValuePair<string, string?>> ToQueryParameters()
        {
            yield return new KeyValuePair<string, string?>("keyword", Keyword);
            yield return new KeyValuePair<string, string?>("pageIndex", PageIndex.ToString());
            yield return new KeyValuePair<string, string?>("pageSize", PageSize.ToString());
        }
    }
}
