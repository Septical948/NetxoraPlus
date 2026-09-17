namespace DBACheck2.App.Models;
public sealed class AgentJobInfo
{
 public Guid JobId {get;set;} public string JobName {get;set;}=""; public bool Enabled {get;set;}
 public int LastOutcome {get;set;} public DateTime? LastRun {get;set;} public string LastMessage {get;set;}=""; public int LastDurationSeconds {get;set;}
 public string Outcome => LastOutcome switch {1=>"SUCCEEDED",0=>"FAILED",2=>"RETRY",3=>"CANCELED",_=>"UNKNOWN"};
 public string Status => Enabled && LastOutcome==0 ? "WARNING" : "OK";
}
