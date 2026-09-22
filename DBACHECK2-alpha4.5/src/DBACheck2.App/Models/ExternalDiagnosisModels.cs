namespace DBACheck2.App.Models;

public enum IntegrationCategory
{
    General, Storage, LogWal, Locking, HaReplication, Backup, Performance, Availability
}

public sealed class ProfileMatchResult
{
    public ServerProfile? Profile { get; init; }
    public string MatchReason { get; init; } = "";
    public bool IsMatch => Profile is not null;
}

public sealed class ExternalDiagnosisResult
{
    public IntegrationEvent Event { get; init; } = new();
    public ServerProfile Profile { get; init; } = new();
    public string MatchReason { get; init; } = "";
    public IntegrationCategory Category { get; init; }
    public string ProviderInfo { get; init; } = "";
    public List<HealthItem> RelevantChecks { get; init; } = new();

    public IncidentDiagnosis ToIncidentDiagnosis()
    {
        var top=RelevantChecks.OrderByDescending(x=>x.Severity).FirstOrDefault();
        var evidence=BuildEvidence();
        return new IncidentDiagnosis {
            Severity=Event.Severity=="INFO" && top is not null ? top.Status : Event.Severity,
            Problem=$"{Event.Source}: {Event.Name}",
            ProbableCause=top is null
                ? Event.CorrelationHint
                : $"{Event.CorrelationHint} Top DB finding: {top.Area} - {top.Summary}",
            Evidence=evidence,
            RecommendedAction="Review the correlated database findings before any corrective action.",
            DbaAction="Use the corresponding DBACHECK diagnostic module to validate the suspected database condition.",
            Verification="Re-run the targeted diagnosis and confirm both the database finding and external monitoring condition are cleared.",
            Safety="READ - correlation and diagnosis only; no external alert or database action was executed."
        };
    }

    public string BuildEvidence()
    {
        var rows=RelevantChecks.Count==0
            ? "No relevant database checks returned."
            : string.Join(Environment.NewLine,RelevantChecks.Select(x=>$"[{x.Status}] {x.Area}: {x.Summary}\n  {x.Detail}"));
        return $@"EXTERNAL MONITORING EVENT
Source: {Event.Source}
Event ID: {Event.ExternalId}
Host: {Event.Host}
Time: {Event.Timestamp:yyyy-MM-dd HH:mm:ss}
Severity: {Event.Severity} ({Event.NativeSeverity})
Problem: {Event.Name}
Tags: {Event.Tags}
Detail: {Event.RawDetail}

PROFILE MATCH
Profile: {Profile.Name}
Engine: {Profile.Engine}
Target: {Profile.Host}{(Profile.Port is null?"":$":{Profile.Port}")}
Reason: {MatchReason}

CORRELATION
Category: {Category}
Hint: {Event.CorrelationHint}
Provider: {ProviderInfo}

DATABASE EVIDENCE
{rows}";
    }
}
