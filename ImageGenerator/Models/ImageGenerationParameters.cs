namespace ImageGenerator.Models;

/// <summary>画像生成 API に渡すパラメーター。</summary>
public sealed record ImageGenerationParameters
{
    /// <summary>生成サイズ。1024x1024 / 1024x1536 / 1536x1024</summary>
    public string Size { get; init; } = "1024x1024";

    /// <summary>画質。low / medium / high</summary>
    public string Quality { get; init; } = "low";

    /// <summary>出力フォーマット。png / jpeg</summary>
    public string OutputFormat { get; init; } = "png";

    /// <summary>圧縮率 0（無圧縮）〜 100（最大圧縮）。JPEG 時のみ有効。</summary>
    public int OutputCompression { get; init; } = 75;
}
