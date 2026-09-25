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

    public bool StripeConfigured=>_secretKey.StartsWith("sk_",StringComparison.Ordinal) || _secretKey.StartsWith("rk_",StringComparison.Ordinal);
    public bool WebhookConfigured=>_webhookSecret.StartsWith("whsec_",StringComparison.Ordinal);
    public bool PricesConfigured=>RequiredPriceVariables().All(x=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x)));

    public object ConfigurationStatus()=>new {
        stripe=StripeConfigured,
        webhook=WebhookConfigured,
        prices=PricesConfigured,
        public_url=_publicUrl,
        mode=_secretKey.StartsWith("sk_live_",StringComparison.Ordinal)||_secretKey.StartsWith("rk_live_",StringComparison.Ordinal)?"live":
             _secretKey.StartsWith("sk_test_",StringComparison.Ordinal)||_secretKey.StartsWith("rk_test_",StringComparison.Ordinal)?"test":"unconfigured"
    };

    public async Task<SubscriptionRecord?> RefreshSubscriptionAsync(string installationId)
    {
        var record=await _store.GetAsync(installationId);
        if(record is null || string.IsNullOrWhiteSpace(record.SubscriptionReference) || !StripeConfigured)
            return record;

        var subscription=await new SubscriptionService().GetAsync(record.SubscriptionReference);
        await ApplySubscriptionAsync(subscription);
        return await _store.GetAsync(installationId);
    }

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

        var requestOptions=string.IsNullOrWhiteSpace(request.RequestId)
            ? null
            : new RequestOptions {IdempotencyKey=$"checkout:{request.InstallationId}:{request.RequestId}"};
        var session=await new SessionService().CreateAsync(options,requestOptions);

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
        if(!await _store.TryBeginEventAsync(stripeEvent.Id,stripeEvent.Type))return;

        try
        {
            switch(stripeEvent.Type)
            {
                case "checkout.session.completed":
                    if(stripeEvent.Data.Object is Stripe.Checkout.Session checkout)
                        await ApplyCheckoutAsync(checkout);
                    break;
                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                case "customer.subscription.paused":
                case "customer.subscription.resumed":
                    if(stripeEvent.Data.Object is Stripe.Subscription subscription)
                        await ApplySubscriptionAsync(subscription);
                    break;
                case "invoice.paid":
                    if(stripeEvent.Data.Object is Stripe.Invoice paidInvoice)
                        await RefreshFromInvoiceAsync(paidInvoice,true);
                    break;
                case "invoice.payment_failed":
                case "invoice.payment_action_required":
                    if(stripeEvent.Data.Object is Stripe.Invoice failedInvoice)
                        await RefreshFromInvoiceAsync(failedInvoice,false);
                    break;
            }

            await _store.MarkEventProcessedAsync(stripeEvent.Id,stripeEvent.Type);
        }
        catch(Exception ex)
        {
            await _store.MarkEventFailedAsync(stripeEvent.Id,ex.Message);
            throw;
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

        if(TryResolvePlanCycleFromPrice(subscription,out var pricePlan,out var priceCycle,out var priceId))
        {
            record.Plan=pricePlan;
            record.Cycle=priceCycle;
            record.PriceReference=priceId;
        }
        else
        {
            record.Plan=ParsePlan(Get(subscription.Metadata,"plan"),record.Plan);
            record.Cycle=ParseCycle(Get(subscription.Metadata,"cycle"),record.Cycle);
        }

        record.CustomerReference=subscription.CustomerId??record.CustomerReference;
        record.SubscriptionReference=subscription.Id;
        record.State=MapState(subscription.Status);
        record.CancelAtPeriodEnd=subscription.CancelAtPeriodEnd;
        record.CurrentPeriodEnd=TryGetPeriodEnd(subscription);
        if(record.State==SubscriptionState.Trial && record.CurrentPeriodEnd.HasValue && !record.AccessUntil.HasValue)
            record.AccessUntil=record.CurrentPeriodEnd;
        await _store.UpsertAsync(record);
    }

    private async Task RefreshFromInvoiceAsync(Stripe.Invoice invoice,bool paid)
    {
        var subscriptionId=GetInvoiceSubscriptionId(invoice);
        if(string.IsNullOrWhiteSpace(subscriptionId))return;

        var subscription=await new Stripe.SubscriptionService().GetAsync(subscriptionId);
        await ApplySubscriptionAsync(subscription);

        if(!paid)return;

        var installation=Get(subscription.Metadata,"installation_id");
        var record=!string.IsNullOrWhiteSpace(installation)
            ? await _store.GetAsync(installation)
            : await _store.FindBySubscriptionAsync(subscription.Id);
        if(record is null)return;

        var end=TryGetPeriodEnd(subscription);
        if(end.HasValue)
        {
            record.AccessUntil=end;
            record.CurrentPeriodEnd=end;
            record.State=MapState(subscription.Status);
            await _store.UpsertAsync(record);
        }
    }

    private static string? GetInvoiceSubscriptionId(Stripe.Invoice invoice)
    {
        static string? ReadId(object? value)
        {
            if(value is null)return null;
            if(value is string s)return s;
            return Convert.ToString(value.GetType().GetProperty("Id")?.GetValue(value));
        }

        var direct=invoice.GetType().GetProperty("SubscriptionId")?.GetValue(invoice);
        var directId=ReadId(direct);
        if(!string.IsNullOrWhiteSpace(directId))return directId;

        var subscription=invoice.GetType().GetProperty("Subscription")?.GetValue(invoice);
        var subscriptionId=ReadId(subscription);
        if(!string.IsNullOrWhiteSpace(subscriptionId))return subscriptionId;

        var parent=invoice.GetType().GetProperty("Parent")?.GetValue(invoice);
        var details=parent?.GetType().GetProperty("SubscriptionDetails")?.GetValue(parent);
        var nested=details?.GetType().GetProperty("SubscriptionId")?.GetValue(details)
            ?? details?.GetType().GetProperty("Subscription")?.GetValue(details);
        return ReadId(nested);
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
        "paused"=>SubscriptionState.Paused,
        "incomplete_expired"=>SubscriptionState.Expired,
        _=>SubscriptionState.Unknown
    };

    private static bool TryResolvePlanCycleFromPrice(Stripe.Subscription subscription,out SubscriptionPlan plan,out BillingCycle cycle,out string priceId)
    {
        plan=SubscriptionPlan.Standard;
        cycle=BillingCycle.Monthly;
        priceId=subscription.Items?.Data?.FirstOrDefault()?.Price?.Id??"";
        if(string.IsNullOrWhiteSpace(priceId))return false;

        var mappings=new[] {
            (Name:"STRIPE_PRICE_STANDARD_MONTHLY",Plan:SubscriptionPlan.Standard,Cycle:BillingCycle.Monthly),
            (Name:"STRIPE_PRICE_STANDARD_ANNUAL",Plan:SubscriptionPlan.Standard,Cycle:BillingCycle.Annual),
            (Name:"STRIPE_PRICE_PLUS_MONTHLY",Plan:SubscriptionPlan.Plus,Cycle:BillingCycle.Monthly),
            (Name:"STRIPE_PRICE_PLUS_ANNUAL",Plan:SubscriptionPlan.Plus,Cycle:BillingCycle.Annual)
        };

        foreach(var x in mappings)
        {
            var configured=(Environment.GetEnvironmentVariable(x.Name)??"").Trim();
            if(!string.IsNullOrWhiteSpace(configured) && string.Equals(configured,priceId,StringComparison.Ordinal))
            {
                plan=x.Plan;
                cycle=x.Cycle;
                return true;
            }
        }
        return false;
    }

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
