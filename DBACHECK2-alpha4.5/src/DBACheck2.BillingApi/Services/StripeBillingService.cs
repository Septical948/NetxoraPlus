using DBACheck2.BillingApi.Models;
using Stripe;
using Stripe.Checkout;

namespace DBACheck2.BillingApi.Services;

public sealed class StripeBillingService
{
    private readonly BillingStore _store;
    private readonly string _secretKey;
    private readonly string _webhookSecret;
    private readonly string _publicUrl;

    public StripeBillingService(BillingStore store)
    {
        _store=store;
        _secretKey=(Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY")??"").Trim();
        _webhookSecret=(Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET")??"").Trim();
        _publicUrl=(Environment.GetEnvironmentVariable("DBACHECK2_PUBLIC_URL")??"http://localhost:5098").Trim().TrimEnd('/');

        if(!string.IsNullOrWhiteSpace(_secretKey))
            StripeConfiguration.ApiKey=_secretKey;
    }

    public bool StripeConfigured=>_secretKey.StartsWith("sk_",StringComparison.Ordinal);
    public bool WebhookConfigured=>_webhookSecret.StartsWith("whsec_",StringComparison.Ordinal);
    public bool PricesConfigured=>RequiredPriceVariables().All(x=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x)));

    public object ConfigurationStatus()=>new {
        stripe=StripeConfigured,
        webhook=WebhookConfigured,
        prices=PricesConfigured,
        public_url=_publicUrl,
        mode=_secretKey.StartsWith("sk_live_",StringComparison.Ordinal)?"live":
             _secretKey.StartsWith("sk_test_",StringComparison.Ordinal)?"test":"unconfigured"
    };

    public async Task<string> CreateCheckoutAsync(CheckoutRequest request)
    {
        EnsureStripe();
        if(!TryPlan(request.Plan,out var plan)||plan==SubscriptionPlan.Enterprise)
            throw new InvalidOperationException("Checkout supports Standard or Plus only.");
        if(!TryCycle(request.Cycle,out var cycle))
            throw new InvalidOperationException("Billing cycle must be monthly or annual.");
        if(string.IsNullOrWhiteSpace(request.InstallationId))
            throw new InvalidOperationException("installation_id is required.");

        var existing=await _store.GetAsync(request.InstallationId);
        if(existing is not null && existing.State is SubscriptionState.Active or SubscriptionState.Trial)
            throw new InvalidOperationException("This installation already has an active subscription. Use the Customer Portal to change or cancel the plan.");

        var priceId=ResolvePrice(plan,cycle);
        var metadata=new Dictionary<string,string> {
            ["installation_id"]=request.InstallationId,
            ["plan"]=plan.ToString().ToLowerInvariant(),
            ["cycle"]=cycle.ToString().ToLowerInvariant()
        };

        var options=new SessionCreateOptions {
            Mode="subscription",
            SuccessUrl=$"{_publicUrl}/billing/success?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl=$"{_publicUrl}/billing/cancel",
            ClientReferenceId=request.InstallationId,
            AllowPromotionCodes=true,
            LineItems=new List<SessionLineItemOptions> {
                new(){Price=priceId,Quantity=1}
            },
            Metadata=metadata,
            SubscriptionData=new SessionSubscriptionDataOptions {Metadata=metadata}
        };

        if(existing is not null && !string.IsNullOrWhiteSpace(existing.CustomerReference))
            options.Customer=existing.CustomerReference;

        var session=await new SessionService().CreateAsync(options);

        await _store.UpsertAsync(new SubscriptionRecord {
            InstallationId=request.InstallationId,
            Plan=plan,
            Cycle=cycle,
            State=SubscriptionState.Unknown,
            CustomerReference=existing?.CustomerReference??"",
            SubscriptionReference=existing?.SubscriptionReference??"",
            CheckoutSessionReference=session.Id
        });

        return session.Url??throw new InvalidOperationException("Stripe did not return a Checkout URL.");
    }

    public async Task<string> CreatePortalAsync(string installationId)
    {
        EnsureStripe();
        var record=await _store.GetAsync(installationId)
            ?? throw new InvalidOperationException("No billing record exists for this installation.");
        if(string.IsNullOrWhiteSpace(record.CustomerReference))
            throw new InvalidOperationException("This installation is not associated with a Stripe Customer yet.");

        var options=new Stripe.BillingPortal.SessionCreateOptions {
            Customer=record.CustomerReference,
            ReturnUrl=$"{_publicUrl}/billing/return"
        };
        var session=await new Stripe.BillingPortal.SessionService().CreateAsync(options);
        return session.Url;
    }

    public Stripe.Event ConstructWebhook(string json,string signature)
    {
        if(!WebhookConfigured)throw new InvalidOperationException("STRIPE_WEBHOOK_SECRET is not configured.");
        return EventUtility.ConstructEvent(json,signature,_webhookSecret);
    }

    public async Task ProcessEventAsync(Stripe.Event stripeEvent)
    {
        if(!await _store.TryMarkEventAsync(stripeEvent.Id,stripeEvent.Type))return;

        switch(stripeEvent.Type)
        {
            case "checkout.session.completed":
                if(stripeEvent.Data.Object is Stripe.Checkout.Session checkout)
                    await ApplyCheckoutAsync(checkout);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                if(stripeEvent.Data.Object is Stripe.Subscription subscription)
                    await ApplySubscriptionAsync(subscription);
                break;
        }
    }

