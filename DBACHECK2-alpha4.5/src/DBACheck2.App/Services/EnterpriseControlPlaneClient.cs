using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class EnterpriseControlPlaneClient
{
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(15)};
    private readonly string _api;
    private readonly string _orgId;
    private readonly string _key;
    private readonly string _actor;
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};

    public EnterpriseControlPlaneClient()
    {
        _api=(Environment.GetEnvironmentVariable("DBACHECK2_ENTERPRISE_API")??"").Trim().TrimEnd('/');
        _orgId=(Environment.GetEnvironmentVariable("DBACHECK2_ENTERPRISE_ORG_ID")??"").Trim();
        _key=(Environment.GetEnvironmentVariable("DBACHECK2_ENTERPRISE_KEY")??"").Trim();
        _actor=(Environment.GetEnvironmentVariable("DBACHECK2_ENTERPRISE_ACTOR")??Environment.UserName).Trim();
    }

    public bool Configured=>Uri.TryCreate(_api,UriKind.Absolute,out _)&&!string.IsNullOrWhiteSpace(_orgId)&&!string.IsNullOrWhiteSpace(_key);
    public string Api=>_api;
    public string OrganizationId=>_orgId;

    public async Task<bool> HealthAsync()
    {
        if(!Configured)return false;
        using var response=await _http.GetAsync($"{_api}/health");
        return response.IsSuccessStatusCode;
    }

    public Task<EnterpriseOrganization> LoadOrganizationAsync()
        =>GetAsync<EnterpriseOrganization>($"/v1/organizations/{E(_orgId)}");

    public Task SaveOrganizationAsync(EnterpriseOrganization value)
        =>PutAsync($"/v1/organizations/{E(_orgId)}",value);

    public Task<List<EnterpriseMember>> LoadMembersAsync()
        =>GetAsync<List<EnterpriseMember>>($"/v1/organizations/{E(_orgId)}/members");

    public Task<EnterpriseMember> AddMemberAsync(EnterpriseMember value)
        =>PostAsync<EnterpriseMember>($"/v1/organizations/{E(_orgId)}/members",new {
            email=value.Email,
            displayName=value.DisplayName,
            role=value.Role,
            seatAssigned=value.SeatAssigned
        });

    public Task DeleteMemberAsync(string memberId)
        =>DeleteAsync($"/v1/organizations/{E(_orgId)}/members/{E(memberId)}");

    public Task<List<SharedOperation>> LoadSharedOperationsAsync()
        =>GetAsync<List<SharedOperation>>($"/v1/organizations/{E(_orgId)}/operations");

    public Task SaveSharedOperationAsync(SharedOperation value)
        =>PostNoResultAsync($"/v1/organizations/{E(_orgId)}/operations",value);

    public Task<List<EnterpriseIntegrationRequest>> LoadIntegrationRequestsAsync()
        =>GetAsync<List<EnterpriseIntegrationRequest>>($"/v1/organizations/{E(_orgId)}/integrations");

    public Task SaveIntegrationRequestAsync(EnterpriseIntegrationRequest value)
        =>PostNoResultAsync($"/v1/organizations/{E(_orgId)}/integrations",value);

    public Task<List<EnterpriseSlaRule>> LoadSlaAsync()
        =>GetAsync<List<EnterpriseSlaRule>>($"/v1/organizations/{E(_orgId)}/sla");

    public Task SaveSlaAsync(List<EnterpriseSlaRule> value)
        =>PutAsync($"/v1/organizations/{E(_orgId)}/sla",value);

    public async Task<List<EnterpriseAuditEvent>> LoadAuditAsync(int limit=500)
        =>await GetAsync<List<EnterpriseAuditEvent>>($"/v1/organizations/{E(_orgId)}/audit?limit={Math.Clamp(limit,1,5000)}");

    private HttpRequestMessage Request(HttpMethod method,string path,HttpContent? content=null)
    {
        var request=new HttpRequestMessage(method,_api+path){Content=content};
        request.Headers.TryAddWithoutValidation("X-DBACHECK-Enterprise-Key",_key);
        request.Headers.TryAddWithoutValidation("X-DBACHECK-Actor",_actor);
        return request;
    }

    private async Task<T> GetAsync<T>(string path)
    {
        EnsureConfigured();
        using var request=Request(HttpMethod.Get,path);
        using var response=await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ??throw new InvalidOperationException("Enterprise Control Plane returned an empty response.");
    }

    private async Task PutAsync<T>(string path,T body)
    {
        EnsureConfigured();
        using var request=Request(HttpMethod.Put,path,JsonContent.Create(body));
        using var response=await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private async Task<T> PostAsync<T>(string path,object body)
    {
        EnsureConfigured();
        using var request=Request(HttpMethod.Post,path,JsonContent.Create(body));
        using var response=await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ??throw new InvalidOperationException("Enterprise Control Plane returned an empty response.");
    }

    private async Task PostNoResultAsync<T>(string path,T body)
    {
        EnsureConfigured();
        using var request=Request(HttpMethod.Post,path,JsonContent.Create(body));
        using var response=await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private async Task DeleteAsync(string path)
    {
        EnsureConfigured();
        using var request=Request(HttpMethod.Delete,path);
        using var response=await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if(response.IsSuccessStatusCode)return;
        var detail=await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Enterprise Control Plane HTTP {(int)response.StatusCode}: {detail}");
    }

    private void EnsureConfigured()
    {
        if(!Configured)
            throw new InvalidOperationException("Enterprise Control Plane is not fully configured. Set DBACHECK2_ENTERPRISE_API, DBACHECK2_ENTERPRISE_ORG_ID and DBACHECK2_ENTERPRISE_KEY.");
    }

    private static string E(string value)=>Uri.EscapeDataString(value);
}
