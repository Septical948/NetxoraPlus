using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class SubscriptionService
{
    private readonly string _path;
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(12)};
    private readonly string _billingApi;

    public SubscriptionService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");
        Directory.CreateDirectory(dir);
        _path=Path.Combine(dir,"subscription.json");
        _billingApi=(Environment.GetEnvironmentVariable("DBACHECK2_BILLING_API")??"").Trim().TrimEnd('/');
    }

    public bool BillingBackendConfigured=>Uri.TryCreate(_billingApi,UriKind.Absolute,out _);
    public string StoragePath=>_path;

    public async Task<SubscriptionSnapshot> LoadAsync()
    {
        if(File.Exists(_path))
        {
            try
            {
                var value=JsonSerializer.Deserialize<SubscriptionSnapshot>(await File.ReadAllTextAsync(_path));
                if(value is not null && !string.IsNullOrWhiteSpace(value.InstallationId))return value;
            }
            catch{}
        }

        // Beta builds remain unrestricted while the commercial backend is being integrated.
        var created=new SubscriptionSnapshot {
            InstallationId=Guid.NewGuid().ToString("N"),
            Plan=SubscriptionPlan.Enterprise,
            State=SubscriptionState.Development,
            DevelopmentLicense=true,
            LastValidatedAt=DateTime.Now
        };
        await SaveAsync(created);
        return created;
    }

    public Task SaveAsync(SubscriptionSnapshot value)
        =>File.WriteAllTextAsync(_path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));

    public async Task<SubscriptionSnapshot> RefreshAsync()
    {
        var local=await LoadAsync();
        if(!BillingBackendConfigured)return local;

        using var response=await _http.GetAsync($"{_billingApi}/v1/subscription/status?installation_id={Uri.EscapeDataString(local.InstallationId)}");
        response.EnsureSuccessStatusCode();
        var remote=await response.Content.ReadFromJsonAsync<SubscriptionSnapshot>()
            ?? throw new InvalidOperationException("Billing API returned an empty subscription status.");
        if(string.IsNullOrWhiteSpace(remote.InstallationId))remote.InstallationId=local.InstallationId;
        remote.DevelopmentLicense=false;
        remote.LastValidatedAt=DateTime.Now;
        await SaveAsync(remote);
        return remote;
    }

    public async Task<string> CreateCheckoutAsync(SubscriptionPlan plan,BillingCycle cycle)
    {
        if(plan==SubscriptionPlan.Enterprise)
            throw new InvalidOperationException("Enterprise subscriptions use a sales-assisted contract flow.");
        EnsureBackend();
        var local=await LoadAsync();
        using var response=await _http.PostAsJsonAsync($"{_billingApi}/v1/checkout",new {
            installation_id=local.InstallationId,
            plan=plan.ToString().ToLowerInvariant(),
            cycle=cycle.ToString().ToLowerInvariant()
        });
        response.EnsureSuccessStatusCode();
        var link=await response.Content.ReadFromJsonAsync<BillingLinkResponse>();
        if(string.IsNullOrWhiteSpace(link?.Url))throw new InvalidOperationException("Billing API did not return a checkout URL.");
        return link.Url;
    }

    public async Task<string> CreateCustomerPortalAsync()
    {
        EnsureBackend();
        var local=await LoadAsync();
        using var response=await _http.PostAsJsonAsync($"{_billingApi}/v1/customer-portal",new {
            installation_id=local.InstallationId
        });
        response.EnsureSuccessStatusCode();
        var link=await response.Content.ReadFromJsonAsync<BillingLinkResponse>();
        if(string.IsNullOrWhiteSpace(link?.Url))throw new InvalidOperationException("Billing API did not return a customer portal URL.");
        return link.Url;
    }

    public bool HasEntitlement(SubscriptionSnapshot snapshot,ProductEntitlement entitlement)
        =>snapshot.DevelopmentLicense || (snapshot.State is SubscriptionState.Active or SubscriptionState.Trial) && PlanCatalog.Includes(snapshot.Plan,entitlement);

    public static void OpenExternal(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});

    private void EnsureBackend()
    {
        if(!BillingBackendConfigured)
            throw new InvalidOperationException("Billing backend is not configured yet. Set DBACHECK2_BILLING_API to the HTTPS licensing API.");
    }
}
