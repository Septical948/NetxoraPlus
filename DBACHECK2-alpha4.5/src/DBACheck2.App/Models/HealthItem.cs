namespace DBACheck2.App.Models;
public sealed class HealthItem
{
    public string Area { get; set; } = "";
    public string Status { get; set; } = "INFO";
    public string Summary { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Severity => Status switch { "CRITICAL" => 5, "ERROR" => 4, "WARNING" => 3, "NO PERMISSION" => 2, "NOT ENABLED" => 1, "UNSUPPORTED" => 1, "UNAVAILABLE" => 1, "OK" => 0, _ => 1 };
}
