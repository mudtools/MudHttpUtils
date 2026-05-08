using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

internal class BodyParameterBinder : IParameterBinder
{
    public bool CanBind(ParameterInfo parameter)
    {
        return parameter.Attributes.Any(attr =>
            attr.Name == HttpClientGeneratorConstants.BodyAttribute ||
            attr.Name == HttpClientGeneratorConstants.FormContentAttribute ||
            attr.Name == HttpClientGeneratorConstants.MultipartFormAttribute ||
            attr.Name == HttpClientGeneratorConstants.UploadAttribute ||
            attr.Name == HttpClientGeneratorConstants.FormAttribute);
    }

    public void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent)
    {
    }
}
