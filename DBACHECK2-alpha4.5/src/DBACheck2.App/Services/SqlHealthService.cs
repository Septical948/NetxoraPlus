using Microsoft.Data.SqlClient;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class SqlHealthService
{
    private readonly string _connectionString;

    public SqlHealthService(string server, bool trustServerCertificate)
    {
        var cs = new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = "master", IntegratedSecurity = true,
            Encrypt = true, TrustServerCertificate = trustServerCertificate,
            ConnectTimeout = 8, ApplicationName = "DBACHECK2"
        };
        _connectionString = cs.ConnectionString;
    }

    public async Task<string> TestAsync()
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128));", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return $"{r.GetString(0)} | SQL {r.GetString(1)} | {r.GetString(2)}";
    }

    public async Task<List<HealthItem>> QuickCheckAsync()
    {
        var result = new List<HealthItem>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        var checks = new (string Area, string Sql)[]
        {
            ("DATABASES", @"SELECT 'DATABASES', CASE WHEN SUM(CASE WHEN state_desc <> 'ONLINE' THEN 1 ELSE 0 END)>0 THEN 'CRITICAL' ELSE 'OK' END, CONCAT(SUM(CASE WHEN state_desc='ONLINE' THEN 1 ELSE 0 END),' online / ',COUNT(*),' total'), COALESCE(STRING_AGG((CASE WHEN state_desc<>'ONLINE' THEN CONCAT(name,': ',state_desc) END) COLLATE DATABASE_DEFAULT,'; '),'') FROM sys.databases;"),

            ("BLOCKING", @"SELECT 'BLOCKING', CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END, CONCAT(COUNT(*),' request(s) blocked'), COALESCE(STRING_AGG(CONCAT('SPID ',session_id,' <- ',blocking_session_id,' ',COALESCE(wait_type,'')) COLLATE DATABASE_DEFAULT,'; '),'') FROM sys.dm_exec_requests WHERE blocking_session_id>0;"),

            ("TRANSACTIONS", @"SELECT 'TRANSACTIONS', CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END, CONCAT(COUNT(*),' transaction(s) > 30 min'), COALESCE(STRING_AGG(CONCAT('SPID ',st.session_id,' ',DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE()),' min') COLLATE DATABASE_DEFAULT,'; '),'') FROM sys.dm_tran_active_transactions at JOIN sys.dm_tran_session_transactions st ON at.transaction_id=st.transaction_id WHERE DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE())>30;"),

            ("TEMPDB", @";WITH f AS (SELECT SUM(CONVERT(bigint,size)) total_pages FROM tempdb.sys.database_files WHERE type=0), u AS (SELECT SUM(CONVERT(bigint,unallocated_extent_page_count)) free_pages, SUM(CONVERT(bigint,version_store_reserved_page_count)) version_pages, SUM(CONVERT(bigint,user_object_reserved_page_count)) user_pages, SUM(CONVERT(bigint,internal_object_reserved_page_count)) internal_pages FROM tempdb.sys.dm_db_file_space_usage) SELECT 'TEMPDB', CASE WHEN used_pct>=90 THEN 'CRITICAL' WHEN used_pct>=75 THEN 'WARNING' ELSE 'OK' END, CONCAT(CAST(used_pct AS decimal(5,1)),'% used'), CONCAT(CAST(total_mb AS decimal(18,1)),' MB total; ',CAST(free_mb AS decimal(18,1)),' MB free; version ',CAST(version_mb AS decimal(18,1)),' MB; user ',CAST(user_mb AS decimal(18,1)),' MB; internal ',CAST(internal_mb AS decimal(18,1)),' MB') FROM (SELECT f.total_pages*8.0/1024 total_mb, ISNULL(u.free_pages,0)*8.0/1024 free_mb, ISNULL(u.version_pages,0)*8.0/1024 version_mb, ISNULL(u.user_pages,0)*8.0/1024 user_mb, ISNULL(u.internal_pages,0)*8.0/1024 internal_mb, 100.0*(f.total_pages-ISNULL(u.free_pages,0))/NULLIF(f.total_pages,0) used_pct FROM f CROSS JOIN u) x;"),

            ("VERSION STORE", @"SELECT 'VERSION STORE', CASE WHEN vs_mb>=10240 THEN 'WARNING' ELSE 'OK' END, CONCAT(CAST(vs_mb AS decimal(18,1)),' MB'), 'tempdb version store' FROM (SELECT SUM(CONVERT(bigint,version_store_reserved_page_count))*8.0/1024 vs_mb FROM tempdb.sys.dm_db_file_space_usage) x;"),

            ("LOG", @"SELECT 'LOG', CASE WHEN MAX(used_pct)>=90 THEN 'CRITICAL' WHEN MAX(used_pct)>=75 THEN 'WARNING' ELSE 'OK' END, CONCAT(CAST(MAX(used_pct) AS decimal(5,1)),'% max used'), COALESCE(STRING_AGG((CASE WHEN used_pct>=75 THEN CONCAT(dbname,' ',CAST(used_pct AS decimal(5,1)),'%') END) COLLATE DATABASE_DEFAULT,'; '),'') FROM (SELECT DB_NAME(database_id) dbname, used_log_space_in_percent used_pct FROM sys.dm_db_log_space_usage) l;"),

            ("BACKUPS", @"SELECT 'BACKUPS', CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END, CONCAT(COUNT(*),' database(s) without full backup in 24h'), COALESCE(STRING_AGG(d.name COLLATE DATABASE_DEFAULT,'; '),'') FROM sys.databases d WHERE d.database_id>4 AND d.state_desc='ONLINE' AND d.source_database_id IS NULL AND NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='D' AND b.backup_finish_date>=DATEADD(HOUR,-24,GETDATE()));"),

            ("JOBS", @"SELECT 'JOBS', CASE WHEN COUNT(*)>0 THEN 'WARNING' ELSE 'OK' END, CONCAT(COUNT(*),' enabled job(s) failed on last run'), COALESCE(STRING_AGG(j.name COLLATE DATABASE_DEFAULT,'; '),'') FROM msdb.dbo.sysjobs j JOIN msdb.dbo.sysjobservers js ON j.job_id=js.job_id WHERE j.enabled=1 AND js.last_run_outcome=0;"),

            ("WAITS", @"SELECT 'WAITS','INFO',COALESCE((SELECT TOP 1 wait_type FROM sys.dm_os_wait_stats WHERE wait_type NOT LIKE 'SLEEP%' AND wait_type NOT IN ('BROKER_TASK_STOP','BROKER_EVENTHANDLER','XE_TIMER_EVENT','XE_DISPATCHER_WAIT','SQLTRACE_BUFFER_FLUSH','CLR_AUTO_EVENT','CLR_MANUAL_EVENT','LAZYWRITER_SLEEP','SOS_WORK_DISPATCHER') ORDER BY wait_time_ms DESC),'N/A'),'Top cumulative wait since SQL Server startup';"),

            ("ALWAYSON", @"IF SERVERPROPERTY('IsHadrEnabled')=1 SELECT 'ALWAYSON', CASE WHEN EXISTS(SELECT 1 FROM sys.dm_hadr_database_replica_states WHERE is_local=1 AND synchronization_health_desc<>'HEALTHY') THEN 'WARNING' ELSE 'OK' END, CONCAT(COUNT(*),' local database replica(s)'), COALESCE(STRING_AGG(CONCAT(DB_NAME(database_id),': ',synchronization_state_desc,'/',synchronization_health_desc) COLLATE DATABASE_DEFAULT,'; '),'') FROM sys.dm_hadr_database_replica_states WHERE is_local=1; ELSE SELECT 'ALWAYSON','INFO','HADR disabled','Instance is not enabled for Always On Availability Groups';")
        };

        foreach (var check in checks)
            result.Add(await ExecuteCheckAsync(cn, check.Area, check.Sql));

        return result.OrderByDescending(x => x.Severity).ThenBy(x => x.Area).ToList();
    }

    private static async Task<HealthItem> ExecuteCheckAsync(SqlConnection cn, string area, string sql)
    {
        try
        {
            await using var cmd = new SqlCommand("SET NOCOUNT ON;\n"+sql, cn) { CommandTimeout=30 };
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Error(area,"Collector returned no rows.");
            return new HealthItem {
                Area=r.IsDBNull(0)?area:Convert.ToString(r.GetValue(0))??area,
                Status=r.IsDBNull(1)?"INFO":Convert.ToString(r.GetValue(1))??"INFO",
                Summary=r.IsDBNull(2)?"":Convert.ToString(r.GetValue(2))??"",
                Detail=r.IsDBNull(3)?"":Convert.ToString(r.GetValue(3))??""
            };
        }
        catch(Exception ex) { return Error(area,ex.Message); }
    }

    private static HealthItem Error(string area,string message) => new() {
        Area=area, Status="ERROR", Summary="Collector failed; remaining checks continued.", Detail=message
    };
    public async Task<List<TransactionIncident>> GetLongTransactionsAsync(int minimumMinutes = 30)
    {
        var items = new List<TransactionIncident>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = @"
SELECT DISTINCT
    CAST(s.session_id AS int) AS session_id,
    CAST(DATEDIFF(MINUTE, at.transaction_begin_time, GETDATE()) AS bigint) AS minutes_open,
    CAST(COALESCE(DB_NAME(r.database_id), DB_NAME(s.database_id), '') AS nvarchar(128)) AS database_name,
    CAST(COALESCE(s.login_name,'') AS nvarchar(128)) AS login_name,
    CAST(COALESCE(s.host_name,'') AS nvarchar(128)) AS host_name,
    CAST(COALESCE(s.program_name,'') AS nvarchar(128)) AS program_name,
    CAST(COALESCE(s.status,'') AS nvarchar(30)) AS session_status,
    CAST(s.open_transaction_count AS int) AS open_transaction_count,
    CAST(COALESCE(r.blocking_session_id,0) AS int) AS blocking_session_id,
    CAST(COALESCE(r.wait_type,'') AS nvarchar(120)) AS wait_type,
    CAST(at.transaction_begin_time AS datetime2) AS transaction_begin_time,
    CAST(COALESCE(r.cpu_time,0) AS bigint) AS cpu_ms,
    CAST(COALESCE(r.reads,0) AS bigint) AS reads,
    CAST(COALESCE(r.writes,0) AS bigint) AS writes,
    CAST(COALESCE(ib.event_info,'') AS nvarchar(max)) AS sql_text
FROM sys.dm_tran_active_transactions at
JOIN sys.dm_tran_session_transactions st ON at.transaction_id=st.transaction_id
JOIN sys.dm_exec_sessions s ON st.session_id=s.session_id
LEFT JOIN sys.dm_exec_requests r ON s.session_id=r.session_id
OUTER APPLY sys.dm_exec_input_buffer(s.session_id,NULL) ib
WHERE DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE()) >= @minutes
  AND s.session_id <> @@SPID
