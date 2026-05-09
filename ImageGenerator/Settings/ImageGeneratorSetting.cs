namespace ImageGenerator.Settings;

public sealed class ImageGeneratorSetting
{
    public string Endpoint { get; set; } = string.Empty;

    public string DeploymentName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string OutputPath { get; set; } = "output";
}
