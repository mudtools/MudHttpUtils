namespace Mud.HttpUtils;

public interface IQueryParameter
{
    IEnumerable<KeyValuePair<string, string?>> ToQueryParameters();
}
