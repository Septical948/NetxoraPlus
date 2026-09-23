namespace DBACheck2.App.Models;

public enum AssessmentMode { Quick, Full }
public enum AssessmentCategory { Platform, Capacity, Temporary, Transactions, Logs, Maintenance, Backup, HighAvailability, Performance, Indexes, Configuration, Security, Other }

public sealed class AssessmentCheck
{
    public string CheckId { get; set; } = "";
    public DatabaseEngine Engine { get; set; }
    public AssessmentCategory Category { get; set; } = AssessmentCategory.Other;
    public string Title { get; set; } = "";
    public string Status { get; set; } = "INFO";
    public int Severity { get; set; }
    public string Summary { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string WhyItMatters { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public string Verification { get; set; } = "";
    public string Capability { get; set; } = "AVAILABLE";
    public bool ReadOnly { get; set; } = true;
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public sealed class AssessmentRun
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string ProfileName { get; set; } = "";
    public string Host { get; set; } = "";
    public DatabaseEngine Engine { get; set; }
    public AssessmentMode Mode { get; set; }
    public string ProviderInfo { get; set; } = "";
    public string PackVersion { get; set; } = "Core Baseline 1";
    public List<AssessmentCheck> Checks { get; set; } = new();

    public int Critical => Checks.Count(x=>x.Status is "CRITICAL" or "ERROR");
    public int Warning => Checks.Count(x=>x.Status=="WARNING");
    public int Ok => Checks.Count(x=>x.Status=="OK");
    public int Info => Checks.Count-Critical-Warning-Ok;
    public string Overall => Critical>0?"CRITICAL":Warning>0?"WARNING":"OK";
    public double DurationSeconds => Math.Max(0,(CompletedAt-StartedAt).TotalSeconds);
}