ORDER BY minutes_open DESC, session_id;";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@minutes", minimumMinutes);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            items.Add(new TransactionIncident {
                SessionId = Convert.ToInt32(r.GetValue(0)), Minutes = Convert.ToInt64(r.GetValue(1)),
                DatabaseName = Convert.ToString(r.GetValue(2)) ?? "", LoginName = Convert.ToString(r.GetValue(3)) ?? "",
                HostName = Convert.ToString(r.GetValue(4)) ?? "", ProgramName = Convert.ToString(r.GetValue(5)) ?? "",
                SessionStatus = Convert.ToString(r.GetValue(6)) ?? "", OpenTransactions = Convert.ToInt32(r.GetValue(7)),
                BlockingSessionId = Convert.ToInt32(r.GetValue(8)), WaitType = Convert.ToString(r.GetValue(9)) ?? "",
                TransactionBeginTime = Convert.ToDateTime(r.GetValue(10)), CpuMs = Convert.ToInt64(r.GetValue(11)),
                Reads = Convert.ToInt64(r.GetValue(12)), Writes = Convert.ToInt64(r.GetValue(13)),
                SqlText = Convert.ToString(r.GetValue(14)) ?? ""
            });
        }
        return items;
    }

    public async Task<string> GetTransactionDiagnosticsAsync(TransactionIncident x)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = @"
SELECT
  (SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id=@spid) AS lock_count,
  (SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id=@spid AND request_status='WAIT') AS waiting_locks,
  COALESCE((SELECT log_reuse_wait_desc FROM sys.databases WHERE name=@db),'UNKNOWN') AS log_reuse_wait,
  COALESCE((SELECT CAST(SUM(size)*8.0/1024 AS decimal(18,1)) FROM sys.master_files WHERE database_id=DB_ID(@db) AND type=1),0) AS log_total_mb,
  COALESCE((SELECT CAST(SUM(version_store_reserved_page_count)*8.0/1024 AS decimal(18,1)) FROM tempdb.sys.dm_db_file_space_usage),0) AS version_store_mb;";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@spid", x.SessionId);
        cmd.Parameters.AddWithValue("@db", x.DatabaseName);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        var lockCount = Convert.ToInt32(r.GetValue(0));
        var waitingLocks = Convert.ToInt32(r.GetValue(1));
        var reuse = Convert.ToString(r.GetValue(2)) ?? "UNKNOWN";
        var totalLog = Convert.ToDecimal(r.GetValue(3));
        var version = Convert.ToDecimal(r.GetValue(4));
        var sleepingOpen = x.SessionStatus.Equals("sleeping", StringComparison.OrdinalIgnoreCase) && x.OpenTransactions > 0;
        var blocking = x.BlockingSessionId == 0 ? "NO" : "YES - blocked by " + x.BlockingSessionId;
        var logImpact = reuse.Equals("ACTIVE_TRANSACTION", StringComparison.OrdinalIgnoreCase)
            ? "POSSIBLE - database reports ACTIVE_TRANSACTION. Correlate before attributing it to this SPID."
            : "NOT DETECTED - current database log reuse wait is " + reuse + ".";
        var lockImpact = waitingLocks > 0 || x.BlockingSessionId > 0 ? "CURRENT CONTENTION DETECTED" : "NO CURRENT WAITING LOCKS DETECTED";

        return $@"DIAGNOSTICO OPERATIVO - SPID {x.SessionId}
Classification: {(sleepingOpen ? "WARNING - SLEEPING WITH OPEN TRANSACTION" : "Long-running open transaction")}
Duration: {x.Minutes} min ({x.Minutes/60}h {x.Minutes%60}m)
Database: {x.DatabaseName}
Login / Host / App: {x.LoginName} / {x.HostName} / {x.ProgramName}
Blocking now: {blocking}
Locks owned/requested: {lockCount}
Waiting locks: {waitingLocks}
Database LOG size: {totalLog} MB
LOG reuse wait: {reuse}
TempDB Version Store GLOBAL: {version} MB
Version Store impact attributable to SPID: NOT DETERMINED

IMPACTO ACTUAL
- Blocking: {(x.BlockingSessionId == 0 ? "No detectado." : "Detectado.")}
- Lock contention: {lockImpact}.
- LOG truncation: {logImpact}
- Version Store: Global value shown for context only; no causal attribution to this SPID.

ACCION RECOMENDADA
1. Confirmar aplicaciÃ³n/usuario propietario de la transacciÃ³n.
2. Revisar SQL/input buffer y objetos bloqueados antes de una acciÃ³n correctiva.
3. Si la sesiÃ³n es abandonada, capturar Evidence Snapshot.
4. Evaluar KILL solamente con aprobaciÃ³n explÃ­cita y revalidaciÃ³n de identidad.

No corrective action was executed.";
    }

    public async Task<string> GetTransactionLocksAsync(TransactionIncident x)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = @"
SELECT request_mode, request_status, resource_type,
       COALESCE(DB_NAME(resource_database_id),'') AS database_name,
       COALESCE(CONVERT(nvarchar(100),resource_associated_entity_id),'') AS resource_id
FROM sys.dm_tran_locks
WHERE request_session_id=@spid
ORDER BY request_status DESC, resource_type, request_mode;";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@spid", x.SessionId);
        await using var r = await cmd.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await r.ReadAsync())
            lines.Add($"{Convert.ToString(r.GetValue(0)),-6} | {Convert.ToString(r.GetValue(1)),-8} | {Convert.ToString(r.GetValue(2)),-12} | {Convert.ToString(r.GetValue(3)),-20} | resource {Convert.ToString(r.GetValue(4))}");
        return $@"LOCKS - SPID {x.SessionId}
Database: {x.DatabaseName}

MODE   | STATUS   | RESOURCE     | DATABASE             | DETAIL
{new string('-',82)}
{(lines.Count == 0 ? "No locks currently reported for this SPID." : string.Join(Environment.NewLine, lines))}

Read-only inspection. No action was executed.";
    }

    public static string GetTransactionSql(TransactionIncident x) =>
$@"SQL / INPUT BUFFER - SPID {x.SessionId}
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Transaction begin: {x.TransactionBeginTime:yyyy-MM-dd HH:mm:ss}
Duration: {x.Minutes} min ({x.Minutes/60}h {x.Minutes%60}m)

{x.SqlText}

Note: for a sleeping session this is the input buffer/last submitted batch visible to SQL Server; it is not proof that this statement opened the transaction.";

    public static string GetTransactionDetail(TransactionIncident x) =>
$@"TRANSACCION - SPID {x.SessionId}
Begin time: {x.TransactionBeginTime:yyyy-MM-dd HH:mm:ss}
Duration: {x.Minutes} min ({x.Minutes/60}h {x.Minutes%60}m)
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Session status: {x.SessionStatus}
Open transactions: {x.OpenTransactions}
Blocked by: {(x.BlockingSessionId == 0 ? "No" : x.BlockingSessionId.ToString())}
Wait: {(string.IsNullOrWhiteSpace(x.WaitType) ? "-" : x.WaitType)}
CPU current request: {x.CpuMs} ms
Reads current request: {x.Reads}
Writes current request: {x.Writes}

This view is read-only.";

    public async Task<(bool SameSession, string Message)> VerifyTransactionIdentityAsync(TransactionIncident x)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = @"
SELECT TOP (1) s.login_name, COALESCE(s.host_name,''), at.transaction_begin_time
FROM sys.dm_exec_sessions s
JOIN sys.dm_tran_session_transactions st ON s.session_id=st.session_id
JOIN sys.dm_tran_active_transactions at ON st.transaction_id=at.transaction_id
WHERE s.session_id=@spid
ORDER BY at.transaction_begin_time;";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@spid", x.SessionId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (false, "The SPID/transaction no longer exists. Nothing was executed.");
        var login = Convert.ToString(r.GetValue(0)) ?? "";
        var host = Convert.ToString(r.GetValue(1)) ?? "";
        var begin = Convert.ToDateTime(r.GetValue(2));
        var same = login == x.LoginName && host == x.HostName && Math.Abs((begin-x.TransactionBeginTime).TotalSeconds) < 2;
        return same
            ? (true, "Identity revalidated: SPID, login, host and transaction begin time still match the Evidence Snapshot.")
            : (false, "SPID identity changed since evidence capture. KILL blocked to avoid acting on a reused/different session.");
    }

    public async Task<string> KillSessionAsync(int sessionId)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"KILL {sessionId};", cn) { CommandTimeout = 30 };
        await cmd.ExecuteNonQueryAsync();
        await Task.Delay(500);
        await using var verify = new SqlCommand("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE session_id=@spid;", cn);
        verify.Parameters.AddWithValue("@spid", sessionId);
        var remains = Convert.ToInt32(await verify.ExecuteScalarAsync());
        return remains == 0
            ? $"KILL {sessionId} executed. Session is no longer present."
            : $"KILL {sessionId} executed, but the session is still present; rollback may be in progress.";
    }

    public async Task<string> GetKillStatusAsync(int sessionId)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        try
        {
            await using var cmd = new SqlCommand($"KILL {sessionId} WITH STATUSONLY;", cn) { CommandTimeout = 15 };
            var messages = new List<string>();
            cn.InfoMessage += (_, e) => messages.Add(e.Message);
            await cmd.ExecuteNonQueryAsync();
            return messages.Count == 0 ? "STATUSONLY completed; SQL Server returned no additional message." : string.Join(Environment.NewLine, messages);
        }
        catch (SqlException ex) { return ex.Message; }
    }

    public static string BuildEvidenceSnapshot(TransactionIncident x) =>
$@"EVIDENCE SNAPSHOT - DBACHECK 2 Alpha 3.3
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
SPID: {x.SessionId}
Transaction begin: {x.TransactionBeginTime:yyyy-MM-dd HH:mm:ss}
Duration: {x.Minutes} min
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Session status: {x.SessionStatus}
Open transactions: {x.OpenTransactions}
Blocking session: {(x.BlockingSessionId == 0 ? "No" : x.BlockingSessionId.ToString())}
Wait: {(string.IsNullOrWhiteSpace(x.WaitType) ? "-" : x.WaitType)}
CPU request: {x.CpuMs} ms
Reads request: {x.Reads}
Writes request: {x.Writes}

