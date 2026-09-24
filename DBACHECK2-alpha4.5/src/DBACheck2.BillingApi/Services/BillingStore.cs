using DBACheck2.BillingApi.Models;
using Microsoft.Data.Sqlite;

namespace DBACheck2.BillingApi.Services;

public sealed class BillingStore
{
    private readonly string _dbPath;

    public BillingStore(IWebHostEnvironment environment)
    {
        _dbPath=(Environment.GetEnvironmentVariable("DBACHECK2_BILLING_DB")??"").Trim();
        if(string.IsNullOrWhiteSpace(_dbPath))
            _dbPath=Path.Combine(environment.ContentRootPath,"data","billing.db");

        var directory=Path.GetDirectoryName(_dbPath);
        if(!string.IsNullOrWhiteSpace(directory))Directory.CreateDirectory(directory);
        Initialize();
    }

    public string DatabasePath=>_dbPath;
    private SqliteConnection Open()=>new($"Data Source={_dbPath}");

    private void Initialize()
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"CREATE TABLE IF NOT EXISTS subscriptions(
 installation_id TEXT PRIMARY KEY,
 plan INTEGER NOT NULL,
 state INTEGER NOT NULL,
 cycle INTEGER NOT NULL,
 period_end TEXT NULL,
 customer_ref TEXT NOT NULL DEFAULT '',
 subscription_ref TEXT NOT NULL DEFAULT '',
 checkout_ref TEXT NOT NULL DEFAULT '',
 updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_subscriptions_subscription
 ON subscriptions(subscription_ref) WHERE subscription_ref<>'';
CREATE INDEX IF NOT EXISTS ix_subscriptions_customer ON subscriptions(customer_ref);

CREATE TABLE IF NOT EXISTS stripe_events(
 event_id TEXT PRIMARY KEY,
 event_type TEXT NOT NULL,
 processed_at TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();
    }

    public async Task<SubscriptionRecord?> GetAsync(string installationId)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT installation_id,plan,state,cycle,period_end,customer_ref,subscription_ref,checkout_ref,updated_at
FROM subscriptions WHERE installation_id=$id;";
        cmd.Parameters.AddWithValue("$id",installationId);
        await using var r=await cmd.ExecuteReaderAsync();
        if(!await r.ReadAsync())return null;
        return Read(r);
    }

    public async Task<SubscriptionRecord?> FindBySubscriptionAsync(string subscriptionReference)
    {
        if(string.IsNullOrWhiteSpace(subscriptionReference))return null;
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT installation_id,plan,state,cycle,period_end,customer_ref,subscription_ref,checkout_ref,updated_at
FROM subscriptions WHERE subscription_ref=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id",subscriptionReference);
        await using var r=await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()?Read(r):null;
    }

    public async Task UpsertAsync(SubscriptionRecord value)
    {
        value.LastValidatedAt=DateTime.UtcNow;
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT INTO subscriptions
(installation_id,plan,state,cycle,period_end,customer_ref,subscription_ref,checkout_ref,updated_at)
VALUES($id,$plan,$state,$cycle,$end,$customer,$subscription,$checkout,$updated)
ON CONFLICT(installation_id) DO UPDATE SET
 plan=excluded.plan,state=excluded.state,cycle=excluded.cycle,period_end=excluded.period_end,
 customer_ref=excluded.customer_ref,subscription_ref=excluded.subscription_ref,
 checkout_ref=excluded.checkout_ref,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id",value.InstallationId);
        cmd.Parameters.AddWithValue("$plan",(int)value.Plan);
        cmd.Parameters.AddWithValue("$state",(int)value.State);
        cmd.Parameters.AddWithValue("$cycle",(int)value.Cycle);
        cmd.Parameters.AddWithValue("$end",(object?)value.CurrentPeriodEnd?.ToString("O")??DBNull.Value);
        cmd.Parameters.AddWithValue("$customer",value.CustomerReference??"");
        cmd.Parameters.AddWithValue("$subscription",value.SubscriptionReference??"");
        cmd.Parameters.AddWithValue("$checkout",value.CheckoutSessionReference??"");
        cmd.Parameters.AddWithValue("$updated",value.LastValidatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> TryMarkEventAsync(string eventId,string eventType)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT OR IGNORE INTO stripe_events(event_id,event_type,processed_at)
VALUES($id,$type,$now); SELECT changes();";
        cmd.Parameters.AddWithValue("$id",eventId);
        cmd.Parameters.AddWithValue("$type",eventType);
        cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync())>0;
    }

    private static SubscriptionRecord Read(SqliteDataReader r)=>new()
    {
        InstallationId=r.GetString(0),
        Plan=(SubscriptionPlan)r.GetInt32(1),
        State=(SubscriptionState)r.GetInt32(2),
        Cycle=(BillingCycle)r.GetInt32(3),
        CurrentPeriodEnd=r.IsDBNull(4)?null:DateTime.TryParse(r.GetString(4),out var end)?end:null,
        CustomerReference=r.GetString(5),
        SubscriptionReference=r.GetString(6),
        CheckoutSessionReference=r.GetString(7),
        LastValidatedAt=DateTime.TryParse(r.GetString(8),out var updated)?updated:DateTime.MinValue,
        DevelopmentLicense=false
    };
}
