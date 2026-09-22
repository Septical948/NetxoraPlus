using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App.Integrations;

public sealed class ZabbixIntegrationClient:IIntegrationProvider
{
    private readonly IntegrationConnectionProfile _profile;

    public ZabbixIntegrationClient(IntegrationConnectionProfile profile)=>_profile=profile;
    public IntegrationSource Source=>IntegrationSource.Zabbix;
    public string DisplayName=>"Zabbix";

    private string ApiUrl()
    {
        var s=(_profile.Endpoint??"").Trim().TrimEnd('/');
        if(string.IsNullOrWhiteSpace(s)) throw new InvalidOperationException("Zabbix API URL is required.");
        if(s.EndsWith("api_jsonrpc.php",StringComparison.OrdinalIgnoreCase)) return s;
        return s+"/api_jsonrpc.php";
    }

    private HttpClient Client()
    {
        var handler=new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback=_profile.VerifyTls
            ? null
            : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
    }

    public async Task<string> TestAsync()
    {
        var version=await CallAsync("apiinfo.version",new { },null,false);
        if(string.IsNullOrWhiteSpace(_profile.Token))
            return $"Zabbix API {version.GetString()} reachable. API token is required to read monitoring data.";
        var hosts=await CallAuthenticatedAsync("host.get",new { output=new[]{"hostid","host","name"}, limit=1 });
        return $"Zabbix API {version.GetString()} | Authentication OK | Accessible host sample: {hosts.GetArrayLength()}";
    }

    public async Task<List<IntegrationEvent>> GetOpenEventsAsync(int limit=100)
    {
        if(string.IsNullOrWhiteSpace(_profile.Token)) throw new InvalidOperationException("Zabbix API token is required.");
        var problems=await CallAuthenticatedAsync("problem.get",new {
            output=new[]{"eventid","objectid","clock","name","severity","acknowledged","suppressed","opdata"},
            selectTags="extend",
            recent=false,
            sortfield=new[]{"eventid"},
            sortorder="DESC",
            limit=Math.Clamp(limit,1,500)
        });

        var triggerIds=problems.EnumerateArray()
            .Select(x=>x.TryGetProperty("objectid",out var o)?o.GetString():null)
            .Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToArray();

        var hostByTrigger=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if(triggerIds.Length>0)
        {
            var triggers=await CallAuthenticatedAsync("trigger.get",new {
                output=new[]{"triggerid","description"},
                triggerids=triggerIds,
                selectHosts=new[]{"hostid","host","name"}
            });
            foreach(var t in triggers.EnumerateArray())
            {
                var id=t.GetProperty("triggerid").GetString()??"";
                var host="";
                if(t.TryGetProperty("hosts",out var hs) && hs.ValueKind==JsonValueKind.Array && hs.GetArrayLength()>0)
                {
                    var h=hs[0];
                    host=h.TryGetProperty("name",out var n)?n.GetString()??"":h.TryGetProperty("host",out var x)?x.GetString()??"":"";
                }
                hostByTrigger[id]=host;
            }
        }

        var result=new List<IntegrationEvent>();
        foreach(var p in problems.EnumerateArray())
        {
            var native=p.TryGetProperty("severity",out var sev)?sev.GetString()??"0":"0";
            var objectId=p.TryGetProperty("objectid",out var oid)?oid.GetString()??"":"";
            var tags="";
            if(p.TryGetProperty("tags",out var tg)&&tg.ValueKind==JsonValueKind.Array)
                tags=string.Join("; ",tg.EnumerateArray().Select(x=>$"{(x.TryGetProperty("tag",out var k)?k.GetString():"")}={(x.TryGetProperty("value",out var v)?v.GetString():"")}"));
            var clock=p.TryGetProperty("clock",out var cl)&&long.TryParse(cl.GetString(),out var unix)?unix:0;
            var ev=new IntegrationEvent {
                Source=IntegrationSource.Zabbix,
                ExternalId=p.TryGetProperty("eventid",out var id)?id.GetString()??"":"",
                Host=hostByTrigger.TryGetValue(objectId,out var host)?host:"",
                Name=p.TryGetProperty("name",out var nm)?nm.GetString()??"":"",
                Severity=MapSeverity(native),
                NativeSeverity=NativeSeverity(native),
                Timestamp=clock>0?DateTimeOffset.FromUnixTimeSeconds(clock).LocalDateTime:DateTime.MinValue,
                Acknowledged=p.TryGetProperty("acknowledged",out var ack)&&ack.GetString()=="1",
                Suppressed=p.TryGetProperty("suppressed",out var sup)&&sup.GetString()=="1",
                Tags=tags,
                RawDetail=p.TryGetProperty("opdata",out var op)?op.GetString()??"":""
            };
            ev.CorrelationHint=IntegrationCorrelationService.Hint(ev);
            result.Add(ev);
        }
        return result;
    }

    private async Task<JsonElement> CallAuthenticatedAsync(string method,object parameters)
    {
        var token=_profile.Token!;
        try { return await CallAsync(method,parameters,token,false); }
        catch(Exception headerError)
        {
            try { return await CallAsync(method,parameters,token,true); }
            catch(Exception legacyError)
            {
                throw new InvalidOperationException($"Zabbix authentication failed. Bearer: {headerError.Message} | Legacy auth field: {legacyError.Message}");
            }
        }
    }

    private async Task<JsonElement> CallAsync(string method,object parameters,string? token,bool legacyAuth)
    {
        using var client=Client();
        using var req=new HttpRequestMessage(HttpMethod.Post,ApiUrl());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if(!string.IsNullOrWhiteSpace(token)&&!legacyAuth)
            req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);

        var payload=legacyAuth&&!string.IsNullOrWhiteSpace(token)
            ? new { jsonrpc="2.0", method, @params=parameters, auth=token, id=1 }
            : (object)new { jsonrpc="2.0", method, @params=parameters, id=1 };

        req.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json-rpc");
        using var response=await client.SendAsync(req);
        var body=await response.Content.ReadAsStringAsync();
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body}");

        using var doc=JsonDocument.Parse(body);
        if(doc.RootElement.TryGetProperty("error",out var error))
        {
            var msg=error.TryGetProperty("message",out var m)?m.GetString():"Zabbix API error";
            var data=error.TryGetProperty("data",out var d)?d.GetString():"";
            throw new InvalidOperationException($"{msg}: {data}".Trim());
        }
        if(!doc.RootElement.TryGetProperty("result",out var result)) throw new InvalidOperationException("Zabbix API response did not contain result.");
        return result.Clone();
    }

    private static string MapSeverity(string value)=>value switch {
        "4" or "5"=>"CRITICAL",
        "2" or "3"=>"WARNING",
        _=>"INFO"
    };
    private static string NativeSeverity(string value)=>value switch {
        "0"=>"Not classified","1"=>"Information","2"=>"Warning","3"=>"Average","4"=>"High","5"=>"Disaster",_=>value
    };
}
