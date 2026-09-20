namespace DBACheck2.App.Models;

public sealed class EngineCapabilities
{
    public string Engine { get; set; } = "";
    public string Version { get; set; } = "";
    public string Role { get; set; } = "Unknown";
    public Dictionary<string,bool> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Summary => $"{Engine} {Version} | Role: {Role}";
}
