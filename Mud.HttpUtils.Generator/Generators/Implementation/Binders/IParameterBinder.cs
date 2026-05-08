using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

internal interface IParameterBinder
{
    bool CanBind(ParameterInfo parameter);

    void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent);
}
