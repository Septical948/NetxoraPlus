using Microsoft.Data.SqlClient;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

/// <summary>
/// Version-aware read-only collectors for SQL Server 2008 R2+.
/// Keeps legacy syntax/DMVs away from collectors that require newer engines.
/// </summary>
public sealed class SqlCompatibilityCollectorService
{
    private readonly string _connectionString;
    public SqlCompatibilityCollectorService(string server, bool trustServerCertificate)
    {
        _connectionString = new SqlConnectionStringBuilder {
            DataSource=server, InitialCatalog="master", IntegratedSecurity=true,
            Encrypt=true, TrustServerCertificate=trustServerCertificate,
            ConnectTimeout=8, ApplicationName="DBACHECK2"
        }.ConnectionString;
    }

    public Task<SqlServerCapabilities> DetectAsync() => SqlServerCompatibility.DetectAsync(_connectionString);

    public async Task<List<HealthItem>> QuickCheckAsync()
    {
        var caps=await DetectAsync();
        var result=new List<HealthItem>();
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();

        var checks=new (string Area,string Sql)[] {
            ("DATABASES", @"SELECT 'DATABASES',CASE WHEN SUM(CASE WHEN state_desc<>'ONLINE' THEN 1 ELSE 0 END)>0 THEN 'CRITICAL' ELSE 'OK' END,
CAST(SUM(CASE WHEN state_desc='ONLINE' THEN 1 ELSE 0 END) AS varchar(20))+' online / '+CAST(COUNT(*) AS varchar(20))+' total',
ISNULL(STUFF((SELECT '; '+d2.name+': '+d2.state_desc FROM sys.databases d2 WHERE d2.state_desc<>'ONLINE' ORDER BY d2.name FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,''),'') FROM sys.databases;"),
            ("BLOCKING", @"SELECT 'BLOCKING',CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,CAST(COUNT(*) AS varchar(20))+' request(s) blocked',
ISNULL(STUFF((SELECT '; SPID '+CAST(r2.session_id AS varchar(12))+' <- '+CAST(r2.blocking_session_id AS varchar(12))+' '+ISNULL(r2.wait_type,'') FROM sys.dm_exec_requests r2 WHERE r2.blocking_session_id>0 ORDER BY r2.session_id FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,''),'') FROM sys.dm_exec_requests WHERE blocking_session_id>0;"),
            ("TRANSACTIONS", @"SELECT 'TRANSACTIONS',CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,CAST(COUNT(*) AS varchar(20))+' transaction(s) > 30 min',
ISNULL(STUFF((SELECT '; SPID '+CAST(st2.session_id AS varchar(12))+' '+CAST(DATEDIFF(MINUTE,at2.transaction_begin_time,GETDATE()) AS varchar(20))+' min' FROM sys.dm_tran_active_transactions at2 JOIN sys.dm_tran_session_transactions st2 ON at2.transaction_id=st2.transaction_id WHERE DATEDIFF(MINUTE,at2.transaction_begin_time,GETDATE())>30 ORDER BY st2.session_id FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,''),'') FROM sys.dm_tran_active_transactions at JOIN sys.dm_tran_session_transactions st ON at.transaction_id=st.transaction_id WHERE DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE())>30;"),
            ("TEMPDB", @";WITH f AS (SELECT SUM(CONVERT(bigint,size)) total_pages FROM tempdb.sys.database_files WHERE type=0),u AS (SELECT SUM(CONVERT(bigint,unallocated_extent_page_count)) free_pages,SUM(CONVERT(bigint,version_store_reserved_page_count)) version_pages,SUM(CONVERT(bigint,user_object_reserved_page_count)) user_pages,SUM(CONVERT(bigint,internal_object_reserved_page_count)) internal_pages FROM tempdb.sys.dm_db_file_space_usage) SELECT 'TEMPDB',CASE WHEN used_pct>=90 THEN 'CRITICAL' WHEN used_pct>=75 THEN 'WARNING' ELSE 'OK' END,CAST(CAST(used_pct AS decimal(5,1)) AS varchar(20))+'% used',CAST(CAST(total_mb AS decimal(18,1)) AS varchar(30))+' MB total; '+CAST(CAST(free_mb AS decimal(18,1)) AS varchar(30))+' MB free; version '+CAST(CAST(version_mb AS decimal(18,1)) AS varchar(30))+' MB' FROM (SELECT f.total_pages*8.0/1024 total_mb,ISNULL(u.free_pages,0)*8.0/1024 free_mb,ISNULL(u.version_pages,0)*8.0/1024 version_mb,100.0*(f.total_pages-ISNULL(u.free_pages,0))/NULLIF(f.total_pages,0) used_pct FROM f CROSS JOIN u)x;"),
            ("VERSION STORE", @"SELECT 'VERSION STORE',CASE WHEN vs_mb>=10240 THEN 'WARNING' ELSE 'OK' END,CAST(CAST(vs_mb AS decimal(18,1)) AS varchar(30))+' MB','tempdb version store' FROM (SELECT SUM(CONVERT(bigint,version_store_reserved_page_count))*8.0/1024 vs_mb FROM tempdb.sys.dm_db_file_space_usage)x;"),
            ("BACKUPS", @"SELECT 'BACKUPS',CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,CAST(COUNT(*) AS varchar(20))+' database(s) without full backup in 24h',ISNULL(STUFF((SELECT '; '+d2.name FROM sys.databases d2 WHERE d2.database_id>4 AND d2.state_desc='ONLINE' AND d2.source_database_id IS NULL AND NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b2 WHERE b2.database_name COLLATE DATABASE_DEFAULT=d2.name COLLATE DATABASE_DEFAULT AND b2.type='D' AND b2.backup_finish_date>=DATEADD(HOUR,-24,GETDATE())) ORDER BY d2.name FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,''),'') FROM sys.databases d WHERE d.database_id>4 AND d.state_desc='ONLINE' AND d.source_database_id IS NULL AND NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='D' AND b.backup_finish_date>=DATEADD(HOUR,-24,GETDATE()));"),
            ("JOBS", @"SELECT 'JOBS',CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END,CAST(COUNT(*) AS varchar(20))+' enabled job(s) failed on last run',ISNULL(STUFF((SELECT '; '+j2.name FROM msdb.dbo.sysjobs j2 JOIN msdb.dbo.sysjobservers js2 ON j2.job_id=js2.job_id WHERE j2.enabled=1 AND js2.last_run_outcome=0 ORDER BY j2.name FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,''),'') FROM msdb.dbo.sysjobs j JOIN msdb.dbo.sysjobservers js ON j.job_id=js.job_id WHERE j.enabled=1 AND js.last_run_outcome=0;"),
            ("WAITS", @"SELECT 'WAITS','INFO',ISNULL((SELECT TOP 1 wait_type FROM sys.dm_os_wait_stats WHERE wait_type NOT LIKE 'SLEEP%' AND wait_type NOT IN('BROKER_TASK_STOP','BROKER_EVENTHANDLER','XE_TIMER_EVENT','XE_DISPATCHER_WAIT','SQLTRACE_BUFFER_FLUSH','CLR_AUTO_EVENT','CLR_MANUAL_EVENT','LAZYWRITER_SLEEP') ORDER BY wait_time_ms DESC),'N/A'),'Top cumulative wait since SQL Server startup';")
        };
        foreach(var c in checks) result.Add(await ExecuteCheckAsync(cn,c.Area,c.Sql));
        result.Add(await GetLogHealthAsync(cn));
        if(caps.SupportsAvailabilityGroups) result.Add(await ExecuteCheckAsync(cn,"ALWAYSON",@"IF SERVERPROPERTY('IsHadrEnabled')=1 SELECT 'ALWAYSON',CASE WHEN EXISTS(SELECT 1 FROM sys.dm_hadr_database_replica_states WHERE is_local=1 AND synchronization_health_desc<>'HEALTHY') THEN 'WARNING' ELSE 'OK' END,CAST(COUNT(*) AS varchar(20))+' local database replica(s)',ISNULL(STUFF((SELECT '; '+DB_NAME(x.database_id)+': '+x.synchronization_state_desc+'/'+x.synchronization_health_desc FROM sys.dm_hadr_database_replica_states x WHERE x.is_local=1 FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,''),'') FROM sys.dm_hadr_database_replica_states WHERE is_local=1; ELSE SELECT 'ALWAYSON','INFO','HADR disabled','Instance is not enabled for Always On Availability Groups';"));
        else result.Add(new HealthItem{Area="ALWAYSON",Status="INFO",Summary="Not available on this SQL Server version",Detail=$"{caps.VersionLabel} does not support Availability Groups."});
        return result.OrderByDescending(x=>x.Severity).ThenBy(x=>x.Area).ToList();
    }

    private static async Task<HealthItem> ExecuteCheckAsync(SqlConnection cn,string area,string sql)
    {
        try { await using var cmd=new SqlCommand("SET NOCOUNT ON;\n"+sql,cn){CommandTimeout=30}; await using var r=await cmd.ExecuteReaderAsync(); if(!await r.ReadAsync()) return Error(area,"Collector returned no rows."); return new HealthItem{Area=Convert.ToString(r.GetValue(0))??area,Status=Convert.ToString(r.GetValue(1))??"INFO",Summary=Convert.ToString(r.GetValue(2))??"",Detail=Convert.ToString(r.GetValue(3))??""}; }
        catch(Exception ex){ return Error(area,ex.Message); }
    }
    private static HealthItem Error(string area,string message)=>new(){Area=area,Status="ERROR",Summary="Collector failed; remaining checks continued.",Detail=message};

    private static async Task<HealthItem> GetLogHealthAsync(SqlConnection cn)
    {
        try { var rows=new List<(string Db,decimal Pct)>(); await using var cmd=new SqlCommand("DBCC SQLPERF(LOGSPACE) WITH NO_INFOMSGS;",cn){CommandTimeout=30}; await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()) rows.Add((Convert.ToString(r.GetValue(0))??"",Convert.ToDecimal(r.GetValue(2)))); var max=rows.Count==0?0:rows.Max(x=>x.Pct); var detail=string.Join("; ",rows.Where(x=>x.Pct>=75).Select(x=>$"{x.Db} {x.Pct:0.0}%")); return new HealthItem{Area="LOG",Status=max>=90?"CRITICAL":max>=75?"WARNING":"OK",Summary=$"{max:0.0}% max used",Detail=detail}; }
        catch(Exception ex){ return Error("LOG",ex.Message); }
    }

    public async Task<List<TransactionIncident>> GetLongTransactionsAsync(int minimumMinutes=30)
    {
        var items=new List<TransactionIncident>(); await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT DISTINCT CAST(s.session_id AS int),CAST(DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE()) AS bigint),CAST(ISNULL(DB_NAME(r.database_id),ISNULL(DB_NAME(s.database_id),'')) AS nvarchar(128)),CAST(ISNULL(s.login_name,'') AS nvarchar(128)),CAST(ISNULL(s.host_name,'') AS nvarchar(128)),CAST(ISNULL(s.program_name,'') AS nvarchar(128)),CAST(ISNULL(s.status,'') AS nvarchar(30)),CAST(s.open_transaction_count AS int),CAST(ISNULL(r.blocking_session_id,0) AS int),CAST(ISNULL(r.wait_type,'') AS nvarchar(120)),CAST(at.transaction_begin_time AS datetime),CAST(ISNULL(r.cpu_time,0) AS bigint),CAST(ISNULL(r.reads,0) AS bigint),CAST(ISNULL(r.writes,0) AS bigint),CAST(ISNULL(txt.text,'') AS nvarchar(max)) FROM sys.dm_tran_active_transactions at JOIN sys.dm_tran_session_transactions st ON at.transaction_id=st.transaction_id JOIN sys.dm_exec_sessions s ON st.session_id=s.session_id LEFT JOIN sys.dm_exec_requests r ON s.session_id=r.session_id LEFT JOIN sys.dm_exec_connections c ON s.session_id=c.session_id OUTER APPLY sys.dm_exec_sql_text(ISNULL(r.sql_handle,c.most_recent_sql_handle)) txt WHERE DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE())>=@minutes AND s.session_id<>@@SPID ORDER BY 2 DESC,1;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=120}; cmd.Parameters.AddWithValue("@minutes",minimumMinutes); await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()) items.Add(new TransactionIncident{SessionId=Convert.ToInt32(r.GetValue(0)),Minutes=Convert.ToInt64(r.GetValue(1)),DatabaseName=Convert.ToString(r.GetValue(2))??"",LoginName=Convert.ToString(r.GetValue(3))??"",HostName=Convert.ToString(r.GetValue(4))??"",ProgramName=Convert.ToString(r.GetValue(5))??"",SessionStatus=Convert.ToString(r.GetValue(6))??"",OpenTransactions=Convert.ToInt32(r.GetValue(7)),BlockingSessionId=Convert.ToInt32(r.GetValue(8)),WaitType=Convert.ToString(r.GetValue(9))??"",TransactionBeginTime=Convert.ToDateTime(r.GetValue(10)),CpuMs=Convert.ToInt64(r.GetValue(11)),Reads=Convert.ToInt64(r.GetValue(12)),Writes=Convert.ToInt64(r.GetValue(13)),SqlText=Convert.ToString(r.GetValue(14))??""}); return items;
    }

