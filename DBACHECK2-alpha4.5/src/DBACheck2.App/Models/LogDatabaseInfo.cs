namespace DBACheck2.App.Models;

public sealed class LogDatabaseInfo
{
    public string DatabaseName { get; set; } = "";
    public decimal LogSizeMb { get; set; }
    public decimal UsedPct { get; set; }
    public decimal UsedMb => Math.Round(LogSizeMb * UsedPct / 100m, 1);
    public string RecoveryModel { get; set; } = "";
    public string ReuseWait { get; set; } = "";
    public DateTime? LastLogBackup { get; set; }
    public string Status => UsedPct >= 90 ? "CRITICAL" : UsedPct >= 75 ? "WARNING" : "OK";
}
