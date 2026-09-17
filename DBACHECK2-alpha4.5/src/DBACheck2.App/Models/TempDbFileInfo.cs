namespace DBACheck2.App.Models;

public sealed class TempDbFileInfo
{
    public int FileId { get; set; }
    public string LogicalName { get; set; } = "";
    public string PhysicalName { get; set; } = "";
    public decimal SizeMb { get; set; }
    public decimal UsedMb { get; set; }
    public decimal FreeMb { get; set; }
    public decimal UsedPct { get; set; }
    public string Growth { get; set; } = "";
}
