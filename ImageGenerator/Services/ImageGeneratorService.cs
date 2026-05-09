namespace ImageGenerator.Services;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using ImageGenerator.Errors;
using ImageGenerator.Models;
using ImageGenerator.Settings;

using Microsoft.Extensions.Options;

public sealed class ImageGeneratorService
{
    private readonly ILogger<ImageGeneratorService> log;
    private readonly HttpClient httpClient;
    private readonly ImageGeneratorSetting setting;

    public ImageGeneratorService(ILogger<ImageGeneratorService> log, IHttpClientFactory httpClientFactory, IOptions<ImageGeneratorSetting> options)
    {
        this.log = log;
        setting = options.Value;
        httpClient = httpClientFactory.CreateClient(nameof(ImageGeneratorService));
    }

    public async Task<GeneratedImage[]> GenerateImagesAsync(
        string prompt,
        int count,
        IReadOnlyList<UploadedImage> referenceImages,
        ImageGenerationParameters parameters,
        IProgress<ProgressState> progress,
        Action<GeneratedImage> onImageReady,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var outputDir = Path.GetFullPath(setting.OutputPath);
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var results = new GeneratedImage[count];
        var ext = parameters.OutputFormat == "jpeg" ? ".jpg" : ".png";

        log.InfoGenerationStarted(
            prompt,
            count,
            referenceImages.Count,
            parameters.Size,
            parameters.Quality,
            parameters.OutputFormat,
            parameters.OutputCompression,
            outputDir);

        var totalSw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var index = i + 1;
            progress.Report(ProgressState.Generating(index, count));

            log.InfoGenerationImageStarted(index, count);

            var sw = Stopwatch.StartNew();
            var imageData = await ExecuteWithRetryAsync(
                ct => EditOrGenerateImageAsync(prompt, referenceImages, parameters, ct, progress),
                cancellationToken);
            sw.Stop();

            var fileName = $"output-{DateTime.Now:yyyyMMdd-HHmmss}-{index:D2}{ext}";
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);

            log.InfoGenerationImageCompleted(
                index,
                count,
                fileName,
                imageData.Length,
                sw.Elapsed);

            var generated = new GeneratedImage
            {
                FileName = fileName,
                FilePath = filePath,
                WebUrl = $"/output/{fileName}",
                Elapsed = sw.Elapsed
            };

            results[i] = generated;
            onImageReady(generated);
        }

        totalSw.Stop();
        log.InfoGenerationCompleted(
            count,
            totalSw.Elapsed,
            string.Join(", ", results.Select(r => r.FileName)));

        return results;
    }

    private async Task<byte[]> EditOrGenerateImageAsync(
        string prompt,
        IReadOnlyList<UploadedImage> referenceImages,
        ImageGenerationParameters parameters,
        CancellationToken cancellationToken,
        IProgress<ProgressState> progress)
    {
        using var content = new MultipartFormDataContent();
#pragma warning disable CA2000
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(setting.DeploymentName), "model");
        content.Add(new StringContent(parameters.Size), "size");
        content.Add(new StringContent(parameters.Quality), "quality");
        content.Add(new StringContent(parameters.OutputFormat), "output_format");
        if (parameters.OutputFormat == "jpeg")
        {
            content.Add(new StringContent(parameters.OutputCompression.ToString(CultureInfo.InvariantCulture)), "output_compression");
        }
        content.Add(new StringContent("1"), "n");
#pragma warning restore CA2000

        foreach (var image in referenceImages)
        {
#pragma warning disable CA2000
            var imageBytes = await File.ReadAllBytesAsync(image.FilePath, cancellationToken);
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            content.Add(imageContent, "image[]", image.Name);
#pragma warning restore CA2000
        }

        var apiPath = referenceImages.Count > 0
            ? $"openai/deployments/{setting.DeploymentName}/images/edits?api-version=2025-04-01-preview"
            : $"openai/deployments/{setting.DeploymentName}/images/generations?api-version=2025-04-01-preview";

        var endpoint = setting.Endpoint.EndsWith('/')
            ? setting.Endpoint
            : setting.Endpoint + "/";
        var requestUrl = new Uri(new Uri(endpoint), apiPath);

        progress.Report(ProgressState.SendingRequest);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(10));

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = content;
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
            throw new AppException(AppErrorCode.GenerationNoData, "API returned empty image data array.");
        }

        var base64 = data[0].GetProperty("b64_json").GetString()
            ?? throw new AppException(AppErrorCode.GenerationNoData, "API returned null b64_json.");

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
            catch (HttpRequestException ex) when (attempt <= setting.MaxRetries && IsRetryable(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                log.WarnGenerationRetry(delay.TotalSeconds, attempt, setting.MaxRetries, ex.Message);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException) when (attempt <= setting.MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                log.WarnGenerationRetryTimeout(delay.TotalSeconds, attempt, setting.MaxRetries);
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

    private static AppException CreateRequestException(HttpStatusCode statusCode, string responseBody)
    {
        var message = $"API request failed. Status={(int)statusCode} {statusCode}. Response={responseBody}";
        return new AppException(AppErrorCode.GenerationRequestFailed, message);
    }
}
