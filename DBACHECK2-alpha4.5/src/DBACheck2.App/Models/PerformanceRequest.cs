namespace DBACheck2.App.Models;

public sealed class PerformanceRequest
{
    public string Status { get; set; } = "";
    public int SessionId { get; set; }
    public long ElapsedSeconds { get; set; }
    public string DatabaseName { get; set; } = "";
    public string LoginName { get; set; } = "";
    public string HostName { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string RequestStatus { get; set; } = "";
    public string Command { get; set; } = "";
    public long CpuMs { get; set; }
    public long Reads { get; set; }
    public long Writes { get; set; }
    public long LogicalReads { get; set; }
    public string WaitType { get; set; } = "";
    public long WaitMs { get; set; }
    public int BlockingSessionId { get; set; }
    public int OpenTransactions { get; set; }
    public long GrantedQueryMemoryKb { get; set; }
    public string SqlText { get; set; } = "";
}
