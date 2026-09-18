using System.Text;
using DBACheck2.App.Models;
namespace DBACheck2.App.Services;
public sealed class IncidentOperationsService
{
 private readonly SqlHealthService _sql; private readonly IncidentHistoryService _history; private readonly IncidentCorrelationEngine _correlation;
 public IncidentOperationsService(SqlHealthService sql){_sql=sql;_history=new();_correlation=new(sql);}
 public async Task<List<IncidentOperationSummary>> OpenAsync()
 {
  var rows=await _history.ListAsync(500);
  return rows.Where(x=>x.Status=="OPEN").Select(x=>new IncidentOperationSummary{Id=x.Id,CreatedAt=x.CreatedAt,ServerName=x.ServerName,DatabaseName=x.DatabaseName,Module=x.Module,Severity=x.Severity,Problem=x.Problem,Cause=x.Cause,Status=x.Status,RecurrenceCount=x.RecurrenceCount}).ToList();
 }
 public async Task<string> BuildIncidentCenterAsync(IncidentOperationSummary x)
 {
  var sb=new StringBuilder(); sb.AppendLine($"INCIDENT CENTER - #{x.Id}");sb.AppendLine($"Database: {x.DatabaseName} | Module: {x.Module} | Severity: {x.Severity} | Recurrence: {x.RecurrenceCount}");sb.AppendLine();sb.AppendLine("PROBLEMA");sb.AppendLine(x.Problem);sb.AppendLine();sb.AppendLine("CAUSA");sb.AppendLine(x.Cause);
  if(x.Module=="Transaction Log")
  {
   var logs=await _sql.GetLogDatabasesAsync();var log=logs.FirstOrDefault(z=>z.DatabaseName.Equals(x.DatabaseName,StringComparison.OrdinalIgnoreCase));
   if(log!=null){sb.AppendLine();sb.AppendLine("CORRELACIÓN OPERATIVA");sb.AppendLine(await _correlation.CorrelateLogAsync(log));}
  }
  sb.AppendLine();sb.AppendLine("SEGURIDAD");sb.AppendLine("READ - Incident Center correlaciona evidencia; no ejecuta cambios.");
  return sb.ToString();
 }
 public async Task<string> BuildTechnicalReportAsync(IncidentOperationSummary x)
 {
  var center=await BuildIncidentCenterAsync(x);var all=await _history.ListAsync(500);var row=all.First(z=>z.Id==x.Id);
  return $"DBACHECK 2 - TECHNICAL INCIDENT REPORT\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{center}\n\nEVIDENCE SNAPSHOT\n{row.Evidence}\n\nACTION TAKEN\n{row.ActionTaken}\n\nVERIFICATION\n{row.Verification}\n\nSTATUS\n{row.Status}\n";
 }
}