SQL / input buffer:
{x.SqlText}

Potential impact:
- Long-running open transaction.
- May prevent transaction log truncation depending on database/recovery state.
- May retain locks while the transaction remains open.
- TempDB Version Store must be correlated separately; a global value does not prove attribution to this SPID.

Safety:
Read-only evidence capture. No KILL or corrective command was executed.";


    public async Task<List<BlockingIncident>> GetBlockingIncidentsAsync()
    {
        var items = new List<BlockingIncident>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = @"
;WITH blocked AS (
    SELECT r.session_id, r.blocking_session_id
    FROM sys.dm_exec_requests r
    WHERE r.blocking_session_id > 0
), ids AS (
    SELECT session_id FROM blocked
    UNION
    SELECT blocking_session_id FROM blocked
)
SELECT
    CAST(s.session_id AS int) session_id,
    CAST(COALESCE(r.blocking_session_id,0) AS int) blocking_session_id,
    CAST(COALESCE(r.wait_time,0)/1000 AS int) wait_seconds,
    CAST(COALESCE(DB_NAME(r.database_id),DB_NAME(s.database_id),'') AS nvarchar(128)) database_name,
    CAST(COALESCE(s.login_name,'') AS nvarchar(128)) login_name,
    CAST(COALESCE(s.host_name,'') AS nvarchar(128)) host_name,
    CAST(COALESCE(s.program_name,'') AS nvarchar(256)) program_name,
    CAST(COALESCE(s.status,'') AS nvarchar(30)) session_status,
    CAST(COALESCE(r.wait_type,'') AS nvarchar(120)) wait_type,
    CAST(COALESCE(r.command,'') AS nvarchar(64)) command,
    CAST(s.open_transaction_count AS int) open_transactions,
    CAST(COALESCE(r.cpu_time,0) AS bigint) cpu_ms,
    CAST(COALESCE(r.reads,0) AS bigint) reads,
    CAST(COALESCE(r.writes,0) AS bigint) writes,
    CAST(COALESCE(ib.event_info,'') AS nvarchar(max)) sql_text
FROM ids i
JOIN sys.dm_exec_sessions s ON s.session_id=i.session_id
LEFT JOIN sys.dm_exec_requests r ON r.session_id=s.session_id
OUTER APPLY sys.dm_exec_input_buffer(s.session_id,NULL) ib
ORDER BY s.session_id;";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout=30 };
        await using var r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) items.Add(new BlockingIncident {
            SessionId=Convert.ToInt32(r.GetValue(0)), BlockingSessionId=Convert.ToInt32(r.GetValue(1)), WaitSeconds=Convert.ToInt32(r.GetValue(2)),
            DatabaseName=Convert.ToString(r.GetValue(3))??"", LoginName=Convert.ToString(r.GetValue(4))??"", HostName=Convert.ToString(r.GetValue(5))??"", ProgramName=Convert.ToString(r.GetValue(6))??"",
            SessionStatus=Convert.ToString(r.GetValue(7))??"", WaitType=Convert.ToString(r.GetValue(8))??"", Command=Convert.ToString(r.GetValue(9))??"", OpenTransactions=Convert.ToInt32(r.GetValue(10)),
            CpuMs=Convert.ToInt64(r.GetValue(11)), Reads=Convert.ToInt64(r.GetValue(12)), Writes=Convert.ToInt64(r.GetValue(13)), SqlText=Convert.ToString(r.GetValue(14))??""
        });
        var byId=items.ToDictionary(x=>x.SessionId);
        foreach(var x in items) {
            var cur=x; var seen=new HashSet<int>();
            while(cur.BlockingSessionId>0 && seen.Add(cur.SessionId) && byId.TryGetValue(cur.BlockingSessionId,out var parent)) cur=parent;
            x.RootBlockerId=cur.SessionId;
        }
        return items.OrderBy(x=>x.RootBlockerId).ThenBy(x=>x.IsRootBlocker?0:1).ThenByDescending(x=>x.WaitSeconds).ToList();
    }

    public async Task<string> GetBlockingLocksAsync(int sessionId)
    {
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT request_mode,request_status,resource_type,COALESCE(DB_NAME(resource_database_id),''),COALESCE(CONVERT(nvarchar(100),resource_associated_entity_id),'') FROM sys.dm_tran_locks WHERE request_session_id=@spid ORDER BY request_status,resource_type,request_mode;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30}; cmd.Parameters.AddWithValue("@spid",sessionId);
        await using var r=await cmd.ExecuteReaderAsync(); var lines=new List<string>{$"LOCKS - SPID {sessionId}","","MODE   | STATUS  | RESOURCE       | DATABASE             | DETAIL","--------------------------------------------------------------------------"};
        while(await r.ReadAsync()) lines.Add($"{Convert.ToString(r.GetValue(0)),-6} | {Convert.ToString(r.GetValue(1)),-7} | {Convert.ToString(r.GetValue(2)),-14} | {Convert.ToString(r.GetValue(3)),-20} | {Convert.ToString(r.GetValue(4))}");
        if(lines.Count==4) lines.Add("No locks currently visible for this SPID."); lines.Add(""); lines.Add("Read-only inspection. No action was executed."); return string.Join(Environment.NewLine,lines);
    }

    public static string BuildBlockingSummary(BlockingIncident x,List<BlockingIncident> all)
    {
        var affected=all.Count(y=>y.RootBlockerId==x.RootBlockerId && y.SessionId!=x.RootBlockerId);
        return $@"BLOCKING INCIDENT - SPID {x.SessionId}
Role: {(x.IsRootBlocker ? "ROOT BLOCKER" : "BLOCKED SESSION")}
Root blocker: {x.RootBlockerId}
Blocked by: {(x.BlockingSessionId==0?"No":x.BlockingSessionId.ToString())}
Affected sessions under root: {affected}
Wait: {(string.IsNullOrWhiteSpace(x.WaitType)?"-":x.WaitType)} | {x.WaitSeconds} sec
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Status: {x.SessionStatus}
Open transactions: {x.OpenTransactions}

Select VER CADENA to reconstruct the blocking tree, VER SQL for the input buffer, or VER LOCKS for current lock resources.";
    }

    public static string BuildBlockingChain(int rootId,List<BlockingIncident> all)
    {
        var group=all.Where(x=>x.RootBlockerId==rootId).ToList(); var byParent=group.Where(x=>x.BlockingSessionId>0).GroupBy(x=>x.BlockingSessionId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.WaitSeconds).ToList());
        var root=group.FirstOrDefault(x=>x.SessionId==rootId); if(root==null) return $"Root blocker {rootId} is no longer visible.";
        var lines=new List<string>{$"BLOCKING CHAIN - ROOT SPID {rootId}",$"{root.LoginName} | {root.HostName} | {root.ProgramName} | {root.SessionStatus} | Open Tran {root.OpenTransactions}",""};
        void Walk(int parent,string prefix,HashSet<int> seen){ if(!byParent.TryGetValue(parent,out var kids)) return; for(int i=0;i<kids.Count;i++){var k=kids[i]; if(!seen.Add(k.SessionId)) continue; var last=i==kids.Count-1; lines.Add($"{prefix}{(last?"â””â”€â”€":"â”œâ”€â”€")} SPID {k.SessionId} | {k.WaitType} | {k.WaitSeconds}s | DB {k.DatabaseName} | {k.LoginName}"); Walk(k.SessionId,prefix+(last?"    ":"â”‚   "),seen);}}
        Walk(rootId,"",new HashSet<int>{rootId}); lines.Add(""); lines.Add($"Affected sessions: {group.Count-1}"); lines.Add($"Longest current wait: {(group.Where(x=>!x.IsRootBlocker).Select(x=>x.WaitSeconds).DefaultIfEmpty(0).Max())} sec"); return string.Join(Environment.NewLine,lines);
    }

    public static string BuildBlockingEvidence(BlockingIncident x,List<BlockingIncident> all) =>
$@"EVIDENCE SNAPSHOT - DBACHECK 2 Alpha 4 - BLOCKING
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Selected SPID: {x.SessionId}
Role: {(x.IsRootBlocker?"ROOT BLOCKER":"BLOCKED SESSION")}
Root blocker: {x.RootBlockerId}
Blocked by: {(x.BlockingSessionId==0?"No":x.BlockingSessionId.ToString())}
Wait: {x.WaitType} | {x.WaitSeconds} sec
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Status: {x.SessionStatus}
Open transactions: {x.OpenTransactions}
CPU current request: {x.CpuMs} ms
Reads current request: {x.Reads}
Writes current request: {x.Writes}

{BuildBlockingChain(x.RootBlockerId,all)}

SQL / input buffer selected SPID:
{x.SqlText}

Safety:
Read-only blocking evidence capture. No KILL or corrective command was executed.";

    public async Task<TempDbSnapshot> GetTempDbSnapshotAsync()
    {
        var snap = new TempDbSnapshot();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        const string summarySql = @"
;WITH f AS (
    SELECT SUM(CONVERT(bigint,size)) total_pages
    FROM tempdb.sys.database_files WHERE type=0
), u AS (
    SELECT SUM(CONVERT(bigint,unallocated_extent_page_count)) free_pages,
           SUM(CONVERT(bigint,version_store_reserved_page_count)) version_pages,
           SUM(CONVERT(bigint,user_object_reserved_page_count)) user_pages,
           SUM(CONVERT(bigint,internal_object_reserved_page_count)) internal_pages
    FROM tempdb.sys.dm_db_file_space_usage
)
SELECT CAST(f.total_pages*8.0/1024 AS decimal(18,1)) total_mb,
       CAST((f.total_pages-ISNULL(u.free_pages,0))*8.0/1024 AS decimal(18,1)) used_mb,
       CAST(ISNULL(u.free_pages,0)*8.0/1024 AS decimal(18,1)) free_mb,
       CAST(100.0*(f.total_pages-ISNULL(u.free_pages,0))/NULLIF(f.total_pages,0) AS decimal(5,1)) used_pct,
       CAST(ISNULL(u.version_pages,0)*8.0/1024 AS decimal(18,1)) version_mb,
       CAST(ISNULL(u.user_pages,0)*8.0/1024 AS decimal(18,1)) user_mb,
       CAST(ISNULL(u.internal_pages,0)*8.0/1024 AS decimal(18,1)) internal_mb
FROM f CROSS JOIN u;";
        await using (var cmd = new SqlCommand(summarySql, cn) { CommandTimeout = 30 })
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync()) {
                snap.TotalMb=Convert.ToDecimal(r.GetValue(0)); snap.UsedMb=Convert.ToDecimal(r.GetValue(1));
                snap.FreeMb=Convert.ToDecimal(r.GetValue(2)); snap.UsedPct=Convert.ToDecimal(r.GetValue(3));
                snap.VersionStoreMb=Convert.ToDecimal(r.GetValue(4)); snap.UserObjectsMb=Convert.ToDecimal(r.GetValue(5)); snap.InternalObjectsMb=Convert.ToDecimal(r.GetValue(6));
            }
        }
        snap.Status = snap.UsedPct >= 90 ? "CRITICAL" : snap.UsedPct >= 75 ? "WARNING" : "OK";

        const string filesSql = @"