    public async Task<List<BlockingIncident>> GetBlockingIncidentsAsync()
    {
        var items=new List<BlockingIncident>(); await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@";WITH blocked AS(SELECT r.session_id,r.blocking_session_id FROM sys.dm_exec_requests r WHERE r.blocking_session_id>0),ids AS(SELECT session_id FROM blocked UNION SELECT blocking_session_id FROM blocked) SELECT CAST(s.session_id AS int),CAST(ISNULL(r.blocking_session_id,0) AS int),CAST(ISNULL(r.wait_time,0)/1000 AS int),CAST(ISNULL(DB_NAME(r.database_id),ISNULL(DB_NAME(s.database_id),'')) AS nvarchar(128)),CAST(ISNULL(s.login_name,'') AS nvarchar(128)),CAST(ISNULL(s.host_name,'') AS nvarchar(128)),CAST(ISNULL(s.program_name,'') AS nvarchar(256)),CAST(ISNULL(s.status,'') AS nvarchar(30)),CAST(ISNULL(r.wait_type,'') AS nvarchar(120)),CAST(ISNULL(r.command,'') AS nvarchar(64)),CAST(s.open_transaction_count AS int),CAST(ISNULL(r.cpu_time,0) AS bigint),CAST(ISNULL(r.reads,0) AS bigint),CAST(ISNULL(r.writes,0) AS bigint),CAST(ISNULL(txt.text,'') AS nvarchar(max)) FROM ids i JOIN sys.dm_exec_sessions s ON s.session_id=i.session_id LEFT JOIN sys.dm_exec_requests r ON r.session_id=s.session_id LEFT JOIN sys.dm_exec_connections c ON s.session_id=c.session_id OUTER APPLY sys.dm_exec_sql_text(ISNULL(r.sql_handle,c.most_recent_sql_handle)) txt ORDER BY s.session_id;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30}; await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()) items.Add(new BlockingIncident{SessionId=Convert.ToInt32(r.GetValue(0)),BlockingSessionId=Convert.ToInt32(r.GetValue(1)),WaitSeconds=Convert.ToInt32(r.GetValue(2)),DatabaseName=Convert.ToString(r.GetValue(3))??"",LoginName=Convert.ToString(r.GetValue(4))??"",HostName=Convert.ToString(r.GetValue(5))??"",ProgramName=Convert.ToString(r.GetValue(6))??"",SessionStatus=Convert.ToString(r.GetValue(7))??"",WaitType=Convert.ToString(r.GetValue(8))??"",Command=Convert.ToString(r.GetValue(9))??"",OpenTransactions=Convert.ToInt32(r.GetValue(10)),CpuMs=Convert.ToInt64(r.GetValue(11)),Reads=Convert.ToInt64(r.GetValue(12)),Writes=Convert.ToInt64(r.GetValue(13)),SqlText=Convert.ToString(r.GetValue(14))??""}); var byId=items.ToDictionary(x=>x.SessionId); foreach(var x in items){var cur=x;var seen=new HashSet<int>();while(cur.BlockingSessionId>0&&seen.Add(cur.SessionId)&&byId.TryGetValue(cur.BlockingSessionId,out var parent))cur=parent;x.RootBlockerId=cur.SessionId;} return items.OrderBy(x=>x.RootBlockerId).ThenBy(x=>x.IsRootBlocker?0:1).ThenByDescending(x=>x.WaitSeconds).ToList();
    }

