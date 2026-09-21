using System.IO;using System.Security.Cryptography;using System.Text;using System.Text.Json;using DBACheck2.App.Models;
namespace DBACheck2.App.Services;
public sealed class ServerProfileService
{
 private sealed class StoredProfile
 {
  public string Name{get;set;}=""; public DatabaseEngine Engine{get;set;} public string Host{get;set;}="localhost"; public int? Port{get;set;} public string DatabaseOrService{get;set;}="";
  public string Environment{get;set;}="PROD"; public string Authentication{get;set;}="Windows"; public string? Username{get;set;} public bool TrustCertificate{get;set;}=true;
  public bool RememberPassword{get;set;} public string? ProtectedPassword{get;set;}
  public bool UseSshTunnel{get;set;} public string? SshHost{get;set;} public int? SshPort{get;set;}=22; public string? SshUsername{get;set;} public string? SshPrivateKeyPath{get;set;}
 }
 private readonly string _path;
 public ServerProfileService(){var d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2");Directory.CreateDirectory(d);_path=Path.Combine(d,"server-profiles.json");}
 public async Task<List<ServerProfile>> LoadAsync()
 {
  if(!File.Exists(_path))return new();
  try
  {
   var json=await File.ReadAllTextAsync(_path);
   var stored=JsonSerializer.Deserialize<List<StoredProfile>>(json);
   if(stored is not null)return stored.Select(ToRuntime).ToList();
   return JsonSerializer.Deserialize<List<ServerProfile>>(json)??new();
  }catch{return new();}
 }
 public async Task SaveAsync(IEnumerable<ServerProfile> x)
 {
  var stored=x.Select(a=>new StoredProfile{Name=a.Name,Engine=a.Engine,Host=a.Host,Port=a.Port,DatabaseOrService=a.DatabaseOrService,Environment=a.Environment,Authentication=a.Authentication,Username=a.Username,TrustCertificate=a.TrustCertificate,RememberPassword=a.RememberPassword,ProtectedPassword=a.RememberPassword&&!string.IsNullOrEmpty(a.Password)?Protect(a.Password):null,UseSshTunnel=a.UseSshTunnel,SshHost=a.SshHost,SshPort=a.SshPort,SshUsername=a.SshUsername,SshPrivateKeyPath=a.SshPrivateKeyPath}).ToList();
  await File.WriteAllTextAsync(_path,JsonSerializer.Serialize(stored,new JsonSerializerOptions{WriteIndented=true}));
 }
 public async Task DeleteAsync(string name){var x=await LoadAsync();x.RemoveAll(a=>a.Name.Equals(name,StringComparison.OrdinalIgnoreCase));await SaveAsync(x);}
 private static ServerProfile ToRuntime(StoredProfile a)=>new(){Name=a.Name,Engine=a.Engine,Host=a.Host,Port=a.Port,DatabaseOrService=a.DatabaseOrService,Environment=a.Environment,Authentication=a.Authentication,Username=a.Username,Password=a.RememberPassword?Unprotect(a.ProtectedPassword):null,RememberPassword=a.RememberPassword,TrustCertificate=a.TrustCertificate,UseSshTunnel=a.UseSshTunnel,SshHost=a.SshHost,SshPort=a.SshPort,SshUsername=a.SshUsername,SshPrivateKeyPath=a.SshPrivateKeyPath};
 private static string Protect(string value){var b=Encoding.UTF8.GetBytes(value);return Convert.ToBase64String(ProtectedData.Protect(b,null,DataProtectionScope.CurrentUser));}
 private static string? Unprotect(string? value){if(string.IsNullOrWhiteSpace(value))return null;try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser));}catch{return null;}}
 public string PathName=>_path;
}