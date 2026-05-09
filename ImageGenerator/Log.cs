namespace ImageGenerator;

internal static partial class Log
{
    // Upload

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload skipped. unsupported content type. name=[{Name}]")]
    public static partial void WarnUploadSkipped(this ILogger log, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Upload completed. name=[{Name}], path=[{Path}]")]
    public static partial void InfoUploadCompleted(this ILogger log, string name, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Upload failed. message=[{Message}]")]
    public static partial void ErrorUploadFailed(this ILogger log, Exception ex, string message);

    // Generation

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation started. prompt=[{Prompt}], count=[{Count}], referenceImages=[{ReferenceImages}], size=[{Size}], quality=[{Quality}], format=[{Format}], compression=[{Compression}], outputDir=[{OutputDir}]")]
    public static partial void InfoGenerationStarted(
        this ILogger log,
        string prompt,
        int count,
        int referenceImages,
        string size,
        string quality,
        string format,
        int compression,
        string outputDir);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation image started. index=[{Index}], count=[{Count}]")]
    public static partial void InfoGenerationImageStarted(this ILogger log, int index, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation image completed. index=[{Index}], count=[{Count}], fileName=[{FileName}], size=[{Size}], elapsed=[{Elapsed}]")]
    public static partial void InfoGenerationImageCompleted(
        this ILogger log,
        int index,
        int count,
        string fileName,
        long size,
        TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation completed. count=[{Count}], totalElapsed=[{TotalElapsed}], files=[{Files}]")]
    public static partial void InfoGenerationCompleted(this ILogger log, int count, TimeSpan totalElapsed, string files);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation retry. delay=[{Delay}], attempt=[{Attempt}], max=[{Max}], message=[{Message}]")]
    public static partial void WarnGenerationRetry(this ILogger log, double delay, int attempt, int max, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation retry by timeout. delay=[{Delay}], attempt=[{Attempt}], max=[{Max}]")]
    public static partial void WarnGenerationRetryTimeout(this ILogger log, double delay, int attempt, int max);

    // UI

    [LoggerMessage(Level = LogLevel.Information, Message = "Generate requested. prompt=[{Prompt}], count=[{Count}], refImages=[{RefImages}]")]
    public static partial void InfoGenerateRequested(this ILogger log, string prompt, int count, int refImages);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generate UI completed. count=[{Count}]")]
    public static partial void InfoGenerateUiCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generate cancelled by user.")]
    public static partial void InfoGenerateCancelled(this ILogger log);

    [LoggerMessage(Level = LogLevel.Error, Message = "Generate failed. message=[{Message}]")]
    public static partial void ErrorGenerateFailed(this ILogger log, Exception ex, string message);
}
