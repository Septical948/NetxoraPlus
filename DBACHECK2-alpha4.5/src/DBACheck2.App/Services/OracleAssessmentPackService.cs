using System.Data.OleDb;
using System.Text.RegularExpressions;
using DBACheck2.App.Models;
using Oracle.ManagedDataAccess.Client;

namespace DBACheck2.App.Services;

public sealed class OracleAssessmentPackService
{
    private readonly ServerProfile p;
    public OracleAssessmentPackService(ServerProfile profile)=>p=profile;

    public async Task<List<AssessmentCheck>> RunFullAsync()
    {
        if(p.OracleMode==OracleConnectionMode.Legacy)return await Task.Run(RunLegacy);
        if(p.OracleMode==OracleConnectionMode.Modern)return await RunModernAsync();

        try{return await RunModernAsync();}
        catch{return await Task.Run(RunLegacy);}
    }

    private string ModernCs()
    {
        var port=p.Port??1521;
        var svc=string.IsNullOrWhiteSpace(p.DatabaseOrService)?"ORCL":p.DatabaseOrService;
        return $"User Id={p.Username};Password={p.Password};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={p.Host})(PORT={port}))(CONNECT_DATA=(SID={svc})));Connection Timeout=8;";
    }

    private string LegacyCs()
    {
        var port=p.Port??1521;
        var sid=string.IsNullOrWhiteSpace(p.DatabaseOrService)?"ORCL":p.DatabaseOrService;
        var ds=$"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={p.Host})(PORT={port}))(CONNECT_DATA=(SID={sid})))";
        return $"Provider=OraOLEDB.Oracle;Data Source={ds};User ID={p.Username};Password={p.Password};OLEDB.NET=True;";
    }

