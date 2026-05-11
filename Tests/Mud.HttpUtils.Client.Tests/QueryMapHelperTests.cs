using System.Collections.Specialized;
using System.Text.Json;

namespace Mud.HttpUtils.Client.Tests;

public class QueryMapHelperTests
{
    #region Null/Empty Input

    [Fact]
    public void FlattenObjectToQueryParams_NullObj_ThrowsArgumentNullException()
    {
        var queryParams = new NameValueCollection();

        var act = () => QueryMapHelper.FlattenObjectToQueryParams(
            null!, "", ".", queryParams, false, false);

        act.Should().Throw<ArgumentNullException>().WithParameterName("obj");
    }

    #endregion

    #region Primitive Types

    [Fact]
    public void FlattenObjectToQueryParams_PrimitiveProperties_AddedToCollection()
    {
        var obj = new { Name = "test", Age = 25, Active = true };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Name"].Should().Be("test");
        queryParams["Age"].Should().Be("25");
        queryParams["Active"].Should().Be("True");
    }

    [Fact]
    public void FlattenObjectToQueryParams_DecimalProperty_AddedToCollection()
    {
        var obj = new { Price = 19.99m };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Price"].Should().Be("19.99");
    }

    [Fact]
    public void FlattenObjectToQueryParts_EnumProperty_AddedAsString()
    {
        var obj = new { Day = DayOfWeek.Monday };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Day"].Should().Be("Monday");
    }

    [Fact]
    public void FlattenObjectToQueryParams_DateTimeProperty_AddedToCollection()
    {
        var date = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var obj = new { CreatedAt = date };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["CreatedAt"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FlattenObjectToQueryParams_GuidProperty_AddedToCollection()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var obj = new { Id = guid };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Id"].Should().Be("550e8400-e29b-41d4-a716-446655440000");
    }

    [Fact]
    public void FlattenObjectToQueryParams_StringProperty_AddedToCollection()
    {
        var obj = new { Query = "hello world" };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Query"].Should().Be("hello world");
    }

    [Fact]
    public void FlattenObjectToQueryParams_DateTimeOffsetProperty_AddedToCollection()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var obj = new { Timestamp = dto };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Timestamp"].Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Prefix and Separator

    [Fact]
    public void FlattenObjectToQueryParams_WithPrefix_UsesPrefixInKeys()
    {
        var obj = new { Name = "test" };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "filter", ".", queryParams, false, false);

        queryParams["filter.Name"].Should().Be("test");
    }

    [Fact]
    public void FlattenObjectToQueryParams_WithCustomSeparator_UsesSeparatorInKeys()
    {
        var obj = new { Name = "test" };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "filter", "_", queryParams, false, false);

