using System.IO;
using Microsoft.Data.Sqlite;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class IncidentHistoryService
{
    private readonly string _dbPath;
    public IncidentHistoryService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");
        Directory.CreateDirectory(dir);
        _dbPath=Path.Combine(dir,"incidents.db");
        Initialize();
    }
    private SqliteConnection Open()=>new($"Data Source={_dbPath}");
    private void Initialize()
    {
        using var cn=Open(); cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"CREATE TABLE IF NOT EXISTS incidents(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 incident_key TEXT NOT NULL,
 created_at TEXT NOT NULL,
 updated_at TEXT NOT NULL,
 server_name TEXT NOT NULL,
 database_name TEXT NOT NULL,
 module TEXT NOT NULL,
 severity TEXT NOT NULL,
 problem TEXT NOT NULL,
 cause TEXT NOT NULL,
 evidence TEXT NOT NULL,
 action_taken TEXT NOT NULL DEFAULT '',
 verification TEXT NOT NULL DEFAULT '',
 status TEXT NOT NULL DEFAULT 'OPEN');
 CREATE INDEX IF NOT EXISTS ix_incidents_key ON incidents(incident_key,created_at DESC);
 CREATE INDEX IF NOT EXISTS ix_incidents_server ON incidents(server_name,created_at DESC);";
        cmd.ExecuteNonQuery();
    }
    public async Task<long> SaveAsync(string server,string database,string module,IncidentDiagnosis d)
    {
        var key=BuildKey(server,database,module,d.ProbableCause);
        await using var cn=Open(); await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT INTO incidents(incident_key,created_at,updated_at,server_name,database_name,module,severity,problem,cause,evidence,status)
 VALUES($key,$now,$now,$server,$db,$module,$severity,$problem,$cause,$evidence,'OPEN'); SELECT last_insert_rowid();";
        var now=DateTime.Now.ToString("O");
        cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$now",now);cmd.Parameters.AddWithValue("$server",server);
        cmd.Parameters.AddWithValue("$db",database);cmd.Parameters.AddWithValue("$module",module);cmd.Parameters.AddWithValue("$severity",d.Severity);
        cmd.Parameters.AddWithValue("$problem",d.Problem);cmd.Parameters.AddWithValue("$cause",d.ProbableCause);cmd.Parameters.AddWithValue("$evidence",d.Evidence);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
    public async Task<int> RecurrenceCountAsync(string server,string database,string module,string cause)
    {
        await using var cn=Open();await cn.OpenAsync();await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM incidents WHERE incident_key=$key;";
        cmd.Parameters.AddWithValue("$key",BuildKey(server,database,module,cause));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
    public async Task<List<IncidentRecord>> ListAsync(int limit=200)
    {
        var list=new List<IncidentRecord>();await using var cn=Open();await cn.OpenAsync();await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT i.id,i.incident_key,i.created_at,i.updated_at,i.server_name,i.database_name,i.module,i.severity,i.problem,i.cause,i.evidence,i.action_taken,i.verification,i.status,
 (SELECT COUNT(*) FROM incidents x WHERE x.incident_key=i.incident_key) recurrence_count
 FROM incidents i ORDER BY i.created_at DESC LIMIT $limit;";cmd.Parameters.AddWithValue("$limit",limit);
        await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())list.Add(Read(r));return list;
    }
    public async Task ResolveAsync(long id,string action,string verification)
    {
        await using var cn=Open();await cn.OpenAsync();await using var cmd=cn.CreateCommand();
        cmd.CommandText="UPDATE incidents SET status='RESOLVED',action_taken=$a,verification=$v,updated_at=$now WHERE id=$id;";
        cmd.Parameters.AddWithValue("$a",action);cmd.Parameters.AddWithValue("$v",verification);cmd.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));cmd.Parameters.AddWithValue("$id",id);await cmd.ExecuteNonQueryAsync();
    }
    public string DatabasePath=>_dbPath;
    private static string BuildKey(string s,string d,string m,string c)=>$"{s}|{d}|{m}|{NormalizeCause(c)}".ToUpperInvariant();
    private static string NormalizeCause(string c)
    {
        var u=c.ToUpperInvariant();
        foreach(var token in new[]{"LOG_BACKUP","ACTIVE_TRANSACTION","AVAILABILITY_REPLICA","REPLICATION","CHECKPOINT","BLOCKING"}) if(u.Contains(token)) return token;
        return u.Length>80?u[..80]:u;
    }
    private static IncidentRecord Read(SqliteDataReader r)=>new(){
        Id=r.GetInt64(0),IncidentKey=r.GetString(1),CreatedAt=DateTime.Parse(r.GetString(2)),UpdatedAt=DateTime.Parse(r.GetString(3)),
        ServerName=r.GetString(4),DatabaseName=r.GetString(5),Module=r.GetString(6),Severity=r.GetString(7),Problem=r.GetString(8),Cause=r.GetString(9),
        Evidence=r.GetString(10),ActionTaken=r.GetString(11),Verification=r.GetString(12),Status=r.GetString(13),RecurrenceCount=r.GetInt32(14)};
}
