using DBACheck2.BillingApi.Models;
using DBACheck2.BillingApi.Services;
using Stripe;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BillingStore>();
builder.Services.AddSingleton<StripeBillingService>();

var app=builder.Build();

app.MapGet("/health",(StripeBillingService stripe,BillingStore store)=>Results.Ok(new {
    service="DBACHECK2 Billing API",
    utc=DateTime.UtcNow,
    database=store.DatabasePath,
    stripe=stripe.ConfigurationStatus()
}));

app.MapGet("/v1/subscription/status",async(string installation_id,BillingStore store)=>{
    if(string.IsNullOrWhiteSpace(installation_id))
        return Results.BadRequest(new{error="installation_id is required"});

    var record=await store.GetAsync(installation_id);
    return record is null
        ? Results.Ok(new SubscriptionRecord {
            InstallationId=installation_id,
            Plan=SubscriptionPlan.Standard,
            State=SubscriptionState.Unknown,
            Cycle=BillingCycle.Monthly,
            DevelopmentLicense=false,
            LastValidatedAt=DateTime.UtcNow
        })
        : Results.Ok(record);
});

app.MapPost("/v1/checkout",async(CheckoutRequest request,StripeBillingService stripe)=>{
    try{return Results.Ok(new LinkResponse{Url=await stripe.CreateCheckoutAsync(request)});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(StripeException ex){return Results.BadRequest(new{error="Stripe checkout failed.",detail=ex.Message});}
});

app.MapPost("/v1/customer-portal",async(PortalRequest request,StripeBillingService stripe)=>{
    try
    {
        if(string.IsNullOrWhiteSpace(request.InstallationId))
            return Results.BadRequest(new{error="installation_id is required"});
        return Results.Ok(new LinkResponse{Url=await stripe.CreatePortalAsync(request.InstallationId)});
    }
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
    catch(StripeException ex){return Results.BadRequest(new{error="Stripe portal failed.",detail=ex.Message});}
});

app.MapPost("/v1/stripe/webhook",async(HttpRequest request,StripeBillingService stripe)=>{
    var json=await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        var signature=request.Headers["Stripe-Signature"].ToString();
        var stripeEvent=stripe.ConstructWebhook(json,signature);
        await stripe.ProcessEventAsync(stripeEvent);
        return Results.Ok(new{received=true});
    }
    catch(StripeException ex){return Results.BadRequest(new{error="Invalid Stripe webhook.",detail=ex.Message});}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapGet("/billing/success",()=>Results.Content("""
<!doctype html><html><head><meta charset="utf-8"><title>DBACHECK2</title>
<style>body{background:#0a0d18;color:#e8edf7;font-family:Segoe UI,Arial;padding:48px}h1{color:#38e8d0}</style></head>
<body><h1>DBACHECK2 subscription received</h1><p>You can return to DBACHECK2 and select <b>Refresh Status</b>.</p></body></html>
""","text/html"));

app.MapGet("/billing/cancel",()=>Results.Content("""
<!doctype html><html><head><meta charset="utf-8"><title>DBACHECK2</title>
<style>body{background:#0a0d18;color:#e8edf7;font-family:Segoe UI,Arial;padding:48px}h1{color:#f0b45a}</style></head>
<body><h1>Checkout canceled</h1><p>No DBACHECK2 subscription change was completed.</p></body></html>
""","text/html"));

app.MapGet("/billing/return",()=>Results.Content("""
<!doctype html><html><head><meta charset="utf-8"><title>DBACHECK2</title>
<style>body{background:#0a0d18;color:#e8edf7;font-family:Segoe UI,Arial;padding:48px}h1{color:#38e8d0}</style></head>
<body><h1>DBACHECK2 Billing</h1><p>You can return to the application and refresh your subscription status.</p></body></html>
""","text/html"));

app.Run();
