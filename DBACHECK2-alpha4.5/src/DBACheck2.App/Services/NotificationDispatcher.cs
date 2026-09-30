using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class NotificationDispatcher
{
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(15)};

    public async Task<IReadOnlyList<string>> DispatchAsync(IncidentNotification incident,IEnumerable<NotificationDestination> destinations,IEnumerable<NotificationRoute> routes,CancellationToken cancellationToken=default)
    {
        var all=destinations.Where(x=>x.Enabled).ToDictionary(x=>x.Name,StringComparer.OrdinalIgnoreCase);
        var targetNames=routes.Where(r=>r.Enabled && Matches(r.HostPattern,incident.Host) && (int)incident.Priority <= (int)r.MinimumPriority)
            .SelectMany(r=>r.DestinationNames).Distinct(StringComparer.OrdinalIgnoreCase);
        var results=new List<string>();
        foreach(var name in targetNames)
        {
            if(!all.TryGetValue(name,out var destination)){results.Add($"{name}: destination not found");continue;}
            try { await SendAsync(destination,incident,cancellationToken); results.Add($"{name}: delivered"); }
            catch(Exception ex){results.Add($"{name}: ERROR {ex.Message}");}
        }
        return results;
    }

    public Task TestAsync(NotificationDestination destination,CancellationToken cancellationToken=default)=>
        SendAsync(destination,new IncidentNotification{IncidentId="TEST",Priority=IncidentPriority.P3,Engine="DBACHECK2",Host=Environment.MachineName,Finding="Integration test successful.",Impact="No production impact.",SuggestedAction="No action required."},cancellationToken);

    private static async Task SendAsync(NotificationDestination d,IncidentNotification i,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(d.Endpoint))throw new InvalidOperationException("Destination endpoint is required.");
        var payload=d.Provider switch {
            NotificationProvider.Slack=>JsonSerializer.Serialize(new{text=Text(i)}),
            NotificationProvider.Discord=>JsonSerializer.Serialize(new{content=Text(i)}),
            NotificationProvider.MicrosoftTeams=>JsonSerializer.Serialize(new{
                type="message",
                attachments=new[]{new{contentType="application/vnd.microsoft.card.adaptive",contentUrl=(string?)null,
                    content=new{type="AdaptiveCard",version="1.4",body=new object[]{
                        new{type="TextBlock",size="Large",weight="Bolder",text=$"{Icon(i.Priority)} DBACHECK2 — {i.Priority}"},
                        new{type="FactSet",facts=new[]{new{title="Incident",value=i.IncidentId},new{title="Host",value=i.Host},new{title="Engine",value=i.Engine},new{title="Database",value=i.Database}}},
                        new{type="TextBlock",wrap=true,text=$"**Finding**\n{i.Finding}"},
                        new{type="TextBlock",wrap=true,text=$"**Impact**\n{i.Impact}"},
                        new{type="TextBlock",wrap=true,text=$"**Suggested action**\n{i.SuggestedAction}"}
                    }}
                }}
            }),
            _=>throw new NotSupportedException($"Provider {d.Provider} is not supported.")
        };
        using var request=new HttpRequestMessage(HttpMethod.Post,d.Endpoint){Content=new StringContent(payload,Encoding.UTF8,"application/json")};
        if(!string.IsNullOrWhiteSpace(d.Secret))request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {d.Secret}");
        using var response=await Http.SendAsync(request,ct);
        var body=await response.Content.ReadAsStringAsync(ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static string Text(IncidentNotification i)=>$"{Icon(i.Priority)} DBACHECK2 — {i.Priority}\nIncident: {i.IncidentId}\nHost: {i.Host}\nEngine: {i.Engine}\nDatabase: {i.Database}\n\nFinding: {i.Finding}\nImpact: {i.Impact}\nSuggested action: {i.SuggestedAction}";
    private static string Icon(IncidentPriority p)=>p switch{IncidentPriority.P1=>"🔴",IncidentPriority.P2=>"🟠",IncidentPriority.P3=>"🟡",_=>"🔵"};
    private static bool Matches(string pattern,string value)
    {
        if(string.IsNullOrWhiteSpace(pattern)||pattern=="*")return true;
        var regex="^"+Regex.Escape(pattern).Replace("\*",".*").Replace("\?",".")+"$";
        return Regex.IsMatch(value??"",regex,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
}