SELECT df.file_id, df.name, df.physical_name,
       CAST(df.size*8.0/1024 AS decimal(18,1)) size_mb,
       CAST((df.size-ISNULL(fs.unallocated_extent_page_count,0))*8.0/1024 AS decimal(18,1)) used_mb,
       CAST(ISNULL(fs.unallocated_extent_page_count,0)*8.0/1024 AS decimal(18,1)) free_mb,
       CAST(100.0*(df.size-ISNULL(fs.unallocated_extent_page_count,0))/NULLIF(df.size,0) AS decimal(5,1)) used_pct,
       CASE WHEN mf.is_percent_growth=1 THEN CONCAT(mf.growth,'%') ELSE CONCAT(CAST(mf.growth*8.0/1024 AS decimal(18,1)),' MB') END growth_desc
FROM tempdb.sys.database_files df
LEFT JOIN tempdb.sys.dm_db_file_space_usage fs ON df.file_id=fs.file_id
LEFT JOIN sys.master_files mf ON mf.database_id=2 AND mf.file_id=df.file_id
WHERE df.type=0 ORDER BY df.file_id;";
        await using (var cmd = new SqlCommand(filesSql, cn) { CommandTimeout = 30 })
        await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync()) snap.Files.Add(new TempDbFileInfo {
            FileId=Convert.ToInt32(r.GetValue(0)), LogicalName=Convert.ToString(r.GetValue(1))??"", PhysicalName=Convert.ToString(r.GetValue(2))??"",
            SizeMb=Convert.ToDecimal(r.GetValue(3)), UsedMb=Convert.ToDecimal(r.GetValue(4)), FreeMb=Convert.ToDecimal(r.GetValue(5)), UsedPct=Convert.ToDecimal(r.GetValue(6)), Growth=Convert.ToString(r.GetValue(7))??""
        });

        const string oldestSql = @"
SELECT TOP (1) ast.session_id, ast.elapsed_time_seconds,
       COALESCE(s.login_name,''), COALESCE(s.host_name,''), COALESCE(s.program_name,''),
       COALESCE(DB_NAME(r.database_id),DB_NAME(s.database_id),'')
FROM sys.dm_tran_active_snapshot_database_transactions ast
LEFT JOIN sys.dm_exec_sessions s ON ast.session_id=s.session_id
LEFT JOIN sys.dm_exec_requests r ON ast.session_id=r.session_id
WHERE ast.session_id <> @@SPID
ORDER BY ast.elapsed_time_seconds DESC;";
        try {
            await using var cmd = new SqlCommand(oldestSql, cn) { CommandTimeout = 15 };
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) {
                snap.OldestSnapshotSessionId=Convert.ToInt32(r.GetValue(0)); snap.OldestSnapshotSeconds=Convert.ToInt64(r.GetValue(1));
                snap.OldestSnapshotLogin=Convert.ToString(r.GetValue(2))??""; snap.OldestSnapshotHost=Convert.ToString(r.GetValue(3))??"";
                snap.OldestSnapshotApp=Convert.ToString(r.GetValue(4))??""; snap.OldestSnapshotDatabase=Convert.ToString(r.GetValue(5))??"";
            }
        } catch { /* Context only: analyzer remains usable if snapshot DMV is unavailable. */ }
        return snap;
    }

    public static string BuildTempDbDiagnosis(TempDbSnapshot x)
    {
        var fileSpread = x.Files.Count < 2 ? 0 : x.Files.Max(f=>f.SizeMb)-x.Files.Min(f=>f.SizeMb);
        var balanced = fileSpread <= 64;
        var versionPct = x.TotalMb <= 0 ? 0 : x.VersionStoreMb*100/x.TotalMb;
        return $@"TEMPDB / VERSION STORE - DIAGNOSTICO
Status: {x.Status}
Total: {x.TotalMb} MB | Used: {x.UsedMb} MB ({x.UsedPct}%) | Free: {x.FreeMb} MB
Version Store GLOBAL: {x.VersionStoreMb} MB ({versionPct:0.0}% of TempDB)
User objects: {x.UserObjectsMb} MB | Internal objects: {x.InternalObjectsMb} MB
Data files: {x.Files.Count} | Size balance: {(balanced ? "OK" : "WARNING - sizes differ by more than 64 MB")}

CURRENT IMPACT
- Space pressure: {(x.UsedPct>=90 ? "CRITICAL" : x.UsedPct>=75 ? "WARNING" : "No immediate pressure detected")}
- Version Store: {(versionPct>=50 ? "HIGH relative to current TempDB size" : versionPct>=25 ? "ELEVATED relative to current TempDB size" : "No high ratio detected")}
- Attribution: NOT DETERMINED. Global Version Store does not prove a specific SPID caused it.

OLDEST SNAPSHOT TRANSACTION (context only)
{(x.OldestSnapshotSessionId.HasValue ? $"SPID {x.OldestSnapshotSessionId} | {x.OldestSnapshotSeconds}s | DB {x.OldestSnapshotDatabase} | {x.OldestSnapshotLogin} | {x.OldestSnapshotHost} | {x.OldestSnapshotApp}" : "No active snapshot transaction reported by the DMV.")}

Recommended DBA checks:
1. Review file balance and free space.
2. Correlate Version Store growth with long snapshot/row-versioning transactions.
3. Do not attribute global Version Store to one SPID without additional evidence.
4. Capture evidence before any corrective action.

Read-only analyzer. No file growth, shrink, KILL or configuration change was executed.";
    }

    public static string BuildTempDbFiles(TempDbSnapshot x)
    {
        var lines=x.Files.Select(f=>$"{f.FileId,2} | {f.LogicalName,-14} | {f.SizeMb,10:0.0} | {f.UsedMb,10:0.0} | {f.FreeMb,10:0.0} | {f.UsedPct,6:0.0}% | {f.Growth,-12} | {f.PhysicalName}");
        return $@"TEMPDB DATA FILES

ID | LOGICAL NAME   | SIZE MB    | USED MB    | FREE MB    | USED %  | GROWTH       | PHYSICAL PATH
{new string('-',145)}
{string.Join(Environment.NewLine,lines)}

Read-only inspection.";
    }

    public static string BuildVersionStoreDetail(TempDbSnapshot x)
    {
        var pct=x.TotalMb<=0?0:x.VersionStoreMb*100/x.TotalMb;
        return $@"TEMPDB VERSION STORE
Global Version Store: {x.VersionStoreMb} MB
TempDB total: {x.TotalMb} MB
Ratio: {pct:0.0}%

Important: this is a GLOBAL TempDB value. It is not attributed automatically to a session.

Oldest active snapshot transaction reported:
{(x.OldestSnapshotSessionId.HasValue ? $"SPID: {x.OldestSnapshotSessionId}\nAge: {x.OldestSnapshotSeconds}s ({x.OldestSnapshotSeconds/60} min)\nDatabase: {x.OldestSnapshotDatabase}\nLogin: {x.OldestSnapshotLogin}\nHost: {x.OldestSnapshotHost}\nApplication: {x.OldestSnapshotApp}" : "None reported.")}

Correlation is contextual evidence, not proof of causality.";
    }

    public static string BuildTempDbEvidence(TempDbSnapshot x) =>
$@"EVIDENCE SNAPSHOT - DBACHECK 2 Alpha 4.1 - TEMPDB
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Status: {x.Status}
Total: {x.TotalMb} MB
Used: {x.UsedMb} MB ({x.UsedPct}%)
Free: {x.FreeMb} MB
Version Store GLOBAL: {x.VersionStoreMb} MB
User objects: {x.UserObjectsMb} MB
Internal objects: {x.InternalObjectsMb} MB
Data files: {x.Files.Count}
Oldest snapshot SPID: {(x.OldestSnapshotSessionId?.ToString() ?? "N/A")}
Oldest snapshot age seconds: {(x.OldestSnapshotSeconds?.ToString() ?? "N/A")}

Safety: read-only evidence. No corrective action was executed.";


    public async Task<List<LogDatabaseInfo>> GetLogDatabasesAsync()
    {
        var items = new List<LogDatabaseInfo>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        var usage = new Dictionary<string,(decimal Size,decimal Pct)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand("DBCC SQLPERF(LOGSPACE) WITH NO_INFOMSGS;",cn){CommandTimeout=30})
        await using (var r = await cmd.ExecuteReaderAsync())
            while(await r.ReadAsync())
                usage[Convert.ToString(r.GetValue(0))??""]=(Convert.ToDecimal(r.GetValue(1)),Convert.ToDecimal(r.GetValue(2)));

        const string sql=@"SELECT d.name,d.recovery_model_desc,d.log_reuse_wait_desc,
 (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='L')
FROM sys.databases d WHERE d.state_desc='ONLINE' ORDER BY d.name;";
        await using var c2=new SqlCommand(sql,cn){CommandTimeout=30};
        await using var rr=await c2.ExecuteReaderAsync();
        while(await rr.ReadAsync())
        {
            var name=Convert.ToString(rr.GetValue(0))??"";
            usage.TryGetValue(name,out var u);
            items.Add(new LogDatabaseInfo {
                DatabaseName=name, LogSizeMb=Math.Round(u.Size,1), UsedPct=Math.Round(u.Pct,1),
                RecoveryModel=Convert.ToString(rr.GetValue(1))??"", ReuseWait=Convert.ToString(rr.GetValue(2))??"",
                LastLogBackup=rr.IsDBNull(3)?null:Convert.ToDateTime(rr.GetValue(3))
            });
        }
        return items.OrderByDescending(x=>x.UsedPct).ThenBy(x=>x.DatabaseName).ToList();
    }

    public static string BuildLogSummary(LogDatabaseInfo x) =>
