namespace DBACheck2.App.Models;
public sealed class IncidentOperationSummary
{
 public long Id {get;init;} public DateTime CreatedAt{get;init;} public string ServerName{get;init;}="" ; public string DatabaseName{get;init;}="";
 public string Module{get;init;}=""; public string Severity{get;init;}=""; public string Problem{get;init;}=""; public string Cause{get;init;}="";
 public string Status{get;init;}="OPEN"; public int RecurrenceCount{get;init;}
}