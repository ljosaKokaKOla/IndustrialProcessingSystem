namespace IndustrialProcessingSystem.App.Models;

public enum JobType
{
    Prime,
    IO
}

public class Job
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JobType Type { get; init; }
    public string Payload { get; init; } = string.Empty;
    public int Priority { get; init; }

    public Job() { }

    public Job(JobType type, string payload, int priority)
    {
        Type = type;
        Payload = payload;
        Priority = priority;
    }
}
