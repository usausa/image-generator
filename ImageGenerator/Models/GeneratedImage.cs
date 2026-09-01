namespace ImageGenerator.Models;

#pragma warning disable CA1056
public sealed record GeneratedImage
{
    public required string FileName { get; init; }

    public required string FilePath { get; init; }

    public required string WebUrl { get; init; }

    public required TimeSpan Elapsed { get; init; }
}
#pragma warning restore CA1056
