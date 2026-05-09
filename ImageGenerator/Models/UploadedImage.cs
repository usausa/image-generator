namespace ImageGenerator.Models;

public sealed record UploadedImage
{
    public required string Name { get; init; }

    public required string ContentType { get; init; }

    public required string FilePath { get; init; }

    public required string WebUrl { get; init; }
}
