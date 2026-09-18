using System.IO;using System.Text.Json;using DBACheck2.App.Models;
namespace DBACheck2.App.Services;
public sealed class ServerProfileService
{
 private readonly string _path;
 public ServerProfileService(){var d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");Directory.CreateDirectory(d);_path=Path.Combine(d,"server-profiles.json");}
 public async Task<List<ServerProfile>> LoadAsync(){if(!File.Exists(_path))return new();try{return JsonSerializer.Deserialize<List<ServerProfile>>(await File.ReadAllTextAsync(_path))??new();}catch{return new();}}
 public async Task SaveAsync(IEnumerable<ServerProfile> x)=>await File.WriteAllTextAsync(_path,JsonSerializer.Serialize(x,new JsonSerializerOptions{WriteIndented=true}));
 public string PathName=>_path;
}