$@"TRANSACTION LOG - {x.DatabaseName}
Status: {x.Status}
Log size: {x.LogSizeMb} MB
Used: {x.UsedMb} MB ({x.UsedPct}%)
Recovery model: {x.RecoveryModel}
Reuse wait: {x.ReuseWait}
Last LOG backup: {(x.LastLogBackup.HasValue?x.LastLogBackup.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}

Select DIAGNÃ“STICO for contextual interpretation.";

    public async Task<string> GetLogDiagnosisAsync(LogDatabaseInfo x)
    {
        var tx=await GetOldestDatabaseTransactionAsync(x.DatabaseName);
        var files=await GetLogFileObjectsAsync(x.DatabaseName);
        var diskFree=files.Count==0?0:files.Min(f=>f.DiskFreeMb);
        string interpretation=x.ReuseWait.ToUpperInvariant() switch {
            "ACTIVE_TRANSACTION"=>"Database reports ACTIVE_TRANSACTION. Review the oldest open transaction before attributing the condition to a specific SPID.",
            "LOG_BACKUP"=>"Database reports LOG_BACKUP. Review log-backup cadence and last successful log backup.",
            "AVAILABILITY_REPLICA"=>"Database reports AVAILABILITY_REPLICA. Correlate with Availability Group synchronization/transport state.",
            "REPLICATION"=>"Database reports REPLICATION. Correlate with replication/log reader state.",
            "NOTHING"=>"No current log reuse wait is reported. A large log file alone does not prove an active incident.",
            _=>$"Current log reuse wait: {x.ReuseWait}. Correlate with the corresponding SQL Server subsystem."
        };
        return $@"TRANSACTION LOG - DIAGNÃ“STICO
Database: {x.DatabaseName}
Status: {x.Status}
Log: {x.UsedMb} MB used / {x.LogSizeMb} MB total ({x.UsedPct}%)
Recovery: {x.RecoveryModel}
Reuse wait: {x.ReuseWait}
Last LOG backup: {(x.LastLogBackup.HasValue?x.LastLogBackup.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}
Lowest free space on LOG volume(s): {diskFree:0.0} MB

CURRENT INTERPRETATION
- Space usage: {(x.UsedPct>=90?"CRITICAL - log usage >= 90%.":x.UsedPct>=75?"WARNING - log usage >= 75%.":"No high log usage detected.")}
- Reuse: {interpretation}

OLDEST OPEN TRANSACTION (context)
{tx}

Recommended DBA checks:
1. Confirm reuse wait and current log usage.
2. Review oldest open transaction when reuse wait is ACTIVE_TRANSACTION.
3. Review log backups when reuse wait is LOG_BACKUP.
4. Review AG/replication state when their reuse waits are reported.
5. Capture evidence before shrink, recovery-model, file, backup or session actions.

Read-only analyzer. No corrective action was executed.";
    }

    private async Task<string> GetOldestDatabaseTransactionAsync(string db)
    {
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT TOP(1) s.session_id,at.transaction_begin_time,
 DATEDIFF(MINUTE,at.transaction_begin_time,GETDATE()),COALESCE(s.login_name,''),COALESCE(s.host_name,''),COALESCE(s.program_name,'')
FROM sys.dm_tran_active_transactions at
JOIN sys.dm_tran_session_transactions st ON at.transaction_id=st.transaction_id
JOIN sys.dm_exec_sessions s ON st.session_id=s.session_id
JOIN sys.dm_tran_database_transactions dt ON at.transaction_id=dt.transaction_id
WHERE dt.database_id=DB_ID(@db)
ORDER BY at.transaction_begin_time;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=20}; cmd.Parameters.AddWithValue("@db",db);
        await using var r=await cmd.ExecuteReaderAsync();
        if(!await r.ReadAsync()) return "No open user session transaction reported for this database.";
        return $"SPID {Convert.ToInt32(r.GetValue(0))} | Begin {Convert.ToDateTime(r.GetValue(1)):yyyy-MM-dd HH:mm:ss} | Age {Convert.ToInt64(r.GetValue(2))} min | Login {Convert.ToString(r.GetValue(3))} | Host {Convert.ToString(r.GetValue(4))} | App {Convert.ToString(r.GetValue(5))}";
    }

    private async Task<List<LogFileInfo>> GetLogFileObjectsAsync(string db)
    {
        var list=new List<LogFileInfo>();
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT mf.file_id,mf.name,CAST(mf.size*8.0/1024 AS decimal(18,1)),
 CASE WHEN mf.is_percent_growth=1 THEN CONCAT(mf.growth,'%') ELSE CONCAT(CAST(mf.growth*8.0/1024 AS decimal(18,1)),' MB') END,
 CASE WHEN mf.max_size=-1 THEN 'UNLIMITED' ELSE CONCAT(CAST(mf.max_size*8.0/1024 AS decimal(18,1)),' MB') END,
 mf.physical_name,CAST(vs.available_bytes/1048576.0 AS decimal(18,1))
FROM sys.master_files mf
CROSS APPLY sys.dm_os_volume_stats(mf.database_id,mf.file_id) vs
WHERE mf.database_id=DB_ID(@db) AND mf.type=1 ORDER BY mf.file_id;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=20}; cmd.Parameters.AddWithValue("@db",db);
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(new LogFileInfo {
            FileId=Convert.ToInt32(r.GetValue(0)),LogicalName=Convert.ToString(r.GetValue(1))??"",
            SizeMb=Convert.ToDecimal(r.GetValue(2)),Growth=Convert.ToString(r.GetValue(3))??"",
            MaxSize=Convert.ToString(r.GetValue(4))??"",PhysicalName=Convert.ToString(r.GetValue(5))??"",
            DiskFreeMb=Convert.ToDecimal(r.GetValue(6))
        });
        return list;
    }

    public async Task<string> GetLogFilesAsync(LogDatabaseInfo x)
    {
        var f=await GetLogFileObjectsAsync(x.DatabaseName);
        var lines=f.Select(a=>$"{a.FileId,2} | {a.LogicalName,-25} | {a.SizeMb,10:0.0} | {a.Growth,-14} | {a.MaxSize,-14} | {a.DiskFreeMb,12:0.0} | {a.PhysicalName}");
        return $@"TRANSACTION LOG FILES - {x.DatabaseName}

ID | LOGICAL NAME              | SIZE MB    | GROWTH         | MAX SIZE       | DISK FREE MB | PHYSICAL PATH
{new string('-',150)}
{(f.Count==0?"No LOG files returned.":string.Join(Environment.NewLine,lines))}

Read-only inspection.";
    }

    public async Task<string> GetLogTransactionsAsync(LogDatabaseInfo x) =>
$@"OPEN TRANSACTIONS - {x.DatabaseName}

Oldest:
{await GetOldestDatabaseTransactionAsync(x.DatabaseName)}

Context only. An open transaction is not automatically the cause of log growth unless correlated with reuse wait and other evidence.";

    public async Task<string> GetLogBackupsAsync(LogDatabaseInfo x)
    {
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT TOP(10) backup_start_date,backup_finish_date,CAST(backup_size/1048576.0 AS decimal(18,1)),COALESCE(user_name,'')
FROM msdb.dbo.backupset WHERE database_name=@db AND type='L' ORDER BY backup_finish_date DESC;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=20}; cmd.Parameters.AddWithValue("@db",x.DatabaseName);
        await using var r=await cmd.ExecuteReaderAsync(); var lines=new List<string>();
        while(await r.ReadAsync()) lines.Add($"{Convert.ToDateTime(r.GetValue(1)):yyyy-MM-dd HH:mm:ss} | {Convert.ToDecimal(r.GetValue(2)),10:0.0} MB | {Convert.ToString(r.GetValue(3))}");
        return $@"LAST LOG BACKUPS - {x.DatabaseName}
Recovery model: {x.RecoveryModel}

FINISH TIME          | BACKUP MB    | USER
{new string('-',70)}
{(lines.Count==0?"No LOG backup history found in msdb.":string.Join(Environment.NewLine,lines))}

Read-only inspection.";
    }

    public async Task<string> BuildLogEvidenceAsync(LogDatabaseInfo x)
    {
        var tx=await GetOldestDatabaseTransactionAsync(x.DatabaseName);
        var files=await GetLogFileObjectsAsync(x.DatabaseName);
        return $@"EVIDENCE SNAPSHOT - DBACHECK 2 Alpha 4.2 - TRANSACTION LOG
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Database: {x.DatabaseName}
Status: {x.Status}
Log total: {x.LogSizeMb} MB
Log used: {x.UsedMb} MB ({x.UsedPct}%)
Recovery model: {x.RecoveryModel}
Reuse wait: {x.ReuseWait}
Last LOG backup: {(x.LastLogBackup.HasValue?x.LastLogBackup.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}
Log files: {files.Count}
Lowest LOG-volume free space: {(files.Count==0?0:files.Min(f=>f.DiskFreeMb)):0.0} MB

Oldest open transaction (context):
{tx}

Safety: read-only evidence. No SHRINK, recovery-model change, file change, backup or KILL was executed.";
    }

    public async Task<List<BackupDatabaseInfo>> GetBackupDatabasesAsync()
    {
        var items=new List<BackupDatabaseInfo>(); await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT d.name,d.recovery_model_desc,
 (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='D') last_full,
 (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='I') last_diff,
 (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='L') last_log,
 (SELECT TOP 1 CAST(backup_size/1048576.0 AS decimal(18,1)) FROM msdb.dbo.backupset b WHERE b.database_name COLLATE DATABASE_DEFAULT=d.name COLLATE DATABASE_DEFAULT AND b.type='D' ORDER BY backup_finish_date DESC) full_mb
 FROM sys.databases d WHERE d.database_id>4 AND d.state_desc='ONLINE' AND d.source_database_id IS NULL ORDER BY d.name;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30}; await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) items.Add(new BackupDatabaseInfo{DatabaseName=Convert.ToString(r.GetValue(0))??"",RecoveryModel=Convert.ToString(r.GetValue(1))??"",LastFull=r.IsDBNull(2)?null:Convert.ToDateTime(r.GetValue(2)),LastDiff=r.IsDBNull(3)?null:Convert.ToDateTime(r.GetValue(3)),LastLog=r.IsDBNull(4)?null:Convert.ToDateTime(r.GetValue(4)),LastFullMb=r.IsDBNull(5)?null:Convert.ToDecimal(r.GetValue(5))});
        return items.OrderByDescending(x=>x.Status=="WARNING").ThenByDescending(x=>x.FullAgeHours).ToList();
    }
    public async Task<string> GetBackupHistoryAsync(BackupDatabaseInfo x)
    {
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT TOP 20 backup_finish_date,type,CAST(backup_size/1048576.0 AS decimal(18,1)),COALESCE(user_name,''),COALESCE(physical_device_name,'') FROM msdb.dbo.backupset b LEFT JOIN msdb.dbo.backupmediafamily m ON b.media_set_id=m.media_set_id WHERE b.database_name=@db ORDER BY backup_finish_date DESC;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30};cmd.Parameters.AddWithValue("@db",x.DatabaseName);await using var r=await cmd.ExecuteReaderAsync();
        var sb=new System.Text.StringBuilder($"BACKUP HISTORY - {x.DatabaseName}\n\nDATE | TYPE | MB | USER | DEVICE\n"); while(await r.ReadAsync()) sb.AppendLine($"{Convert.ToDateTime(r.GetValue(0)):yyyy-MM-dd HH:mm:ss} | {r.GetValue(1)} | {r.GetValue(2)} | {r.GetValue(3)} | {r.GetValue(4)}"); return sb.ToString();
    }
    public static string BuildBackupDiagnosis(BackupDatabaseInfo x)=>$@"BACKUP DIAGNOSTIC - {x.DatabaseName}
