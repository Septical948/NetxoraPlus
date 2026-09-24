using System.IO;
using System.Text.Json;
using DBACheck2.App.Models;
using Microsoft.Data.Sqlite;

namespace DBACheck2.App.Services;

public sealed class EnterpriseWorkspaceService
{
    private readonly string _dbPath;
    private readonly string _enterpriseApi;

    public EnterpriseWorkspaceService()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");
        Directory.CreateDirectory(dir);
        _dbPath=Path.Combine(dir,"enterprise.db");
        _enterpriseApi=(Environment.GetEnvironmentVariable("DBACHECK2_ENTERPRISE_API")??"").Trim().TrimEnd('/');
        Initialize();
    }

    public string DatabasePath=>_dbPath;
    public bool EnterpriseBackendConfigured=>Uri.TryCreate(_enterpriseApi,UriKind.Absolute,out _);
    public string EnterpriseBackend=>_enterpriseApi;

    private SqliteConnection Open()=>new($"Data Source={_dbPath}");

    private void Initialize()
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"CREATE TABLE IF NOT EXISTS enterprise_state(
 key TEXT PRIMARY KEY,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS enterprise_members(
 member_id TEXT PRIMARY KEY,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS enterprise_integrations(
 request_id TEXT PRIMARY KEY,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS shared_operations(
 operation_id TEXT PRIMARY KEY,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS enterprise_audit(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 ts TEXT NOT NULL,
 actor TEXT NOT NULL,
 action TEXT NOT NULL,
 target TEXT NOT NULL,
 detail TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();

        using var count=cn.CreateCommand();
        count.CommandText="SELECT COUNT(*) FROM enterprise_state WHERE key='organization';";
        if(Convert.ToInt32(count.ExecuteScalar())==0)
        {
            SaveStateSync("organization",new EnterpriseOrganization());
            SaveStateSync("sla",DefaultSla());
            AddAuditSync("SYSTEM","WORKSPACE_CREATED","Enterprise","Local Enterprise workspace initialized.");
        }
    }

    public async Task<EnterpriseOrganization> LoadOrganizationAsync()
        =>await LoadStateAsync<EnterpriseOrganization>("organization") ?? new();

    public async Task SaveOrganizationAsync(EnterpriseOrganization value,string actor="LOCAL ADMIN")
    {
        value.UpdatedAt=DateTime.Now;
        await SaveStateAsync("organization",value);
        await AddAuditAsync(actor,"ORGANIZATION_UPDATED",value.OrganizationId,$"Name={value.Name}; Domain={value.Domain}; Seats={value.SeatLimit}");
    }

    public async Task<List<EnterpriseMember>> LoadMembersAsync()=>await LoadRowsAsync<EnterpriseMember>("enterprise_members","payload","updated_at DESC");
    public async Task SaveMemberAsync(EnterpriseMember value,string actor="LOCAL ADMIN")
    {
        await SaveRowAsync("enterprise_members","member_id",value.MemberId,value);
        await AddAuditAsync(actor,"MEMBER_SAVED",value.Email,$"Role={value.Role}; State={value.State}; Seat={value.SeatAssigned}");
    }

    public async Task DeleteMemberAsync(string memberId,string actor="LOCAL ADMIN")
    {
        var members=await LoadMembersAsync();
        var member=members.FirstOrDefault(x=>x.MemberId==memberId);
        await DeleteRowAsync("enterprise_members","member_id",memberId);
        await AddAuditAsync(actor,"MEMBER_DELETED",member?.Email??memberId,"Member removed from local Enterprise workspace.");
    }

    public async Task<List<EnterpriseIntegrationRequest>> LoadIntegrationRequestsAsync()=>await LoadRowsAsync<EnterpriseIntegrationRequest>("enterprise_integrations","payload","updated_at DESC");
    public async Task SaveIntegrationRequestAsync(EnterpriseIntegrationRequest value,string actor="LOCAL ADMIN")
    {
        await SaveRowAsync("enterprise_integrations","request_id",value.RequestId,value);
        await AddAuditAsync(actor,"INTEGRATION_REQUEST_SAVED",value.Name,$"Type={value.IntegrationType}; State={value.State}");
    }

    public async Task<List<SharedOperation>> LoadSharedOperationsAsync()=>await LoadRowsAsync<SharedOperation>("shared_operations","payload","updated_at DESC");
    public async Task SaveSharedOperationAsync(SharedOperation value,string actor="LOCAL ADMIN")
    {
        value.UpdatedAt=DateTime.Now;
        await SaveRowAsync("shared_operations","operation_id",value.OperationId,value);
        await AddAuditAsync(actor,"SHARED_OPERATION_SAVED",value.Host,$"{value.Priority} | {value.State} | {value.Summary}");
    }

    public async Task<List<EnterpriseSlaRule>> LoadSlaAsync()=>await LoadStateAsync<List<EnterpriseSlaRule>>("sla") ?? DefaultSla();
    public async Task SaveSlaAsync(List<EnterpriseSlaRule> value,string actor="LOCAL ADMIN")
    {
        await SaveStateAsync("sla",value);
        await AddAuditAsync(actor,"SLA_UPDATED","Enterprise SLA",string.Join("; ",value.Select(x=>$"{x.Priority}:{x.ResponseTargetMinutes}m")));
    }

    public async Task<List<EnterpriseAuditEvent>> LoadAuditAsync(int limit=500)
    {
        var list=new List<EnterpriseAuditEvent>();
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT id,ts,actor,action,target,detail FROM enterprise_audit ORDER BY id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit",limit);
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())list.Add(new(){
            Id=r.GetInt64(0),
            Timestamp=DateTime.TryParse(r.GetString(1),out var ts)?ts:DateTime.MinValue,
            Actor=r.GetString(2),Action=r.GetString(3),Target=r.GetString(4),Detail=r.GetString(5)
        });
        return list;
    }

    private async Task<T?> LoadStateAsync<T>(string key)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT payload FROM enterprise_state WHERE key=$key;";
        cmd.Parameters.AddWithValue("$key",key);
        var value=await cmd.ExecuteScalarAsync();
        if(value is null||value is DBNull)return default;
        try{return JsonSerializer.Deserialize<T>(Convert.ToString(value)??"");}catch{return default;}
    }

    private async Task SaveStateAsync<T>(string key,T value)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT INTO enterprise_state(key,payload,updated_at) VALUES($key,$payload,$now)
ON CONFLICT(key) DO UPDATE SET payload=excluded.payload,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$key",key);
        cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));
        cmd.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private void SaveStateSync<T>(string key,T value)
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT INTO enterprise_state(key,payload,updated_at) VALUES($key,$payload,$now)
ON CONFLICT(key) DO UPDATE SET payload=excluded.payload,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));cmd.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private async Task<List<T>> LoadRowsAsync<T>(string table,string payloadColumn,string order)
    {
        var list=new List<T>();
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=$"SELECT {payloadColumn} FROM {table} ORDER BY {order};";
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())
        {
            try{var value=JsonSerializer.Deserialize<T>(r.GetString(0));if(value is not null)list.Add(value);}catch{}
        }
        return list;
    }

    private async Task SaveRowAsync<T>(string table,string keyColumn,string key,T value)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=$@"INSERT INTO {table}({keyColumn},payload,updated_at) VALUES($id,$payload,$now)
