namespace ImageGenerator.Models;

/// <summary>Payload sent from JavaScript when files are dropped.</summary>
public sealed record DroppedFileEntry
{
    public required string Name { get; init; }

    public required string ContentType { get; init; }

    public required string ObjectUrl { get; init; }
}