Status: {x.Status}
Recovery: {x.RecoveryModel}
Last FULL: {(x.LastFull.HasValue?x.LastFull.Value.ToString("yyyy-MM-dd HH:mm:ss"):"NEVER / NOT IN MSDB")}
Last DIFF: {(x.LastDiff.HasValue?x.LastDiff.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}
Last LOG: {(x.LastLog.HasValue?x.LastLog.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}
Last FULL size: {(x.LastFullMb.HasValue?x.LastFullMb+" MB":"N/A")}

Interpretation:
{(x.Status=="WARNING"?"- WARNING: no FULL backup recorded in the last 24 hours. Verify the intended backup policy/job before taking action.":"- FULL backup recorded within 24 hours.")}
- MSDB history proves recorded backup completion; restore validity still requires restore testing/verification.";

    public async Task<List<AgentJobInfo>> GetAgentJobsAsync()
    {
        var items=new List<AgentJobInfo>();await using var cn=new SqlConnection(_connectionString);await cn.OpenAsync();
        const string sql=@"SELECT j.job_id,j.name,j.enabled,COALESCE(js.last_run_outcome,5),
 CASE WHEN js.last_run_date>0 THEN msdb.dbo.agent_datetime(js.last_run_date,js.last_run_time) END,
 COALESCE(h.message,''),COALESCE(h.run_duration,0)
 FROM msdb.dbo.sysjobs j LEFT JOIN msdb.dbo.sysjobservers js ON j.job_id=js.job_id
 OUTER APPLY(SELECT TOP 1 message,run_duration FROM msdb.dbo.sysjobhistory hh WHERE hh.job_id=j.job_id AND hh.step_id=0 ORDER BY hh.instance_id DESC) h
 ORDER BY CASE WHEN j.enabled=1 AND COALESCE(js.last_run_outcome,5)=0 THEN 0 ELSE 1 END,j.name;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30};await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync()){int raw=Convert.ToInt32(r.GetValue(6));int sec=(raw/10000)*3600+((raw%10000)/100)*60+(raw%100);items.Add(new AgentJobInfo{JobId=(Guid)r.GetValue(0),JobName=Convert.ToString(r.GetValue(1))??"",Enabled=Convert.ToBoolean(r.GetValue(2)),LastOutcome=Convert.ToInt32(r.GetValue(3)),LastRun=r.IsDBNull(4)?null:Convert.ToDateTime(r.GetValue(4)),LastMessage=Convert.ToString(r.GetValue(5))??"",LastDurationSeconds=sec});}return items;
    }
    public async Task<List<JobCorrelationInfo>> GetJobStepCorrelationsAsync(string databaseName)
    {
        var items=new List<JobCorrelationInfo>();
        await using var cn=new SqlConnection(_connectionString); await cn.OpenAsync();
        const string sql=@"SELECT j.job_id,j.name,j.enabled,s.step_id,s.step_name,s.subsystem,COALESCE(s.command,''),
 COALESCE(js.last_run_outcome,5),
 CASE WHEN js.last_run_date>0 THEN msdb.dbo.agent_datetime(js.last_run_date,js.last_run_time) END,
 COALESCE(h.message,'')
 FROM msdb.dbo.sysjobs j
 JOIN msdb.dbo.sysjobsteps s ON j.job_id=s.job_id
 LEFT JOIN msdb.dbo.sysjobservers js ON j.job_id=js.job_id
 OUTER APPLY(SELECT TOP 1 message FROM msdb.dbo.sysjobhistory hh WHERE hh.job_id=j.job_id AND hh.step_id=0 ORDER BY hh.instance_id DESC) h
 WHERE s.command LIKE @db ESCAPE '\\' OR s.step_name LIKE @db ESCAPE '\\' OR j.name LIKE @db ESCAPE '\\'
 ORDER BY j.name,s.step_id;";
        static string EscapeLike(string v)=>v.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30};
        cmd.Parameters.AddWithValue("@db","%"+EscapeLike(databaseName)+"%");
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) items.Add(new JobCorrelationInfo {
            JobId=(Guid)r.GetValue(0),JobName=Convert.ToString(r.GetValue(1))??"",Enabled=Convert.ToBoolean(r.GetValue(2)),
            StepId=Convert.ToInt32(r.GetValue(3)),StepName=Convert.ToString(r.GetValue(4))??"",Subsystem=Convert.ToString(r.GetValue(5))??"",
            Command=Convert.ToString(r.GetValue(6))??"",LastOutcome=Convert.ToInt32(r.GetValue(7)),LastRun=r.IsDBNull(8)?null:Convert.ToDateTime(r.GetValue(8)),
            LastMessage=Convert.ToString(r.GetValue(9))??""
        });
        return items;
    }

    public async Task<string> GetJobHistoryAsync(AgentJobInfo x)
    {
        await using var cn=new SqlConnection(_connectionString);await cn.OpenAsync();const string sql=@"SELECT TOP 30 h.step_id,COALESCE(h.step_name,''),h.run_status,h.run_date,h.run_time,h.run_duration,COALESCE(h.message,'') FROM msdb.dbo.sysjobhistory h WHERE h.job_id=@id ORDER BY h.instance_id DESC;";await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30};cmd.Parameters.AddWithValue("@id",x.JobId);await using var r=await cmd.ExecuteReaderAsync();var sb=new System.Text.StringBuilder($"JOB HISTORY - {x.JobName}\n\nSTEP | STATUS | DATE | DURATION | MESSAGE\n");while(await r.ReadAsync()){var st=Convert.ToInt32(r.GetValue(2)) switch{0=>"FAILED",1=>"SUCCEEDED",2=>"RETRY",3=>"CANCELED",_=>"UNKNOWN"};sb.AppendLine($"{r.GetValue(0)} {r.GetValue(1)} | {st} | {r.GetValue(3)} {r.GetValue(4)} | {r.GetValue(5)} | {r.GetValue(6)}");}return sb.ToString();
    }
    public static string BuildJobDiagnosis(AgentJobInfo x)=>$@"SQL AGENT JOB DIAGNOSTIC
Job: {x.JobName}
Enabled: {x.Enabled}
Last outcome: {x.Outcome}
Last run: {(x.LastRun.HasValue?x.LastRun.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}
Duration: {x.LastDurationSeconds}s
Last message: {x.LastMessage}

Interpretation:
{(x.Status=="WARNING"?"- WARNING: enabled job reports FAILED on its last run. Review failing step/history before rerunning.":"- No enabled-job last-run failure detected for this job.")}
Safety: read-only. DBACHECK did not start, stop, or modify the job.";

    public async Task<List<AlwaysOnItem>> GetAlwaysOnItemsAsync()
    {
        var items = new List<AlwaysOnItem>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        const string sql = @"
IF SERVERPROPERTY('IsHadrEnabled') <> 1
BEGIN
    SELECT CAST(NULL AS sysname) availability_group, CAST(NULL AS sysname) replica_server,
           CAST(NULL AS nvarchar(60)) role_desc, CAST(NULL AS sysname) database_name,
           CAST(NULL AS nvarchar(60)) synchronization_state_desc, CAST(NULL AS nvarchar(60)) synchronization_health_desc,
           CAST(0 AS bit) is_suspended, CAST(0 AS bigint) log_send_queue_size, CAST(0 AS bigint) redo_queue_size,
           CAST(0 AS bigint) log_send_rate, CAST(0 AS bigint) redo_rate,
           CAST(NULL AS nvarchar(60)) connected_state_desc, CAST(NULL AS nvarchar(60)) operational_state_desc
    WHERE 1=0;
    RETURN;
END;

SELECT ag.name,
       ar.replica_server_name,
       COALESCE(ars.role_desc,'UNKNOWN'),
       DB_NAME(drs.database_id),
       COALESCE(drs.synchronization_state_desc,'UNKNOWN'),
       COALESCE(drs.synchronization_health_desc,'UNKNOWN'),
       drs.is_suspended,
       COALESCE(CONVERT(bigint,drs.log_send_queue_size),0),
       COALESCE(CONVERT(bigint,drs.redo_queue_size),0),
       COALESCE(CONVERT(bigint,drs.log_send_rate),0),
       COALESCE(CONVERT(bigint,drs.redo_rate),0),
       COALESCE(ars.connected_state_desc,'UNKNOWN'),
       COALESCE(ars.operational_state_desc,'UNKNOWN')
FROM sys.availability_groups ag
JOIN sys.availability_replicas ar ON ag.group_id=ar.group_id
LEFT JOIN sys.dm_hadr_availability_replica_states ars
       ON ar.replica_id=ars.replica_id AND ars.is_local=1
JOIN sys.dm_hadr_database_replica_states drs
       ON ar.replica_id=drs.replica_id AND drs.is_local=1
ORDER BY ag.name, DB_NAME(drs.database_id);";

        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        await using var r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())
        {
            var sync = Convert.ToString(r.GetValue(4)) ?? "UNKNOWN";
            var health = Convert.ToString(r.GetValue(5)) ?? "UNKNOWN";
            var suspended = Convert.ToBoolean(r.GetValue(6));
            var sendQ = Convert.ToInt64(r.GetValue(7));
            var redoQ = Convert.ToInt64(r.GetValue(8));
            var status = suspended || health == "NOT_HEALTHY" ? "CRITICAL"
                       : sync == "NOT SYNCHRONIZING" || health == "PARTIALLY_HEALTHY" ? "WARNING"
                       : "OK";
            items.Add(new AlwaysOnItem {
                Status=status,
                AvailabilityGroup=Convert.ToString(r.GetValue(0)) ?? "",
                ReplicaServer=Convert.ToString(r.GetValue(1)) ?? "",
                Role=Convert.ToString(r.GetValue(2)) ?? "",
                DatabaseName=Convert.ToString(r.GetValue(3)) ?? "",
                SynchronizationState=sync,
                SynchronizationHealth=health,
                IsSuspended=suspended,
                LogSendQueueKb=sendQ,
                RedoQueueKb=redoQ,
                LogSendRateKb=Convert.ToInt64(r.GetValue(9)),
                RedoRateKb=Convert.ToInt64(r.GetValue(10)),
                ConnectedState=Convert.ToString(r.GetValue(11)) ?? "",
                OperationalState=Convert.ToString(r.GetValue(12)) ?? ""
            });
        }
        return items;
    }

    public async Task<string> GetAlwaysOnReplicaDetailAsync(string agName)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = @"
