using System.IO;using System.Text;using DBACheck2.App.Models;
namespace DBACheck2.App.Services;
public static class ReportExportService
{
 public static async Task<string> SaveTextAsync(ServerProfile p,IEnumerable<HealthItem> items,string? folder=null){folder??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"DBACHECK2 Reports");Directory.CreateDirectory(folder);var path=Path.Combine(folder,$"DBACHECK_{Safe(p.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");var b=new StringBuilder();b.AppendLine("DBACHECK 2 - TECHNICAL HEALTH REPORT");b.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");b.AppendLine($"Profile: {p.Display}");b.AppendLine();foreach(var x in items)b.AppendLine($"[{x.Status}] {x.Area}\n{x.Summary}\n{x.Detail}\n");await File.WriteAllTextAsync(path,b.ToString());return path;}
 static string Safe(string s)=>string.Concat((string.IsNullOrWhiteSpace(s)?"server":s).Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch));
}