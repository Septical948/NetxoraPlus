namespace DBACheck2.App.Models;

public sealed class IncidentRecord
{
    public long Id { get; init; }
    public string IncidentKey { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string ServerName { get; init; } = "";
    public string DatabaseName { get; init; } = "";
    public string Module { get; init; } = "";
    public string Severity { get; init; } = "";
    public string Problem { get; init; } = "";
    public string Cause { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string ActionTaken { get; init; } = "";
    public string Verification { get; init; } = "";
    public string Status { get; init; } = "OPEN";
    public int RecurrenceCount { get; init; }
}