    private async Task<List<AssessmentCheck>> RunModernAsync()
    {
        var x=new List<AssessmentCheck>();
        await using var c=new OracleConnection(ModernCs());await c.OpenAsync();
        var banner=await Scalar(c,"select banner from v$version where rownum=1");
        var major=Major(banner);
        x.Add(Info("ORA.PLATFORM.VERSION",AssessmentCategory.Platform,"Oracle Version",banner,$"Provider=Modern ODP.NET | Major={major}"));
        x.Add(await Q(c,"ORA.CONFIG.RESOURCES",AssessmentCategory.Configuration,"Sessions / Processes Capacity",@"select case when max(pct)>=80 then 'WARNING' else 'OK' end,round(max(pct),1)||'% max resource utilization',max(resource_name||'='||current_utilization||'/'||limit_value) from (select resource_name,current_utilization,limit_value,case when regexp_like(limit_value,'^[0-9]+$') and to_number(limit_value)>0 then current_utilization*100/to_number(limit_value) else 0 end pct from v$resource_limit where resource_name in ('sessions','processes'))"));
        x.Add(await Q(c,"ORA.TRAN.LONG",AssessmentCategory.Transactions,"Open / Long Transactions",@"select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' transaction(s) > 30 min',nvl(max('SID='||s.sid||' START='||t.start_time),'') from v$transaction t,v$session s where s.taddr=t.addr and t.start_time<to_char(sysdate-(30/1440),'MM/DD/YY HH24:MI:SS')"));
        x.Add(await Q(c,"ORA.PERF.BLOCKING",AssessmentCategory.Performance,"Blocking Chains",@"select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' blocked session(s)',nvl(max('SID '||s.sid||' waits for SID '||b.sid),'') from v$lock l1,v$lock l2,v$session s,v$session b where l1.request>0 and l2.lmode>0 and l1.id1=l2.id1 and l1.id2=l2.id2 and s.sid=l1.sid and b.sid=l2.sid"));
        x.Add(await Q(c,"ORA.CAPACITY.TABLESPACE",AssessmentCategory.Capacity,"Tablespace Usage",@"select case when max(pct)>=90 then 'CRITICAL' when max(pct)>=80 then 'WARNING' else 'OK' end,round(max(pct),1)||'% max used',max(tablespace_name||'='||round(pct,1)||'%') from (select df.tablespace_name,100*(1-nvl(fs.free_mb,0)/df.total_mb) pct from (select tablespace_name,sum(bytes)/1024/1024 total_mb from dba_data_files group by tablespace_name) df,(select tablespace_name,sum(bytes)/1024/1024 free_mb from dba_free_space group by tablespace_name) fs where fs.tablespace_name(+)=df.tablespace_name)"));
        x.Add(await Q(c,"ORA.TEMP.CAPACITY",AssessmentCategory.Temporary,"TEMP Capacity",@"select case when sum(bytes)/1024/1024/1024>=100 then 'INFO' else 'OK' end,round(sum(bytes)/1024/1024,1)||' MB TEMP allocated',nvl(max(tablespace_name),'') from dba_temp_files"));
        x.Add(await Q(c,"ORA.LOG.ARCHIVE",AssessmentCategory.Logs,"Archive Log Mode",@"select case when log_mode='ARCHIVELOG' then 'OK' else 'WARNING' end,'Log mode: '||log_mode,'Open mode: '||open_mode from v$database"));
        x.Add(await Q(c,"ORA.LOG.REDO",AssessmentCategory.Logs,"Redo Configuration",@"select 'INFO',count(*)||' redo log group(s)',to_char(round(sum(bytes)/1024/1024,1))||' MB allocated' from v$log"));
        x.Add(await Q(c,"ORA.LOG.SWITCHES",AssessmentCategory.Logs,"Redo Switch Frequency",@"select case when count(*)>100 then 'WARNING' else 'OK' end,count(*)||' log switch(es) in last 24h',nvl(to_char(max(first_time),'YYYY-MM-DD HH24:MI:SS'),'') from v$log_history where first_time>=sysdate-1"));
        x.AddRange(await OracleIndexChecksModern(c));
        x.Add(await Q(c,"ORA.OBJECT.INVALID",AssessmentCategory.Maintenance,"Invalid Objects",@"select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' invalid object(s)',nvl(max(owner||'.'||object_name||' '||object_type),'') from dba_objects where status='INVALID'"));
        x.Add(await StatsModern(c,major));
        x.Add(await JobsModern(c,major));
        x.Add(await Q(c,"ORA.CONFIG.CORE",AssessmentCategory.Configuration,"Core Memory / Optimizer Settings",@"select 'INFO','Core Oracle settings',max(case when name='sga_target' then 'sga_target='||value end)||'; '||max(case when name='pga_aggregate_target' then 'pga_aggregate_target='||value end)||'; '||max(case when name='optimizer_mode' then 'optimizer_mode='||value end) from v$parameter where name in ('sga_target','pga_aggregate_target','optimizer_mode')"));
        x.Add(major>=9?await Q(c,"ORA.HA.ROLE",AssessmentCategory.HighAvailability,"Database Role",@"select 'INFO','Database role: '||database_role,'Open mode: '||open_mode from v$database"):Unsupported("ORA.HA.ROLE",AssessmentCategory.HighAvailability,"Database Role","DATABASE_ROLE assessment is not enabled for Oracle 8i legacy capability."));
        x.Add(major>=10?await Q(c,"ORA.CAPACITY.FRA",AssessmentCategory.Capacity,"Fast Recovery Area",@"select case when space_limit>0 and space_used*100/space_limit>=85 then 'WARNING' else 'OK' end,round(case when space_limit>0 then space_used*100/space_limit else 0 end,1)||'% FRA used','used='||round(space_used/1024/1024)||'MB limit='||round(space_limit/1024/1024)||'MB' from v$recovery_file_dest"):Unsupported("ORA.CAPACITY.FRA",AssessmentCategory.Capacity,"Fast Recovery Area","FRA was introduced after Oracle 9i; not applicable to this legacy release."));
        x.Add(major>=9?await Q(c,"ORA.BACKUP.RMAN",AssessmentCategory.Backup,"RMAN Backup Evidence",@"select case when max(completion_time) is null then 'WARNING' when max(completion_time)<sysdate-2 then 'WARNING' else 'OK' end,'Last RMAN backup: '||nvl(to_char(max(completion_time),'YYYY-MM-DD HH24:MI:SS'),'none'),'v$backup_set evidence' from v$backup_set"):Unavailable("ORA.BACKUP.RMAN",AssessmentCategory.Backup,"RMAN Backup Evidence","Oracle 8i backup evidence requires legacy RMAN/catalog integration; no universal authoritative query is assumed."));
        x.Add(major>=9?await Q(c,"ORA.UNDO.MODE",AssessmentCategory.Temporary,"UNDO Management",@"select case when value='AUTO' then 'OK' else 'INFO' end,'UNDO management: '||value,'undo_management parameter' from v$parameter where name='undo_management'"):Unavailable("ORA.UNDO.MODE",AssessmentCategory.Temporary,"UNDO / Rollback Segments","Oracle 8i uses rollback segments rather than automatic UNDO management."));
        return x;
    }

