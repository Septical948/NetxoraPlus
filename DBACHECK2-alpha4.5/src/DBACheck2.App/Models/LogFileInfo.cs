namespace DBACheck2.App.Models;

public sealed class LogFileInfo
{
    public int FileId { get; set; }
    public string LogicalName { get; set; } = "";
    public decimal SizeMb { get; set; }
    public string Growth { get; set; } = "";
    public string MaxSize { get; set; } = "";
    public string PhysicalName { get; set; } = "";
    public decimal DiskFreeMb { get; set; }
}
