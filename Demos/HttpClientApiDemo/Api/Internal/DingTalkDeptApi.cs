namespace HttpClientApiTest.Api.Internal;

using Mud.Common.CodeGenerator;

partial class DingTalkDeptApi : IDingTalkDeptApi
{
    public async Task<SysDeptInfoOutput> GetDeptXXXAsync([Token("TenantAccessToken")][Header("X-API-Key")] string apiKey, [Path] string? id)
    {

        return null;
    }
}