using System.Xml.Linq;
using IndustrialProcessingSystem.App.Models;

namespace IndustrialProcessingSystem.App.Services;

public class ReportService : IDisposable
{
    private readonly string _reportDir;
    private readonly Timer _timer;
    private readonly Func<IEnumerable<JobResult>> _getResults;
    // FIX: start at -1 so first Increment gives slot 0
    private int _reportIndex = -1;
    private const int MaxReports = 10;

    public ReportService(string reportDir, Func<IEnumerable<JobResult>> getResults)
    {
        _reportDir  = reportDir;
        _getResults = getResults;
        Directory.CreateDirectory(reportDir);

        // FIX: find the highest existing slot and start just before it
        // so next report continues the circular sequence correctly
        var existing = Directory.GetFiles(reportDir, "report_*.xml");
        if (existing.Length > 0)
            _reportIndex = existing.Length - 1;

        _timer = new Timer(_ => GenerateReport(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void GenerateReport()
    {
        try
        {
            var results = _getResults().ToList();

            var byType = results.GroupBy(r => r.JobType);

            var jobCountByType = byType
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToList();

            var avgDurationByType = byType
                .Select(g => new { Type = g.Key, AvgMs = g.Average(r => r.Duration.TotalMilliseconds) })
                .ToList();

            var failedByType = results
                .Where(r => !r.Success)
                .GroupBy(r => r.JobType)
                .OrderBy(g => g.Key.ToString())
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToList();

            var doc = new XDocument(
                new XElement("Report",
                    new XAttribute("GeneratedAt", DateTime.Now.ToString("o")),
                    new XElement("JobCountByType",
                        jobCountByType.Select(x =>
                            new XElement("Entry",
                                new XAttribute("Type", x.Type),
                                new XAttribute("Count", x.Count)))),
                    new XElement("AvgDurationMsByType",
                        avgDurationByType.Select(x =>
                            new XElement("Entry",
                                new XAttribute("Type", x.Type),
                                new XAttribute("AvgMs", x.AvgMs.ToString("F2"))))),
                    new XElement("FailedJobsByType",
                        failedByType.Select(x =>
                            new XElement("Entry",
                                new XAttribute("Type", x.Type),
                                new XAttribute("Count", x.Count))))));

            // FIX: increment first, then mod — slot goes 0,1,...,9,0,1,... correctly
            int slotIndex = (int)(Interlocked.Increment(ref _reportIndex) % MaxReports);
            var filePath = Path.Combine(_reportDir, $"report_{slotIndex:D2}.xml");
            doc.Save(filePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ReportService] Error generating report: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
