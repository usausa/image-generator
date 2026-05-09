namespace ImageGenerator.Models;

public sealed record UploadedImage
{
    /// <summary>プロンプト内での参照名 (Image01, Image02 ...)。</summary>
    public required string Name { get; init; }

    public required string ContentType { get; init; }

    /// <summary>サーバー上の物理パス（API 送信に使用）。</summary>
    public required string FilePath { get; init; }

    /// <summary>ブラウザからアクセスできる相対 URL（サムネイル表示に使用）。</summary>
    public required string WebUrl { get; init; }
}
