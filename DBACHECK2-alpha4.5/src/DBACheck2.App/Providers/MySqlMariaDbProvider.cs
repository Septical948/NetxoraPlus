using System.IO;
using DBACheck2.App.Models;
using MySqlConnector;
using Renci.SshNet;

namespace DBACheck2.App.Providers;
public sealed class MySqlMariaDbProvider:IDatabaseProvider
{
 readonly ServerProfile profile;
 public MySqlMariaDbProvider(ServerProfile p){profile=p;}
 private string Cs(string host,uint port)=>new MySqlConnectionStringBuilder{Server=host,Port=port,Database=profile.DatabaseOrService,UserID=profile.Username??"",Password=profile.Password??"",ConnectionTimeout=8,DefaultCommandTimeout=15,Pooling=false}.ConnectionString;
 private PrivateKeyFile LoadPrivateKey()
 {
  var path=profile.SshPrivateKeyPath??"";
  if(!File.Exists(path))throw new InvalidOperationException($"SSH KEY FILE FAILED: file not found: {path}");
  try{return string.IsNullOrEmpty(profile.SshKeyPassphrase)?new PrivateKeyFile(path):new PrivateKeyFile(path,profile.SshKeyPassphrase);}
  catch(Exception e){throw new InvalidOperationException($"PPK LOAD FAILED: {e.Message} Check the key passphrase and confirm the same .ppk opens in PuTTYgen.",e);}
 }
 private (SshClient Client,ForwardedPortLocal Port)? OpenTunnel()
 {
  if(!profile.UseSshTunnel)return null;
  if(string.IsNullOrWhiteSpace(profile.SshHost)||string.IsNullOrWhiteSpace(profile.SshUsername)||string.IsNullOrWhiteSpace(profile.SshPrivateKeyPath))throw new InvalidOperationException("SSH tunnel requires SSH host, SSH user and private key path.");
  var key=LoadPrivateKey();
  var client=new SshClient(profile.SshHost,profile.SshPort??22,profile.SshUsername,key);
  try{client.Connect();}catch(Exception e){client.Dispose();throw new InvalidOperationException($"SSH AUTH FAILED: key loaded correctly, but SSH authentication/connection to {profile.SshHost}:{profile.SshPort??22} as {profile.SshUsername} failed: {e.Message}",e);}
  try{var port=new ForwardedPortLocal("127.0.0.1",0,profile.Host,(uint)(profile.Port??3306));client.AddForwardedPort(port);port.Start();return(client,port);}catch(Exception e){client.Disconnect();client.Dispose();throw new InvalidOperationException($"SSH TUNNEL FAILED: SSH authenticated, but forwarding to {profile.Host}:{profile.Port??3306} failed: {e.Message}",e);}
 }
 private async Task<T> WithConnection<T>(Func<MySqlConnection,Task<T>> work)
 {
  var tunnel=OpenTunnel();try{var host=tunnel is null?profile.Host:"127.0.0.1";var port=tunnel is null?(uint)(profile.Port??3306):tunnel.Value.Port.BoundPort;await using var c=new MySqlConnection(Cs(host,port));await c.OpenAsync();return await work(c);}finally{if(tunnel is not null){tunnel.Value.Port.Stop();tunnel.Value.Client.Disconnect();tunnel.Value.Port.Dispose();tunnel.Value.Client.Dispose();}}
 }
 public DatabaseEngine Engine=>DatabaseEngine.MySqlMariaDb; public string DisplayName=>"MySQL / MariaDB";
 public async Task<string> TestAsync(){try{return await WithConnection(async c=>{await using var q=new MySqlCommand("select version(),database(),current_user()",c);await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();return $"{(profile.UseSshTunnel?"PPK LOAD: OK\\nSSH AUTH: OK\\nSSH TUNNEL: OK\\n":"")}MYSQL TCP/AUTH: OK\\n{r.GetString(0)}\\nDatabase: {(r.IsDBNull(1)?"N/A":r.GetString(1))} | User: {r.GetString(2)}";});}catch(MySqlException e){throw new InvalidOperationException($"MYSQL TCP/AUTH FAILED: SSH tunnel {(profile.UseSshTunnel?"was established, but ":"")}MySQL rejected or could not complete the connection: {e.Message}",e);}}
 public Task<List<HealthItem>> QuickCheckAsync()=>WithConnection(async c=>{var x=new List<HealthItem>();
  var version=await Scalar(c,"select version()"); var maria=version.Contains("MariaDB",StringComparison.OrdinalIgnoreCase);
  x.Add(new(){Area="ENGINE",Status="INFO",Summary=$"{(maria?"MariaDB":"MySQL")} {version}",Detail=$"Host: {profile.Host} | Database: {profile.DatabaseOrService} | User: {profile.Username}"});
  x.Add(await Q(c,"SESSIONS","select if(count(*)>=200,'WARNING','OK'),concat(count(*),' connection(s)'),concat('running=',sum(command<>'Sleep'),', sleeping=',sum(command='Sleep')) from information_schema.processlist"));
  x.Add(await Q(c,"LONG QUERIES","select if(count(*)>0,'WARNING','OK'),concat(count(*),' query(s) > 60 sec'),coalesce(group_concat(concat('id ',id,' ',time,'s') separator '; '),'') from information_schema.processlist where command<>'Sleep' and time>60"));
  x.Add(await Q(c,"DATABASE SIZE","select 'OK',concat(count(distinct table_schema),' database(s)'),coalesce(group_concat(concat(table_schema,'=',round(sum_mb,1),'MB') separator '; '),'') from (select table_schema,sum(data_length+index_length)/1024/1024 sum_mb from information_schema.tables where table_schema not in ('mysql','information_schema','performance_schema','sys') group by table_schema) s"));
  x.Add(await Q(c,"TRANSACTIONS","select if(count(*)>0,'WARNING','OK'),concat(count(*),' transaction(s) > 30 min'),coalesce(group_concat(concat('trx ',trx_id,' ',timestampdiff(minute,trx_started,now()),'m') separator '; '),'') from information_schema.innodb_trx where trx_started < now()-interval 30 minute"));
  x.Add(await Q(c,"BLOCKING","select if(count(*)>0,'WARNING','OK'),concat(count(*),' InnoDB lock wait(s)'),coalesce(group_concat(concat('requesting=',requesting_trx_id,' blocking=',blocking_trx_id) separator '; '),'') from information_schema.innodb_lock_waits"));
  x.Add(await TempUsage(c));
  x.Add(await Q(c,"BINLOG","select if(@@log_bin=1,'OK','INFO'),concat('Binary log: ',@@log_bin),concat('format=',@@binlog_format)"));
  x.Add(await Q(c,"PERFORMANCE","select case when @@max_connections>0 and (select count(*) from information_schema.processlist)*100/@@max_connections>=80 then 'WARNING' else 'OK' end,concat((select count(*) from information_schema.processlist),' / ',@@max_connections,' connections'),concat('version=',version())"));
  x.Add(await Q(c,"INDEXES","select 'INFO',concat(count(*),' index definition(s)'),concat(count(distinct table_schema), ' schema(s)') from information_schema.statistics where table_schema not in ('mysql','information_schema','performance_schema','sys')"));
  x.Add(await Replication(c,maria,version));
  return x;});
 static async Task<string> Scalar(MySqlConnection c,string sql){await using var q=new MySqlCommand(sql,c);return Convert.ToString(await q.ExecuteScalarAsync())??"";}
 static async Task<HealthItem> TempUsage(MySqlConnection c){try{long total=0,disk=0;await using var q=new MySqlCommand("SHOW GLOBAL STATUS WHERE Variable_name IN ('Created_tmp_tables','Created_tmp_disk_tables')",c);await using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync()){var n=Convert.ToString(r.GetValue(0))??"";long.TryParse(Convert.ToString(r.GetValue(1)),out var v);if(n.Equals("Created_tmp_tables",StringComparison.OrdinalIgnoreCase))total=v;else if(n.Equals("Created_tmp_disk_tables",StringComparison.OrdinalIgnoreCase))disk=v;}var pct=total>0?disk*100.0/total:0;return new(){Area="TEMP USAGE",Status=pct>=25?"WARNING":"INFO",Summary=$"{pct:0.0}% temp tables on disk",Detail=$"Created_tmp_tables={total}; Created_tmp_disk_tables={disk}; cumulative counters"};}catch(Exception ex){return Classify("TEMP USAGE",ex);}}
 static async Task<HealthItem> Replication(MySqlConnection c,bool maria,string version){try{var major=0;var first=(version??"").Split('.')[0];int.TryParse(first,out major);var sql=maria||major<8?"SHOW SLAVE STATUS":"SHOW REPLICA STATUS";await using var q=new MySqlCommand(sql,c){CommandTimeout=15};await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())return new(){Area="REPLICATION",Status="INFO",Summary="Standalone / no replica status",Detail=$"No replication receiver status returned. Command={sql}"};string Read(params string[] names){foreach(var n in names){try{var i=r.GetOrdinal(n);return r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";}catch{}}return "";}var io=Read("Replica_IO_Running","Slave_IO_Running");var sqlrun=Read("Replica_SQL_Running","Slave_SQL_Running");var lag=Read("Seconds_Behind_Source","Seconds_Behind_Master");var ok=io.Equals("Yes",StringComparison.OrdinalIgnoreCase)&&sqlrun.Equals("Yes",StringComparison.OrdinalIgnoreCase);return new(){Area="REPLICATION",Status=ok?"OK":"WARNING",Summary=$"IO={io} SQL={sqlrun}",Detail=$"LagSeconds={lag}; Command={sql}"};}catch(Exception ex){return Classify("REPLICATION",ex);}}
 static async Task<HealthItem> Q(MySqlConnection c,string area,string sql){try{await using var q=new MySqlCommand(sql,c){CommandTimeout=15};await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();return new(){Area=area,Status=Convert.ToString(r.GetValue(0))??"INFO",Summary=Convert.ToString(r.GetValue(1))??"",Detail=Convert.ToString(r.GetValue(2))??""};}catch(Exception e){return Classify(area,e);}}
 static HealthItem Classify(string area,Exception e){if(e is MySqlException m){if(m.Number is 1044 or 1045 or 1142 or 1227)return new(){Area=area,Status="NO PERMISSION",Summary="Insufficient privileges",Detail=m.Message};if(m.Number is 1054 or 1146 or 1305)return new(){Area=area,Status="UNSUPPORTED",Summary="Feature not available for this MySQL/MariaDB version",Detail=m.Message};}return new(){Area=area,Status="ERROR",Summary="MySQL/MariaDB collector failed",Detail=e.Message};}
}