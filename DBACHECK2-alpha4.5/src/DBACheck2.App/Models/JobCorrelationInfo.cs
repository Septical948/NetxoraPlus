namespace DBACheck2.App.Models;

public sealed class JobCorrelationInfo
{
    public Guid JobId { get; init; }
    public string JobName { get; init; } = "";
    public int StepId { get; init; }
    public string StepName { get; init; } = "";
    public string Subsystem { get; init; } = "";
    public string Command { get; init; } = "";
    public bool Enabled { get; init; }
    public int LastOutcome { get; init; }
    public DateTime? LastRun { get; init; }
    public string LastMessage { get; init; } = "";
    public string Outcome => LastOutcome switch { 0=>"FAILED",1=>"SUCCEEDED",2=>"RETRY",3=>"CANCELED",4=>"IN PROGRESS",_=>"UNKNOWN" };
}
