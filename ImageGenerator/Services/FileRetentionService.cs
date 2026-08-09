namespace ImageGenerator.Services;

using ImageGenerator.Settings;

using Microsoft.Extensions.Options;

public sealed class FileRetentionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly ILogger<FileRetentionService> log;

    private readonly ImageGeneratorSetting setting;

    public FileRetentionService(ILogger<FileRetentionService> log, IOptions<ImageGeneratorSetting> options)
    {
        this.log = log;
        setting = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (setting.RetentionDays <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                var cutoff = DateTime.Now.AddDays(-setting.RetentionDays);
                var deleted = Cleanup(setting.OutputPath, cutoff) + Cleanup(setting.UploadPath, cutoff);
                if (deleted > 0)
                {
                    log.InfoRetentionCompleted(deleted, setting.RetentionDays);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    private int Cleanup(string path, DateTime cutoff)
    {
        var directory = Path.GetFullPath(path);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (File.GetLastWriteTime(file) >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (IOException e)
            {
                log.WarnRetentionDeleteFailed(file, e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                log.WarnRetentionDeleteFailed(file, e.Message);
            }
        }

        return deleted;
    }
}
