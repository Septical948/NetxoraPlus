using DBACheck2.App.Models;
using Oracle.ManagedDataAccess.Client;

namespace DBACheck2.App.Providers;
public sealed class OracleProvider:IDatabaseProvider
{
 readonly ServerProfile p; readonly string cs;
 public OracleProvider(ServerProfile p){this.p=p;var port=p.Port??1521;var svc=string.IsNullOrWhiteSpace(p.DatabaseOrService)?"ORCL":p.DatabaseOrService;cs=$"User Id={p.Username};Password={p.Password};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={p.Host})(PORT={port}))(CONNECT_DATA=(SID={svc})));Connection Timeout=8;";}
 public DatabaseEngine Engine=>DatabaseEngine.Oracle; public string DisplayName=>"Oracle";
 public async Task<string> TestAsync(){await using var c=new OracleConnection(cs);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="select banner from v$version where rownum=1";return Convert.ToString(await cmd.ExecuteScalarAsync())??"Oracle conectado";}
 public async Task<List<HealthItem>> QuickCheckAsync(){var x=new List<HealthItem>();await using var c=new OracleConnection(cs);await c.OpenAsync();
  x.Add(await Q(c,"SESSIONS","select case when count(*)>=200 then 'WARNING' else 'OK' end,count(*)||' session(es)','ACTIVE='||sum(case when status='ACTIVE' then 1 else 0 end) from v$session where type='USER'"));
  x.Add(await Q(c,"BLOCKING","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' sesión(es) bloqueadas',nvl(listagg('SID '||sid||' blocker '||blocking_session,'; ') within group(order by sid),'') from v$session where blocking_session is not null"));
  x.Add(await Q(c,"TRANSACTIONS","select case when count(*)>0 then 'WARNING' else 'OK' end,count(*)||' transacción(es) > 30 min',nvl(listagg('SID '||s.sid||' '||round((sysdate-t.start_time)*1440)||' min','; ') within group(order by s.sid),'') from v$transaction t join v$session s on s.taddr=t.addr where (sysdate-t.start_time)*1440>30"));
  x.Add(await Q(c,"TABLESPACE","select case when max(pct)>=90 then 'CRITICAL' when max(pct)>=80 then 'WARNING' else 'OK' end,round(max(pct),1)||'% max used',nvl(listagg(tablespace_name||'='||round(pct,1)||'%','; ') within group(order by pct desc),'') from (select df.tablespace_name,100*(1-nvl(fs.free_mb,0)/df.total_mb) pct from (select tablespace_name,sum(bytes)/1024/1024 total_mb from dba_data_files group by tablespace_name) df left join (select tablespace_name,sum(bytes)/1024/1024 free_mb from dba_free_space group by tablespace_name) fs on fs.tablespace_name=df.tablespace_name)"));
  x.Add(await Q(c,"ARCHIVELOG","select case when log_mode='ARCHIVELOG' then 'OK' else 'INFO' end,'Log mode: '||log_mode,'Open mode: '||open_mode from v$database"));
  return x;}
 static async Task<HealthItem> Q(OracleConnection c,string area,string sql){try{await using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=15;await using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();return new(){Area=area,Status=Convert.ToString(r.GetValue(0))??"INFO",Summary=Convert.ToString(r.GetValue(1))??"",Detail=Convert.ToString(r.GetValue(2))??""};}catch(Exception e){return new(){Area=area,Status="ERROR",Summary="Collector Oracle no disponible",Detail=e.Message};}}
}