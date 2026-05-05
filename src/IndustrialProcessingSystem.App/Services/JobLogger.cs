namespace IndustrialProcessingSystem.App.Services;

public class JobLogger : IDisposable
{
    private readonly string _logPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public JobLogger(string logPath = "job_log.txt")
    {
        _logPath = logPath;
    }

    public async Task LogAsync(Guid jobId, string status, int result)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{status}] {jobId}, {result}";
        await WriteLineAsync(line);
    }

    public async Task LogAbortAsync(Guid jobId)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ABORT] {jobId}";
        await WriteLineAsync(line);
    }

    private async Task WriteLineAsync(string line)
    {
        await _semaphore.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logPath, line + Environment.NewLine);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
