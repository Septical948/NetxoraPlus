using DBACheck2.App.Models;

namespace DBACheck2.App.Integrations;

public static class IntegrationProviderFactory
{
    public static IIntegrationProvider Create(IntegrationConnectionProfile profile)=>profile.Source switch
    {
        IntegrationSource.Zabbix=>new ZabbixIntegrationClient(profile),
        _=>new PlannedIntegrationProvider(profile.Source)
    };

    private sealed class PlannedIntegrationProvider(IntegrationSource source):IIntegrationProvider
    {
        public IntegrationSource Source=>source;
        public string DisplayName=>source.ToString();
        public Task<string> TestAsync()=>Task.FromException<string>(new NotSupportedException($"{source} adapter is planned but not enabled yet."));
        public Task<List<IntegrationEvent>> GetOpenEventsAsync(int limit=100)=>Task.FromException<List<IntegrationEvent>>(new NotSupportedException($"{source} event ingestion is planned but not enabled yet."));
    }
}
