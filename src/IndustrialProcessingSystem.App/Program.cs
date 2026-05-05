using IndustrialProcessingSystem.App.Models;
using IndustrialProcessingSystem.App.Services;

namespace IndustrialProcessingSystem.App;

class Program
{
    static async Task Main(string[] args)
    {
        string configPath = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SystemConfig.xml");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config file not found: {configPath}");
            return;
        }

        SystemConfig config;
        try { config = ConfigLoader.Load(configPath); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to load config: {ex.Message}"); return; }

        Console.WriteLine($"[Main] WorkerCount={config.WorkerCount}, MaxQueueSize={config.MaxQueueSize}");

        using var system = new ProcessingSystem(config.WorkerCount, config.MaxQueueSize);

        foreach (var job in config.InitialJobs)
        {
            try
            {
                var handle = system.Submit(job);
                if (handle != null)
                    Console.WriteLine($"[Main] Submitted initial job {job.Id} Type={job.Type} Priority={job.Priority}");
                else
                    Console.WriteLine($"[Main] Rejected initial job {job.Id} (duplicate or queue full)");
            }
            catch (Exception ex) { Console.Error.WriteLine($"[Main] Error: {ex.Message}"); }
        }

        var random = new Random();
        var jobTypes = Enum.GetValues<JobType>();
        var cts = new CancellationTokenSource();

        for (int i = 0; i < config.WorkerCount; i++)
        {
            var t = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var type = jobTypes[random.Next(jobTypes.Length)];
                        string payload = type == JobType.Prime
                            ? $"numbers:{random.Next(1000, 50000)},threads:{random.Next(1, 5)}"
                            : $"delay:{random.Next(100, 2000)}";
                        int priority = random.Next(1, 6);
                        var job = new Job(type, payload, priority);
                        var handle = system.Submit(job);
                        if (handle != null)
                            Console.WriteLine($"[Producer] Job {job.Id} Type={type} Priority={priority}");
                        else
                            Console.WriteLine($"[Producer] Rejected (queue full or duplicate).");
                        Thread.Sleep(random.Next(200, 800));
                    }
                    catch (Exception ex) { Console.Error.WriteLine($"[Producer] {ex.Message}"); }
                }
            }) { IsBackground = true };
            t.Start();
        }

        Console.WriteLine("[Main] Running 15s. Press Enter to stop early.");
        try { await Task.Delay(15_000, cts.Token); } catch { }
        cts.Cancel();
        Console.WriteLine("[Main] Shutdown complete.");
    }
}
