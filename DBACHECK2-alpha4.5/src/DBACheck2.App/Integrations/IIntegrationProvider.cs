using DBACheck2.App.Models;

namespace DBACheck2.App.Integrations;

public interface IIntegrationProvider
{
    IntegrationSource Source { get; }
    string DisplayName { get; }
    Task<string> TestAsync();
    Task<List<IntegrationEvent>> GetOpenEventsAsync(int limit=100);
}