    public async Task<List<PerformanceRequest>> GetPerformanceRequestsAsync()
    {
        var items=new List<PerformanceRequest>(); await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT TOP(100) CAST(r.session_id AS int),CAST(r.total_elapsed_time/1000 AS bigint),CAST(ISNULL(DB_NAME(r.database_id),'') AS nvarchar(128)),CAST(ISNULL(s.login_name,'') AS nvarchar(128)),CAST(ISNULL(s.host_name,'') AS nvarchar(128)),CAST(ISNULL(s.program_name,'') AS nvarchar(256)),CAST(ISNULL(r.status,'') AS nvarchar(30)),CAST(ISNULL(r.command,'') AS nvarchar(64)),CAST(ISNULL(r.cpu_time,0) AS bigint),CAST(ISNULL(r.reads,0) AS bigint),CAST(ISNULL(r.writes,0) AS bigint),CAST(ISNULL(r.logical_reads,0) AS bigint),CAST(ISNULL(r.wait_type,'') AS nvarchar(120)),CAST(ISNULL(r.wait_time,0) AS bigint),CAST(ISNULL(r.blocking_session_id,0) AS int),CAST(ISNULL(s.open_transaction_count,0) AS int),CAST(ISNULL(r.granted_query_memory,0)*8 AS bigint),CAST(ISNULL(t.text,'') AS nvarchar(max)) FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON r.session_id=s.session_id OUTER APPLY sys.dm_exec_sql_text(r.sql_handle)t WHERE r.session_id<>@@SPID AND s.is_user_process=1 ORDER BY r.cpu_time DESC,r.logical_reads DESC,r.total_elapsed_time DESC;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=15}; await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()){var elapsed=Convert.ToInt64(r.GetValue(1));var cpu=Convert.ToInt64(r.GetValue(8));var logical=Convert.ToInt64(r.GetValue(11));var wait=Convert.ToString(r.GetValue(12))??"";var waitMs=Convert.ToInt64(r.GetValue(13));var blocked=Convert.ToInt32(r.GetValue(14));var status=blocked>0||wait.StartsWith("LCK_",StringComparison.OrdinalIgnoreCase)?"CRITICAL":elapsed>=300||cpu>=30000||logical>=1000000||waitMs>=30000?"WARNING":"OK";items.Add(new PerformanceRequest{Status=status,SessionId=Convert.ToInt32(r.GetValue(0)),ElapsedSeconds=elapsed,DatabaseName=Convert.ToString(r.GetValue(2))??"",LoginName=Convert.ToString(r.GetValue(3))??"",HostName=Convert.ToString(r.GetValue(4))??"",ProgramName=Convert.ToString(r.GetValue(5))??"",RequestStatus=Convert.ToString(r.GetValue(6))??"",Command=Convert.ToString(r.GetValue(7))??"",CpuMs=cpu,Reads=Convert.ToInt64(r.GetValue(9)),Writes=Convert.ToInt64(r.GetValue(10)),LogicalReads=logical,WaitType=wait,WaitMs=waitMs,BlockingSessionId=blocked,OpenTransactions=Convert.ToInt32(r.GetValue(15)),GrantedQueryMemoryKb=Convert.ToInt64(r.GetValue(16)),SqlText=Convert.ToString(r.GetValue(17))??""});} return items;
    }
}