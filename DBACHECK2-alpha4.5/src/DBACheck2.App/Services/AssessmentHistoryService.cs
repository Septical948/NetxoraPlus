using System.IO;
using System.Text.Json;
using DBACheck2.App.Models;
using Microsoft.Data.Sqlite;

namespace DBACheck2.App.Services;

public sealed class AssessmentHistoryService
{
    private readonly string _dbPath;

    public AssessmentHistoryService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");
        Directory.CreateDirectory(dir);
        _dbPath=Path.Combine(dir,"assessments.db");
        Initialize();
    }

    private SqliteConnection Open()=>new($"Data Source={_dbPath}");

    private void Initialize()
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"CREATE TABLE IF NOT EXISTS assessment_runs(
 run_id TEXT PRIMARY KEY,
 started_at TEXT NOT NULL,
 completed_at TEXT NOT NULL,
 profile_name TEXT NOT NULL,
 host TEXT NOT NULL,
 engine TEXT NOT NULL,
 mode TEXT NOT NULL,
 overall TEXT NOT NULL,
 critical_count INTEGER NOT NULL,
 warning_count INTEGER NOT NULL,
 ok_count INTEGER NOT NULL,
 payload TEXT NOT NULL);
 CREATE INDEX IF NOT EXISTS ix_assessment_host_date ON assessment_runs(host,started_at DESC);";
        cmd.ExecuteNonQuery();
    }

    public async Task SaveAsync(AssessmentRun run)
    {
        var payload=JsonSerializer.Serialize(run);
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT OR REPLACE INTO assessment_runs
(run_id,started_at,completed_at,profile_name,host,engine,mode,overall,critical_count,warning_count,ok_count,payload)
VALUES($id,$s,$c,$p,$h,$e,$m,$o,$cr,$w,$ok,$payload);";
        cmd.Parameters.AddWithValue("$id",run.RunId);cmd.Parameters.AddWithValue("$s",run.StartedAt.ToString("O"));cmd.Parameters.AddWithValue("$c",run.CompletedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$p",run.ProfileName);cmd.Parameters.AddWithValue("$h",run.Host);cmd.Parameters.AddWithValue("$e",run.Engine.ToString());cmd.Parameters.AddWithValue("$m",run.Mode.ToString());
        cmd.Parameters.AddWithValue("$o",run.Overall);cmd.Parameters.AddWithValue("$cr",run.Critical);cmd.Parameters.AddWithValue("$w",run.Warning);cmd.Parameters.AddWithValue("$ok",run.Ok);cmd.Parameters.AddWithValue("$payload",payload);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AssessmentRun>> ListAsync(int limit=100)
    {
        var list=new List<AssessmentRun>();
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT payload FROM assessment_runs ORDER BY started_at DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit",limit);
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())
        {
            try{var run=JsonSerializer.Deserialize<AssessmentRun>(r.GetString(0));if(run is not null)list.Add(run);}catch{}
        }
        return list;
    }

    public string DatabasePath=>_dbPath;
}
