using DBACheck2.App.Models;
using DBACheck2.App.Providers;

namespace DBACheck2.App.Services;

public sealed class ExternalEventDiagnosisService
{
    private readonly IntegrationTargetResolver _resolver=new();

    public async Task<ExternalDiagnosisResult> DiagnoseAsync(IntegrationEvent e)
    {
        var match=await _resolver.ResolveAsync(e);
        if(match.Profile is null) throw new InvalidOperationException(match.MatchReason);

        var provider=DatabaseProviderFactory.Create(match.Profile);
        var providerInfo=await provider.TestAsync();
        var all=await provider.QuickCheckAsync();
        var category=IntegrationCorrelationService.Category(e);
        var relevant=Filter(all,category).ToList();

        return new ExternalDiagnosisResult {
            Event=e, Profile=match.Profile, MatchReason=match.MatchReason,
            Category=category, ProviderInfo=providerInfo, RelevantChecks=relevant
        };
    }

    private static IEnumerable<HealthItem> Filter(IEnumerable<HealthItem> items,IntegrationCategory category)
    {
        var all=items.ToList();
        string[] areas=category switch {
            IntegrationCategory.Storage=>new[]{"DATABASES","DATABASE SIZE","TABLESPACE","LOG","WAL","REDO","BINLOG","TEMPDB","TEMP USAGE","BACKUPS"},
            IntegrationCategory.LogWal=>new[]{"LOG","WAL","REDO","BINLOG","ARCHIVELOG","TRANSACTIONS","LONG QUERIES","REPLICATION","ALWAYSON"},
            IntegrationCategory.Locking=>new[]{"BLOCKING","LOCKS","TRANSACTIONS","LONG QUERIES","WAITS","SESSIONS"},
            IntegrationCategory.HaReplication=>new[]{"ALWAYSON","REPLICATION","DATAGUARD","ARCHIVELOG","DATABASES"},
            IntegrationCategory.Backup=>new[]{"BACKUPS","JOBS","ARCHIVELOG","MAINTENANCE","DATABASES"},
            IntegrationCategory.Performance=>new[]{"PERFORMANCE","WAITS","BLOCKING","TRANSACTIONS","LONG QUERIES","SESSIONS","TEMPDB","TEMP USAGE","MEMORY","INDEXES","VACUUM","DATABASE SIZE"},
            IntegrationCategory.Availability=>new[]{"DATABASES","SESSIONS","ALWAYSON","REPLICATION","CONNECTIONS"},
            _=>Array.Empty<string>()
        };

        var filtered=areas.Length==0
            ? all.Where(x=>x.Status is "CRITICAL" or "ERROR" or "WARNING").OrderByDescending(x=>x.Severity).Take(8).ToList()
            : all.Where(x=>areas.Contains(x.Area,StringComparer.OrdinalIgnoreCase)).OrderByDescending(x=>x.Severity).ToList();

        if(filtered.Count==0)
            filtered=all.OrderByDescending(x=>x.Severity).Take(8).ToList();

        return filtered;
    }
}
