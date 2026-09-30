namespace DBACheck2.App.Models;

public enum NotificationProvider { MicrosoftTeams, Slack, Discord }
public enum IncidentPriority { P1=1, P2=2, P3=3, P4=4 }

public sealed class NotificationDestination
{
    public string Name { get; set; } = "";
    public NotificationProvider Provider { get; set; }
    public string TenantOrWorkspaceId { get; set; } = "";
    public string TenantOrWorkspaceName { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string? Secret { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? ConnectedUtc { get; set; }
    public string Display => $"{Name} | {Provider} | {TenantOrWorkspaceName} | {ChannelName}";
}

public sealed class NotificationRoute
{
    public string Name { get; set; } = "Default";
    public string HostPattern { get; set; } = "*";
    public IncidentPriority MinimumPriority { get; set; } = IncidentPriority.P2;
    public List<string> DestinationNames { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public sealed class IncidentNotification
{
    public string IncidentId { get; set; } = "";
    public IncidentPriority Priority { get; set; } = IncidentPriority.P3;
    public string Engine { get; set; } = "";
    public string Host { get; set; } = "";
    public string Database { get; set; } = "";
    public string Finding { get; set; } = "";
    public string Impact { get; set; } = "";
    public string SuggestedAction { get; set; } = "";
    public string? DetailsUrl { get; set; }
}
