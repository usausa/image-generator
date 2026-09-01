namespace ImageGenerator.Components.Pages;

using System.Diagnostics;
using System.Globalization;

using ImageGenerator.Errors;
using ImageGenerator.Models;
using ImageGenerator.Services;
using ImageGenerator.Settings;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

public sealed partial class Generate : IDisposable
{
    private static readonly string[] SupportedContentTypes = ["image/png", "image/jpeg", "image/webp"];

    //--------------------------------------------------------------------------------
    // State
    //--------------------------------------------------------------------------------

    private readonly List<UploadedImage> uploadedImages = [];
    private readonly List<GeneratedImage> generatedImages = [];
    private GeneratedImage? previewImage;

    private string prompt = string.Empty;
    private int generateCount = 1;
    private ImageGenerationParameters generateParameters = new();

    private bool isBusy;
    private bool isUploading;
    private string progressMessage = string.Empty;
    private string progressElapsed = string.Empty;
    private AppErrorCode? errorCode;

    private bool isDragOver;

    private ElementReference dropZoneRef;

    private CancellationTokenSource? generationCts;
    private Timer? elapsedTimer;
    private Stopwatch? totalStopwatch;

    //--------------------------------------------------------------------------------
    // Computed
    //--------------------------------------------------------------------------------

    private bool IsBusy => isBusy || isUploading;

    private string? ErrorMessage => errorCode.HasValue
        ? (string?)L[$"Error_{errorCode.Value}"]
        : null;

    //--------------------------------------------------------------------------------
    // Inject
    //--------------------------------------------------------------------------------

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public ImageGeneratorService Generator { get; set; } = default!;

    [Inject]
    public IOptions<ImageGeneratorSetting> Setting { get; set; } = default!;

    [Inject]
    public ILogger<Generate> Log { get; set; } = default!;

    [Inject]
    public IStringLocalizer<Generate> L { get; set; } = default!;

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("initDropZone", dropZoneRef);
        }
    }

    public void Dispose()
    {
        generationCts?.Cancel();
        generationCts?.Dispose();
        elapsedTimer?.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Drop zone handlers (HTML drag events bound via @on* attributes)
    //--------------------------------------------------------------------------------

    private void OnDragEnter() => isDragOver = true;

    private void OnDragLeave() => isDragOver = false;

    private void OnDrop() => isDragOver = false;

    //--------------------------------------------------------------------------------
    // File upload: save to server disk, register as UploadedImage
    //--------------------------------------------------------------------------------

    private async Task OnFilesChangedAsync(InputFileChangeEventArgs e)
    {
        isUploading = true;
        errorCode = null;

        var uploadDir = Path.GetFullPath(Setting.Value.UploadPath);
        Directory.CreateDirectory(uploadDir);

        try
        {
            foreach (var file in e.GetMultipleFiles(maximumFileCount: 20))
            {
                var contentType = NormalizeContentType(file.ContentType, file.Name);
                if (contentType is null)
                {
                    Log.WarnUploadSkipped(file.Name);
                    continue;
                }

                var ext = ExtensionFromContentType(contentType);
                var savedName = $"upload-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ext}";
                var savedPath = Path.Combine(uploadDir, savedName);

                await using (var dest = File.Create(savedPath))
                await using (var src = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024))
                {
                    await src.CopyToAsync(dest);
                }

                var index = uploadedImages.Count + 1;
                uploadedImages.Add(new UploadedImage
                {
                    Name = $"Image{index:D2}",
                    ContentType = contentType,
                    FilePath = savedPath,
                    WebUrl = $"/uploads/{savedName}"
                });

                Log.InfoUploadCompleted(file.Name, savedPath);
            }
        }
        catch (IOException ex)
        {
            Log.ErrorUploadFailed(ex, ex.Message);
            errorCode = AppErrorCode.UploadIoError;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ErrorUploadFailed(ex, ex.Message);
            errorCode = AppErrorCode.UploadAccessDenied;
        }
        finally
        {
            isUploading = false;
        }
    }

    //--------------------------------------------------------------------------------
    // Actions
    //--------------------------------------------------------------------------------

    private async Task GenerateAsync()
    {
        if (isBusy || String.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        isBusy = true;
        errorCode = null;
        generatedImages.Clear();
        progressMessage = L["ProgressPreparing"];
        progressElapsed = string.Empty;

        totalStopwatch = Stopwatch.StartNew();
        elapsedTimer = new Timer(_ =>
        {
            progressElapsed = FormatElapsed(totalStopwatch.Elapsed);
            InvokeAsync(StateHasChanged);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        generationCts = new CancellationTokenSource();

        Log.InfoGenerateRequested(prompt, generateCount, uploadedImages.Count);

        try
        {
            var progress = new Progress<ProgressState>(state =>
            {
#pragma warning disable CA1305
                progressMessage = state.Kind switch
                {
                    ProgressKind.Generating => String.Format(CultureInfo.CurrentUICulture, L["ProgressGenerating"], state.Current, state.Total),
                    ProgressKind.SendingRequest => L["ProgressSendingRequest"],
                    _ => L["ProgressPreparing"]
                };
#pragma warning restore CA1305
                InvokeAsync(StateHasChanged);
            });

            void OnImageReady(GeneratedImage image)
            {
                generatedImages.Add(image);
                InvokeAsync(StateHasChanged);
            }

            await Generator.GenerateImagesAsync(
                prompt,
                generateCount,
                uploadedImages,
                generateParameters,
                progress,
                OnImageReady,
                generationCts.Token);

            Log.InfoGenerateUiCompleted(generatedImages.Count);
        }
        catch (OperationCanceledException) when (generationCts?.IsCancellationRequested == true)
        {
            Log.InfoGenerateCancelled();
        }
        catch (OperationCanceledException ex)
        {
            Log.ErrorGenerateFailed(ex, ex.Message);
            errorCode = AppErrorCode.GenerationTimeout;
        }
        catch (AppException ex)
        {
            Log.ErrorGenerateFailed(ex, ex.Message);
            errorCode = ex.Code;
        }
        catch (HttpRequestException ex)
        {
            Log.ErrorGenerateFailed(ex, ex.Message);
            errorCode = AppErrorCode.GenerationRequestFailed;
        }
        finally
        {
            if (elapsedTimer is not null)
            {
                await elapsedTimer.DisposeAsync();
                elapsedTimer = null;
            }
            totalStopwatch?.Stop();
            isBusy = false;
            progressMessage = string.Empty;
            progressElapsed = string.Empty;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ClearInputs()
    {
        prompt = string.Empty;
        uploadedImages.Clear();
        generatedImages.Clear();
        previewImage = null;
        errorCode = null;
    }

    private void RemoveUploadedImage(UploadedImage image)
    {
        uploadedImages.Remove(image);

        for (var i = 0; i < uploadedImages.Count; i++)
        {
            uploadedImages[i] = uploadedImages[i] with { Name = $"Image{i + 1:D2}" };
        }
    }

    private void OpenPreview(GeneratedImage image) => previewImage = image;

    private void ClosePreview() => previewImage = null;

    //--------------------------------------------------------------------------------
    // Helpers
    //--------------------------------------------------------------------------------

    private static string? NormalizeContentType(string contentType, string fileName)
    {
        if (SupportedContentTypes.Contains(contentType))
        {
            return contentType;
        }

        // ブラウザによって ContentType が空や application/octet-stream になることがある
        #pragma warning disable CA1308
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        #pragma warning restore CA1308
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private static string ExtensionFromContentType(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".png"
    };

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