        queryParams["filter_Name"].Should().Be("test");
    }

    [Fact]
    public void FlattenObjectToQueryParams_EmptyPrefix_NoPrefixInKeys()
    {
        var obj = new { Name = "test" };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Name"].Should().Be("test");
    }

    #endregion

    #region Null Value Handling

    [Fact]
    public void FlattenObjectToQueryParams_NullProperty_ExcludeNullValues()
    {
        var obj = new { Name = (string?)null, Age = 25 };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Name"].Should().BeNull();
        queryParams["Age"].Should().Be("25");
    }

    [Fact]
    public void FlattenObjectToQueryParams_NullProperty_IncludeNullValues()
    {
        var obj = new { Name = (string?)null, Age = 25 };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, true, false);

        queryParams["Name"].Should().Be("");
        queryParams["Age"].Should().Be("25");
    }

    #endregion

    #region JSON Serialization Mode

    [Fact]
    public void FlattenObjectToQueryParams_UseJsonSerialization_SerializesAsJson()
    {
        var obj = new { Name = "test", Count = 42 };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, true);

        queryParams["Name"].Should().Be("\"test\"");
        queryParams["Count"].Should().Be("42");
    }

    [Fact]
    public void FlattenObjectToQueryParams_JsonSerialization_BoolSerializesAsJson()
    {
        var obj = new { Active = true };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, true);

        queryParams["Active"].Should().Be("true");
    }

    #endregion

    #region IQueryParameter Implementation

    [Fact]
    public void FlattenObjectToQueryParams_IQueryParameter_UsesToQueryParameters()
    {
        var obj = new
        {
            Filter = new TestQueryParameter(new Dictionary<string, string?>
            {
                ["status"] = "active",
                ["role"] = "admin"
            })
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Filter.status"].Should().Be("active");
        queryParams["Filter.role"].Should().Be("admin");
    }

    [Fact]
    public void FlattenObjectToQueryParams_IQueryParameter_WithPrefix_UsesSubKey()
    {
        var obj = new
        {
            Filter = new TestQueryParameter(new Dictionary<string, string?>
            {
                ["q"] = "search"
            })
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "root", "_", queryParams, false, false);

        queryParams["root_Filter_q"].Should().Be("search");
    }

    [Fact]
    public void FlattenObjectToQueryParams_IQueryParameter_NullValue_ExcludedByDefault()
    {
        var obj = new
        {
            Filter = new TestQueryParameter(new Dictionary<string, string?>
            {
                ["status"] = "active",
                ["empty"] = null
            })
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Filter.status"].Should().Be("active");
        queryParams["Filter.empty"].Should().BeNull();
    }

    [Fact]
    public void FlattenObjectToQueryParams_IQueryParameter_NullValue_IncludedWhenFlagSet()
    {
        var obj = new
        {
            Filter = new TestQueryParameter(new Dictionary<string, string?>
            {
                ["status"] = "active",
                ["empty"] = null
            })
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, true, false);

        queryParams["Filter.status"].Should().Be("active");
        queryParams["Filter.empty"].Should().Be("");
    }

    #endregion

    #region Nested Complex Objects

    [Fact]
    public void FlattenObjectToQueryParams_NestedObject_FlattensRecursively()
    {
        var obj = new
        {
            Filter = new
            {
                Name = "test",
                Age = 25
            }
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Filter.Name"].Should().Be("test");
        queryParams["Filter.Age"].Should().Be("25");
    }

    [Fact]
    public void FlattenObjectToQueryParams_DeeplyNestedObject_FlattensAllLevels()
    {
        var obj = new
        {
            Level1 = new
            {
                Level2 = new
                {
                    Value = "deep"
                }
            }
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Level1.Level2.Value"].Should().Be("deep");
    }

    [Fact]
    public void FlattenObjectToQueryParams_NestedObjectWithNullProperty_FlattensCorrectly()
    {
        var obj = new
        {
            Filter = new
            {
                Name = (string?)null,
                Age = 25
            }
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Filter.Name"].Should().BeNull();
        queryParams["Filter.Age"].Should().Be("25");
    }

    #endregion

    #region Recursion Depth Limit

    [Fact]
    public void FlattenObjectToQueryParams_ExceedsMaxDepth_ThrowsInvalidOperationException()
    {
        var depth11Obj = CreateNestedObject(11);
        var queryParams = new NameValueCollection();

        var act = () => QueryMapHelper.FlattenObjectToQueryParams(
            depth11Obj, "", ".", queryParams, false, false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maximum recursion depth*");
    }

    [Fact]
    public void FlattenObjectToQueryParams_AtMaxDepth_Succeeds()
    {
        var depth10Obj = CreateNestedObject(9);
        var queryParams = new NameValueCollection();

        var act = () => QueryMapHelper.FlattenObjectToQueryParams(
            depth10Obj, "", ".", queryParams, false, false);

        act.Should().NotThrow();
    }

    private static object CreateNestedObject(int depth)
    {
        if (depth <= 0)
            return new { Value = "leaf" };

        return new { Child = CreateNestedObject(depth - 1) };
    }

    #endregion

    #region rawPairs Mode

    [Fact]
    public void FlattenObjectToQueryParams_WithRawPairs_AddsToRawPairsList()
    {
        var obj = new { Name = "test", Age = 25 };
        var queryParams = new NameValueCollection();
        var rawPairs = new List<string>();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false, urlEncode: false, rawPairs);

        rawPairs.Should().Contain(p => p.Contains("Name") && p.Contains("test"));
        rawPairs.Should().Contain(p => p.Contains("Age") && p.Contains("25"));
    }

    [Fact]
    public void FlattenObjectToQueryParams_RawPairsWithNullValue_IncludeNullValues()
    {
        var obj = new { Name = (string?)null };
        var queryParams = new NameValueCollection();
        var rawPairs = new List<string>();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, true, false, urlEncode: false, rawPairs);

        rawPairs.Should().Contain(p => p.Contains("Name"));
    }

    [Fact]
    public void FlattenObjectToQueryParams_RawPairsWithNullValue_ExcludeNullValues()
    {
        var obj = new { Name = (string?)null, Age = 25 };
        var queryParams = new NameValueCollection();
        var rawPairs = new List<string>();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false, urlEncode: false, rawPairs);

        rawPairs.Should().NotContain(p => p.Contains("Name"));
        rawPairs.Should().Contain(p => p.Contains("Age"));
    }

    #endregion

    #region Mixed Types

    [Fact]
    public void FlattenObjectToQueryParams_MixedTypes_AllFlattenedCorrectly()
    {
        var obj = new
        {
            Keyword = "search",
            Page = 1,
            PageSize = 20,
            SortBy = (string?)null,
            IsActive = true,
            Price = 9.99m,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var queryParams = new NameValueCollection();

        QueryMapHelper.FlattenObjectToQueryParams(obj, "", ".", queryParams, false, false);

        queryParams["Keyword"].Should().Be("search");
        queryParams["Page"].Should().Be("1");
        queryParams["PageSize"].Should().Be("20");
        queryParams["SortBy"].Should().BeNull();
        queryParams["IsActive"].Should().Be("True");
        queryParams["Price"].Should().Be("9.99");
        queryParams["CreatedAt"].Should().NotBeNullOrEmpty();
    }

    #endregion

    private class TestQueryParameter : IQueryParameter
    {
        private readonly Dictionary<string, string?> _params;

        public TestQueryParameter(Dictionary<string, string?> parameters)
        {
            _params = parameters;
        }

        public IEnumerable<KeyValuePair<string, string?>> ToQueryParameters()
        {
            return _params;
        }
    }
}
