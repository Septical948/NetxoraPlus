using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBACheck2.ControlPlane.Models;
using Microsoft.Data.Sqlite;

namespace DBACheck2.ControlPlane.Services;

public sealed class ControlPlaneStore
{
    private readonly string _dbPath;
    public ControlPlaneStore(IWebHostEnvironment env)
    {
        _dbPath=(Environment.GetEnvironmentVariable("DBACHECK2_CONTROL_PLANE_DB")??"").Trim();
        if(string.IsNullOrWhiteSpace(_dbPath))
            _dbPath=Path.Combine(env.ContentRootPath,"data","control-plane.db");
        var dir=Path.GetDirectoryName(_dbPath);
        if(!string.IsNullOrWhiteSpace(dir))Directory.CreateDirectory(dir);
        Initialize();
    }

    public string DatabasePath=>_dbPath;
    private SqliteConnection Open()=>new($"Data Source={_dbPath}");

    private void Initialize()
    {
        using var cn=Open();cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText=@"CREATE TABLE IF NOT EXISTS organizations(
 organization_id TEXT PRIMARY KEY,
 payload TEXT NOT NULL,
 enterprise_key_hash TEXT NOT NULL,
 created_at TEXT NOT NULL,
 updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS members(
 organization_id TEXT NOT NULL,
 member_id TEXT NOT NULL,
 email TEXT NOT NULL,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL,
 PRIMARY KEY(organization_id,member_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_members_org_email ON members(organization_id,email);
CREATE TABLE IF NOT EXISTS operations(
 organization_id TEXT NOT NULL,
 operation_id TEXT NOT NULL,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL,
 PRIMARY KEY(organization_id,operation_id)
);
CREATE TABLE IF NOT EXISTS integration_requests(
 organization_id TEXT NOT NULL,
 request_id TEXT NOT NULL,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL,
 PRIMARY KEY(organization_id,request_id)
);
CREATE TABLE IF NOT EXISTS organization_state(
 organization_id TEXT NOT NULL,
 state_key TEXT NOT NULL,
 payload TEXT NOT NULL,
 updated_at TEXT NOT NULL,
 PRIMARY KEY(organization_id,state_key)
);
CREATE TABLE IF NOT EXISTS audit(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 organization_id TEXT NOT NULL,
 ts TEXT NOT NULL,
 actor TEXT NOT NULL,
 action TEXT NOT NULL,
 target TEXT NOT NULL,
 detail TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_audit_org_id ON audit(organization_id,id DESC);";
        cmd.ExecuteNonQuery();
    }

    public async Task<(EnterpriseOrganization Org,string Key)> BootstrapAsync(BootstrapOrganizationRequest request)
    {
        var org=new EnterpriseOrganization {
            Name=string.IsNullOrWhiteSpace(request.Name)?"DBACHECK2 Enterprise Workspace":request.Name.Trim(),
            Domain=request.Domain.Trim(),
            SeatLimit=Math.Max(1,request.SeatLimit)
        };
        var key=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var hash=Hash(key);

        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();

        await using(var cmd=cn.CreateCommand())
        {
            cmd.Transaction=(SqliteTransaction)tx;
            cmd.CommandText=@"INSERT INTO organizations(organization_id,payload,enterprise_key_hash,created_at,updated_at)
VALUES($id,$payload,$hash,$now,$now);";
            cmd.Parameters.AddWithValue("$id",org.OrganizationId);
            cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(org));
            cmd.Parameters.AddWithValue("$hash",hash);
            cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        if(!string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            var admin=new EnterpriseMember {
                Email=request.AdminEmail.Trim(),
                DisplayName=request.AdminName.Trim(),
                Role=EnterpriseRole.Administrator,
                State=EnterpriseMemberState.Active,
                SeatAssigned=true
            };
            await SaveMemberAsync(cn,(SqliteTransaction)tx,org.OrganizationId,admin);
        }

        await AddAuditAsync(cn,(SqliteTransaction)tx,org.OrganizationId,"SYSTEM","ORGANIZATION_BOOTSTRAPPED",org.OrganizationId,$"Name={org.Name}; Seats={org.SeatLimit}");
        await tx.CommitAsync();
        return(org,key);
    }

    public async Task<bool> AuthenticateAsync(string organizationId,string enterpriseKey)
    {
        if(string.IsNullOrWhiteSpace(organizationId)||string.IsNullOrWhiteSpace(enterpriseKey))return false;
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT enterprise_key_hash FROM organizations WHERE organization_id=$id;";
        cmd.Parameters.AddWithValue("$id",organizationId);
        var stored=Convert.ToString(await cmd.ExecuteScalarAsync());
        if(string.IsNullOrWhiteSpace(stored))return false;
        return FixedEquals(stored,Hash(enterpriseKey));
    }

    public async Task<EnterpriseOrganization?> GetOrganizationAsync(string organizationId)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT payload FROM organizations WHERE organization_id=$id;";
        cmd.Parameters.AddWithValue("$id",organizationId);
        var value=Convert.ToString(await cmd.ExecuteScalarAsync());
        return Deserialize<EnterpriseOrganization>(value);
    }

    public async Task SaveOrganizationAsync(string organizationId,EnterpriseOrganization value,string actor)
    {
        var current=await GetOrganizationAsync(organizationId)??throw new InvalidOperationException("Organization not found.");
        if(current.SeatLimitManagedByBilling)
        {
            value.SeatLimit=current.SeatLimit;
            value.LicenseState=current.LicenseState;
            value.SeatLimitManagedByBilling=true;
            value.ContractReference=current.ContractReference;
            value.LicenseExpiresAt=current.LicenseExpiresAt;
        }
        value.OrganizationId=organizationId;value.UpdatedAt=DateTime.UtcNow;
        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        await using(var cmd=cn.CreateCommand())
        {
            cmd.Transaction=(SqliteTransaction)tx;
            cmd.CommandText="UPDATE organizations SET payload=$payload,updated_at=$now WHERE organization_id=$id;";
            cmd.Parameters.AddWithValue("$id",organizationId);
            cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));
            cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        await AddAuditAsync(cn,(SqliteTransaction)tx,organizationId,actor,"ORGANIZATION_UPDATED",organizationId,$"Name={value.Name}; Seats={value.SeatLimit}; SSO={value.SsoMode}");
        await tx.CommitAsync();
    }

    public async Task<EnterpriseOrganization> ApplyLicenseAsync(string organizationId,EnterpriseLicenseUpdateRequest request,string actor)
    {
        var org=await GetOrganizationAsync(organizationId)??throw new InvalidOperationException("Organization not found.");
        var members=await GetMembersAsync(organizationId);
        var used=members.Count(x=>x.SeatAssigned&&x.State!=EnterpriseMemberState.Suspended);
        if(request.SeatLimit<used)
            throw new InvalidOperationException($"Seat limit {request.SeatLimit} is below the {used} currently assigned active seats.");

        org.SeatLimit=Math.Max(1,request.SeatLimit);
        org.LicenseState=request.State;
        org.SeatLimitManagedByBilling=request.ManagedByBilling;
        org.ContractReference=request.ContractReference.Trim();
        org.LicenseExpiresAt=request.ExpiresAt;
        org.UpdatedAt=DateTime.UtcNow;

        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        await using(var cmd=cn.CreateCommand())
        {
            cmd.Transaction=(SqliteTransaction)tx;
            cmd.CommandText="UPDATE organizations SET payload=$payload,updated_at=$now WHERE organization_id=$id;";
            cmd.Parameters.AddWithValue("$id",organizationId);
            cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(org));
            cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        await AddAuditAsync(cn,(SqliteTransaction)tx,organizationId,actor,"LICENSE_UPDATED",organizationId,$"State={org.LicenseState}; Seats={org.SeatLimit}; Managed={org.SeatLimitManagedByBilling}; Contract={org.ContractReference}");
        await tx.CommitAsync();
        return org;
    }

    public async Task<List<EnterpriseMember>> GetMembersAsync(string organizationId)
        =>await LoadRowsAsync<EnterpriseMember>("members","organization_id",organizationId,"payload","updated_at DESC");

    public async Task<EnterpriseMember> AddMemberAsync(string organizationId,MemberCreateRequest request,string actor)
    {
        var org=await GetOrganizationAsync(organizationId)??throw new InvalidOperationException("Organization not found.");
        var members=await GetMembersAsync(organizationId);
        if(request.SeatAssigned && members.Count(x=>x.SeatAssigned&&x.State!=EnterpriseMemberState.Suspended)>=org.SeatLimit)
            throw new InvalidOperationException("Enterprise seat limit reached.");
        if(members.Any(x=>x.Email.Equals(request.Email.Trim(),StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A member with that email already exists.");

        var member=new EnterpriseMember {
            Email=request.Email.Trim(),
            DisplayName=request.DisplayName.Trim(),
            Role=request.Role,
            State=EnterpriseMemberState.Invited,
            SeatAssigned=request.SeatAssigned
        };

        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        await SaveMemberAsync(cn,(SqliteTransaction)tx,organizationId,member);
        await AddAuditAsync(cn,(SqliteTransaction)tx,organizationId,actor,"MEMBER_ADDED",member.Email,$"Role={member.Role}; Seat={member.SeatAssigned}");
        await tx.CommitAsync();
        return member;
    }

    public async Task<EnterpriseMember> UpdateMemberAsync(string organizationId,string memberId,MemberUpdateRequest request,string actor)
    {
        var org=await GetOrganizationAsync(organizationId)??throw new InvalidOperationException("Organization not found.");
        var members=await GetMembersAsync(organizationId);
        var member=members.FirstOrDefault(x=>x.MemberId==memberId)??throw new InvalidOperationException("Member not found.");

        var currentlyConsumes=member.SeatAssigned&&member.State!=EnterpriseMemberState.Suspended;
        var willConsume=request.SeatAssigned&&request.State!=EnterpriseMemberState.Suspended;
        var otherUsed=members.Count(x=>x.MemberId!=memberId&&x.SeatAssigned&&x.State!=EnterpriseMemberState.Suspended);
        if(willConsume && otherUsed>=org.SeatLimit)
            throw new InvalidOperationException("Enterprise seat limit reached.");

        member.DisplayName=request.DisplayName.Trim();
        member.Role=request.Role;
        member.State=request.State;
        member.SeatAssigned=request.SeatAssigned;

        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        await SaveMemberAsync(cn,(SqliteTransaction)tx,organizationId,member);
        await AddAuditAsync(cn,(SqliteTransaction)tx,organizationId,actor,"MEMBER_UPDATED",member.Email,$"Role={member.Role}; State={member.State}; Seat={member.SeatAssigned}; ConsumedBefore={currentlyConsumes}; ConsumedAfter={willConsume}");
        await tx.CommitAsync();
        return member;
    }

    public async Task DeleteMemberAsync(string organizationId,string memberId,string actor)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        await using(var cmd=cn.CreateCommand())
        {
            cmd.Transaction=(SqliteTransaction)tx;
            cmd.CommandText="DELETE FROM members WHERE organization_id=$org AND member_id=$id;";
            cmd.Parameters.AddWithValue("$org",organizationId);cmd.Parameters.AddWithValue("$id",memberId);
            await cmd.ExecuteNonQueryAsync();
        }
        await AddAuditAsync(cn,(SqliteTransaction)tx,organizationId,actor,"MEMBER_DELETED",memberId,"Member removed.");
        await tx.CommitAsync();
    }

    public async Task<List<SharedOperation>> GetOperationsAsync(string organizationId)
        =>await LoadRowsAsync<SharedOperation>("operations","organization_id",organizationId,"payload","updated_at DESC");

    public async Task SaveOperationAsync(string organizationId,SharedOperation value,string actor)
    {
        value.UpdatedAt=DateTime.UtcNow;
        await SaveGenericAsync("operations","operation_id",organizationId,value.OperationId,value);
        await AddAuditAsync(organizationId,actor,"SHARED_OPERATION_SAVED",value.Host,$"{value.Priority} | {value.State} | {value.Summary}");
    }

    public async Task<List<EnterpriseIntegrationRequest>> GetIntegrationRequestsAsync(string organizationId)
        =>await LoadRowsAsync<EnterpriseIntegrationRequest>("integration_requests","organization_id",organizationId,"payload","updated_at DESC");

    public async Task SaveIntegrationRequestAsync(string organizationId,EnterpriseIntegrationRequest value,string actor)
    {
        await SaveGenericAsync("integration_requests","request_id",organizationId,value.RequestId,value);
        await AddAuditAsync(organizationId,actor,"INTEGRATION_REQUEST_SAVED",value.Name,$"Type={value.IntegrationType}; State={value.State}");
    }

    public async Task<List<EnterpriseSlaRule>> GetSlaAsync(string organizationId)
        =>await LoadStateAsync<List<EnterpriseSlaRule>>(organizationId,"sla")??DefaultSla();

    public async Task SaveSlaAsync(string organizationId,List<EnterpriseSlaRule> value,string actor)
    {
        await SaveStateAsync(organizationId,"sla",value);
        await AddAuditAsync(organizationId,actor,"SLA_UPDATED","Enterprise SLA",string.Join("; ",value.Select(x=>$"{x.Priority}:{x.ResponseTargetMinutes}m")));
    }

    public async Task<List<EnterpriseAuditEvent>> GetAuditAsync(string organizationId,int limit)
    {
        var list=new List<EnterpriseAuditEvent>();
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT id,ts,actor,action,target,detail FROM audit WHERE organization_id=$org ORDER BY id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$org",organizationId);cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,5000));
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())list.Add(new(){
            Id=r.GetInt64(0),Timestamp=DateTime.Parse(r.GetString(1)),Actor=r.GetString(2),Action=r.GetString(3),Target=r.GetString(4),Detail=r.GetString(5)
        });
        return list;
    }

    private async Task SaveMemberAsync(SqliteConnection cn,SqliteTransaction tx,string org,EnterpriseMember value)
    {
        await using var cmd=cn.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=@"INSERT INTO members(organization_id,member_id,email,payload,updated_at)
VALUES($org,$id,$email,$payload,$now)
ON CONFLICT(organization_id,member_id) DO UPDATE SET email=excluded.email,payload=excluded.payload,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$org",org);cmd.Parameters.AddWithValue("$id",value.MemberId);cmd.Parameters.AddWithValue("$email",value.Email);
        cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SaveGenericAsync<T>(string table,string idColumn,string org,string id,T value)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=$@"INSERT INTO {table}(organization_id,{idColumn},payload,updated_at)
VALUES($org,$id,$payload,$now)
ON CONFLICT(organization_id,{idColumn}) DO UPDATE SET payload=excluded.payload,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$org",org);cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<T>> LoadRowsAsync<T>(string table,string orgColumn,string org,string payloadColumn,string order)
    {
        var list=new List<T>();
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=$"SELECT {payloadColumn} FROM {table} WHERE {orgColumn}=$org ORDER BY {order};";
        cmd.Parameters.AddWithValue("$org",org);
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()){var value=Deserialize<T>(r.GetString(0));if(value is not null)list.Add(value);}
        return list;
    }

    private async Task<T?> LoadStateAsync<T>(string org,string key)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT payload FROM organization_state WHERE organization_id=$org AND state_key=$key;";
        cmd.Parameters.AddWithValue("$org",org);cmd.Parameters.AddWithValue("$key",key);
        return Deserialize<T>(Convert.ToString(await cmd.ExecuteScalarAsync()));
    }

    private async Task SaveStateAsync<T>(string org,string key,T value)
    {
        await using var cn=Open();await cn.OpenAsync();
        await using var cmd=cn.CreateCommand();
        cmd.CommandText=@"INSERT INTO organization_state(organization_id,state_key,payload,updated_at)
VALUES($org,$key,$payload,$now)
ON CONFLICT(organization_id,state_key) DO UPDATE SET payload=excluded.payload,updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$org",org);cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(value));cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task AddAuditAsync(string org,string actor,string action,string target,string detail)
    {
        await using var cn=Open();await cn.OpenAsync();
        await AddAuditAsync(cn,null,org,actor,action,target,detail);
    }

    private static async Task AddAuditAsync(SqliteConnection cn,SqliteTransaction? tx,string org,string actor,string action,string target,string detail)
    {
        await using var cmd=cn.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO audit(organization_id,ts,actor,action,target,detail) VALUES($org,$ts,$actor,$action,$target,$detail);";
        cmd.Parameters.AddWithValue("$org",org);cmd.Parameters.AddWithValue("$ts",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$actor",actor);
        cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$target",target);cmd.Parameters.AddWithValue("$detail",detail);
        await cmd.ExecuteNonQueryAsync();
    }

    private static T? Deserialize<T>(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return default;
        try{return JsonSerializer.Deserialize<T>(json);}catch{return default;}
    }

    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedEquals(string a,string b)
    {
        try
        {
            var aa=Convert.FromHexString(a);var bb=Convert.FromHexString(b);
            return aa.Length==bb.Length&&CryptographicOperations.FixedTimeEquals(aa,bb);
        }
        catch{return false;}
    }

    public static List<EnterpriseSlaRule> DefaultSla()=>new(){
        new(){Priority="P1",ResponseTargetMinutes=15,ResolutionTargetMinutes=240},
        new(){Priority="P2",ResponseTargetMinutes=60,ResolutionTargetMinutes=480},
        new(){Priority="P3",ResponseTargetMinutes=240,ResolutionTargetMinutes=1440},
        new(){Priority="P4",ResponseTargetMinutes=480,ResolutionTargetMinutes=2880}
    };
}
