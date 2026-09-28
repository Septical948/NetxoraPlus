using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed record FeatureGateResult(
    bool Allowed,
    SubscriptionSnapshot Snapshot,
    ProductEntitlement Entitlement,
    SubscriptionPlan RequiredPlan,
    string Message);

public sealed class FeatureGateService
{
    private readonly SubscriptionService _subscriptions=new();

    public async Task<FeatureGateResult> CheckAsync(ProductEntitlement entitlement,bool refresh=false)
    {
        var snapshot=refresh
            ? await _subscriptions.RefreshAsync()
            : await _subscriptions.LoadAsync();

        if(snapshot.DevelopmentLicense)
            return new(true,snapshot,entitlement,SubscriptionPlan.Standard,"Development license");

        var required=RequiredPlan(entitlement);

        if(_subscriptions.HasEntitlement(snapshot,entitlement))
        {
            var access=snapshot.State==SubscriptionState.PastDue && snapshot.AccessUntil.HasValue
                ? $" · grace until {snapshot.AccessUntil.Value.ToLocalTime():yyyy-MM-dd}"
                : "";
            return new(true,snapshot,entitlement,required,$"{snapshot.Plan} · {snapshot.State}{access}");
        }

        var planIncludes=PlanCatalog.Includes(snapshot.Plan,entitlement);
        var reason=planIncludes
            ? $"Subscription state: {snapshot.State}"
            : $"Requires DBACHECK2 {required}";

        return new(false,snapshot,entitlement,required,reason);
    }

    public static SubscriptionPlan RequiredPlan(ProductEntitlement entitlement)
    {
        if(PlanCatalog.Get(SubscriptionPlan.Standard).Features.Any(x=>x.Entitlement==entitlement && x.Availability==FeatureAvailability.Available))
            return SubscriptionPlan.Standard;

        if(PlanCatalog.Get(SubscriptionPlan.Plus).Features.Any(x=>x.Entitlement==entitlement && x.Availability==FeatureAvailability.Available))
            return SubscriptionPlan.Plus;

        return SubscriptionPlan.Enterprise;
    }
}
