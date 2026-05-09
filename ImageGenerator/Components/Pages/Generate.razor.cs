namespace ImageGenerator.Components.Pages;

using System.Diagnostics;

using global::ImageGenerator.Models;
using global::ImageGenerator.Services;
using global::ImageGenerator.Settings;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

public partial class Generate : IDisposable
{
    private static readonly string[] SupportedContentTypes = ["image/png", "image/jpeg", "image/webp"];

    //--------------------------------------------------------------------------------
    // State
    //--------------------------------------------------------------------------------

    private List<UploadedImage> uploadedImages = [];
    private List<GeneratedImage> generatedImages = [];
    private GeneratedImage? previewImage;

    private string prompt = string.Empty;
    private int generateCount = 1;

    private bool isBusy;
    private bool isUploading;
    private string progressMessage = string.Empty;
    private string progressElapsed = string.Empty;
    private string? errorMessage;

    private bool isDragOver;

    private ElementReference dropZoneRef;

    private CancellationTokenSource? generationCts;
    private Timer? elapsedTimer;
    private Stopwatch? totalStopwatch;

    //--------------------------------------------------------------------------------
    // Computed
    //--------------------------------------------------------------------------------

    private bool IsBusy => isBusy || isUploading;

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
        GC.SuppressFinalize(this);
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
        errorMessage = null;

        var uploadDir = Path.GetFullPath(Setting.Value.UploadPath);
        Directory.CreateDirectory(uploadDir);

        try
        {
            foreach (var file in e.GetMultipleFiles(maximumFileCount: 20))
            {
                var contentType = NormalizeContentType(file.ContentType, file.Name);
                if (contentType is null)
                {
                    Log.LogWarning("スキップ: サポート外の形式 {Name}", file.Name);
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

                Log.LogInformation("アップロード完了: {Name} -> {Path}", file.Name, savedPath);
            }
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "アップロードエラー: {Message}", ex.Message);
            errorMessage = $"アップロードに失敗しました: {ex.Message}";
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
        if (isBusy || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        isBusy = true;
        errorMessage = null;
        generatedImages.Clear();
        progressMessage = "準備中...";
        progressElapsed = string.Empty;

        totalStopwatch = Stopwatch.StartNew();
        elapsedTimer = new Timer(_ =>
        {
            progressElapsed = FormatElapsed(totalStopwatch.Elapsed);
            InvokeAsync(StateHasChanged);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        generationCts = new CancellationTokenSource();

        Log.LogInformation("生成開始 UI: Prompt={Prompt} Count={Count} RefImages={Ref}",
            prompt, generateCount, uploadedImages.Count);

        try
        {
            var progress = new Progress<string>(msg =>
            {
                progressMessage = msg;
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
                progress,
                OnImageReady,
                generationCts.Token);

            Log.LogInformation("生成完了 UI: {Count} 件", generatedImages.Count);
        }
        catch (OperationCanceledException) when (generationCts?.IsCancellationRequested == true)
        {
            Log.LogInformation("生成キャンセル（ユーザー操作）");
        }
        catch (OperationCanceledException ex)
        {
            Log.LogError(ex, "生成タイムアウトまたは予期せぬキャンセル");
            errorMessage = "リクエストがタイムアウトまたはキャンセルされました。";
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "生成エラー: {Message}", ex.Message);
            errorMessage = ex.Message;
        }
        finally
        {
            elapsedTimer?.Dispose();
            elapsedTimer = null;
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
        errorMessage = null;
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
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
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
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");
}
