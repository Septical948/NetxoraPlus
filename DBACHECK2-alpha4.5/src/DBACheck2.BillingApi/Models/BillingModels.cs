using System.Text.Json.Serialization;

namespace DBACheck2.BillingApi.Models;

public enum SubscriptionPlan { Standard, Plus, Enterprise }
public enum BillingCycle { Monthly, Annual }
public enum SubscriptionState { Development, Trial, Active, PastDue, Canceled, Expired, Unknown }

public sealed class SubscriptionRecord
{
    public string InstallationId { get; set; } = "";
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Standard;
    public SubscriptionState State { get; set; } = SubscriptionState.Unknown;
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    public DateTime? CurrentPeriodEnd { get; set; }
    public string CustomerReference { get; set; } = "";
    public string SubscriptionReference { get; set; } = "";
    public string CheckoutSessionReference { get; set; } = "";
    public DateTime LastValidatedAt { get; set; } = DateTime.UtcNow;
    public bool DevelopmentLicense { get; set; }
}

public sealed class CheckoutRequest
{
    [JsonPropertyName("installation_id")]
    public string InstallationId { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Cycle { get; set; } = "";
}

public sealed class PortalRequest
{
    [JsonPropertyName("installation_id")]
    public string InstallationId { get; set; } = "";
}

public sealed class LinkResponse
{
    public string Url { get; set; } = "";
}
