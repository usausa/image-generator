namespace ImageGenerator.Settings;

public sealed class ImageGeneratorSetting
{
    public string Endpoint { get; set; } = string.Empty;

    public string DeploymentName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>生成画像の保存先ディレクトリ（物理パス）。</summary>
    public string OutputPath { get; set; } = "output";

    /// <summary>アップロード参照画像の保存先ディレクトリ（物理パス）。</summary>
    public string UploadPath { get; set; } = "uploads";
}
