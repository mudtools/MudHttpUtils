using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

internal class PathParameterBinder : IParameterBinder
{
    public bool CanBind(ParameterInfo parameter)
    {
        return parameter.Attributes.Any(attr =>
            HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name));
    }

    public void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent)
    {
    }
}