SELECT ar.replica_server_name, ar.availability_mode_desc, ar.failover_mode_desc,
       ar.seeding_mode_desc, ar.secondary_role_allow_connections_desc,
       COALESCE(ars.role_desc,'REMOTE/UNKNOWN'), COALESCE(ars.connected_state_desc,'UNKNOWN'),
       COALESCE(ars.operational_state_desc,'UNKNOWN'), COALESCE(ars.synchronization_health_desc,'UNKNOWN')
FROM sys.availability_groups ag
JOIN sys.availability_replicas ar ON ag.group_id=ar.group_id
LEFT JOIN sys.dm_hadr_availability_replica_states ars ON ar.replica_id=ars.replica_id
WHERE ag.name=@ag
ORDER BY ar.replica_server_name;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30};
        cmd.Parameters.AddWithValue("@ag",agName);
        await using var r=await cmd.ExecuteReaderAsync();
        var sb=new System.Text.StringBuilder($"ALWAYSON REPLICAS - {agName}\r\n\r\nSERVER | AVAILABILITY | FAILOVER | SEEDING | SECONDARY CONNECTIONS | ROLE | CONNECTED | OPERATIONAL | HEALTH\r\n");
        sb.AppendLine(new string('-',150));
        while(await r.ReadAsync())
            sb.AppendLine($"{r.GetValue(0)} | {r.GetValue(1)} | {r.GetValue(2)} | {r.GetValue(3)} | {r.GetValue(4)} | {r.GetValue(5)} | {r.GetValue(6)} | {r.GetValue(7)} | {r.GetValue(8)}");
        sb.AppendLine("\r\nRead-only inspection. No failover or configuration change was executed.");
        return sb.ToString();
    }

    public async Task<string> GetAlwaysOnDatabaseDetailAsync(string agName,string databaseName)
    {
        var all=await GetAlwaysOnItemsAsync();
        var rows=all.Where(x=>x.AvailabilityGroup==agName && x.DatabaseName==databaseName).ToList();
        var sb=new System.Text.StringBuilder($"DATABASE REPLICA - {agName} / {databaseName}\r\n\r\n");
        foreach(var x in rows)
        {
            sb.AppendLine($"Replica: {x.ReplicaServer}");
            sb.AppendLine($"Role: {x.Role}");
            sb.AppendLine($"Synchronization: {x.SynchronizationState}");
            sb.AppendLine($"Health: {x.SynchronizationHealth}");
            sb.AppendLine($"Suspended: {(x.IsSuspended ? "YES" : "NO")}");
            sb.AppendLine($"Log send queue: {x.LogSendQueueKb} KB");
            sb.AppendLine($"Redo queue: {x.RedoQueueKb} KB");
            sb.AppendLine($"Log send rate: {x.LogSendRateKb} KB/s");
            sb.AppendLine($"Redo rate: {x.RedoRateKb} KB/s");
            sb.AppendLine();
        }
        sb.AppendLine("Queue values are current observations; a single sample does not establish a growth trend.");
        return sb.ToString();
    }

    public async Task<string> GetAlwaysOnListenersAsync()
    {
        await using var cn=new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql=@"
SELECT ag.name, COALESCE(l.dns_name,''), COALESCE(CONVERT(varchar(12),l.port),''),
       COALESCE(ip.ip_address,''), COALESCE(ip.state_desc,'')
FROM sys.availability_groups ag
LEFT JOIN sys.availability_group_listeners l ON ag.group_id=l.group_id
LEFT JOIN sys.availability_group_listener_ip_addresses ip ON l.listener_id=ip.listener_id
ORDER BY ag.name,l.dns_name,ip.ip_address;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=30};
        await using var r=await cmd.ExecuteReaderAsync();
        var sb=new System.Text.StringBuilder("ALWAYSON LISTENERS\r\n\r\nAG | DNS | PORT | IP | STATE\r\n");
        sb.AppendLine(new string('-',100));
        while(await r.ReadAsync()) sb.AppendLine($"{r.GetValue(0)} | {r.GetValue(1)} | {r.GetValue(2)} | {r.GetValue(3)} | {r.GetValue(4)}");
        sb.AppendLine("\r\nRead-only inspection.");
        return sb.ToString();
    }

    public Task<string> GetAlwaysOnDiagnosticAsync(AlwaysOnItem x)
    {
        var interpretation = x.IsSuspended
            ? "CRITICAL: data movement is suspended for this local database replica. Investigate the suspend reason before any RESUME action."
            : x.SynchronizationHealth == "NOT_HEALTHY"
                ? "CRITICAL: synchronization health is NOT_HEALTHY."
                : x.SynchronizationState == "NOT SYNCHRONIZING"
                    ? "WARNING: database replica is NOT SYNCHRONIZING."
                    : x.SynchronizationState == "SYNCHRONIZING"
                        ? "INFO: replica is SYNCHRONIZING. This may be expected for asynchronous replicas; review queues and trend."
                        : "No immediate synchronization fault detected in this sample.";

        var text=$@"ALWAYSON / HA - DIAGNOSTIC
AG: {x.AvailabilityGroup}
Replica: {x.ReplicaServer}
Role: {x.Role}
Database: {x.DatabaseName}
Status: {x.Status}
Synchronization: {x.SynchronizationState}
Health: {x.SynchronizationHealth}
Suspended: {(x.IsSuspended ? "YES" : "NO")}
Connected: {x.ConnectedState}
Operational: {x.OperationalState}
Log send queue: {x.LogSendQueueKb} KB
Redo queue: {x.RedoQueueKb} KB
Log send rate: {x.LogSendRateKb} KB/s
Redo rate: {x.RedoRateKb} KB/s

CURRENT INTERPRETATION
- {interpretation}
- Queue sizes and rates are point-in-time values; DBACHECK does not infer a persistent lag from one sample.

SAFETY
Read-only diagnosis. No RESUME, FAILOVER, REMOVE DATABASE or AG configuration change was executed.";
        return Task.FromResult(text);
    }

    public static string BuildAlwaysOnEvidence(AlwaysOnItem x) =>
$@"EVIDENCE SNAPSHOT - DBACHECK 2 Alpha 4.4
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Area: AlwaysOn / HA
AG: {x.AvailabilityGroup}
Replica: {x.ReplicaServer}
Role: {x.Role}
Database: {x.DatabaseName}
Status: {x.Status}
Synchronization: {x.SynchronizationState}
Health: {x.SynchronizationHealth}
Suspended: {(x.IsSuspended ? "YES" : "NO")}
Connected: {x.ConnectedState}
Operational: {x.OperationalState}
Log send queue: {x.LogSendQueueKb} KB
Redo queue: {x.RedoQueueKb} KB
Log send rate: {x.LogSendRateKb} KB/s
Redo rate: {x.RedoRateKb} KB/s

Safety: read-only evidence capture. No HA action was executed.";


    public async Task<List<PerformanceRequest>> GetPerformanceRequestsAsync()
    {
        var items=new List<PerformanceRequest>();
        await using var cn=new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql=@"
SELECT TOP (100)
 r.session_id,
 CONVERT(bigint,r.total_elapsed_time/1000) elapsed_seconds,
 COALESCE(DB_NAME(r.database_id),'') database_name,
 COALESCE(s.login_name,'') login_name,
 COALESCE(s.host_name,'') host_name,
 COALESCE(s.program_name,'') program_name,
 COALESCE(r.status,'') request_status,
 COALESCE(r.command,'') command,
 CONVERT(bigint,r.cpu_time) cpu_ms,
 CONVERT(bigint,r.reads) reads,
 CONVERT(bigint,r.writes) writes,
 CONVERT(bigint,r.logical_reads) logical_reads,
 COALESCE(r.wait_type,'') wait_type,
 CONVERT(bigint,r.wait_time) wait_ms,
 COALESCE(r.blocking_session_id,0) blocking_session_id,
 COALESCE(s.open_transaction_count,0) open_transactions,
 CONVERT(bigint,r.granted_query_memory)*8 granted_query_memory_kb,
 COALESCE(SUBSTRING(t.text,
   (r.statement_start_offset/2)+1,
   ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text) ELSE r.statement_end_offset END-r.statement_start_offset)/2)+1
 ),t.text,'') sql_text
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON r.session_id=s.session_id
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.session_id<>@@SPID
  AND s.is_user_process=1
