namespace DBACheck2.App.Models;

public enum SubscriptionPlan { Standard, Plus, Enterprise }
public enum BillingCycle { Monthly, Annual }
public enum SubscriptionState { Development, Trial, Active, PastDue, Canceled, Expired, Unknown, Paused }
public enum FeatureAvailability { Available, Planned }

public enum ProductEntitlement
{
    MultiEngine,
    QuickCheck,
    Diagnostics,
    FullAssessment,
    AssessmentExport,
    IncidentHistory,
    MonitoringIntegrations,
    AlertInbox,
    AlertCorrelation,
    TargetedDiagnosis,
    IncidentOperations,
    PrioritySupport,
    TeamLicensing,
    SharedOperations,
    SsoRbac,
    Audit,
    CustomIntegrations,
    EnterpriseSla
}

public sealed class PlanFeature
{
    public string Name { get; init; } = "";
    public ProductEntitlement Entitlement { get; init; }
    public FeatureAvailability Availability { get; init; } = FeatureAvailability.Available;
}

public sealed class PlanDefinition
{
    public SubscriptionPlan Plan { get; init; }
    public string Name { get; init; } = "";
    public string Positioning { get; init; } = "";
    public decimal? MonthlyUsd { get; init; }
    public decimal? AnnualUsd { get; init; }
    public bool ContactSales { get; init; }
    public IReadOnlyList<PlanFeature> Features { get; init; } = Array.Empty<PlanFeature>();
}

public sealed class SubscriptionSnapshot
{
    public string InstallationId { get; set; } = "";
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Standard;
    public SubscriptionState State { get; set; } = SubscriptionState.Development;
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? AccessUntil { get; set; }
    public string CustomerReference { get; set; } = "";
    public string SubscriptionReference { get; set; } = "";
    public DateTime LastValidatedAt { get; set; } = DateTime.MinValue;
    public bool DevelopmentLicense { get; set; } = true;
    public bool CancelAtPeriodEnd { get; set; }
    public string PriceReference { get; set; } = "";
}

public sealed class BillingLinkResponse
{
    public string Url { get; set; } = "";
}
