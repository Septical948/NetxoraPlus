using DBACheck2.App.Models;
using MySqlConnector;

namespace DBACheck2.App.Providers;
public sealed class MySqlMariaDbProvider:IDatabaseProvider
{
 readonly string cs;
 public MySqlMariaDbProvider(ServerProfile p){cs=new MySqlConnectionStringBuilder{Server=p.Host,Port=(uint)(p.Port??3306),Database=p.DatabaseOrService,UserID=p.Username??"",Password=p.Password??"",ConnectionTimeout=8,DefaultCommandTimeout=15,Pooling=false}.ConnectionString;}
 public DatabaseEngine Engine=>DatabaseEngine.MySqlMariaDb; public string DisplayName=>"MySQL / MariaDB";
 public async Task<string> TestAsync(){await using var c=new MySqlConnection(cs);await c.OpenAsync();await using var q=new MySqlCommand("select version(),database(),current_user()",c);await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();return $"{r.GetString(0)}\nDatabase: {(r.IsDBNull(1)?"N/A":r.GetString(1))} | User: {r.GetString(2)}";}
 public async Task<List<HealthItem>> QuickCheckAsync(){var x=new List<HealthItem>();await using var c=new MySqlConnection(cs);await c.OpenAsync();
  x.Add(await Q(c,"SESSIONS","select if(count(*)>=200,'WARNING','OK'),concat(count(*),' connection(s)'),concat('running=',sum(command<>'Sleep'),', sleeping=',sum(command='Sleep')) from information_schema.processlist"));
  x.Add(await Q(c,"LONG QUERIES","select if(count(*)>0,'WARNING','OK'),concat(count(*),' query(s) > 60 sec'),coalesce(group_concat(concat('id ',id,' ',time,'s') separator '; '),'') from information_schema.processlist where command<>'Sleep' and time>60"));
  x.Add(await Q(c,"DATABASE SIZE","select 'OK',concat(count(distinct table_schema),' database(s)'),coalesce(group_concat(concat(table_schema,'=',round(sum_mb,1),'MB') separator '; '),'') from (select table_schema,sum(data_length+index_length)/1024/1024 sum_mb from information_schema.tables where table_schema not in ('mysql','information_schema','performance_schema','sys') group by table_schema) s"));
  x.Add(await Q(c,"TRANSACTIONS","select if(count(*)>0,'WARNING','OK'),concat(count(*),' transaction(s) > 30 min'),coalesce(group_concat(concat('trx ',trx_id,' ',timestampdiff(minute,trx_started,now()),'m') separator '; '),'') from information_schema.innodb_trx where trx_started < now()-interval 30 minute"));
  x.Add(await Q(c,"BLOCKING","select if(count(*)>0,'WARNING','OK'),concat(count(*),' InnoDB lock wait(s)'),coalesce(group_concat(concat('requesting=',requesting_trx_id,' blocking=',blocking_trx_id) separator '; '),'') from information_schema.innodb_lock_waits"));
  x.Add(await Q(c,"TEMP USAGE","select 'INFO','Temporary tables since startup',concat('created_tmp_tables=',@@global.created_tmp_tables,', disk=',@@global.created_tmp_disk_tables)"));
  x.Add(await Q(c,"BINLOG","select if(@@log_bin=1,'OK','INFO'),concat('Binary log: ',@@log_bin),concat('format=',@@binlog_format)"));
  x.Add(await Q(c,"PERFORMANCE","select case when @@max_connections>0 and (select count(*) from information_schema.processlist)*100/@@max_connections>=80 then 'WARNING' else 'OK' end,concat((select count(*) from information_schema.processlist),' / ',@@max_connections,' connections'),concat('version=',version())"));
  x.Add(await Q(c,"REPLICATION","select 'INFO','Replication capability','Consultar SHOW REPLICA STATUS / SHOW SLAVE STATUS según variante, versión y privilegios'"));
  return x;}
 static async Task<HealthItem> Q(MySqlConnection c,string area,string sql){try{await using var q=new MySqlCommand(sql,c){CommandTimeout=15};await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();return new(){Area=area,Status=Convert.ToString(r.GetValue(0))??"INFO",Summary=Convert.ToString(r.GetValue(1))??"",Detail=Convert.ToString(r.GetValue(2))??""};}catch(Exception e){return new(){Area=area,Status="ERROR",Summary="Collector MySQL/MariaDB no disponible",Detail=e.Message};}}
}