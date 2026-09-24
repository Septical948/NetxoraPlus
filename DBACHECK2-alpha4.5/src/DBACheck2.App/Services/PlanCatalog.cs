using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public static class PlanCatalog
{
    public static IReadOnlyList<PlanDefinition> All { get; } = new[]
    {
        new PlanDefinition {
            Plan=SubscriptionPlan.Standard,
            Name="DBACHECK2 Standard",
            Positioning="Individual DBA toolkit: assess, diagnose and resolve manually.",
            MonthlyUsd=29m,
            AnnualUsd=290m,
            Features=new[] {
                F("SQL Server / PostgreSQL / Oracle / MySQL-MariaDB",ProductEntitlement.MultiEngine),
                F("Quick Check",ProductEntitlement.QuickCheck),
                F("DB diagnostics modules",ProductEntitlement.Diagnostics),
                F("Full multi-engine Assessment",ProductEntitlement.FullAssessment),
                F("Full Assessment HTML export",ProductEntitlement.AssessmentExport),
                F("Incident History",ProductEntitlement.IncidentHistory)
            }
        },
        new PlanDefinition {
            Plan=SubscriptionPlan.Plus,
            Name="DBACHECK2 Plus",
            Positioning="DBA operations platform: detect, prioritize, correlate and diagnose.",
            MonthlyUsd=69m,
            AnnualUsd=690m,
            Features=new[] {
                F("Everything in Standard",ProductEntitlement.MultiEngine),
                F("Monitoring integrations",ProductEntitlement.MonitoringIntegrations),
                F("Alert Inbox P1-P4",ProductEntitlement.AlertInbox),
                F("Cross-monitor correlation",ProductEntitlement.AlertCorrelation),
                F("Alert → targeted DB diagnosis",ProductEntitlement.TargetedDiagnosis),
                F("Incident Operations",ProductEntitlement.IncidentOperations),
                F("Priority support",ProductEntitlement.PrioritySupport)
            }
        },
        new PlanDefinition {
            Plan=SubscriptionPlan.Enterprise,
            Name="DBACHECK2 Enterprise",
            Positioning="Organization-wide DBA operations, governance and commercial support.",
            ContactSales=true,
            Features=new[] {
                F("Everything in Plus",ProductEntitlement.AlertInbox),
                F("Organization / multi-seat licensing",ProductEntitlement.TeamLicensing),
                F("Custom integrations and onboarding",ProductEntitlement.CustomIntegrations),
                F("Enterprise support / SLA",ProductEntitlement.EnterpriseSla),
                F("Shared team operations",ProductEntitlement.SharedOperations,FeatureAvailability.Planned),
                F("SSO / RBAC",ProductEntitlement.SsoRbac,FeatureAvailability.Planned),
                F("Audit and central policy",ProductEntitlement.Audit,FeatureAvailability.Planned)
            }
        }
    };

    public static PlanDefinition Get(SubscriptionPlan plan)=>All.First(x=>x.Plan==plan);

    public static bool Includes(SubscriptionPlan plan,ProductEntitlement entitlement)
    {
        if(plan==SubscriptionPlan.Enterprise)
            return Get(SubscriptionPlan.Enterprise).Features.Any(x=>x.Entitlement==entitlement && x.Availability==FeatureAvailability.Available)
                || Includes(SubscriptionPlan.Plus,entitlement);
        if(plan==SubscriptionPlan.Plus)
            return Get(SubscriptionPlan.Plus).Features.Any(x=>x.Entitlement==entitlement && x.Availability==FeatureAvailability.Available)
                || Includes(SubscriptionPlan.Standard,entitlement);
        return Get(SubscriptionPlan.Standard).Features.Any(x=>x.Entitlement==entitlement && x.Availability==FeatureAvailability.Available);
    }

    private static PlanFeature F(string name,ProductEntitlement entitlement,FeatureAvailability availability=FeatureAvailability.Available)
        =>new(){Name=name,Entitlement=entitlement,Availability=availability};
}
