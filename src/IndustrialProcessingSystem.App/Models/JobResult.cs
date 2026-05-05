namespace IndustrialProcessingSystem.App.Models;

public class JobResult
{
    public Guid JobId { get; init; }
    public JobType JobType { get; init; }
    public int Result { get; init; }
    public bool Success { get; init; }
    public DateTime CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
}
