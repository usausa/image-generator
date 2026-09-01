namespace ImageGenerator.Models;

#pragma warning disable CA1056
public sealed record UploadedImage
{
    public required string Name { get; init; }

    public required string ContentType { get; init; }

    public required string FilePath { get; init; }

    public required string WebUrl { get; init; }
}
#pragma warning restore CA1056
