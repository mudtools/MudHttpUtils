namespace Mud.HttpUtils;

public interface IAppContextSwitcher
{
    IMudAppContext UseApp(string appKey);

    IMudAppContext UseDefaultApp();

    Task<string> GetTokenAsync();
}
