using System.IO;
using System.Text.Json;
using DBACheck2.App.Models;
using Microsoft.Data.Sqlite;

namespace DBACheck2.App.Services;

public sealed class AlertInboxStore
{
    private readonly string _dbPath;

    public AlertInboxStore()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");
        Directory.CreateDirectory(dir);
        _dbPath=Path.Combine(dir,"alert-inbox.db");
        Initialize();
    }

    private SqliteConnection Open()=>new($"Data Source={_dbPath}");

    private void Initialize()
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"CREATE TABLE IF NOT EXISTS inbox_snapshot(
 id INTEGER PRIMARY KEY CHECK(id=1),
 captured_at TEXT NOT NULL,
 payload TEXT NOT NULL);
 INSERT OR IGNORE INTO inbox_snapshot(id,captured_at,payload) VALUES(1,'','[]');";
        cmd.ExecuteNonQuery();
    }

    public async Task SaveAsync(IEnumerable<AlertInboxItem> items)
    {
        var json=JsonSerializer.Serialize(items,new JsonSerializerOptions{WriteIndented=false});
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="UPDATE inbox_snapshot SET captured_at=$now,payload=$payload WHERE id=1;";
        cmd.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$payload",json);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(DateTime? CapturedAt,List<AlertInboxItem> Items)> LoadAsync()
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT captured_at,payload FROM inbox_snapshot WHERE id=1;";
        await using var r=await cmd.ExecuteReaderAsync();
        if(!await r.ReadAsync())return(null,new());
        var capturedRaw=r.GetString(0);
        var payload=r.GetString(1);
        DateTime? captured=DateTime.TryParse(capturedRaw,out var dt)?dt:null;
        try{return(captured,JsonSerializer.Deserialize<List<AlertInboxItem>>(payload)??new());}
        catch{return(captured,new());}
    }

    public string DatabasePath=>_dbPath;
}
