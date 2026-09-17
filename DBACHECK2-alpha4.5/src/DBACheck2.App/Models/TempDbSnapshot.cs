namespace DBACheck2.App.Models;

public sealed class TempDbSnapshot
{
    public decimal TotalMb { get; set; }
    public decimal UsedMb { get; set; }
    public decimal FreeMb { get; set; }
    public decimal UsedPct { get; set; }
    public decimal VersionStoreMb { get; set; }
    public decimal UserObjectsMb { get; set; }
    public decimal InternalObjectsMb { get; set; }
    public string Status { get; set; } = "OK";
    public List<TempDbFileInfo> Files { get; set; } = new();
    public int? OldestSnapshotSessionId { get; set; }
    public long? OldestSnapshotSeconds { get; set; }
    public string OldestSnapshotLogin { get; set; } = "";
    public string OldestSnapshotHost { get; set; } = "";
    public string OldestSnapshotApp { get; set; } = "";
    public string OldestSnapshotDatabase { get; set; } = "";
}
