using DBACheck2.App.Models;using DBACheck2.App.Services;
namespace DBACheck2.App.Providers;
public sealed class SqlServerProvider:IDatabaseProvider
{
 private readonly SqlHealthService _health; private readonly SqlCompatibilityCollectorService _compat;
 public SqlServerProvider(ServerProfile p){_health=new(p.Host,p.TrustCertificate);_compat=new(p.Host,p.TrustCertificate);}
 public DatabaseEngine Engine=>DatabaseEngine.SqlServer; public string DisplayName=>"SQL Server";
 public Task<string> TestAsync()=>_health.TestAsync(); public Task<List<HealthItem>> QuickCheckAsync()=>_compat.QuickCheckAsync();
}