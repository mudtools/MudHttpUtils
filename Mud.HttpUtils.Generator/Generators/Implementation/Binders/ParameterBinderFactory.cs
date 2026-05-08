using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

internal class ParameterBinderFactory
{
    private readonly List<IParameterBinder> _binders;

    public ParameterBinderFactory()
    {
        _binders = new List<IParameterBinder>
        {
            new PathParameterBinder(),
            new QueryParameterBinder(),
            new HeaderParameterBinder(),
            new BodyParameterBinder()
        };
    }

    public IParameterBinder? GetBinder(ParameterInfo parameter)
    {
        return _binders.FirstOrDefault(b => b.CanBind(parameter));
    }

    public IEnumerable<ParameterInfo> GetParametersByBinder<T>(IEnumerable<ParameterInfo> parameters) where T : IParameterBinder
    {
        var binder = _binders.OfType<T>().FirstOrDefault();
        if (binder == null)
            return Enumerable.Empty<ParameterInfo>();

        return parameters.Where(p => binder.CanBind(p));
    }

    public void RegisterBinder(IParameterBinder binder)
    {
        _binders.Add(binder);
    }
}
