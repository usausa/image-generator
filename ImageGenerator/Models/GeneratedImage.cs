namespace ImageGenerator.Models;

public sealed record GeneratedImage
{
    public required string FileName { get; init; }

    /// <summary>サーバー上の物理パス。</summary>
    public required string FilePath { get; init; }

    /// <summary>ブラウザからアクセスできる相対 URL（サムネイル・プレビューに使用）。</summary>
    public required string WebUrl { get; init; }

    public required TimeSpan Elapsed { get; init; }
}
