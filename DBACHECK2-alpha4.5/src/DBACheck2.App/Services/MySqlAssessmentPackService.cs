using System.IO;
using System.Text.RegularExpressions;
using DBACheck2.App.Models;
using MySqlConnector;
using Renci.SshNet;

namespace DBACheck2.App.Services;

public sealed class MySqlAssessmentPackService
{
    private readonly ServerProfile p;
    private CancellationToken _cancellationToken;
    public MySqlAssessmentPackService(ServerProfile profile)=>p=profile;

    public Task<List<AssessmentCheck>> RunFullAsync(CancellationToken cancellationToken=default)
    {
        _cancellationToken=cancellationToken;
        return WithConnection(async c=>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var x=new List<AssessmentCheck>();
        var version=await Scalar(c,"select version()");
        var maria=version.Contains("MariaDB",StringComparison.OrdinalIgnoreCase);
        var major=Major(version);

        cancellationToken.ThrowIfCancellationRequested();
        x.Add(Info("MYSQL.PLATFORM.VERSION",AssessmentCategory.Platform,$"{(maria?"MariaDB":"MySQL")} Version",version,$"Database={c.Database} | User={p.Username} | SSH={p.UseSshTunnel}"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.CONFIG.CONNECTIONS",AssessmentCategory.Configuration,"Connection Capacity",@"select if(@@max_connections>0 and (select count(*) from information_schema.processlist)*100/@@max_connections>=80,'WARNING','OK'),concat((select count(*) from information_schema.processlist),' / ',@@max_connections,' connections'),concat('running=',(select count(*) from information_schema.processlist where command<>'Sleep'),'; sleeping=',(select count(*) from information_schema.processlist where command='Sleep'))"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.TRAN.LONG",AssessmentCategory.Transactions,"Long InnoDB Transactions",@"select if(count(*)>0,'WARNING','OK'),concat(count(*),' transaction(s) > 30 min'),coalesce(group_concat(concat('trx=',trx_id,' age=',timestampdiff(minute,trx_started,now()),'m') separator '; '),'') from information_schema.innodb_trx where trx_started < now()-interval 30 minute"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.PERF.LONGSQL",AssessmentCategory.Performance,"Long Running SQL",@"select if(count(*)>0,'WARNING','OK'),concat(count(*),' query(s) > 60 sec'),coalesce(group_concat(concat('id=',id,' ',time,'s db=',coalesce(db,'')) separator '; '),'') from information_schema.processlist where command<>'Sleep' and time>60"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Blocking(c,maria,major));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.CAPACITY.DATABASES",AssessmentCategory.Capacity,"Database Size",@"select 'OK',concat(count(distinct table_schema),' database(s)'),coalesce(group_concat(concat(table_schema,'=',round(sum_mb,1),'MB') separator '; '),'') from (select table_schema,sum(data_length+index_length)/1024/1024 sum_mb from information_schema.tables where table_schema not in ('mysql','information_schema','performance_schema','sys') group by table_schema) s"));
        cancellationToken.ThrowIfCancellationRequested();
        x.AddRange(await IndexChecks(c));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.CAPACITY.FRAGMENTATION",AssessmentCategory.Capacity,"Table Free Space Candidates",@"select if(count(*)>0,'WARNING','OK'),concat(count(*),' table(s) with >1GB DATA_FREE'),coalesce(group_concat(concat(table_schema,'.',table_name,' free=',round(data_free/1024/1024),'MB') separator '; '),'') from information_schema.tables where table_schema not in ('mysql','information_schema','performance_schema','sys') and data_free>1073741824"));
        cancellationToken.ThrowIfCancellationRequested();
        x.AddRange(await TempChecks(c));
        cancellationToken.ThrowIfCancellationRequested();
        x.AddRange(await InnoDbChecks(c));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.LOG.BINLOG",AssessmentCategory.Logs,"Binary Log Configuration",@"select if(@@log_bin=1,'OK','INFO'),concat('Binary log=',@@log_bin),concat('format=',@@binlog_format,'; sync_binlog=',@@sync_binlog)"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Replication(c,maria,major));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.CONFIG.CORE",AssessmentCategory.Configuration,"Core InnoDB Settings",@"select 'INFO','Core MySQL/MariaDB settings',concat('buffer_pool=',@@innodb_buffer_pool_size,'; max_connections=',@@max_connections,'; tmp_table_size=',@@tmp_table_size,'; max_heap_table_size=',@@max_heap_table_size)"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(await Q(c,"MYSQL.MAINT.EVENTS",AssessmentCategory.Maintenance,"Event Scheduler",@"select 'INFO',concat('event_scheduler=',@@event_scheduler),concat((select count(*) from information_schema.events),' event definition(s)')"));
        cancellationToken.ThrowIfCancellationRequested();
        x.Add(Unavailable("MYSQL.BACKUP.EVIDENCE",AssessmentCategory.Backup,"Backup Evidence","MySQL/MariaDB does not expose a universal authoritative last-backup record. Integrate the actual backup tool/filesystem evidence instead of inferring recoverability."));
        cancellationToken.ThrowIfCancellationRequested();
        return x;
    },cancellationToken);
    }

    private async Task<AssessmentCheck> Blocking(MySqlConnection c,bool maria,int major)
    {
        if(!maria && major>=8)
            return await Q(c,"MYSQL.PERF.BLOCKING",AssessmentCategory.Performance,"InnoDB Lock Waits",@"select if(count(*)>0,'WARNING','OK'),concat(count(*),' InnoDB lock wait(s)'),coalesce(group_concat(concat('requesting=',requesting_engine_transaction_id,' blocking=',blocking_engine_transaction_id) separator '; '),'') from performance_schema.data_lock_waits");
        return await Q(c,"MYSQL.PERF.BLOCKING",AssessmentCategory.Performance,"InnoDB Lock Waits",@"select if(count(*)>0,'WARNING','OK'),concat(count(*),' InnoDB lock wait(s)'),coalesce(group_concat(concat('requesting=',requesting_trx_id,' blocking=',blocking_trx_id) separator '; '),'') from information_schema.innodb_lock_waits");
    }

    private async Task<List<AssessmentCheck>> IndexChecks(MySqlConnection c)
    {
        try
        {
            var rows=new List<Idx>();
            await using var q=new MySqlCommand(@"select table_schema,table_name,index_name,non_unique,seq_in_index,column_name from information_schema.statistics where table_schema not in ('mysql','information_schema','performance_schema','sys') order by table_schema,table_name,index_name,seq_in_index",c){CommandTimeout=30};
            await using var r=await q.ExecuteReaderAsync(_cancellationToken);
            while(await r.ReadAsync(_cancellationToken))rows.Add(new(S(r,0),S(r,1),S(r,2),Convert.ToInt32(r.GetValue(3)),Convert.ToInt32(r.GetValue(4)),S(r,5)));

            var defs=rows.GroupBy(x=>new{x.Schema,x.Table,x.Index,x.NonUnique})
                .Select(g=>new{g.Key.Schema,g.Key.Table,g.Key.Index,g.Key.NonUnique,Cols=string.Join(",",g.OrderBy(x=>x.Seq).Select(x=>x.Column))}).ToList();

            var dups=defs.GroupBy(x=>$"{x.Schema}|{x.Table}|{x.Cols}",StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).ToList();
            var tables=defs.Select(x=>$"{x.Schema}|{x.Table}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var withPk=defs.Where(x=>x.Index.Equals("PRIMARY",StringComparison.OrdinalIgnoreCase)).Select(x=>$"{x.Schema}|{x.Table}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var noPk=tables.Where(t=>!withPk.Contains(t)).ToList();

            var result=new List<AssessmentCheck>{
                new(){CheckId="MYSQL.INDEX.DUPLICATE",Engine=DatabaseEngine.MySqlMariaDb,Category=AssessmentCategory.Indexes,Title="Duplicate Index Definitions",Status=dups.Count>0?"WARNING":"OK",Severity=dups.Count>0?3:0,Summary=$"{dups.Count} duplicate index definition group(s)",Evidence=string.Join("; ",dups.Take(100).Select(g=>{var z=g.First();return $"{z.Schema}.{z.Table} ({z.Cols}) => {string.Join(",",g.Select(i=>i.Index))}";})),WhyItMatters="Equivalent index definitions can increase storage and DML maintenance cost.",RecommendedAction="Review uniqueness, constraints and workload before dropping or consolidating indexes.",Verification="Re-run the assessment and confirm only intended definitions remain.",Capability="AVAILABLE",ReadOnly=true},
                new(){CheckId="MYSQL.TABLE.NOPK",Engine=DatabaseEngine.MySqlMariaDb,Category=AssessmentCategory.Indexes,Title="Tables Without Primary Key",Status=noPk.Count>0?"WARNING":"OK",Severity=noPk.Count>0?3:0,Summary=$"{noPk.Count} table(s) without PRIMARY KEY",Evidence=string.Join("; ",noPk.Take(150).Select(z=>z.Replace("|","."))),WhyItMatters="Primary keys are important for row identity and can materially affect InnoDB replication and access patterns.",RecommendedAction="Validate table semantics and workload before adding a primary key.",Verification="Re-run the assessment after approved schema changes.",Capability="AVAILABLE",ReadOnly=true}
            };

            result.Add(await UnusedIndexes(c));
            return result;
        }
        catch(OperationCanceledException){throw;}catch(Exception ex){return new(){Error("MYSQL.INDEX.INVENTORY",AssessmentCategory.Indexes,"Index Inventory",ex)};}
    }

    private async Task<AssessmentCheck> UnusedIndexes(MySqlConnection c)
    {
        return await Q(c,"MYSQL.INDEX.UNUSED",AssessmentCategory.Indexes,"Unused Index Candidates",@"select if(count(*)>0,'WARNING','OK'),concat(count(*),' unused index candidate(s)'),coalesce(group_concat(concat(object_schema,'.',object_name,'.',index_name) separator '; '),'') from performance_schema.table_io_waits_summary_by_index_usage where object_schema not in ('mysql','performance_schema','sys') and index_name is not null and index_name<>'PRIMARY' and count_star=0");
    }

    private async Task<List<AssessmentCheck>> TempChecks(MySqlConnection c)
    {
        try
        {
            var status=await StatusValues(c,"Created_tmp_tables","Created_tmp_disk_tables");
            status.TryGetValue("Created_tmp_tables",out var all);status.TryGetValue("Created_tmp_disk_tables",out var disk);
            var ratio=all>0?disk*100.0/all:0;
            return new(){new(){CheckId="MYSQL.TEMP.TABLES",Engine=DatabaseEngine.MySqlMariaDb,Category=AssessmentCategory.Temporary,Title="Temporary Tables",Status=ratio>=25?"WARNING":"OK",Severity=ratio>=25?3:0,Summary=$"{ratio:0.0}% temp tables created on disk",Evidence=$"Created_tmp_tables={all}; Created_tmp_disk_tables={disk}",WhyItMatters="A high disk-temp ratio can indicate queries exceeding in-memory temp limits or unsuitable access patterns.",RecommendedAction="Correlate with slow SQL before increasing temp limits; avoid tuning solely from a cumulative counter.",Verification="Compare the delta/rate during a representative workload period.",Capability="AVAILABLE",ReadOnly=true}};
        }catch(OperationCanceledException){throw;}catch(Exception ex){return new(){Error("MYSQL.TEMP.TABLES",AssessmentCategory.Temporary,"Temporary Tables",ex)};}
    }

    private async Task<List<AssessmentCheck>> InnoDbChecks(MySqlConnection c)
    {
        var list=new List<AssessmentCheck>();
        try
        {
            var s=await StatusValues(c,"Innodb_buffer_pool_pages_total","Innodb_buffer_pool_pages_dirty","Innodb_buffer_pool_pages_free","Innodb_deadlocks");
            s.TryGetValue("Innodb_buffer_pool_pages_total",out var total);s.TryGetValue("Innodb_buffer_pool_pages_dirty",out var dirty);s.TryGetValue("Innodb_buffer_pool_pages_free",out var free);s.TryGetValue("Innodb_deadlocks",out var deadlocks);
            var dirtyPct=total>0?dirty*100.0/total:0;
            list.Add(new(){CheckId="MYSQL.INNODB.BUFFER",Engine=DatabaseEngine.MySqlMariaDb,Category=AssessmentCategory.Performance,Title="InnoDB Buffer Pool",Status=dirtyPct>=60?"WARNING":"OK",Severity=dirtyPct>=60?3:0,Summary=$"{dirtyPct:0.0}% dirty buffer pages",Evidence=$"total={total}; dirty={dirty}; free={free}",WhyItMatters="Buffer-pool pressure and dirty-page accumulation can increase checkpoint and storage pressure.",RecommendedAction="Correlate with write workload, flushing and I/O before changing InnoDB memory settings.",Verification="Compare status deltas during a representative workload window.",Capability="AVAILABLE",ReadOnly=true});
            list.Add(new(){CheckId="MYSQL.INNODB.DEADLOCKS",Engine=DatabaseEngine.MySqlMariaDb,Category=AssessmentCategory.Transactions,Title="InnoDB Deadlocks",Status="INFO",Severity=1,Summary=$"{deadlocks} deadlock(s) since status reset/startup",Evidence="Innodb_deadlocks is cumulative; use deltas before judging current severity.",WhyItMatters="Deadlocks indicate conflicting transactional access patterns.",RecommendedAction="Capture the deadlock graph/log evidence and identify the involved statements before changing isolation or indexes.",Verification="Monitor the delta after remediation.",Capability=s.ContainsKey("Innodb_deadlocks")?"AVAILABLE":"UNAVAILABLE",ReadOnly=true});
        }catch(OperationCanceledException){throw;}catch(Exception ex){list.Add(Error("MYSQL.INNODB.STATUS",AssessmentCategory.Performance,"InnoDB Status Counters",ex));}
        return list;
    }

    private async Task<AssessmentCheck> Replication(MySqlConnection c,bool maria,int major)
    {
        try
        {
            var sql=maria||major<8?"SHOW SLAVE STATUS":"SHOW REPLICA STATUS";
            await using var q=new MySqlCommand(sql,c){CommandTimeout=30};await using var r=await q.ExecuteReaderAsync();
            if(!await r.ReadAsync(_cancellationToken))return Info("MYSQL.HA.REPLICATION",AssessmentCategory.HighAvailability,"Replication","Standalone / no replica status","No replication receiver status returned.");
            string Read(params string[] names){foreach(var n in names){try{var i=r.GetOrdinal(n);return r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";}catch{}}return "";}
            var io=Read("Replica_IO_Running","Slave_IO_Running");var sqlRun=Read("Replica_SQL_Running","Slave_SQL_Running");
            var lag=Read("Seconds_Behind_Source","Seconds_Behind_Master");var ok=io.Equals("Yes",StringComparison.OrdinalIgnoreCase)&&sqlRun.Equals("Yes",StringComparison.OrdinalIgnoreCase);
            return new(){CheckId="MYSQL.HA.REPLICATION",Engine=DatabaseEngine.MySqlMariaDb,Category=AssessmentCategory.HighAvailability,Title="Replication",Status=ok?"OK":"WARNING",Severity=ok?0:3,Summary=$"IO={io} SQL={sqlRun}",Evidence=$"LagSeconds={lag} | command={sql}",WhyItMatters="Replication health directly affects resilience and recovery objectives.",RecommendedAction="Review receiver/applier errors and network/SQL lag before restarting or reconfiguring replication.",Verification="Confirm both threads are healthy and lag returns to an acceptable level.",Capability="AVAILABLE",ReadOnly=true};
        }catch(OperationCanceledException){throw;}catch(Exception ex){return Error("MYSQL.HA.REPLICATION",AssessmentCategory.HighAvailability,"Replication",ex);}
    }

    private async Task<Dictionary<string,long>> StatusValues(MySqlConnection c,params string[] names)
    {
        var dict=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        var quoted=string.Join(",",names.Select(n=>$"'{n.Replace("'","''")}'"));
        await using var q=new MySqlCommand($"SHOW GLOBAL STATUS WHERE Variable_name IN ({quoted})",c){CommandTimeout=30};
        await using var r=await q.ExecuteReaderAsync();
        while(await r.ReadAsync())if(long.TryParse(S(r,1),out var v))dict[S(r,0)]=v;
        return dict;
    }

    private string Cs(string host,uint port)=>new MySqlConnectionStringBuilder{Server=host,Port=port,Database=p.DatabaseOrService,UserID=p.Username??"",Password=p.Password??"",ConnectionTimeout=8,DefaultCommandTimeout=30,Pooling=false}.ConnectionString;
    private PrivateKeyFile LoadKey()=>string.IsNullOrEmpty(p.SshKeyPassphrase)?new PrivateKeyFile(p.SshPrivateKeyPath!):new PrivateKeyFile(p.SshPrivateKeyPath!,p.SshKeyPassphrase);
    private (SshClient Client,ForwardedPortLocal Port)? Tunnel()
    {
        if(!p.UseSshTunnel)return null;
        if(string.IsNullOrWhiteSpace(p.SshHost)||string.IsNullOrWhiteSpace(p.SshUsername)||string.IsNullOrWhiteSpace(p.SshPrivateKeyPath)||!File.Exists(p.SshPrivateKeyPath))throw new InvalidOperationException("SSH assessment requires a valid SSH host, user and private key.");
        var client=new SshClient(p.SshHost,p.SshPort??22,p.SshUsername,LoadKey());client.Connect();
        var port=new ForwardedPortLocal("127.0.0.1",0,p.Host,(uint)(p.Port??3306));client.AddForwardedPort(port);port.Start();return(client,port);
    }
    private async Task<T> WithConnection<T>(Func<MySqlConnection,Task<T>> work,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var t=Tunnel();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var host=t is null?p.Host:"127.0.0.1";
            var port=t is null?(uint)(p.Port??3306):t.Value.Port.BoundPort;
            await using var c=new MySqlConnection(Cs(host,port));
            await c.OpenAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await work(c);
        }
        finally
        {
            if(t is not null){t.Value.Port.Stop();t.Value.Client.Disconnect();t.Value.Port.Dispose();t.Value.Client.Dispose();}
        }
    }

    private async Task<string> Scalar(MySqlConnection c,string sql){_cancellationToken.ThrowIfCancellationRequested();await using var q=new MySqlCommand(sql,c);return Convert.ToString(await q.ExecuteScalarAsync(_cancellationToken))??"";}
    private async Task<AssessmentCheck> Q(MySqlConnection c,string id,AssessmentCategory cat,string title,string sql)
    {
        var st=DateTime.Now;
        try{_cancellationToken.ThrowIfCancellationRequested();await using var q=new MySqlCommand(sql,c){CommandTimeout=30};await using var r=await q.ExecuteReaderAsync(_cancellationToken);await r.ReadAsync(_cancellationToken);var status=S(r,0);return Make(id,cat,title,status,S(r,1),S(r,2),(long)(DateTime.Now-st).TotalMilliseconds);}
        catch(OperationCanceledException){throw;}
        catch(MySqlException ex) when(ex.Number is 1044 or 1045 or 1142 or 1227){return NoPermission(id,cat,title,ex.Message);}
        catch(MySqlException ex) when(ex.Number is 1054 or 1146 or 1305 or 1064){return Unsupported(id,cat,title,ex.Message);}
        catch(Exception ex){return Error(id,cat,title,ex);}
    }
    private static AssessmentCheck Make(string id,AssessmentCategory c,string title,string status,string summary,string evidence,long ms)=>new(){CheckId=id,Engine=DatabaseEngine.MySqlMariaDb,Category=c,Title=title,Status=status,Severity=Severity(status),Summary=summary,Evidence=evidence,WhyItMatters=Why(c),RecommendedAction=Action(c,status),Verification="Re-run this check after remediation and compare evidence.",Capability="AVAILABLE",ReadOnly=true,DurationMs=ms,Timestamp=DateTime.Now};
    private static AssessmentCheck Info(string id,AssessmentCategory c,string title,string summary,string evidence)=>new(){CheckId=id,Engine=DatabaseEngine.MySqlMariaDb,Category=c,Title=title,Status="INFO",Severity=1,Summary=summary,Evidence=evidence,WhyItMatters=Why(c),RecommendedAction="Keep as baseline evidence.",Verification="Compare with future assessments.",Capability="AVAILABLE",ReadOnly=true};
    private static AssessmentCheck Unsupported(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.MySqlMariaDb,Category=c,Title=title,Status="UNSUPPORTED",Severity=1,Summary="Feature/table not available on this engine version",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="Use the legacy capability available for this version; DBACHECK will not force a server change.",Verification="Re-run after an engine upgrade if applicable.",Capability="UNSUPPORTED",ReadOnly=true};
    private static AssessmentCheck NoPermission(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.MySqlMariaDb,Category=c,Title=title,Status="NO PERMISSION",Severity=2,Summary="Insufficient privileges",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="Grant only the minimum read privilege required, or document the limitation.",Verification="Re-run the check.",Capability="NO PERMISSION",ReadOnly=true};
    private static AssessmentCheck Unavailable(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.MySqlMariaDb,Category=c,Title=title,Status="UNAVAILABLE",Severity=1,Summary="Authoritative evidence is external or unavailable",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="Integrate the actual external source rather than infer a result.",Verification="Validate against the external source.",Capability="UNAVAILABLE",ReadOnly=true};
    private static AssessmentCheck Error(string id,AssessmentCategory c,string title,Exception ex)
    {
        if(ex is MySqlException m && m.Number is 1054 or 1146 or 1305 or 1064)return Unsupported(id,c,title,m.Message);
        if(ex is MySqlException pex && pex.Number is 1044 or 1045 or 1142 or 1227)return NoPermission(id,c,title,pex.Message);
        return new(){CheckId=id,Engine=DatabaseEngine.MySqlMariaDb,Category=c,Title=title,Status="ERROR",Severity=4,Summary="MySQL/MariaDB assessment check failed",Evidence=ex.Message,WhyItMatters=Why(c),RecommendedAction="Review version compatibility and permissions; do not modify production only to satisfy the assessment.",Verification="Re-run after correcting the collector limitation.",Capability="ERROR",ReadOnly=true};
    }
    private static int Major(string version){var m=Regex.Match(version??"",@"^(\d+)");return m.Success&&int.TryParse(m.Groups[1].Value,out var n)?n:0;}
    private static string S(System.Data.Common.DbDataReader r,int i)=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";
    private static int Severity(string s)=>s switch{"CRITICAL"=>5,"ERROR"=>4,"WARNING"=>3,"NO PERMISSION"=>2,_=>s=="OK"?0:1};
    private static string Why(AssessmentCategory c)=>c switch{AssessmentCategory.Indexes=>"Index definitions and primary-key coverage affect read cost, write overhead and replication behavior.",AssessmentCategory.Transactions=>"Long transactions, locks and deadlocks affect concurrency and InnoDB history retention.",AssessmentCategory.Temporary=>"Disk temporary tables can indicate memory pressure or expensive query operations.",AssessmentCategory.HighAvailability=>"Replication health directly affects resilience and recovery objectives.",AssessmentCategory.Performance=>"InnoDB and process counters help identify current workload pressure.",AssessmentCategory.Logs=>"Binlog configuration affects recovery and replication.",_=>"This check contributes to the MySQL/MariaDB operational baseline."};
    private static string Action(AssessmentCategory c,string status)=>status=="OK"?"No immediate action. Keep as baseline evidence.":c switch{AssessmentCategory.Indexes=>"Validate constraints and real workload before adding, dropping or consolidating indexes.",AssessmentCategory.Transactions=>"Identify the owning application and SQL before terminating transactions.",AssessmentCategory.Performance=>"Use counter deltas and active SQL before tuning memory or I/O settings.",AssessmentCategory.HighAvailability=>"Review replication thread errors and lag before restarting or reconfiguring replication.",_=>"Review the evidence with the corresponding DBACHECK diagnostic module."};
    private sealed record Idx(string Schema,string Table,string Index,int NonUnique,int Seq,string Column);
}