    private async Task ApplyCheckoutAsync(Stripe.Checkout.Session session)
    {
        var installation=Get(session.Metadata,"installation_id")??session.ClientReferenceId;
        if(string.IsNullOrWhiteSpace(installation))return;

        var current=await _store.GetAsync(installation)??new(){InstallationId=installation};
        current.Plan=ParsePlan(Get(session.Metadata,"plan"),current.Plan);
        current.Cycle=ParseCycle(Get(session.Metadata,"cycle"),current.Cycle);
        current.CustomerReference=session.CustomerId??current.CustomerReference;
        current.SubscriptionReference=session.SubscriptionId??current.SubscriptionReference;
        current.CheckoutSessionReference=session.Id;

        // The subscription webhook is authoritative for state. Checkout completion means
        // provisioning can begin, but we keep Unknown until subscription status arrives.
        await _store.UpsertAsync(current);
    }

    private async Task ApplySubscriptionAsync(Stripe.Subscription subscription)
    {
        var installation=Get(subscription.Metadata,"installation_id");
        var existing=!string.IsNullOrWhiteSpace(installation)
            ? await _store.GetAsync(installation)
            : await _store.FindBySubscriptionAsync(subscription.Id);

        if(existing is null && string.IsNullOrWhiteSpace(installation))return;

        var record=existing??new SubscriptionRecord{InstallationId=installation!};
        record.Plan=ParsePlan(Get(subscription.Metadata,"plan"),record.Plan);
        record.Cycle=ParseCycle(Get(subscription.Metadata,"cycle"),record.Cycle);
        record.CustomerReference=subscription.CustomerId??record.CustomerReference;
        record.SubscriptionReference=subscription.Id;
        record.State=MapState(subscription.Status);
        record.CurrentPeriodEnd=TryGetPeriodEnd(subscription);
        await _store.UpsertAsync(record);
    }

    private static DateTime? TryGetPeriodEnd(Stripe.Subscription subscription)
    {
        // Stripe moved period fields across API generations. Reflection keeps this service
        // compatible with both subscription-level and item-level period representations.
        var direct=subscription.GetType().GetProperty("CurrentPeriodEnd")?.GetValue(subscription);
        if(direct is DateTime dt)return dt;
        if(direct is DateTimeOffset dto)return dto.UtcDateTime;

        var items=subscription.Items?.Data;
        if(items is null||items.Count==0)return null;
        DateTime? max=null;
        foreach(var item in items)
        {
            var value=item.GetType().GetProperty("CurrentPeriodEnd")?.GetValue(item);
            DateTime? parsed=value switch {
                DateTime x=>x,
                DateTimeOffset x=>x.UtcDateTime,
                _=>null
            };
            if(parsed.HasValue && (!max.HasValue||parsed.Value>max.Value))max=parsed;
        }
        return max;
    }

    private static SubscriptionState MapState(string? status)=>status switch {
        "trialing"=>SubscriptionState.Trial,
        "active"=>SubscriptionState.Active,
        "past_due" or "unpaid" or "incomplete"=>SubscriptionState.PastDue,
        "canceled"=>SubscriptionState.Canceled,
        "incomplete_expired"=>SubscriptionState.Expired,
        _=>SubscriptionState.Unknown
    };

    private static string? Get(IDictionary<string,string>? metadata,string key)
        =>metadata is not null && metadata.TryGetValue(key,out var value)?value:null;

    private static bool TryPlan(string value,out SubscriptionPlan plan)
        =>Enum.TryParse(value,true,out plan);
    private static bool TryCycle(string value,out BillingCycle cycle)
        =>Enum.TryParse(value,true,out cycle);
    private static SubscriptionPlan ParsePlan(string? value,SubscriptionPlan fallback)
        =>value is not null&&TryPlan(value,out var parsed)?parsed:fallback;
    private static BillingCycle ParseCycle(string? value,BillingCycle fallback)
        =>value is not null&&TryCycle(value,out var parsed)?parsed:fallback;

    private static IEnumerable<string> RequiredPriceVariables()=>new[]{
        "STRIPE_PRICE_STANDARD_MONTHLY",
        "STRIPE_PRICE_STANDARD_ANNUAL",
        "STRIPE_PRICE_PLUS_MONTHLY",
        "STRIPE_PRICE_PLUS_ANNUAL"
    };

    private static string ResolvePrice(SubscriptionPlan plan,BillingCycle cycle)
    {
        var variable=(plan,cycle) switch {
            (SubscriptionPlan.Standard,BillingCycle.Monthly)=>"STRIPE_PRICE_STANDARD_MONTHLY",
            (SubscriptionPlan.Standard,BillingCycle.Annual)=>"STRIPE_PRICE_STANDARD_ANNUAL",
            (SubscriptionPlan.Plus,BillingCycle.Monthly)=>"STRIPE_PRICE_PLUS_MONTHLY",
            (SubscriptionPlan.Plus,BillingCycle.Annual)=>"STRIPE_PRICE_PLUS_ANNUAL",
            _=>throw new InvalidOperationException("No automatic Checkout price exists for this plan.")
        };
        var value=(Environment.GetEnvironmentVariable(variable)??"").Trim();
        if(string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{variable} is not configured.");
        return value;
    }

    private void EnsureStripe()
    {
        if(!StripeConfigured)
            throw new InvalidOperationException("STRIPE_SECRET_KEY is not configured on the Billing API.");
    }
}
