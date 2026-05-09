namespace ImageGenerator.Components.Pages;

using System.Diagnostics;

using global::ImageGenerator.Models;
using global::ImageGenerator.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public partial class Generate : IAsyncDisposable
{
    //--------------------------------------------------------------------------------
    // State
    //--------------------------------------------------------------------------------

    private List<UploadedImage> uploadedImages = [];
    private List<GeneratedImage> generatedImages = [];
    private GeneratedImage? previewImage;

    private string prompt = string.Empty;
    private int generateCount = 1;

    private bool isBusy;
    private string progressMessage = string.Empty;
    private string progressElapsed = string.Empty;
    private string? errorMessage;

    private bool isDragOver;

    private ElementReference dropZoneRef;
    private IJSObjectReference? jsModule;
    private DotNetObjectReference<Generate>? dotNetRef;

    private CancellationTokenSource? generationCts;
    private Timer? elapsedTimer;
    private Stopwatch? totalStopwatch;

    //--------------------------------------------------------------------------------
    // Computed
    //--------------------------------------------------------------------------------

    private bool IsBusy => isBusy;

    //--------------------------------------------------------------------------------
    // Inject
    //--------------------------------------------------------------------------------

    [Inject]
    public ImageGeneratorService Generator { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public ILogger<Generate> Log { get; set; } = default!;

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        generationCts?.Cancel();
        generationCts?.Dispose();
        elapsedTimer?.Dispose();
        dotNetRef?.Dispose();
        if (jsModule is not null)
        {
            try
            {
                // Revoke all Object URLs before disposing the module.
                if (uploadedImages.Count > 0)
                {
                    var urls = uploadedImages.Select(x => x.ObjectUrl).ToArray();
                    await jsModule.InvokeVoidAsync("revokeObjectUrls", [urls]);
                }
                await jsModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Circuit already disconnected; safe to ignore.
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetRef = DotNetObjectReference.Create(this);
            jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/imageGenerator.js");
            await jsModule.InvokeVoidAsync("initDropZone", dropZoneRef, dotNetRef);
        }
    }

    //--------------------------------------------------------------------------------
    // JS Callbacks
    //--------------------------------------------------------------------------------

    [JSInvokable]
    public async Task OnFilesDroppedAsync(DroppedFileEntry[] entries)
    {
        foreach (var entry in entries)
        {
            var index = uploadedImages.Count + 1;
            uploadedImages.Add(new UploadedImage
            {
                Name = $"Image{index:D2}",
                ContentType = entry.ContentType,
                ObjectUrl = entry.ObjectUrl
            });
        }

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnDragEnterAsync()
    {
        isDragOver = true;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnDragLeaveAsync()
    {
        isDragOver = false;
        return InvokeAsync(StateHasChanged);
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
            // Fetch binary data from the browser just before sending to the API.
            if (uploadedImages.Count > 0 && jsModule is not null)
            {
                progressMessage = "参照画像を取得中...";
                StateHasChanged();
                var urls = uploadedImages.Select(x => x.ObjectUrl).ToArray();
                var base64List = await jsModule.InvokeAsync<string[]>("getImagesAsBase64", [urls]);
                for (var i = 0; i < uploadedImages.Count; i++)
                {
                    uploadedImages[i].Data = Convert.FromBase64String(base64List[i]);
                }
            }

            var progress = new Progress<string>(msg =>
            {
                progressMessage = msg;
                InvokeAsync(StateHasChanged);
            });

            var results = await Generator.GenerateImagesAsync(
                prompt,
                generateCount,
                uploadedImages,
                progress,
                generationCts.Token);

            generatedImages.AddRange(results);
            Log.LogInformation("生成完了 UI: {Count} 件", results.Length);
        }
        catch (OperationCanceledException)
        {
            Log.LogInformation("生成キャンセル");
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

    private async Task ClearInputsAsync()
    {
        if (jsModule is not null && uploadedImages.Count > 0)
        {
            var urls = uploadedImages.Select(x => x.ObjectUrl).ToArray();
            await jsModule.InvokeVoidAsync("revokeObjectUrls", [urls]);
        }

        prompt = string.Empty;
        uploadedImages.Clear();
        errorMessage = null;
    }

    private async Task RemoveUploadedImageAsync(UploadedImage image)
    {
        if (jsModule is not null)
        {
            await jsModule.InvokeVoidAsync("revokeObjectUrls", [new[] { image.ObjectUrl }]);
        }

        uploadedImages.Remove(image);

        // Renumber labels
        for (var i = 0; i < uploadedImages.Count; i++)
        {
            var old = uploadedImages[i];
            uploadedImages[i] = old with { Name = $"Image{i + 1:D2}" };
        }
    }

    private void OpenPreview(GeneratedImage image)
    {
        previewImage = image;
    }

    private void ClosePreview()
    {
        previewImage = null;
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");
}
