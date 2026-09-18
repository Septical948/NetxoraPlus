using DBACheck2.App.Models;
namespace DBACheck2.App.Providers;
public static class DatabaseProviderFactory
{
 public static IDatabaseProvider Create(ServerProfile p)=>p.Engine switch {
  DatabaseEngine.SqlServer=>new SqlServerProvider(p),
  _=>new PlannedDatabaseProvider(p.Engine)
 };
 private sealed class PlannedDatabaseProvider(DatabaseEngine engine):IDatabaseProvider
 {
  public DatabaseEngine Engine=>engine; public string DisplayName=>engine.ToString();
  public Task<string> TestAsync()=>Task.FromException<string>(new NotSupportedException($"{DisplayName} provider está preparado arquitectónicamente pero todavía no está habilitado en esta Alpha."));
  public Task<List<HealthItem>> QuickCheckAsync()=>Task.FromException<List<HealthItem>>(new NotSupportedException($"{DisplayName} Quick Check se incorporará en el provider específico."));
 }
}