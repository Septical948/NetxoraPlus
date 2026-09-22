using System.Text.Json.Serialization;

namespace DBACheck2.App.Models;

public enum IntegrationSource { Zabbix, Nagios, Prometheus, Datadog }

public sealed class IntegrationConnectionProfile
{
    public string Name { get; set; } = "Zabbix";
    public IntegrationSource Source { get; set; } = IntegrationSource.Zabbix;
    public string Endpoint { get; set; } = "";
    [JsonIgnore] public string? Token { get; set; }
    public bool RememberToken { get; set; }
    public bool VerifyTls { get; set; } = true;
    public string Display => $"{Name} | {Source} | {Endpoint}";
}

public sealed class IntegrationEvent
{
    public IntegrationSource Source { get; set; }
    public string ExternalId { get; set; } = "";
    public string Host { get; set; } = "";
    public string Name { get; set; } = "";
    public string Severity { get; set; } = "INFO";
    public string NativeSeverity { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool Acknowledged { get; set; }
    public bool Suppressed { get; set; }
    public string Tags { get; set; } = "";
    public string RawDetail { get; set; } = "";
    public string CorrelationHint { get; set; } = "";
}
