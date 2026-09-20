using DBACheck2.App.Models;
using Oracle.ManagedDataAccess.Client;

namespace DBACheck2.App.Providers;
public sealed class OracleProvider:IDatabaseProvider
{
 readonly ServerProfile p; readonly string cs;
 public OracleProvider(ServerProfile p){this.p=p;var port=p.Port??1521;var svc=string.IsNullOrWhiteSpace(p.DatabaseOrService)?"ORCL":p.DatabaseOrService;cs=$"User Id={p.Username};Password={p.Password};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={p.Host})(PORT={port}))(CONNECT_DATA=(SID={svc})));Connection Timeout=8;";}
 public DatabaseEngine Engine=>DatabaseEngine.Oracle; public string DisplayName=>"Oracle";
 public async Task<string> TestAsync(){await using var c=new OracleConnection(cs);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="select banner from v$version where rownum=1";return Convert.ToString(await cmd.ExecuteScalarAsync())??"Oracle connected";}
 public async Task<List<HealthItem>> QuickCheckAsync(){var x=new List<HealthItem>();await using var c=new OracleConnection(cs);await c.OpenAsync();
  var banner=await Scalar(c,"select banner from v$version where rownum=1");
  x.Add(new(){Area="ENGINE",Status="INFO",Summary=banner,Detail=$"Host: {p.Host} | SID/Service: {p.DatabaseOrService} | User: {p.Username}"});
  x.Add(await Q(c,"ROLE","select 'INFO','Database role: '||database_role,'Open mode: '||open_mode from v$database"));
  x.Add(await Q(c,"SESSIONS","select case when count(*)>=200 then 'WARNING' else 'OK' end,count(*)||' session(s)','ACTIVE='||sum(case when status='ACTIVE' then 1 else 0 end) from v$session where type='USER'"));
  x.Add(await Q(c,"BLOCKING",@"select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' blocked session(s)',nvl(max('SID '||s.sid||' waits for SID '||b.sid),'') from v$lock l1,v$lock l2,v$session s,v$session b where l1.block=0 and l2.block=1 and l1.id1=l2.id1 and l1.id2=l2.id2 and s.sid=l1.sid and b.sid=l2.sid"));
  x.Add(await Q(c,"TRANSACTIONS",@"select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' transaction(s) > 30 min',nvl(max('SID '||s.sid||' START='||to_char(t.start_time)), '') from v$transaction t,v$session s where s.taddr=t.addr and t.start_time < sysdate-(30/1440)"));
  x.Add(await Q(c,"TABLESPACE",@"select case when max(pct)>=90 then 'CRITICAL' when max(pct)>=80 then 'WARNING' else 'OK' end,round(max(pct),1)||'% max used',max(tablespace_name||'='||round(pct,1)||'%') from (select df.tablespace_name,100*(1-nvl(fs.free_mb,0)/df.total_mb) pct from (select tablespace_name,sum(bytes)/1024/1024 total_mb from dba_data_files group by tablespace_name) df,(select tablespace_name,sum(bytes)/1024/1024 free_mb from dba_free_space group by tablespace_name) fs where fs.tablespace_name(+)=df.tablespace_name)"));
  x.Add(await Q(c,"ARCHIVELOG","select case when log_mode='ARCHIVELOG' then 'OK' else 'INFO' end,'Log mode: '||log_mode,'Open mode: '||open_mode from v$database"));
  x.Add(await Q(c,"REDO","select 'INFO',count(*)||' redo log group(s)',to_char(sum(bytes)/1024/1024)||' MB allocated' from v$log"));
  x.Add(await Q(c,"INDEXES","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' unusable index(es)',nvl(max(owner||'.'||index_name),'') from dba_indexes where status='UNUSABLE'"));
  x.Add(await Q(c,"INVALID OBJECTS","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' invalid object(s)',nvl(max(owner||'.'||object_name||' '||object_type),'') from dba_objects where status='INVALID'"));
  return x;}
 static async Task<string> Scalar(OracleConnection c,string sql){await using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToString(await q.ExecuteScalarAsync())??"";}
 static async Task<HealthItem> Q(OracleConnection c,string area,string sql){try{await using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=15;await using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();return new(){Area=area,Status=Convert.ToString(r.GetValue(0))??"INFO",Summary=Convert.ToString(r.GetValue(1))??"",Detail=Convert.ToString(r.GetValue(2))??""};}catch(Exception e){return Classify(area,e);}}
 static HealthItem Classify(string area,Exception e){if(e is OracleException o){if(o.Number is 942 or 1031)return new(){Area=area,Status="NO PERMISSION",Summary="Insufficient privileges or dictionary view unavailable",Detail=o.Message};if(o.Number is 904 or 918 or 923 or 933)return new(){Area=area,Status="UNSUPPORTED",Summary="Collector SQL not supported by this Oracle version",Detail=o.Message};}return new(){Area=area,Status="ERROR",Summary="Oracle collector failed",Detail=e.Message};}
}