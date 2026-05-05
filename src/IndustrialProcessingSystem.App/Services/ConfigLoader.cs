using System.Xml.Linq;
using IndustrialProcessingSystem.App.Models;

namespace IndustrialProcessingSystem.App.Services;

public static class ConfigLoader
{
    public static SystemConfig Load(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var root = doc.Root ?? throw new InvalidOperationException("Invalid XML config.");

        var config = new SystemConfig
        {
            WorkerCount = int.Parse(root.Element("WorkerCount")?.Value ?? "4"),
            MaxQueueSize = int.Parse(root.Element("MaxQueueSize")?.Value ?? "100")
        };

        var jobsEl = root.Element("Jobs");
        if (jobsEl != null)
        {
            foreach (var jobEl in jobsEl.Elements("Job"))
            {
                var typeStr = jobEl.Attribute("Type")?.Value ?? "IO";
                var type = Enum.Parse<JobType>(typeStr, ignoreCase: true);
                var payload = jobEl.Attribute("Payload")?.Value ?? "";
                var priority = int.Parse(jobEl.Attribute("Priority")?.Value ?? "5");

                config.InitialJobs.Add(new Job(type, payload, priority));
            }
        }

        return config;
    }
}
