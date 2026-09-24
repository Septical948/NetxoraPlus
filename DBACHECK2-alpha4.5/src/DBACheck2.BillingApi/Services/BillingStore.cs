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
 cancel_at_period_end INTEGER NOT NULL DEFAULT 0,
 price_ref TEXT NOT NULL DEFAULT '',
 updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_subscriptions_subscription
 ON subscriptions(subscription_ref) WHERE subscription_ref<>'';
CREATE INDEX IF NOT EXISTS ix_subscriptions_customer ON subscriptions(customer_ref);

CREATE TABLE IF NOT EXISTS stripe_events(
 event_id TEXT PRIMARY KEY,
 event_type TEXT NOT NULL,
 processed_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS stripe_event_processing(
 event_id TEXT PRIMARY KEY,
 event_type TEXT NOT NULL,
 state TEXT NOT NULL,
 last_error TEXT NOT NULL DEFAULT '',
 updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS installation_credentials(
 installation_id TEXT PRIMARY KEY,
 secret_hash TEXT NOT NULL,
 created_at TEXT NOT NULL,
 last_seen_at TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();

        EnsureColumn(cn,"subscriptions","cancel_at_period_end","INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn,"subscriptions","price_ref","TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection cn,string table,string column,string definition)
    {
        using var check=cn.CreateCommand();
        check.CommandText=$"PRAGMA table_info({table});";
        using var r=check.ExecuteReader();
        var found=false;
        while(r.Read())
            if(string.Equals(r.GetString(1),column,StringComparison.OrdinalIgnoreCase)){found=true;break;}
        if(found)return;
        using var alter=cn.CreateCommand();
        alter.CommandText=$"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    public async Task<SubscriptionRecord?> GetAsync(string installationId)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT installation_id,plan,state,cycle,period_end,customer_ref,subscription_ref,checkout_ref,cancel_at_period_end,price_ref,updated_at
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
        cmd.CommandText=@"SELECT installation_id,plan,state,cycle,period_end,customer_ref,subscription_ref,checkout_ref,cancel_at_period_end,price_ref,updated_at
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
(installation_id,plan,state,cycle,period_end,customer_ref,subscription_ref,checkout_ref,cancel_at_period_end,price_ref,updated_at)
VALUES($id,$plan,$state,$cycle,$end,$customer,$subscription,$checkout,$cancel,$price,$updated)
ON CONFLICT(installation_id) DO UPDATE SET
 plan=excluded.plan,state=excluded.state,cycle=excluded.cycle,period_end=excluded.period_end,
 customer_ref=excluded.customer_ref,subscription_ref=excluded.subscription_ref,
 checkout_ref=excluded.checkout_ref,cancel_at_period_end=excluded.cancel_at_period_end,
 price_ref=excluded.price_ref,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id",value.InstallationId);
        cmd.Parameters.AddWithValue("$plan",(int)value.Plan);
        cmd.Parameters.AddWithValue("$state",(int)value.State);
        cmd.Parameters.AddWithValue("$cycle",(int)value.Cycle);
        cmd.Parameters.AddWithValue("$end",(object?)value.CurrentPeriodEnd?.ToString("O")??DBNull.Value);
        cmd.Parameters.AddWithValue("$customer",value.CustomerReference??"");
        cmd.Parameters.AddWithValue("$subscription",value.SubscriptionReference??"");
        cmd.Parameters.AddWithValue("$checkout",value.CheckoutSessionReference??"");
        cmd.Parameters.AddWithValue("$cancel",value.CancelAtPeriodEnd?1:0);
        cmd.Parameters.AddWithValue("$price",value.PriceReference??"");
        cmd.Parameters.AddWithValue("$updated",value.LastValidatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> TryBeginEventAsync(string eventId,string eventType)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();

        await using var read=cn.CreateCommand();
        read.Transaction=(SqliteTransaction)tx;
        read.CommandText="SELECT state FROM stripe_event_processing WHERE event_id=$id;";
        read.Parameters.AddWithValue("$id",eventId);
        var existing=Convert.ToString(await read.ExecuteScalarAsync());

        if(existing=="PROCESSED" || existing=="PROCESSING")
        {
            await tx.RollbackAsync();
            return false;
        }

        await using var cmd=cn.CreateCommand();
        cmd.Transaction=(SqliteTransaction)tx;
        cmd.CommandText=@"INSERT INTO stripe_event_processing(event_id,event_type,state,last_error,updated_at)
VALUES($id,$type,'PROCESSING','',$now)
ON CONFLICT(event_id) DO UPDATE SET event_type=excluded.event_type,state='PROCESSING',last_error='',updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id",eventId);
        cmd.Parameters.AddWithValue("$type",eventType);
        cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return true;
    }

    public async Task MarkEventProcessedAsync(string eventId,string eventType)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();

        await using(var cmd=cn.CreateCommand())
        {
            cmd.Transaction=(SqliteTransaction)tx;
            cmd.CommandText=@"UPDATE stripe_event_processing SET state='PROCESSED',last_error='',updated_at=$now WHERE event_id=$id;";
            cmd.Parameters.AddWithValue("$id",eventId);
            cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        await using(var legacy=cn.CreateCommand())
        {
            legacy.Transaction=(SqliteTransaction)tx;
            legacy.CommandText=@"INSERT OR REPLACE INTO stripe_events(event_id,event_type,processed_at) VALUES($id,$type,$now);";
            legacy.Parameters.AddWithValue("$id",eventId);
            legacy.Parameters.AddWithValue("$type",eventType);
            legacy.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await legacy.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task MarkEventFailedAsync(string eventId,string error)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"UPDATE stripe_event_processing SET state='FAILED',last_error=$error,updated_at=$now WHERE event_id=$id;";
        cmd.Parameters.AddWithValue("$id",eventId);
        cmd.Parameters.AddWithValue("$error",error.Length>2000?error[..2000]:error);
        cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
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
        CancelAtPeriodEnd=!r.IsDBNull(8) && r.GetInt32(8)!=0,
        PriceReference=r.IsDBNull(9)?"":r.GetString(9),
        LastValidatedAt=DateTime.TryParse(r.GetString(10),out var updated)?updated:DateTime.MinValue,
        DevelopmentLicense=false
    };
}
