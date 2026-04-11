namespace HttpClientApiTest.Api;

using Mud.HttpUtils.Attributes;

/// <summary>
/// 微信公众号API测试接口
/// 测试QueryToken特性，用于将Token作为URL查询参数传递
/// 示例URL: https://api.weixin.qq.com/cgi-bin/user/tag/get?access_token=ACCESS_TOKEN
/// </summary>
[HttpClientApi("https://api.weixin.qq.com", Timeout = 30, TokenManage = nameof(IFeishuAppManager), RegistryGroupName = "Weixin")]
[Token(TokenType = "UserAccessToken", Name = "provider_access_token", InjectionMode = TokenInjectionMode.Header)]
public interface IWeixinApi
{
    /// <summary>
    /// 获取用户标签列表
    /// 接口：GET /cgi-bin/tags/get
    /// 特点：使用QueryToken，access_token通过URL查询参数传递
    /// </summary>
    [Get("/cgi-bin/tags/get")]
    Task<WeixinTagListResult?> GetTagsAsync();

    /// <summary>
    /// 创建用户标签
    /// 接口：POST /cgi-bin/tags/create
    /// 特点：使用QueryToken和Body参数
    /// </summary>
    [Post("/cgi-bin/tags/create")]
    Task<WeixinTagResult?> CreateTagAsync([Body(EnableEncrypt = true)] WeixinTagCreateRequest tagCreateRequest);

    /// <summary>
    /// 获取用户身上的标签列表
    /// 接口：POST /cgi-bin/tags/getidlist
    /// 特点：使用QueryToken和Body参数
    /// </summary>
    [Post("/cgi-bin/tags/getidlist")]
    Task<WeixinUserTagListResult?> GetUserTagsAsync([Body] WeixinUserTagRequest userTagRequest);

    /// <summary>
    /// 获取用户基本信息
    /// 接口：GET /cgi-bin/user/info
    /// 特点：使用QueryToken和额外的Query参数
    /// </summary>
    [Get("/cgi-bin/user/info")]
    Task<WeixinUserInfo?> GetUserInfoAsync([Query] string openid, [Query] string lang = "zh_CN");
}

/// <summary>
/// 微信标签列表结果
/// </summary>
public class WeixinTagListResult
{
    /// <summary>
    /// 标签列表
    /// </summary>
    public List<WeixinTag>? Tags { get; set; }
}

/// <summary>
/// 微信标签
/// </summary>
public class WeixinTag
{
    /// <summary>
    /// 标签ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 标签名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 标签下粉丝数
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// 微信标签创建请求
/// </summary>
public class WeixinTagCreateRequest
{
    /// <summary>
    /// 标签信息
    /// </summary>
    public WeixinTagCreateInfo? Tag { get; set; }
}

/// <summary>
/// 微信标签创建信息
/// </summary>
public class WeixinTagCreateInfo
{
    /// <summary>
    /// 标签名称（30个字符以内）
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// 微信标签创建结果
/// </summary>
public class WeixinTagResult
{
    /// <summary>
    /// 创建的标签信息
    /// </summary>
    public WeixinTag? Tag { get; set; }
}

/// <summary>
/// 微信用户标签请求
/// </summary>
public class WeixinUserTagRequest
{
    /// <summary>
    /// 用户OpenID
    /// </summary>
    public string? Openid { get; set; }
}

/// <summary>
/// 微信用户标签列表结果
/// </summary>
public class WeixinUserTagListResult
{
    /// <summary>
    /// 标签ID列表
    /// </summary>
    public List<int>? TagidList { get; set; }
}

/// <summary>
/// 微信用户信息
/// </summary>
public class WeixinUserInfo
{
    /// <summary>
    /// 用户是否订阅该公众号
    /// </summary>
    public int Subscribe { get; set; }

    /// <summary>
    /// 用户的OpenID
    /// </summary>
    public string? Openid { get; set; }

    /// <summary>
    /// 用户的昵称
    /// </summary>
    public string? Nickname { get; set; }

    /// <summary>
    /// 用户的性别
    /// </summary>
    public int Sex { get; set; }

    /// <summary>
    /// 用户所在城市
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// 用户所在国家
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// 用户所在省份
    /// </summary>
    public string? Province { get; set; }

    /// <summary>
    /// 用户的语言
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// 用户头像
    /// </summary>
    public string? Headimgurl { get; set; }

    /// <summary>
    /// 用户关注时间
    /// </summary>
    public long SubscribeTime { get; set; }

    /// <summary>
    /// 只有在用户将公众号绑定到微信开放平台帐号后，才会出现该字段
    /// </summary>
    public string? Unionid { get; set; }
}