    private List<AssessmentCheck> RunLegacy()
    {
        var x=new List<AssessmentCheck>();
        using var c=new OleDbConnection(LegacyCs());
        try{c.Open();}catch(Exception ex){throw new InvalidOperationException("Oracle Legacy assessment requires a working OraOLEDB.Oracle client with matching process architecture.",ex);}
        var banner=Scalar(c,"select banner from v$version where rownum=1");var major=Major(banner);
        x.Add(Info("ORA.PLATFORM.VERSION",AssessmentCategory.Platform,"Oracle Version",banner,$"Provider=OraOLEDB Legacy | Major={major}"));
        x.Add(Q(c,"ORA.CONFIG.RESOURCES",AssessmentCategory.Configuration,"Sessions / Processes Capacity","select decode(sign(max(pct)-79),1,'WARNING','OK'),to_char(round(max(pct),1))||'% max resource utilization',max(resource_name||'='||to_char(current_utilization)||'/'||limit_value) from (select resource_name,current_utilization,limit_value,decode(translate(limit_value,'0123456789',''),' ',decode(to_number(limit_value),0,0,current_utilization*100/to_number(limit_value)),0) pct from v$resource_limit where resource_name in ('sessions','processes'))"));
        x.Add(Q(c,"ORA.TRAN.OPEN",AssessmentCategory.Transactions,"Open Transactions","select 'INFO',to_char(count(*))||' open transaction(s)',nvl(max('SID='||to_char(s.sid)||' START='||t.start_time),'') from v$transaction t,v$session s where s.taddr=t.addr"));
        x.Add(Q(c,"ORA.PERF.BLOCKING",AssessmentCategory.Performance,"Blocking Chains","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' blocked session(s)',nvl(max('SID '||to_char(s.sid)||' waits for SID '||to_char(b.sid)),'') from v$lock l1,v$lock l2,v$session s,v$session b where l1.request>0 and l2.lmode>0 and l1.id1=l2.id1 and l1.id2=l2.id2 and s.sid=l1.sid and b.sid=l2.sid"));
        x.Add(Q(c,"ORA.CAPACITY.TABLESPACE",AssessmentCategory.Capacity,"Tablespace Usage","select decode(sign(max(pct)-89),1,'CRITICAL',decode(sign(max(pct)-79),1,'WARNING','OK')),to_char(round(max(pct),1))||'% max used',max(tablespace_name||'='||to_char(round(pct,1))||'%') from (select df.tablespace_name,100*(1-nvl(fs.free_mb,0)/df.total_mb) pct from (select tablespace_name,sum(bytes)/1024/1024 total_mb from dba_data_files group by tablespace_name) df,(select tablespace_name,sum(bytes)/1024/1024 free_mb from dba_free_space group by tablespace_name) fs where fs.tablespace_name(+)=df.tablespace_name)"));
        x.Add(Q(c,"ORA.TEMP.CAPACITY",AssessmentCategory.Temporary,"TEMP Capacity","select 'INFO',to_char(round(sum(bytes)/1024/1024,1))||' MB TEMP allocated',nvl(max(tablespace_name),'') from dba_temp_files"));
        x.Add(Q(c,"ORA.LOG.ARCHIVE",AssessmentCategory.Logs,"Archive Log Mode","select decode(log_mode,'ARCHIVELOG','OK','WARNING'),'Log mode: '||log_mode,'Legacy Oracle' from v$database"));
        x.Add(Q(c,"ORA.LOG.REDO",AssessmentCategory.Logs,"Redo Configuration","select 'INFO',to_char(count(*))||' redo log group(s)',to_char(round(sum(bytes)/1024/1024,1))||' MB allocated' from v$log"));
        x.Add(Q(c,"ORA.LOG.SWITCHES",AssessmentCategory.Logs,"Redo Switch Frequency","select decode(sign(count(*)-100),1,'WARNING','OK'),to_char(count(*))||' log switch(es) in last 24h',nvl(to_char(max(first_time),'YYYY-MM-DD HH24:MI:SS'),'') from v$log_history where first_time>=sysdate-1"));
        x.AddRange(OracleIndexChecksLegacy(c));
        x.Add(Q(c,"ORA.OBJECT.INVALID",AssessmentCategory.Maintenance,"Invalid Objects","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' invalid object(s)',nvl(max(owner||'.'||object_name||' '||object_type),'') from dba_objects where status='INVALID'"));
        x.Add(StatsLegacy(c,major));
        x.Add(JobsLegacy(c,major));
        x.Add(Q(c,"ORA.CONFIG.LEGACY",AssessmentCategory.Configuration,"Core Legacy Settings","select 'INFO','Core Oracle settings',max(decode(name,'optimizer_mode','optimizer_mode='||value,null))||'; '||max(decode(name,'shared_pool_size','shared_pool_size='||value,null))||'; '||max(decode(name,'db_block_buffers','db_block_buffers='||value,null)) from v$parameter where name in ('optimizer_mode','shared_pool_size','db_block_buffers')"));
        x.Add(major>=9?Q(c,"ORA.HA.ROLE",AssessmentCategory.HighAvailability,"Database Role","select 'INFO','Database role: '||database_role,'Legacy role evidence' from v$database"):Unsupported("ORA.HA.ROLE",AssessmentCategory.HighAvailability,"Database Role","Oracle 8i does not expose the same Data Guard role model used by later releases."));
        x.Add(major>=10?Q(c,"ORA.CAPACITY.FRA",AssessmentCategory.Capacity,"Fast Recovery Area","select decode(sign(case when space_limit>0 then space_used*100/space_limit else 0 end-84),1,'WARNING','OK'),to_char(round(case when space_limit>0 then space_used*100/space_limit else 0 end,1))||'% FRA used','FRA' from v$recovery_file_dest"):Unsupported("ORA.CAPACITY.FRA",AssessmentCategory.Capacity,"Fast Recovery Area","FRA is not available on this Oracle release."));
        x.Add(major>=9?Q(c,"ORA.BACKUP.RMAN",AssessmentCategory.Backup,"RMAN Backup Evidence","select decode(max(completion_time),null,'WARNING',decode(sign(sysdate-max(completion_time)-2),1,'WARNING','OK')),'Last RMAN backup: '||nvl(to_char(max(completion_time),'YYYY-MM-DD HH24:MI:SS'),'none'),'v$backup_set evidence' from v$backup_set"):Unavailable("ORA.BACKUP.RMAN",AssessmentCategory.Backup,"RMAN Backup Evidence","Oracle 8i backup evidence requires legacy RMAN/catalog integration."));
        x.Add(major>=9?Q(c,"ORA.UNDO.MODE",AssessmentCategory.Temporary,"UNDO Management","select decode(value,'AUTO','OK','INFO'),'UNDO management: '||value,'undo_management parameter' from v$parameter where name='undo_management'"):Unavailable("ORA.UNDO.MODE",AssessmentCategory.Temporary,"UNDO / Rollback Segments","Oracle 8i uses rollback segments rather than automatic UNDO management."));
        return x;
    }

