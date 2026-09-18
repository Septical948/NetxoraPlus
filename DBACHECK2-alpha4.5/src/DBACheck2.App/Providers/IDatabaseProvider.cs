using DBACheck2.App.Models;
namespace DBACheck2.App.Providers;
public interface IDatabaseProvider
{
 DatabaseEngine Engine{get;} string DisplayName{get;}
 Task<string> TestAsync(); Task<List<HealthItem>> QuickCheckAsync();
}