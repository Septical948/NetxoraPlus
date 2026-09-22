using DBACheck2.App.Integrations;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class AlertInboxService
{
    private readonly IntegrationProfileService _integrations=new();
    private readonly IntegrationTargetResolver _resolver=new();
    private readonly AlertInboxStore _store=new();
    private static bool En=>LocalizationService.Current==AppLanguage.En;

    public async Task<AlertInboxLoadResult> LoadAsync()
    {
        var result=new AlertInboxLoadResult();
        var profiles=await _integrations.LoadAsync();
        result.ProfilesConfigured=profiles.Count;
        var events=new List<IntegrationEvent>();

        foreach(var p in profiles)
        {
            try
            {
                var provider=IntegrationProviderFactory.Create(p);
                var rows=await provider.GetOpenEventsAsync(250);
                foreach(var e in rows)
                {
                    e.CorrelationHint=IntegrationCorrelationService.Hint(e);
                    events.Add(e);
                }
                result.SourcesOk++;
                result.SourceStatus.Add($"{p.Name} | {p.Source} | OK | {rows.Count} open event(s)");
            }
            catch(Exception ex)
            {
                result.SourcesFailed++;
                result.SourceStatus.Add($"{p.Name} | {p.Source} | ERROR | {ex.Message}");
            }
        }

        if(result.SourcesOk==0)
        {
            var cached=await _store.LoadAsync();
            if(cached.Items.Count>0)
            {
                foreach(var item in cached.Items)item.State="CACHED";
                result.Items=cached.Items;
                result.SourceStatus.Add(cached.CapturedAt.HasValue
                    ? (En?$"LOCAL CACHE | last successful snapshot {cached.CapturedAt:yyyy-MM-dd HH:mm:ss}":$"CACHE LOCAL | última captura exitosa {cached.CapturedAt:yyyy-MM-dd HH:mm:ss}")
                    : (En?"LOCAL CACHE | previous snapshot":"CACHE LOCAL | captura previa"));
                return result;
            }
        }

        var enriched=new List<(IntegrationEvent Event,ProfileMatchResult Match)>();
        foreach(var e in events)
            enriched.Add((e,await _resolver.ResolveAsync(e)));

        foreach(var group in enriched.GroupBy(x=>BuildGroupKey(x.Event)))
        {
            var rows=group.Select(x=>x.Event).ToList();
            var matches=group.Select(x=>x.Match).Where(x=>x.Profile is not null).ToList();
            var match=matches.FirstOrDefault();
            var primary=rows.OrderByDescending(x=>SeverityRank(x.Severity)).ThenBy(x=>x.Timestamp).First();
            var first=rows.Where(x=>x.Timestamp!=DateTime.MinValue).Select(x=>x.Timestamp).DefaultIfEmpty(DateTime.Now).Min();
            var last=rows.Where(x=>x.Timestamp!=DateTime.MinValue).Select(x=>x.Timestamp).DefaultIfEmpty(DateTime.Now).Max();
            var category=IntegrationCorrelationService.Category(primary);
            var environment=match?.Profile?.Environment?.ToUpperInvariant() ?? "UNKNOWN";
            var score=Score(rows,environment,category,first);
            var priority=score>=80?"P1":score>=55?"P2":score>=30?"P3":"P4";
            var sources=string.Join(", ",rows.Select(x=>x.Source.ToString()).Distinct());
            var summary=rows.Count==1?primary.Name:$"{primary.Name} (+{rows.Count-1} related alert(s))";

            result.Items.Add(new AlertInboxItem {
                Priority=priority,
                PriorityScore=score,
                State="DETECTED",
                Host=primary.Host,
                Environment=environment,
                Category=category,
                Sources=sources,
                AlertCount=rows.Count,
                FirstSeen=first,
                LastSeen=last,
                AgeText=Age(first),
                Summary=summary,
                ProfileName=match?.Profile?.Name ?? "",
                MatchReason=match?.MatchReason ?? group.Select(x=>x.Match.MatchReason).FirstOrDefault() ?? "",
                PriorityReason=PriorityReason(rows,environment,category,first,score),
                ProfileMatched=match?.Profile is not null,
                Events=rows
            });
        }

        result.Items=result.Items
            .OrderBy(x=>PriorityRank(x.Priority))
            .ThenByDescending(x=>x.PriorityScore)
            .ThenBy(x=>x.FirstSeen)
            .ToList();

        if(result.SourcesOk>0) await _store.SaveAsync(result.Items);
        return result;
    }

    private static string BuildGroupKey(IntegrationEvent e)
    {
        var host=Short(e.Host).ToUpperInvariant();
        var category=IntegrationCorrelationService.Category(e);
        return $"{host}|{category}";
    }

    private static string Short(string value)
    {
        value=(value??"").Trim();
        var slash=value.IndexOf('\\'); if(slash>=0)value=value[(slash+1)..];
        var dot=value.IndexOf('.'); if(dot>0)value=value[..dot];
        return value;
    }

    private static int Score(List<IntegrationEvent> rows,string environment,IntegrationCategory category,DateTime first)
    {
        var max=rows.Max(x=>SeverityRank(x.Severity));
        var score=max switch{3=>60,2=>35,_=>10};

        if(environment=="PROD" || environment=="PRODUCTION") score+=20;
        else if(environment is "QA" or "UAT" or "STAGE" or "STAGING") score+=8;

        var age=DateTime.Now-first;
        if(age>=TimeSpan.FromHours(6))score+=15;
        else if(age>=TimeSpan.FromHours(2))score+=10;
        else if(age>=TimeSpan.FromMinutes(30))score+=5;

        if(rows.Any(x=>!x.Acknowledged))score+=5;
        if(rows.All(x=>x.Suppressed))score-=15;

        score+=category switch {
            IntegrationCategory.Availability=>12,
            IntegrationCategory.LogWal=>10,
            IntegrationCategory.Storage=>10,
            IntegrationCategory.Locking=>10,
            IntegrationCategory.HaReplication=>10,
            IntegrationCategory.Backup=>5,
            IntegrationCategory.Performance=>7,
            _=>0
        };

        var sourceCount=rows.Select(x=>x.Source).Distinct().Count();
        if(sourceCount>=3)score+=15;
        else if(sourceCount==2)score+=10;

        return Math.Clamp(score,0,100);
    }

    private static string PriorityReason(List<IntegrationEvent> rows,string environment,IntegrationCategory category,DateTime first,int score)
    {
        var reasons=new List<string>();
        var sev=rows.OrderByDescending(x=>SeverityRank(x.Severity)).First().Severity;
        reasons.Add(En?$"monitor severity={sev}":$"severidad monitor={sev}");
        if(environment!="UNKNOWN")reasons.Add(En?$"environment={environment}":$"ambiente={environment}");
        reasons.Add(En?$"active for {Age(first)}":$"activa hace {Age(first)}");
        if(rows.Any(x=>!x.Acknowledged))reasons.Add(En?"unacknowledged":"sin reconocer");
        var sourceCount=rows.Select(x=>x.Source).Distinct().Count();
        if(sourceCount>1)reasons.Add(En?$"{sourceCount} monitoring sources":$"{sourceCount} fuentes de monitoreo");
        reasons.Add(En?$"category={category}":$"categoría={category}");
        reasons.Add($"score={score}/100");
        return string.Join(" | ",reasons);
    }

    private static string Age(DateTime first)
    {
        var span=DateTime.Now-first;
        if(span<TimeSpan.Zero)span=TimeSpan.Zero;
        if(span.TotalDays>=1)return $"{(int)span.TotalDays}d {span.Hours}h";
        if(span.TotalHours>=1)return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{Math.Max(0,(int)span.TotalMinutes)}m";
    }

    private static int SeverityRank(string value)=>value switch{"CRITICAL"=>3,"WARNING"=>2,_=>1};
    private static int PriorityRank(string value)=>value switch{"P1"=>1,"P2"=>2,"P3"=>3,_=>4};
}
