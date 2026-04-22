namespace Mud.HttpUtils;

public interface IAppManager<TAppContext>
    where TAppContext : IMudAppContext
{
    TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
        where TContextSwitcher : IAppContextSwitcher;

    TContextSwitcher GetDefalutWebApi<TContextSwitcher>()
        where TContextSwitcher : IAppContextSwitcher;

    TAppContext GetDefaultApp();

    TAppContext GetApp(string appKey);

    bool TryGetApp(string appKey, out TAppContext? appContext);

    IEnumerable<TAppContext> GetAllApps();

    bool HasApp(string appKey);

    bool RemoveApp(string appKey);
}