ON CONFLICT({keyColumn}) DO UPDATE SET payload=excluded.payload,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id",key);cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));cmd.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DeleteRowAsync(string table,string keyColumn,string key)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=$"DELETE FROM {table} WHERE {keyColumn}=$id;";
        cmd.Parameters.AddWithValue("$id",key);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task AddAuditAsync(string actor,string action,string target,string detail)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="INSERT INTO enterprise_audit(ts,actor,action,target,detail) VALUES($ts,$actor,$action,$target,$detail);";
        cmd.Parameters.AddWithValue("$ts",DateTime.Now.ToString("O"));cmd.Parameters.AddWithValue("$actor",actor);cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$target",target);cmd.Parameters.AddWithValue("$detail",detail);
        await cmd.ExecuteNonQueryAsync();
    }

    private void AddAuditSync(string actor,string action,string target,string detail)
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText="INSERT INTO enterprise_audit(ts,actor,action,target,detail) VALUES($ts,$actor,$action,$target,$detail);";
        cmd.Parameters.AddWithValue("$ts",DateTime.Now.ToString("O"));cmd.Parameters.AddWithValue("$actor",actor);cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$target",target);cmd.Parameters.AddWithValue("$detail",detail);
        cmd.ExecuteNonQuery();
    }

    private static List<EnterpriseSlaRule> DefaultSla()=>new(){
        new(){Priority="P1",ResponseTargetMinutes=15,ResolutionTargetMinutes=240},
        new(){Priority="P2",ResponseTargetMinutes=60,ResolutionTargetMinutes=480},
        new(){Priority="P3",ResponseTargetMinutes=240,ResolutionTargetMinutes=1440},
        new(){Priority="P4",ResponseTargetMinutes=480,ResolutionTargetMinutes=2880}
    };
}
