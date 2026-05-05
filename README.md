# Industrial Processing System

Thread-safe, async, priority-based job processing system implemented in C# / .NET 8.

## Project Structure

```
solution/
├── src/
│   └── IndustrialProcessingSystem.App/
│       ├── Models/         (Job, JobHandle, JobResult, SystemConfig)
│       ├── Services/       (ProcessingSystem, JobProcessor, JobLogger,
│       │                    ReportService, ConfigLoader)
│       ├── Program.cs
│       └── SystemConfig.xml
└── tests/
    └── IndustrialProcessingSystem.Tests/
        └── Tests.cs        (xUnit – 30+ test cases)
```

## How to Run

```bash
# Run the application
dotnet run --project src/IndustrialProcessingSystem.App

# Run tests
dotnet test

# Run tests with code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:CoverletOutput=./coverage/

# Run tests and print summary to console
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:Threshold=67
```

## Key Design Decisions

| Concern | Solution |
|---|---|
| Priority queue | `SortedSet` + lock + sequence counter (FIFO within same priority) |
| Thread-safe queue access | `lock (_queueLock)` on every read/write |
| Worker signalling | `SemaphoreSlim _workSignal` — one release per submitted job |
| Idempotency | `ConcurrentDictionary<Guid, bool> _seenIds` |
| Async result delivery | `TaskCompletionSource<int>` per job → `JobHandle.Result` |
| Retry / timeout | 3 attempts × 2 s timeout; ABORT on 3rd failure (no extra `JobFailed` event) |
| Logging | `SemaphoreSlim(1,1)` guards `File.AppendAllTextAsync` |
| Reports | Circular buffer of 10 XML files via `Interlocked.Increment % 10` |

## CI / Coverage

GitHub Actions workflow runs on every push and PR, collects line coverage via **coverlet**,
and uploads the lcov report as a build artifact.
