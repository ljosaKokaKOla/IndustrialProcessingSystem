using System.Collections.Concurrent;
using IndustrialProcessingSystem.App.Models;

namespace IndustrialProcessingSystem.App.Services;

public class ProcessingSystem : IDisposable
{
    // ── configuration ──────────────────────────────────────────────────────────
    private readonly int _maxQueueSize;
    private readonly int _workerCount;

    // ── priority queue (lower Priority int = higher importance) ────────────────
    private readonly SortedSet<(int priority, long seq, Job job)> _queue;
    private readonly object _queueLock = new();
    private long _sequenceCounter = 0;

    // ── idempotency: tracks every ID we have ever seen ─────────────────────────
    private readonly ConcurrentDictionary<Guid, bool> _seenIds = new();

    // ── per-job TCS so Submit() returns a real awaitable handle ───────────────
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<int>> _pending = new();

    // ── completed results for reports ─────────────────────────────────────────
    private readonly ConcurrentBag<JobResult> _completedResults = new();

    // ── worker infrastructure ──────────────────────────────────────────────────
    private readonly List<Task> _workers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _workSignal = new(0);

    // ── services ───────────────────────────────────────────────────────────────
    private readonly JobLogger _logger;
    private readonly ReportService _reportService;

    // ── events ─────────────────────────────────────────────────────────────────
    public event Func<Guid, int, Task>? JobCompleted;
    public event Func<Guid, Task>? JobFailed;

    public ProcessingSystem(int workerCount, int maxQueueSize,
        string logPath = "job_log.txt", string reportDir = "reports")
    {
        _workerCount  = workerCount;
        _maxQueueSize = maxQueueSize;

        _queue = new SortedSet<(int, long, Job)>(
            Comparer<(int priority, long seq, Job job)>.Create((a, b) =>
            {
                int cmp = a.priority.CompareTo(b.priority);
                return cmp != 0 ? cmp : a.seq.CompareTo(b.seq);
            }));

        _logger        = new JobLogger(logPath);
        _reportService = new ReportService(reportDir, () => _completedResults.ToArray());

        // Lambda subscriptions
        JobCompleted += async (id, result) => await _logger.LogAsync(id, "COMPLETED", result);
        JobFailed    += async (id)          => await _logger.LogAsync(id, "FAILED", -1);

        for (int i = 0; i < _workerCount; i++)
            _workers.Add(Task.Run(() => WorkerLoopAsync(_cts.Token)));
    }

    // ── Submit ─────────────────────────────────────────────────────────────────
    public JobHandle? Submit(Job job)
    {
        if (!_seenIds.TryAdd(job.Id, true))
            return null;

        lock (_queueLock)
        {
            if (_queue.Count >= _maxQueueSize)
            {
                _seenIds.TryRemove(job.Id, out _);
                return null;
            }

            long seq = Interlocked.Increment(ref _sequenceCounter);
            _queue.Add((job.Priority, seq, job));
        }

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[job.Id] = tcs;
        _workSignal.Release();

        return new JobHandle(job.Id, tcs.Task);
    }

    // ── Worker loop ────────────────────────────────────────────────────────────
    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _workSignal.WaitAsync(ct); }
            catch (OperationCanceledException) { break; }

            Job? job = Dequeue();
            if (job == null) continue;

            int result = await ExecuteWithRetryAsync(job);

            if (_pending.TryRemove(job.Id, out var tcs))
                tcs.TrySetResult(result);
        }
    }

    private Job? Dequeue()
    {
        lock (_queueLock)
        {
            if (_queue.Count == 0) return null;
            var item = _queue.Min;
            _queue.Remove(item);
            return item.job;
        }
    }

    // ── Execute with retry (3 attempts, 2 s timeout each) ─────────────────────
    // FIX: JobFailed fires only on intermediate failures (attempt 1 & 2).
    //      On the 3rd failure only ABORT is logged — no extra JobFailed entry.
    public async Task<int> ExecuteWithRetryAsync(Job job)
    {
        const int maxAttempts = 3;
        const int timeoutMs   = 2000;

        TimeSpan lastDuration = TimeSpan.Zero;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool succeeded = false;

            try
            {
                var processTask = JobProcessor.ProcessAsync(job);
                var timeoutTask = Task.Delay(timeoutMs);

                var winner = await Task.WhenAny(processTask, timeoutTask);
                sw.Stop();
                lastDuration = sw.Elapsed;

                if (winner == processTask && processTask.IsCompletedSuccessfully)
                {
                    int result = processTask.Result;
                    RecordResult(job, result, true, sw.Elapsed);
                    if (JobCompleted != null) await JobCompleted.Invoke(job.Id, result);
                    return result;
                }

                // Timed out — fall through to failure handling below
            }
            catch (Exception ex)
            {
                sw.Stop();
                lastDuration = sw.Elapsed;
                Console.Error.WriteLine(
                    $"[ProcessingSystem] Job {job.Id} attempt {attempt} threw: {ex.Message}");
            }

            if (!succeeded)
            {
                if (attempt < maxAttempts)
                {
                    // Intermediate failure: log FAILED and retry
                    if (JobFailed != null) await JobFailed.Invoke(job.Id);
                }
                else
                {
                    // Third failure: ABORT only — no JobFailed event
                    await _logger.LogAbortAsync(job.Id);
                    RecordResult(job, -1, false, lastDuration);
                    return -1;
                }
            }
        }

        return -1;
    }

    private void RecordResult(Job job, int result, bool success, TimeSpan duration)
    {
        _completedResults.Add(new JobResult
        {
            JobId       = job.Id,
            JobType     = job.Type,
            Result      = result,
            Success     = success,
            CompletedAt = DateTime.Now,
            Duration    = duration
        });
    }

    // ── Query methods ──────────────────────────────────────────────────────────
    public IEnumerable<Job> GetTopJobs(int n)
    {
        lock (_queueLock)
            return _queue.Take(n).Select(x => x.job).ToList();
    }

    public Job? GetJob(Guid id)
    {
        lock (_queueLock)
            return _queue.FirstOrDefault(x => x.job.Id == id).job;
    }

    public int QueueCount
    {
        get { lock (_queueLock) return _queue.Count; }
    }

    public IReadOnlyCollection<JobResult> GetResults() => _completedResults.ToArray();

    // ── Dispose ────────────────────────────────────────────────────────────────
    public void Dispose()
    {
        _cts.Cancel();
        Task.WhenAll(_workers).Wait(TimeSpan.FromSeconds(5));
        _workSignal.Dispose();
        _cts.Dispose();
        _logger.Dispose();
        _reportService.Dispose();
    }
}
