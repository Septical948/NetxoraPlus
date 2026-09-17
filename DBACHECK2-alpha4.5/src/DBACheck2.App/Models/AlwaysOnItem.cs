namespace DBACheck2.App.Models;

public sealed class AlwaysOnItem
{
    public string Status { get; set; } = "";
    public string AvailabilityGroup { get; set; } = "";
    public string ReplicaServer { get; set; } = "";
    public string Role { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string SynchronizationState { get; set; } = "";
    public string SynchronizationHealth { get; set; } = "";
    public bool IsSuspended { get; set; }
    public long LogSendQueueKb { get; set; }
    public long RedoQueueKb { get; set; }
    public long LogSendRateKb { get; set; }
    public long RedoRateKb { get; set; }
    public string ConnectedState { get; set; } = "";
    public string OperationalState { get; set; } = "";
}
