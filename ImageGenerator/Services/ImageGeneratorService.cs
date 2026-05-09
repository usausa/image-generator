namespace ImageGenerator.Services;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using ImageGenerator.Models;
using ImageGenerator.Settings;

using Microsoft.Extensions.Options;

public sealed class ImageGeneratorService
{
    private const int MaxRetries = 5;

    private readonly ILogger<ImageGeneratorService> log;
    private readonly HttpClient httpClient;
    private readonly ImageGeneratorSetting setting;

    public ImageGeneratorService(ILogger<ImageGeneratorService> log, IHttpClientFactory httpClientFactory, IOptions<ImageGeneratorSetting> options)
    {
        this.log = log;
        this.setting = options.Value;
        this.httpClient = httpClientFactory.CreateClient(nameof(ImageGeneratorService));
    }

    /// <summary>
    /// 画像を生成してローカルに保存する。
    /// </summary>
    public async Task<GeneratedImage[]> GenerateImagesAsync(
        string prompt,
        int count,
        IReadOnlyList<UploadedImage> referenceImages,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var outputDir = Path.GetFullPath(setting.OutputPath);
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var results = new GeneratedImage[count];

        log.LogInformation(
            "画像生成開始: Prompt=\"{Prompt}\" Count={Count} ReferenceImages={RefCount} OutputDir={OutputDir}",
            prompt, count, referenceImages.Count, outputDir);

        var totalSw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var index = i + 1;
            progress.Report($"生成中 {index}/{count}...");

            log.LogInformation("生成開始: {Index}/{Count}", index, count);

            var sw = Stopwatch.StartNew();
            var imageData = await ExecuteWithRetryAsync(
                ct => EditOrGenerateImageAsync(prompt, referenceImages, ct, progress),
                cancellationToken);
            sw.Stop();

            var fileName = $"output-{DateTime.Now:yyyyMMdd-HHmmss}-{index:D2}.png";
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);

            log.LogInformation(
                "生成完了: {Index}/{Count} FileName={FileName} Size={Size}bytes Elapsed={Elapsed}",
                index, count, fileName, imageData.Length, sw.Elapsed);

            results[i] = new GeneratedImage
            {
                FileName = fileName,
                FilePath = filePath,
                WebUrl = $"/output/{fileName}",
                Elapsed = sw.Elapsed
            };
        }

        totalSw.Stop();
        log.LogInformation(
            "画像生成終了: Count={Count} TotalElapsed={Elapsed} Files=[{Files}]",
            count, totalSw.Elapsed, string.Join(", ", results.Select(r => r.FileName)));

        return results;
    }

    private async Task<byte[]> EditOrGenerateImageAsync(
        string prompt,
        IReadOnlyList<UploadedImage> referenceImages,
        CancellationToken cancellationToken,
        IProgress<string> progress)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(prompt, Encoding.UTF8), "prompt");
        content.Add(new StringContent(setting.DeploymentName, Encoding.UTF8), "model");
        content.Add(new StringContent("1024x1024", Encoding.UTF8), "size");
        content.Add(new StringContent("low", Encoding.UTF8), "quality");
        content.Add(new StringContent("1", Encoding.UTF8), "n");

        foreach (var image in referenceImages)
        {
            // ファイルから読み込む（メモリに byte[] を持たない設計）
            var imageBytes = await File.ReadAllBytesAsync(image.FilePath, cancellationToken);
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            content.Add(imageContent, "image[]", image.Name);
        }

        var apiPath = referenceImages.Count > 0
            ? $"openai/deployments/{setting.DeploymentName}/images/edits?api-version=2025-04-01-preview"
            : $"openai/deployments/{setting.DeploymentName}/images/generations?api-version=2025-04-01-preview";

        var baseUrl = setting.Endpoint.TrimEnd('/') + "/";
        var requestUrl = new Uri(new Uri(baseUrl), apiPath);

        progress.Report("APIリクエスト送信中...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(10));

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = content
        };
        request.Headers.Add("api-key", setting.ApiKey);

        using var response = await httpClient.SendAsync(request, cts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(response.StatusCode, responseBody);
        }

        using var document = JsonDocument.Parse(responseBody);
        var data = document.RootElement.GetProperty("data");

        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("画像データが返されませんでした。");
        }

        var base64 = data[0].GetProperty("b64_json").GetString()
            ?? throw new InvalidOperationException("画像データが空です。");

        return Convert.FromBase64String(base64);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt <= MaxRetries && IsRetryable(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                log.LogWarning("ビジーエラーのため {Delay} 秒後に再試行 (試行 {Attempt}/{Max}): {Message}", delay.TotalSeconds, attempt, MaxRetries, ex.Message);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException) when (attempt <= MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                log.LogWarning("タイムアウトのため {Delay} 秒後に再試行 (試行 {Attempt}/{Max})", delay.TotalSeconds, attempt, MaxRetries);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsRetryable(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static HttpRequestException CreateRequestException(HttpStatusCode statusCode, string responseBody)
    {
        var message = $"API 呼び出しに失敗しました。Status={(int)statusCode} {statusCode}. Response={responseBody}";
        return new HttpRequestException(message, null, statusCode);
    }
}