ORDER BY r.cpu_time DESC, r.logical_reads DESC, r.total_elapsed_time DESC;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=15};
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())
        {
            var elapsed=Convert.ToInt64(r.GetValue(1));
            var cpu=Convert.ToInt64(r.GetValue(8));
            var logical=Convert.ToInt64(r.GetValue(11));
            var wait=Convert.ToString(r.GetValue(12)) ?? "";
            var waitMs=Convert.ToInt64(r.GetValue(13));
            var blocked=Convert.ToInt32(r.GetValue(14));
            var status=blocked>0 || wait.StartsWith("LCK_",StringComparison.OrdinalIgnoreCase) ? "CRITICAL"
                      : elapsed>=300 || cpu>=30000 || logical>=1000000 || waitMs>=30000 ? "WARNING"
                      : "OK";
            items.Add(new PerformanceRequest {
                Status=status, SessionId=r.GetInt32(0), ElapsedSeconds=elapsed,
                DatabaseName=Convert.ToString(r.GetValue(2))??"", LoginName=Convert.ToString(r.GetValue(3))??"",
                HostName=Convert.ToString(r.GetValue(4))??"", ProgramName=Convert.ToString(r.GetValue(5))??"",
                RequestStatus=Convert.ToString(r.GetValue(6))??"", Command=Convert.ToString(r.GetValue(7))??"",
                CpuMs=cpu, Reads=Convert.ToInt64(r.GetValue(9)), Writes=Convert.ToInt64(r.GetValue(10)),
                LogicalReads=logical, WaitType=wait, WaitMs=waitMs, BlockingSessionId=blocked,
                OpenTransactions=Convert.ToInt32(r.GetValue(15)), GrantedQueryMemoryKb=Convert.ToInt64(r.GetValue(16)),
                SqlText=Convert.ToString(r.GetValue(17))??""
            });
        }
        return items;
    }

    public async Task<string> GetPerformanceWaitsAsync()
    {
        await using var cn=new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql=@"
SELECT TOP (30) wt.session_id, COALESCE(DB_NAME(r.database_id),'') database_name,
 wt.wait_type, CONVERT(bigint,wt.wait_duration_ms), COALESCE(wt.blocking_session_id,0),
 COALESCE(s.login_name,''), COALESCE(s.host_name,''), COALESCE(s.program_name,'')
FROM sys.dm_os_waiting_tasks wt
LEFT JOIN sys.dm_exec_requests r ON wt.session_id=r.session_id
LEFT JOIN sys.dm_exec_sessions s ON wt.session_id=s.session_id
WHERE wt.session_id>50
 AND wt.wait_type NOT IN ('BROKER_EVENTHANDLER','BROKER_RECEIVE_WAITFOR','BROKER_TASK_STOP','BROKER_TO_FLUSH',
 'CHECKPOINT_QUEUE','CLR_AUTO_EVENT','CLR_MANUAL_EVENT','DIRTY_PAGE_POLL','DISPATCHER_QUEUE_SEMAPHORE',
 'FT_IFTS_SCHEDULER_IDLE_WAIT','HADR_FILESTREAM_IOMGR_IOCOMPLETION','LAZYWRITER_SLEEP','LOGMGR_QUEUE',
 'REQUEST_FOR_DEADLOCK_SEARCH','RESOURCE_QUEUE','SERVER_IDLE_CHECK','SLEEP_TASK','SLEEP_SYSTEMTASK',
 'SQLTRACE_BUFFER_FLUSH','WAITFOR','XE_DISPATCHER_WAIT','XE_TIMER_EVENT','SOS_WORK_DISPATCHER')
ORDER BY wt.wait_duration_ms DESC;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=15};
        await using var r=await cmd.ExecuteReaderAsync();
        var sb=new System.Text.StringBuilder("CURRENT WAITING TASKS\r\n\r\nSPID | DATABASE | WAIT | WAIT MS | BLOCKED BY | LOGIN | HOST | APP\r\n");
        sb.AppendLine(new string('-',150));
        var count=0;
        while(await r.ReadAsync()){count++; sb.AppendLine($"{r.GetValue(0)} | {r.GetValue(1)} | {r.GetValue(2)} | {r.GetValue(3)} | {r.GetValue(4)} | {r.GetValue(5)} | {r.GetValue(6)} | {r.GetValue(7)}");}
        if(count==0) sb.AppendLine("No actionable waiting tasks detected in this sample.");
        sb.AppendLine("\r\nPoint-in-time view. A wait observed once does not establish the root cause.");
        return sb.ToString();
    }

    public async Task<string> GetMemoryGrantsAsync()
    {
        await using var cn=new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql=@"
SELECT TOP (30) mg.session_id, COALESCE(DB_NAME(r.database_id),'') database_name,
 COALESCE(s.login_name,''), mg.requested_memory_kb, mg.granted_memory_kb,
 mg.required_memory_kb, mg.used_memory_kb, mg.max_used_memory_kb,
 COALESCE(mg.wait_time_ms,0), COALESCE(mg.dop,0)
FROM sys.dm_exec_query_memory_grants mg
LEFT JOIN sys.dm_exec_requests r ON mg.session_id=r.session_id
LEFT JOIN sys.dm_exec_sessions s ON mg.session_id=s.session_id
ORDER BY mg.wait_time_ms DESC, mg.requested_memory_kb DESC;";
        await using var cmd=new SqlCommand(sql,cn){CommandTimeout=15};
        await using var r=await cmd.ExecuteReaderAsync();
        var sb=new System.Text.StringBuilder("QUERY MEMORY GRANTS\r\n\r\nSPID | DATABASE | LOGIN | REQUESTED KB | GRANTED KB | REQUIRED KB | USED KB | MAX USED KB | WAIT MS | DOP\r\n");
        sb.AppendLine(new string('-',145));
        var count=0; var pending=0;
        while(await r.ReadAsync())
        {
            count++; var granted=Convert.ToInt64(r.GetValue(4)); var wait=Convert.ToInt64(r.GetValue(8));
            if(granted==0 && wait>0) pending++;
            sb.AppendLine($"{r.GetValue(0)} | {r.GetValue(1)} | {r.GetValue(2)} | {r.GetValue(3)} | {r.GetValue(4)} | {r.GetValue(5)} | {r.GetValue(6)} | {r.GetValue(7)} | {r.GetValue(8)} | {r.GetValue(9)}");
        }
        if(count==0) sb.AppendLine("No active query memory grants in this sample.");
        sb.AppendLine($"\r\nActive grants: {count} | Pending observed: {pending}");
        sb.AppendLine("Read-only point-in-time inspection.");
        return sb.ToString();
    }

    public static string BuildPerformanceRequestDetail(PerformanceRequest x) =>
$@"ACTIVE REQUEST - SPID {x.SessionId}
Status: {x.RequestStatus}
Command: {x.Command}
Duration: {x.ElapsedSeconds}s
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
CPU: {x.CpuMs} ms
Reads: {x.Reads}
Logical reads: {x.LogicalReads}
Writes: {x.Writes}
Wait: {(string.IsNullOrWhiteSpace(x.WaitType) ? "-" : x.WaitType)}
Wait time: {x.WaitMs} ms
Blocked by: {(x.BlockingSessionId==0 ? "No" : x.BlockingSessionId.ToString())}
Open transactions: {x.OpenTransactions}
Granted query memory: {x.GrantedQueryMemoryKb} KB

This is a point-in-time request snapshot.";

    public static string BuildPerformanceDiagnostic(PerformanceRequest x)
    {
        var notes=new List<string>();
        if(x.BlockingSessionId>0) notes.Add($"BLOCKING: request is currently blocked by SPID {x.BlockingSessionId}.");
        if(x.WaitType.StartsWith("LCK_",StringComparison.OrdinalIgnoreCase)) notes.Add($"LOCK WAIT: current wait is {x.WaitType}.");
        if(x.WaitType.StartsWith("PAGEIOLATCH_",StringComparison.OrdinalIgnoreCase)) notes.Add($"I/O WAIT: {x.WaitType} can indicate waiting for data pages from storage; correlate with latency and repeated samples.");
        if(x.WaitType=="RESOURCE_SEMAPHORE") notes.Add("MEMORY GRANT WAIT: request is waiting for execution memory; inspect MEMORY GRANTS.");
        if(x.WaitType.StartsWith("CX",StringComparison.OrdinalIgnoreCase)) notes.Add($"PARALLELISM: {x.WaitType} is present; do not change MAXDOP from a single sample.");
        if(x.ElapsedSeconds>=300) notes.Add($"LONG ACTIVE REQUEST: elapsed time is {x.ElapsedSeconds}s.");
        if(x.CpuMs>=30000) notes.Add($"CPU CONSUMPTION: request has accumulated {x.CpuMs} ms CPU.");
        if(x.LogicalReads>=1000000) notes.Add($"HIGH LOGICAL READS: {x.LogicalReads:N0} logical reads observed.");
        if(notes.Count==0) notes.Add("No immediate high-impact condition detected by the Alpha 4.5 rules in this sample.");
        return $@"PERFORMANCE - DIAGNOSTIC SPID {x.SessionId}
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Status: {x.Status}
Duration: {x.ElapsedSeconds}s
CPU: {x.CpuMs} ms
Reads: {x.Reads}
Logical reads: {x.LogicalReads}
Writes: {x.Writes}
Wait: {(string.IsNullOrWhiteSpace(x.WaitType) ? "-" : x.WaitType)} ({x.WaitMs} ms)
Blocked by: {(x.BlockingSessionId==0 ? "No" : x.BlockingSessionId.ToString())}
Granted query memory: {x.GrantedQueryMemoryKb} KB

CURRENT INTERPRETATION
- {string.Join("\r\n- ",notes)}

SAFETY
Read-only diagnosis. No KILL, cache clear, index change, MAXDOP or memory configuration change was executed.";
    }

    public static string BuildPerformanceEvidence(PerformanceRequest x) =>
$@"EVIDENCE SNAPSHOT - DBACHECK 2 Alpha 4.5
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Area: Performance
SPID: {x.SessionId}
Database: {x.DatabaseName}
Login: {x.LoginName}
Host: {x.HostName}
Application: {x.ProgramName}
Request status: {x.RequestStatus}
Command: {x.Command}
Duration: {x.ElapsedSeconds}s
CPU: {x.CpuMs} ms
Reads: {x.Reads}
Logical reads: {x.LogicalReads}
Writes: {x.Writes}
Wait: {x.WaitType}
Wait ms: {x.WaitMs}
Blocked by: {x.BlockingSessionId}
Open transactions: {x.OpenTransactions}
Granted query memory: {x.GrantedQueryMemoryKb} KB

SQL / current statement:
{x.SqlText}

Safety: read-only evidence capture. No corrective action was executed.";


}

