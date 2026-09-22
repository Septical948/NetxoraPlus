namespace DBACheck2.App.Models;

public sealed class AlertInboxItem
{
    public string Priority { get; set; } = "P4";
    public int PriorityScore { get; set; }
    public string State { get; set; } = "DETECTED";
    public string Host { get; set; } = "";
    public string Environment { get; set; } = "UNKNOWN";
    public IntegrationCategory Category { get; set; } = IntegrationCategory.General;
    public string Sources { get; set; } = "";
    public int AlertCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string AgeText { get; set; } = "";
    public string Summary { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string MatchReason { get; set; } = "";
    public string PriorityReason { get; set; } = "";
    public bool ProfileMatched { get; set; }
    public List<IntegrationEvent> Events { get; set; } = new();
    public IntegrationEvent PrimaryEvent => Events.OrderByDescending(x=>SeverityRank(x.Severity)).ThenBy(x=>x.Timestamp).FirstOrDefault() ?? new();

    private static int SeverityRank(string value)=>value switch
    {
        "CRITICAL"=>3,
        "WARNING"=>2,
        _=>1
    };
}

public sealed class AlertInboxLoadResult
{
    public List<AlertInboxItem> Items { get; set; } = new();
    public List<string> SourceStatus { get; set; } = new();
    public int ProfilesConfigured { get; set; }
    public int SourcesOk { get; set; }
    public int SourcesFailed { get; set; }
}
