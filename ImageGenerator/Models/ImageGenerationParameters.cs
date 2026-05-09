namespace ImageGenerator.Models;

public sealed record ImageGenerationParameters
{
    public string Size { get; init; } = "1024x1024";

    public string Quality { get; init; } = "low";

    public string OutputFormat { get; init; } = "png";

    public int OutputCompression { get; init; } = 75;
}
