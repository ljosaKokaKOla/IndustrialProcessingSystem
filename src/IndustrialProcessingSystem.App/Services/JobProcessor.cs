using IndustrialProcessingSystem.App.Models;

namespace IndustrialProcessingSystem.App.Services;

public static class JobProcessor
{
    private static readonly Random _rng = new();

    public static async Task<int> ProcessAsync(Job job)
    {
        return job.Type switch
        {
            JobType.Prime => await ProcessPrimeAsync(job.Payload),
            JobType.IO => await ProcessIOAsync(job.Payload),
            _ => throw new NotSupportedException($"Job type {job.Type} not supported.")
        };
    }

    private static Task<int> ProcessPrimeAsync(string payload)
    {
        // Payload format: "numbers:10_000,threads:3"
        var (limit, threadCount) = ParsePrimePayload(payload);
        threadCount = Math.Clamp(threadCount, 1, 8);

        return Task.Run(() =>
        {
            var count = CountPrimesParallel(limit, threadCount);
            return count;
        });
    }

    public static (int limit, int threads) ParsePrimePayload(string payload)
    {
        int limit = 10000;
        int threads = 1;

        foreach (var part in payload.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var val = int.Parse(kv[1].Trim().Replace("_", ""));
            if (key == "numbers") limit = val;
            else if (key == "threads") threads = val;
        }

        return (limit, threads);
    }

    private static int CountPrimesParallel(int limit, int threadCount)
    {
        if (limit < 2) return 0;

        var options = new ParallelOptions { MaxDegreeOfParallelism = threadCount };
        int count = 0;

        Parallel.For(2, limit + 1, options, () => 0,
            (n, _, localCount) =>
            {
                if (IsPrime(n)) localCount++;
                return localCount;
            },
            localCount => Interlocked.Add(ref count, localCount));

        return count;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2) return true;
        if (n % 2 == 0) return false;
        for (int i = 3; (long)i * i <= n; i += 2)
            if (n % i == 0) return false;
        return true;
    }

    private static async Task<int> ProcessIOAsync(string payload)
    {
        // Payload format: "delay:1_000"
        int delay = ParseIOPayload(payload);
        await Task.Run(() => Thread.Sleep(delay));
        lock (_rng)
            return _rng.Next(0, 101);
    }

    public static int ParseIOPayload(string payload)
    {
        foreach (var part in payload.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && kv[0].Trim() == "delay")
                return int.Parse(kv[1].Trim().Replace("_", ""));
        }
        return 1000;
    }
}
