namespace DBACheck2.App.Models;
public sealed class BackupDatabaseInfo
{
 public string DatabaseName {get;set;}=""; public string RecoveryModel {get;set;}="";
 public DateTime? LastFull {get;set;} public DateTime? LastDiff {get;set;} public DateTime? LastLog {get;set;}
 public decimal? LastFullMb {get;set;}
 public double FullAgeHours => LastFull.HasValue ? (DateTime.Now-LastFull.Value).TotalHours : double.MaxValue;
 public string Status => !LastFull.HasValue || FullAgeHours>24 ? "WARNING" : "OK";
}
