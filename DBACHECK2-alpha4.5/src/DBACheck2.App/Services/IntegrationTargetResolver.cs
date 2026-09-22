using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class IntegrationTargetResolver
{
    private readonly ServerProfileService _profiles=new();

    public async Task<ProfileMatchResult> ResolveAsync(IntegrationEvent e)
    {
        var profiles=await _profiles.LoadAsync();
        if(profiles.Count==0) return new(){MatchReason="No DBACHECK server profiles are saved."};

        var tagged=TagValue(e.Tags,"dbacheck_profile");
        if(!string.IsNullOrWhiteSpace(tagged))
        {
            var byTag=profiles.FirstOrDefault(x=>Eq(x.Name,tagged));
            if(byTag is not null) return new(){Profile=byTag,MatchReason=$"Explicit tag dbacheck_profile={tagged}"};
        }

        var host=(e.Host??"").Trim();
        if(!string.IsNullOrWhiteSpace(host))
        {
            var exact=profiles.Where(x=>Eq(x.Host,host)).ToList();
            if(exact.Count==1) return new(){Profile=exact[0],MatchReason="Exact monitoring host -> profile host match"};

            var byName=profiles.Where(x=>Eq(x.Name,host)).ToList();
            if(byName.Count==1) return new(){Profile=byName[0],MatchReason="Exact monitoring host -> profile name match"};

            var shortHost=Short(host);
            var shortMatches=profiles.Where(x=>Eq(Short(x.Host),shortHost)||Eq(Short(x.Name),shortHost)).ToList();
            if(shortMatches.Count==1) return new(){Profile=shortMatches[0],MatchReason="Unique short-host match"};
            if(shortMatches.Count>1) return new(){MatchReason=$"Ambiguous host '{host}': {shortMatches.Count} DBACHECK profiles match."};
        }

        var names=string.Join(", ",profiles.Take(8).Select(x=>x.Name));
        return new(){MatchReason=$"No DBACHECK profile matched host '{e.Host}'. Saved profiles: {names}"};
    }

    private static string Short(string value)
    {
        value=(value??"").Trim();
        var slash=value.IndexOf('\\'); if(slash>=0) value=value[(slash+1)..];
        var dot=value.IndexOf('.'); if(dot>0) value=value[..dot];
        return value;
    }

    private static bool Eq(string? a,string? b)=>string.Equals(a?.Trim(),b?.Trim(),StringComparison.OrdinalIgnoreCase);

    private static string? TagValue(string tags,string key)
    {
        foreach(var part in (tags??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var i=part.IndexOf('=');
            if(i<=0) continue;
            if(part[..i].Trim().Equals(key,StringComparison.OrdinalIgnoreCase)) return part[(i+1)..].Trim();
        }
        return null;
    }
}