    private static async Task<List<AssessmentCheck>> OracleIndexChecksModern(OracleConnection c)
    {
        try
        {
            var rows=new List<(string Owner,string Table,string Index,string Status,int Pos,string Col)>();
            await using var q=c.CreateCommand();q.CommandText=@"select i.owner,i.table_name,i.index_name,i.status,c.column_position,c.column_name from dba_indexes i join dba_ind_columns c on c.index_owner=i.owner and c.index_name=i.index_name and c.table_owner=i.table_owner and c.table_name=i.table_name where i.owner not in ('SYS','SYSTEM') order by i.owner,i.table_name,i.index_name,c.column_position";
            await using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync())rows.Add((S(r,0),S(r,1),S(r,2),S(r,3),Convert.ToInt32(r.GetValue(4)),S(r,5)));
            return IndexChecks(rows);
        }catch(Exception ex){return new(){OracleError("ORA.INDEX.INVENTORY",AssessmentCategory.Indexes,"Index Inventory",ex)};}
    }
    private static List<AssessmentCheck> OracleIndexChecksLegacy(OleDbConnection c)
    {
        try
        {
            var rows=new List<(string Owner,string Table,string Index,string Status,int Pos,string Col)>();
            using var q=new OleDbCommand("select i.owner,i.table_name,i.index_name,i.status,c.column_position,c.column_name from dba_indexes i,dba_ind_columns c where c.index_owner=i.owner and c.index_name=i.index_name and c.table_owner=i.table_owner and c.table_name=i.table_name and i.owner not in ('SYS','SYSTEM') order by i.owner,i.table_name,i.index_name,c.column_position",c);
            using var r=q.ExecuteReader();if(r is not null)while(r.Read())rows.Add((S(r,0),S(r,1),S(r,2),S(r,3),Convert.ToInt32(r.GetValue(4)),S(r,5)));
            return IndexChecks(rows);
        }catch(Exception ex){return new(){OracleError("ORA.INDEX.INVENTORY",AssessmentCategory.Indexes,"Index Inventory",ex)};}
    }
    private static List<AssessmentCheck> IndexChecks(List<(string Owner,string Table,string Index,string Status,int Pos,string Col)> rows)
    {
        var defs=rows.GroupBy(x=>new{x.Owner,x.Table,x.Index,x.Status}).Select(g=>new{g.Key.Owner,g.Key.Table,g.Key.Index,g.Key.Status,Cols=string.Join(",",g.OrderBy(x=>x.Pos).Select(x=>x.Col))}).ToList();
        var unusable=defs.Where(x=>x.Status.Equals("UNUSABLE",StringComparison.OrdinalIgnoreCase)).ToList();
        var dups=defs.GroupBy(x=>$"{x.Owner}|{x.Table}|{x.Cols}",StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).ToList();
        return new(){
            new(){CheckId="ORA.INDEX.UNUSABLE",Engine=DatabaseEngine.Oracle,Category=AssessmentCategory.Indexes,Title="Unusable Indexes",Status=unusable.Count>0?"WARNING":"OK",Severity=unusable.Count>0?3:0,Summary=$"{unusable.Count} unusable index(es)",Evidence=string.Join("; ",unusable.Take(100).Select(x=>$"{x.Owner}.{x.Table}.{x.Index}")),WhyItMatters="Unusable indexes can break expected access paths or DML depending on context.",RecommendedAction="Validate why the index became unusable and rebuild only when operationally appropriate.",Verification="Confirm STATUS is VALID after remediation.",Capability="AVAILABLE",ReadOnly=true},
            new(){CheckId="ORA.INDEX.DUPLICATE",Engine=DatabaseEngine.Oracle,Category=AssessmentCategory.Indexes,Title="Duplicate Index Definitions",Status=dups.Count>0?"WARNING":"OK",Severity=dups.Count>0?3:0,Summary=$"{dups.Count} duplicate index definition group(s)",Evidence=string.Join("; ",dups.Take(100).Select(g=>{var first=g.First();return $"{first.Owner}.{first.Table} ({first.Cols}) => {string.Join(",",g.Select(x=>x.Index))}";})),WhyItMatters="Equivalent index definitions can add storage and DML maintenance cost.",RecommendedAction="Review uniqueness, constraints and workload before dropping or consolidating any index.",Verification="Re-run the assessment and confirm only intended definitions remain.",Capability="AVAILABLE",ReadOnly=true}
        };
    }

    private static async Task<AssessmentCheck> StatsModern(OracleConnection c,int major)=>major>=10
        ? await Q(c,"ORA.MAINT.STATS",AssessmentCategory.Maintenance,"Stale Statistics","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' table(s) with stale statistics',nvl(max(owner||'.'||table_name),'') from dba_tab_statistics where stale_stats='YES' and owner not in ('SYS','SYSTEM')")
        : await Q(c,"ORA.MAINT.STATS",AssessmentCategory.Maintenance,"Statistics Recency","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' table(s) not analyzed in 30d or never',nvl(max(owner||'.'||table_name),'') from dba_tables where owner not in ('SYS','SYSTEM') and (last_analyzed is null or last_analyzed<sysdate-30)");
    private static AssessmentCheck StatsLegacy(OleDbConnection c,int major)=>major>=10
        ? Q(c,"ORA.MAINT.STATS",AssessmentCategory.Maintenance,"Stale Statistics","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' table(s) with stale statistics',nvl(max(owner||'.'||table_name),'') from dba_tab_statistics where stale_stats='YES' and owner not in ('SYS','SYSTEM')")
        : Q(c,"ORA.MAINT.STATS",AssessmentCategory.Maintenance,"Statistics Recency","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' table(s) not analyzed in 30d or never',nvl(max(owner||'.'||table_name),'') from dba_tables where owner not in ('SYS','SYSTEM') and (last_analyzed is null or last_analyzed<sysdate-30)");
    private static Task<AssessmentCheck> JobsModern(OracleConnection c,int major)=>major>=10
        ? Q(c,"ORA.MAINT.JOBS",AssessmentCategory.Maintenance,"Scheduler Jobs","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' enabled scheduler job(s) in failed/broken state',nvl(max(owner||'.'||job_name||' '||state),'') from dba_scheduler_jobs where enabled='TRUE' and state in ('BROKEN','FAILED')")
        : Q(c,"ORA.MAINT.JOBS",AssessmentCategory.Maintenance,"DBMS_JOB Jobs","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' broken DBMS_JOB job(s)',nvl(to_char(max(job)),'') from dba_jobs where broken='Y'");
    private static AssessmentCheck JobsLegacy(OleDbConnection c,int major)=>major>=10
        ? Q(c,"ORA.MAINT.JOBS",AssessmentCategory.Maintenance,"Scheduler Jobs","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' enabled scheduler job(s) in failed/broken state',nvl(max(owner||'.'||job_name||' '||state),'') from dba_scheduler_jobs where enabled='TRUE' and state in ('BROKEN','FAILED')")
        : Q(c,"ORA.MAINT.JOBS",AssessmentCategory.Maintenance,"DBMS_JOB Jobs","select decode(count(*),0,'OK','WARNING'),to_char(count(*))||' broken DBMS_JOB job(s)',nvl(to_char(max(job)),'') from dba_jobs where broken='Y'");

    private static async Task<string> Scalar(OracleConnection c,string sql){await using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToString(await q.ExecuteScalarAsync())??"";}
    private static string Scalar(OleDbConnection c,string sql){using var q=new OleDbCommand(sql,c);return Convert.ToString(q.ExecuteScalar())??"";}
    private static async Task<AssessmentCheck> Q(OracleConnection c,string id,AssessmentCategory cat,string title,string sql)
    {
        var st=DateTime.Now;try{await using var q=c.CreateCommand();q.CommandText=sql;q.CommandTimeout=30;await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();var status=S(r,0);return Make(id,cat,title,status,S(r,1),S(r,2),(long)(DateTime.Now-st).TotalMilliseconds);}
        catch(Exception ex){return OracleError(id,cat,title,ex);}
    }
    private static AssessmentCheck Q(OleDbConnection c,string id,AssessmentCategory cat,string title,string sql)
    {
        var st=DateTime.Now;try{using var q=new OleDbCommand(sql,c){CommandTimeout=30};using var r=q.ExecuteReader();if(r is null||!r.Read())return Make(id,cat,title,"INFO","No data","",(long)(DateTime.Now-st).TotalMilliseconds);return Make(id,cat,title,S(r,0),S(r,1),S(r,2),(long)(DateTime.Now-st).TotalMilliseconds);}
        catch(Exception ex){return OracleError(id,cat,title,ex);}
    }
    private static AssessmentCheck Make(string id,AssessmentCategory c,string title,string status,string summary,string evidence,long ms)=>new(){CheckId=id,Engine=DatabaseEngine.Oracle,Category=c,Title=title,Status=status,Severity=Severity(status),Summary=summary,Evidence=evidence,WhyItMatters=Why(c),RecommendedAction=Action(c,status),Verification="Re-run this check after remediation and compare evidence.",Capability="AVAILABLE",ReadOnly=true,DurationMs=ms,Timestamp=DateTime.Now};
    private static AssessmentCheck OracleError(string id,AssessmentCategory c,string title,Exception ex)
    {
        var msg=ex.Message.ToUpperInvariant();var noPerm=msg.Contains("ORA-01031")||msg.Contains("ORA-00942");var unsupported=msg.Contains("ORA-00904")||msg.Contains("ORA-00923")||msg.Contains("ORA-00933")||msg.Contains("ORA-00911");
        var status=noPerm?"NO PERMISSION":unsupported?"UNSUPPORTED":"ERROR";
        return new(){CheckId=id,Engine=DatabaseEngine.Oracle,Category=c,Title=title,Status=status,Severity=Severity(status),Summary=noPerm?"Insufficient privileges":unsupported?"Feature/view not available on this Oracle version":"Oracle assessment check failed",Evidence=ex.Message,WhyItMatters=Why(c),RecommendedAction=noPerm?"Grant only the minimum read privilege required, or document the limitation.":"Use the capability available on this Oracle version; do not modify production only to satisfy the assessment.",Verification="Re-run the check.",Capability=status,ReadOnly=true};
    }
    private static AssessmentCheck Info(string id,AssessmentCategory c,string title,string summary,string evidence)=>new(){CheckId=id,Engine=DatabaseEngine.Oracle,Category=c,Title=title,Status="INFO",Severity=1,Summary=summary,Evidence=evidence,WhyItMatters=Why(c),RecommendedAction="Keep as baseline evidence.",Verification="Compare with future assessments.",Capability="AVAILABLE",ReadOnly=true};
    private static AssessmentCheck Unsupported(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.Oracle,Category=c,Title=title,Status="UNSUPPORTED",Severity=1,Summary="Not supported by detected Oracle release",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="No database change required. Use the legacy capability available for this release.",Verification="Re-run after an engine upgrade if applicable.",Capability="UNSUPPORTED",ReadOnly=true};
    private static AssessmentCheck Unavailable(string id,AssessmentCategory c,string title,string detail)=>new(){CheckId=id,Engine=DatabaseEngine.Oracle,Category=c,Title=title,Status="UNAVAILABLE",Severity=1,Summary="Authoritative evidence unavailable in this pack",Evidence=detail,WhyItMatters=Why(c),RecommendedAction="Use the authoritative RMAN/catalog/OS source instead of inferring a result.",Verification="Validate against the external source.",Capability="UNAVAILABLE",ReadOnly=true};
    private static int Major(string banner){var m=Regex.Match(banner??"",@"Release\s+(\d+)",RegexOptions.IgnoreCase);return m.Success&&int.TryParse(m.Groups[1].Value,out var n)?n:0;}
    private static int Severity(string s)=>s switch{"CRITICAL"=>5,"ERROR"=>4,"WARNING"=>3,"NO PERMISSION"=>2,_=>s=="OK"?0:1};
    private static string S(System.Data.Common.DbDataReader r,int i)=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";
    private static string Why(AssessmentCategory c)=>c switch{AssessmentCategory.Indexes=>"Index validity and redundant definitions affect access paths, storage and DML cost.",AssessmentCategory.Capacity=>"Tablespace/FRA pressure can interrupt allocations and recovery operations.",AssessmentCategory.Logs=>"Redo/archive health affects recoverability and standby transport.",AssessmentCategory.Maintenance=>"Statistics, jobs and object validity affect optimizer behavior and scheduled operations.",AssessmentCategory.HighAvailability=>"Database role and transport capability define resilience.",AssessmentCategory.Transactions=>"Open transactions can retain locks and UNDO.",_=>"This check contributes to the Oracle operational baseline."};
    private static string Action(AssessmentCategory c,string status)=>status=="OK"?"No immediate action. Keep as baseline evidence.":c switch{AssessmentCategory.Indexes=>"Validate constraints and workload before rebuilding or removing indexes.",AssessmentCategory.Capacity=>"Review growth, autoextend and filesystem/ASM capacity before resizing.",AssessmentCategory.Logs=>"Review archive destination, redo sizing and Data Guard implications before changing configuration.",AssessmentCategory.Transactions=>"Identify session owner/application before interrupting work.",_=>"Review the evidence with the corresponding DBACHECK diagnostic module."};

}
