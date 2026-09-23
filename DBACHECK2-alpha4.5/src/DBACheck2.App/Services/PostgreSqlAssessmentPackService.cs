using DBACheck2.App.Models;
using Npgsql;

namespace DBACheck2.App.Services;

public sealed class PostgreSqlAssessmentPackService
{
    private readonly ServerProfile p;
    private readonly string cs;
    public PostgreSqlAssessmentPackService(ServerProfile profile)
    {
        p=profile;
        cs=new NpgsqlConnectionStringBuilder {
            Host=p.Host,Port=p.Port??5432,
            Database=string.IsNullOrWhiteSpace(p.DatabaseOrService)?"postgres":p.DatabaseOrService,
            Username=p.Username??"",Password=p.Password??"",
            Timeout=8,CommandTimeout=30,ApplicationName="DBACHECK2 Assessment",Pooling=false
        }.ConnectionString;
    }

    public async Task<List<AssessmentCheck>> RunFullAsync(CancellationToken cancellationToken=default)
    {
        var x=new List<AssessmentCheck>();
        await using var c=new NpgsqlConnection(cs);await c.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var v=c.PostgreSqlVersion;
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(Info("PG.PLATFORM.VERSION",AssessmentCategory.Platform,"PostgreSQL Version",$"PostgreSQL {v}",$"Database={c.Database} | User={p.Username}","Version-aware pack selection."));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.CAPACITY.DATABASES",AssessmentCategory.Capacity,"Database Size",@"SELECT 'OK',COUNT(*)||' database(s)',COALESCE(string_agg(datname||'='||pg_size_pretty(pg_database_size(datname)), '; '),'') FROM pg_database WHERE datallowconn;"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.CONFIG.CONNECTIONS",AssessmentCategory.Configuration,"Connection Capacity",@"SELECT CASE WHEN current_setting('max_connections')::int>0 AND count(*)*100/current_setting('max_connections')::int>=80 THEN 'WARNING' ELSE 'OK' END,count(*)||' / '||current_setting('max_connections')||' connections','active='||SUM(CASE WHEN state='active' THEN 1 ELSE 0 END)||', idle='||SUM(CASE WHEN state='idle' THEN 1 ELSE 0 END) FROM pg_stat_activity;"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.TRAN.LONG",AssessmentCategory.Transactions,"Long Transactions",@"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' transaction(s) > 30 min',COALESCE(string_agg('pid='||pid||' age='||(EXTRACT(EPOCH FROM(now()-xact_start))::bigint/60)||'m', '; '),'') FROM pg_stat_activity WHERE xact_start IS NOT NULL AND now()-xact_start>interval '30 minutes';"));
        if(v.Major>9 || (v.Major==9&&v.Minor>=2))
            x.Add(await Q(c,"PG.TRAN.IDLE",AssessmentCategory.Transactions,"Idle In Transaction",@"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' idle-in-transaction session(s) > 15 min',COALESCE(string_agg('pid='||pid||' age='||(EXTRACT(EPOCH FROM(now()-xact_start))::bigint/60)||'m', '; '),'') FROM pg_stat_activity WHERE state LIKE 'idle in transaction%' AND xact_start IS NOT NULL AND now()-xact_start>interval '15 minutes';"));
        else
            x.Add(Unsupported("PG.TRAN.IDLE",AssessmentCategory.Transactions,"Idle In Transaction",$"pg_stat_activity.state is not available in the required form on PostgreSQL {v}."));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Blocking(c,v));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.MAINT.XID",AssessmentCategory.Maintenance,"XID Age",@"SELECT CASE WHEN COALESCE(MAX(age(datfrozenxid)),0)>1500000000 THEN 'WARNING' ELSE 'OK' END,'max XID age='||COALESCE(MAX(age(datfrozenxid)),0),COALESCE(string_agg(datname||'='||age(datfrozenxid), '; '),'') FROM pg_database WHERE datallowconn;"));
        var vacuumSql=v.Major>9 || (v.Major==9&&v.Minor>=4)
            ? @"SELECT CASE WHEN COUNT(*) FILTER (WHERE COALESCE(last_autovacuum,last_vacuum) IS NULL)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*) FILTER (WHERE COALESCE(last_autovacuum,last_vacuum) IS NULL)||' table(s) without vacuum timestamp',COALESCE(string_agg(schemaname||'.'||relname||' dead='||n_dead_tup, '; '),'') FROM pg_stat_user_tables;"
            : @"SELECT CASE WHEN SUM(CASE WHEN COALESCE(last_autovacuum,last_vacuum) IS NULL THEN 1 ELSE 0 END)>0 THEN 'WARNING' ELSE 'OK' END,SUM(CASE WHEN COALESCE(last_autovacuum,last_vacuum) IS NULL THEN 1 ELSE 0 END)||' table(s) without vacuum timestamp',COALESCE(string_agg(schemaname||'.'||relname||' dead='||n_dead_tup, '; '),'') FROM pg_stat_user_tables;";
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.MAINT.VACUUM",AssessmentCategory.Maintenance,"Vacuum / Analyze Recency",vacuumSql));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.MAINT.DEAD",AssessmentCategory.Maintenance,"Dead Tuples",@"SELECT CASE WHEN COALESCE(MAX(CASE WHEN n_live_tup+n_dead_tup>0 THEN n_dead_tup*100.0/(n_live_tup+n_dead_tup) ELSE 0 END),0)>=20 THEN 'WARNING' ELSE 'OK' END,ROUND(COALESCE(MAX(CASE WHEN n_live_tup+n_dead_tup>0 THEN n_dead_tup*100.0/(n_live_tup+n_dead_tup) ELSE 0 END),0),1)||'% max dead tuple ratio',COALESCE(string_agg(schemaname||'.'||relname||'='||n_dead_tup, '; '),'') FROM pg_stat_user_tables;"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.INDEX.UNUSED",AssessmentCategory.Indexes,"Unused Index Candidates",@"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' unused non-unique index candidate(s)',COALESCE(string_agg(s.schemaname||'.'||s.relname||'.'||s.indexrelname, '; '),'') FROM pg_stat_user_indexes s JOIN pg_index i ON i.indexrelid=s.indexrelid WHERE s.idx_scan=0 AND NOT i.indisunique AND pg_relation_size(s.indexrelid)>10485760;"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.INDEX.INVALID",AssessmentCategory.Indexes,"Invalid Indexes",@"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' invalid index(es)',COALESCE(string_agg(n.nspname||'.'||ci.relname, '; '),'') FROM pg_index i JOIN pg_class ci ON ci.oid=i.indexrelid JOIN pg_namespace n ON n.oid=ci.relnamespace WHERE NOT i.indisvalid;"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.PERF.SEQSCAN",AssessmentCategory.Performance,"Sequential Scan Candidates",@"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' large table(s) dominated by sequential scans',COALESCE(string_agg(schemaname||'.'||relname||' seq='||seq_scan||' idx='||idx_scan, '; '),'') FROM pg_stat_user_tables WHERE pg_total_relation_size(relid)>1073741824 AND seq_scan>COALESCE(idx_scan,0)*4 AND seq_scan>100;"));
        if(v.Major>9 || (v.Major==9&&v.Minor>=4))
        {
            x.Add(await Q(c,"PG.LOG.ARCHIVER",AssessmentCategory.Logs,"WAL Archive Health",@"SELECT CASE WHEN failed_count>0 AND last_failed_time>COALESCE(last_archived_time,'epoch'::timestamp) THEN 'WARNING' ELSE 'OK' END,'archived='||archived_count||' failed='||failed_count,COALESCE(last_failed_wal,'')||' '||COALESCE(last_failed_time::text,'') FROM pg_stat_archiver;"));
            x.Add(await Q(c,"PG.HA.SLOTS",AssessmentCategory.HighAvailability,"Replication Slots",@"SELECT CASE WHEN COUNT(*) FILTER (WHERE NOT active)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' replication slot(s)',COALESCE(string_agg(slot_name||' active='||active::text, '; '),'') FROM pg_replication_slots;"));
        }
        else
        {
            x.Add(Unsupported("PG.LOG.ARCHIVER",AssessmentCategory.Logs,"WAL Archive Health",$"pg_stat_archiver assessment requires PostgreSQL 9.4+. Detected {v}."));
            x.Add(Unsupported("PG.HA.SLOTS",AssessmentCategory.HighAvailability,"Replication Slots",$"Replication slots are not available on this PostgreSQL version. Detected {v}."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Replication(c,v));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Checkpoints(c,v));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"PG.CONFIG.CORE",AssessmentCategory.Configuration,"Core Memory / WAL Settings",@"SELECT 'INFO','Core PostgreSQL settings','shared_buffers='||current_setting('shared_buffers')||'; work_mem='||current_setting('work_mem')||'; maintenance_work_mem='||current_setting('maintenance_work_mem')||'; max_connections='||current_setting('max_connections')||'; autovacuum='||current_setting('autovacuum');"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await PgStatStatements(c));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(Unavailable("PG.BACKUP.EVIDENCE",AssessmentCategory.Backup,"Backup Evidence","PostgreSQL does not expose a universal authoritative last-backup record. Integrate pgBackRest/Barman/filesystem evidence rather than infer recoverability."));
        cancellationToken.ThrowIfCancellationRequested();
        return x;
    }

    private static async Task<AssessmentCheck> Blocking(NpgsqlConnection c,Version v)
    {
        var sql=v.Major>=10 || (v.Major==9&&v.Minor>=6)
            ? @"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' session(s) waiting on Lock',COALESCE(string_agg('pid='||pid||' '||COALESCE(wait_event,''), '; '),'') FROM pg_stat_activity WHERE wait_event_type='Lock';"
            : @"SELECT CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,COUNT(*)||' session(s) waiting on lock',COALESCE(string_agg('pid='||pid, '; '),'') FROM pg_stat_activity WHERE waiting;";
        return await Q(c,"PG.PERF.BLOCKING",AssessmentCategory.Performance,"Blocking / Lock Waits",sql);
    }

    private static async Task<AssessmentCheck> Replication(NpgsqlConnection c,Version v)
    {
        try
        {
            await using var q=new NpgsqlCommand("SELECT pg_is_in_recovery()",c);var standby=Convert.ToBoolean(await q.ExecuteScalarAsync());
            if(standby)
            {
                var sql=v.Major>=10
                    ? "SELECT 'INFO','Standby / recovery','replay_lsn='||COALESCE(pg_last_wal_replay_lsn()::text,'')"
                    : "SELECT 'INFO','Standby / recovery','replay_location='||COALESCE(pg_last_xlog_replay_location()::text,'')";
                return await Q(c,"PG.HA.REPLICATION",AssessmentCategory.HighAvailability,"Streaming Replication",sql);
            }
            return await Q(c,"PG.HA.REPLICATION",AssessmentCategory.HighAvailability,"Streaming Replication",@"SELECT CASE WHEN COUNT(*)>0 THEN 'OK' ELSE 'INFO' END,COUNT(*)||' streaming replica(s)',COALESCE(string_agg(COALESCE(client_addr::text,'local')||' state='||COALESCE(state,''), '; '),'') FROM pg_stat_replication;");
        }catch(Exception ex){return Error("PG.HA.REPLICATION",AssessmentCategory.HighAvailability,"Streaming Replication",ex);}
    }

    private static Task<AssessmentCheck> Checkpoints(NpgsqlConnection c,Version v)
    {
        var sql=v.Major>=17
            ? @"SELECT 'INFO','Checkpoint activity','timed='||num_timed||'; requested='||num_requested||'; write_ms='||write_time||'; sync_ms='||sync_time FROM pg_stat_checkpointer;"
            : @"SELECT 'INFO','Checkpoint activity','timed='||checkpoints_timed||'; requested='||checkpoints_req||'; write_ms='||checkpoint_write_time||'; sync_ms='||checkpoint_sync_time FROM pg_stat_bgwriter;";
        return Q(c,"PG.PERF.CHECKPOINTS",AssessmentCategory.Performance,"Checkpoint Activity",sql);
    }

    private static async Task<AssessmentCheck> PgStatStatements(NpgsqlConnection c)
    {
        try
        {
            await using var q=new NpgsqlCommand("SELECT COUNT(*) FROM pg_extension WHERE extname='pg_stat_statements'",c);
            var installed=Convert.ToInt32(await q.ExecuteScalarAsync())>0;
            return installed
                ? Info("PG.PERF.PGSS",AssessmentCategory.Performance,"pg_stat_statements","Extension installed","Detailed SQL workload assessment can use pg_stat_statements.","AVAILABLE")
                : new AssessmentCheck{CheckId="PG.PERF.PGSS",Engine=DatabaseEngine.PostgreSql,Category=AssessmentCategory.Performance,Title="pg_stat_statements",Status="NOT ENABLED",Severity=1,Summary="Extension not installed",Evidence="pg_stat_statements is not present in pg_extension.",WhyItMatters="Without it, historical SQL-level workload analysis is limited.",RecommendedAction="Enable only if operational policy permits; DBACHECK does not enable extensions automatically.",Verification="Re-run the assessment after extension deployment.",Capability="NOT ENABLED",ReadOnly=true};
        }catch(Exception ex){return Error("PG.PERF.PGSS",AssessmentCategory.Performance,"pg_stat_statements",ex);}
    }

    private static async Task<AssessmentCheck> Q(NpgsqlConnection c,string id,AssessmentCategory category,string title,string sql)
    {
        var started=DateTime.Now;
        try
        {
            await using var q=new NpgsqlCommand(sql,c){CommandTimeout=30};await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();
            var status=Convert.ToString(r.GetValue(0))??"INFO";
            return new(){CheckId=id,Engine=DatabaseEngine.PostgreSql,Category=category,Title=title,Status=status,Severity=Severity(status),Summary=Convert.ToString(r.GetValue(1))??"",Evidence=Convert.ToString(r.GetValue(2))??"",WhyItMatters=Why(category),RecommendedAction=Action(category,status),Verification=Verify(category),Capability="AVAILABLE",ReadOnly=true,DurationMs=(long)(DateTime.Now-started).TotalMilliseconds,Timestamp=DateTime.Now};
        }
        catch(PostgresException ex) when(ex.SqlState=="42501"){return NoPermission(id,category,title,ex.MessageText);}
        catch(PostgresException ex) when(ex.SqlState is "42703" or "42883" or "42P01"){return Unsupported(id,category,title,ex.MessageText);}
        catch(Exception ex){return Error(id,category,title,ex);}
    }

    private static AssessmentCheck Info(string id,AssessmentCategory c,string title,string summary,string evidence,string capability="AVAILABLE")=>new(){CheckId=id,Engine=DatabaseEngine.PostgreSql,Category=c,Title=title,Status="INFO",Severity=1,Summary=summary,Evidence=evidence,WhyItMatters=Why(c),RecommendedAction="Keep as baseline evidence.",Verification="Compare with future assessments.",Capability=capability,ReadOnly=true};
    private static AssessmentCheck Unsupported(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.PostgreSql,Category=c,Title=title,Status="UNSUPPORTED",Severity=1,Summary="Not supported by detected PostgreSQL version",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="No database change required. Use the capability available for this server version.",Verification="Re-run after an engine upgrade if applicable.",Capability="UNSUPPORTED",ReadOnly=true};
    private static AssessmentCheck Unavailable(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.PostgreSql,Category=c,Title=title,Status="UNAVAILABLE",Severity=1,Summary="Authoritative evidence is external or unavailable",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="Integrate the authoritative external source instead of inferring a result.",Verification="Validate against the configured backup/monitoring source.",Capability="UNAVAILABLE",ReadOnly=true};
    private static AssessmentCheck NoPermission(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.PostgreSql,Category=c,Title=title,Status="NO PERMISSION",Severity=2,Summary="Insufficient privileges",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="Grant only the minimum read privilege required, or document the limitation.",Verification="Re-run the check.",Capability="NO PERMISSION",ReadOnly=true};
    private static AssessmentCheck Error(string id,AssessmentCategory c,string title,Exception ex)=>new(){CheckId=id,Engine=DatabaseEngine.PostgreSql,Category=c,Title=title,Status="ERROR",Severity=4,Summary="Assessment check failed",Evidence=ex.Message,WhyItMatters=Why(c),RecommendedAction="Review version compatibility and permissions; do not change production only to satisfy the assessment.",Verification="Re-run after correcting the collector limitation.",Capability="ERROR",ReadOnly=true};
    private static int Severity(string s)=>s switch{"CRITICAL"=>5,"ERROR"=>4,"WARNING"=>3,"NO PERMISSION"=>2,_=>s=="OK"?0:1};
    private static string Why(AssessmentCategory c)=>c switch{AssessmentCategory.Indexes=>"Index usage and validity affect read cost, write overhead and plan quality.",AssessmentCategory.Maintenance=>"Vacuum/analyze and transaction ID health are core PostgreSQL maintenance concerns.",AssessmentCategory.Logs=>"WAL/archive health affects recovery, replication and disk retention.",AssessmentCategory.HighAvailability=>"Replication state and slots directly affect resilience and WAL retention.",AssessmentCategory.Transactions=>"Long or idle transactions can retain locks, tuples and WAL.",AssessmentCategory.Performance=>"Activity statistics expose current workload pressure and inefficient access patterns.",AssessmentCategory.Capacity=>"Capacity growth can lead to write failures or emergency storage work.",_=>"This check contributes to the PostgreSQL operational baseline."};
    private static string Action(AssessmentCategory c,string status)=>status=="OK"?"No immediate action. Keep as baseline evidence.":c switch{AssessmentCategory.Indexes=>"Validate workload evidence before creating or dropping indexes.",AssessmentCategory.Maintenance=>"Review autovacuum/analyze behavior, dead tuples and XID age before changing thresholds.",AssessmentCategory.Transactions=>"Identify session owner and application before terminating any transaction.",AssessmentCategory.Logs=>"Review archive/slot/replication retention before deleting WAL or changing capacity.",AssessmentCategory.Performance=>"Correlate with active SQL and application workload before tuning.",_=>"Review the evidence with the corresponding DBACHECK diagnostic module."};
    private static string Verify(AssessmentCategory c)=>c switch{AssessmentCategory.Transactions=>"Confirm the transaction/session is gone and retained resources are released.",AssessmentCategory.Logs=>"Confirm WAL/archive/retention returns to a healthy state.",AssessmentCategory.HighAvailability=>"Confirm replica/slot state and lag are healthy.",_=>"Re-run this assessment check and compare evidence."};
}
