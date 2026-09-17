namespace DBACheck2.App.Models;
public sealed class HealthItem
{
    public string Area { get; set; } = "";
    public string Status { get; set; } = "INFO";
    public string Summary { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Severity => Status switch { "CRITICAL" => 3, "WARNING" => 2, "OK" => 0, _ => 1 };
}
