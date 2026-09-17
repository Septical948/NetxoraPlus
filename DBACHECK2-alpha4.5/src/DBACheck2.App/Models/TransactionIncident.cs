namespace DBACheck2.App.Models;

public sealed class TransactionIncident
{
    public int SessionId { get; set; }
    public long Minutes { get; set; }
    public string DatabaseName { get; set; } = "";
    public string LoginName { get; set; } = "";
    public string HostName { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string SessionStatus { get; set; } = "";
    public int OpenTransactions { get; set; }
    public int BlockingSessionId { get; set; }
    public string WaitType { get; set; } = "";
    public DateTime TransactionBeginTime { get; set; }
    public long CpuMs { get; set; }
    public long Reads { get; set; }
    public long Writes { get; set; }
    public string SqlText { get; set; } = "";
}
