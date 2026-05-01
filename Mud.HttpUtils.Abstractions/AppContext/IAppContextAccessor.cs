namespace Mud.HttpUtils;

public interface IAppContextAccessor
{
    IMudAppContext? Current { get; set; }

    IDisposable BeginScope(IMudAppContext context);
}
