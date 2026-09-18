using DBACheck2.App.Models;
using Npgsql;

namespace DBACheck2.App.Providers;

public sealed class PostgreSqlProvider : IDatabaseProvider
{
    private readonly ServerProfile _profile;
    private readonly string _connectionString;

    public PostgreSqlProvider(ServerProfile profile)
    {
        _profile=profile;
        var cs=new NpgsqlConnectionStringBuilder {
            Host=profile.Host,
            Port=profile.Port ?? 5432,
            Database=string.IsNullOrWhiteSpace(profile.DatabaseOrService)?"postgres":profile.DatabaseOrService,
            Username=profile.Username ?? "",
            Password=profile.Password ?? "",
            Timeout=8,
            CommandTimeout=15,
            ApplicationName="DBACHECK2",
            Pooling=false
        };
        _connectionString=cs.ConnectionString;
    }

    public DatabaseEngine Engine=>DatabaseEngine.PostgreSql;
    public string DisplayName=>"PostgreSQL";

    public async Task<string> TestAsync()
    {
        await using var cn=new NpgsqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd=new NpgsqlCommand("SELECT version(), current_database(), current_user;",cn);
        await using var r=await cmd.ExecuteReaderAsync(); await r.ReadAsync();
        return $"{r.GetString(0)}\nDatabase: {r.GetString(1)} | User: {r.GetString(2)}";
    }

    public async Task<List<HealthItem>> QuickCheckAsync()
    {
        var result=new List<HealthItem>();
        await using var cn=new NpgsqlConnection(_connectionString);
        await cn.OpenAsync();

        result.Add(await ScalarCheck(cn,"DATABASES",@"
SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,
       COUNT(*)||' database(s) sin permitir conexiones',
       COALESCE(string_agg(datname, ', '),'')
FROM pg_database WHERE NOT datallowconn AND datname NOT IN ('template0','template1');"));

        result.Add(await ScalarCheck(cn,"SESSIONS",@"
SELECT CASE WHEN COUNT(*)>=100 THEN 'WARNING' ELSE 'OK' END,
       COUNT(*)||' sesión(es) conectadas',
       'active='||SUM(CASE WHEN state='active' THEN 1 ELSE 0 END)||', idle='||SUM(CASE WHEN state='idle' THEN 1 ELSE 0 END)
FROM pg_stat_activity;"));

        result.Add(await ScalarCheck(cn,"BLOCKING",@"
SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,
       COUNT(*)||' sesión(es) esperando lock',
       COALESCE(string_agg('pid '||a.pid||' wait='||COALESCE(a.wait_event_type,'lock'), '; '),'')
FROM pg_stat_activity a
WHERE a.wait_event_type='Lock';"));

        result.Add(await ScalarCheck(cn,"TRANSACTIONS",@"
SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,
       COUNT(*)||' transacción(es) > 30 min',
       COALESCE(string_agg('pid '||pid||' '||EXTRACT(EPOCH FROM (now()-xact_start))::bigint/60||' min', '; '),'')
FROM pg_stat_activity
WHERE xact_start IS NOT NULL AND now()-xact_start > interval '30 minutes';"));

        result.Add(await ScalarCheck(cn,"DATABASE SIZE",@"
SELECT 'OK',
       COUNT(*)||' database(s)',
       COALESCE(string_agg(datname||'='||pg_size_pretty(pg_database_size(datname)), '; '),'')
FROM pg_database WHERE datallowconn;"));

        result.Add(await ScalarCheck(cn,"REPLICATION",@"
SELECT CASE WHEN pg_is_in_recovery() THEN 'INFO' WHEN COUNT(*)>0 THEN 'OK' ELSE 'INFO' END,
       CASE WHEN pg_is_in_recovery() THEN 'Servidor en recovery/standby'
            WHEN COUNT(*)>0 THEN COUNT(*)||' réplica(s) conectada(s)'
            ELSE 'Primary sin réplicas streaming visibles' END,
       COALESCE(string_agg(COALESCE(client_addr::text,'local')||' '||COALESCE(state,''), '; '),'')
FROM pg_stat_replication;"));

        result.Add(await WalCheck(cn));
        return result;
    }

    private static async Task<HealthItem> ScalarCheck(NpgsqlConnection cn,string area,string sql)
    {
        try {
            await using var cmd=new NpgsqlCommand(sql,cn){CommandTimeout=15};
            await using var r=await cmd.ExecuteReaderAsync(); await r.ReadAsync();
            return new HealthItem { Area=area, Status=Convert.ToString(r.GetValue(0))??"INFO", Summary=Convert.ToString(r.GetValue(1))??"", Detail=Convert.ToString(r.GetValue(2))??"" };
        } catch(Exception ex) { return new HealthItem {Area=area,Status="ERROR",Summary="Collector PostgreSQL no disponible",Detail=ex.Message}; }
    }

    private static async Task<HealthItem> WalCheck(NpgsqlConnection cn)
    {
        try {
            var version=cn.PostgreSqlVersion;
            string sql=version.Major>=10
                ? @"SELECT 'INFO','WAL / LSN actual', pg_current_wal_lsn()::text;"
                : @"SELECT 'INFO','WAL / XLOG actual', pg_current_xlog_location()::text;";
            if(await IsRecovery(cn))
                sql=version.Major>=10
                    ? @"SELECT 'INFO','Standby WAL replay', pg_last_wal_replay_lsn()::text;"
                    : @"SELECT 'INFO','Standby XLOG replay', pg_last_xlog_replay_location()::text;";
            return await ScalarCheck(cn,"WAL",sql);
        } catch(Exception ex) { return new HealthItem {Area="WAL",Status="ERROR",Summary="No se pudo obtener estado WAL",Detail=ex.Message}; }
    }

    private static async Task<bool> IsRecovery(NpgsqlConnection cn)
    {
        await using var cmd=new NpgsqlCommand("SELECT pg_is_in_recovery();",cn);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
    }
}