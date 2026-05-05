namespace IndustrialProcessingSystem.App.Models;

public class SystemConfig
{
    public int WorkerCount { get; set; } = 4;
    public int MaxQueueSize { get; set; } = 100;
    public List<Job> InitialJobs { get; set; } = new();
}
