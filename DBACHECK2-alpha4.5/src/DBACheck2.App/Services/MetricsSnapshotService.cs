using System.IO;using System.Text.Json;using DBACheck2.App.Models;using DBACheck2.App.Providers;
namespace DBACheck2.App.Services;
public sealed class MetricsSnapshotService
{
 readonly string dir;
 public MetricsSnapshotService(){dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2","metrics");Directory.CreateDirectory(dir);}
 public async Task<string> CollectAsync(ServerProfile p){var provider=DatabaseProviderFactory.Create(p);var rows=await provider.QuickCheckAsync();var payload=new{timestamp=DateTimeOffset.Now,profile=p.Name,engine=p.Engine.ToString(),host=p.Host,checks=rows};var path=Path.Combine(dir,$"{San(p.Name)}-latest.json");await File.WriteAllTextAsync(path,JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true}));return path;}
 static string San(string s)=>string.Concat((string.IsNullOrWhiteSpace(s)?"server":s).Where(char.IsLetterOrDigit));
}