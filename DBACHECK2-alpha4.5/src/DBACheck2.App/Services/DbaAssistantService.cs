using System.Text;using DBACheck2.App.Models;using DBACheck2.App.Providers;
namespace DBACheck2.App.Services;
public sealed class DbaAssistantService
{
 public async Task<string> AskAsync(ServerProfile profile,string question)
 {
  if(string.IsNullOrWhiteSpace(question))return "Ingresá una pregunta DBA.";
  var provider=DatabaseProviderFactory.Create(profile);
  var q=question.ToLowerInvariant();var sb=new StringBuilder();
  sb.AppendLine($"DBA ASSISTANT - {provider.DisplayName}");sb.AppendLine($"Target: {profile.Host} | Environment: {profile.Environment}");sb.AppendLine();
  if(profile.Engine!=DatabaseEngine.SqlServer){sb.AppendLine($"Provider {provider.DisplayName}: estructura multi-engine disponible; collectors específicos aún no habilitados.");sb.AppendLine("No se ejecutó ninguna consulta contra el motor.");return sb.ToString();}
  var health=await provider.QuickCheckAsync();
  IEnumerable<HealthItem> selected=health;
  if(q.Contains("bloq")||q.Contains("lock"))selected=health.Where(x=>x.Area=="BLOCKING");
  else if(q.Contains("log"))selected=health.Where(x=>x.Area=="LOG"||x.Area=="TRANSACTIONS"||x.Area=="BACKUPS");
  else if(q.Contains("backup"))selected=health.Where(x=>x.Area=="BACKUPS"||x.Area=="JOBS");
  else if(q.Contains("temp"))selected=health.Where(x=>x.Area=="TEMPDB"||x.Area=="VERSION STORE"||x.Area=="TRANSACTIONS");
  else if(q.Contains("trans"))selected=health.Where(x=>x.Area=="TRANSACTIONS"||x.Area=="BLOCKING");
  var rows=selected.ToList();sb.AppendLine("EVIDENCIA RECOLECTADA");
  foreach(var x in rows)sb.AppendLine($"[{x.Status}] {x.Area}: {x.Summary} | {x.Detail}");
  sb.AppendLine();sb.AppendLine("INTERPRETACIÓN");var bad=rows.Where(x=>x.Status is "WARNING" or "CRITICAL" or "ERROR").ToList();
  if(bad.Count==0)sb.AppendLine("No se detectó una condición de alerta en los checks relacionados con la pregunta.");
  else foreach(var x in bad)sb.AppendLine($"- {x.Area}: revisar {x.Summary}. La evidencia anterior proviene del collector DBACHECK.");
  sb.AppendLine();sb.AppendLine("SAFETY: sólo lectura. El Assistant no ejecutó cambios.");
  return sb.ToString();
 }
}