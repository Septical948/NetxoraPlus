using System.Text.Json.Serialization;
namespace DBACheck2.App.Models;
public enum DatabaseEngine { SqlServer, PostgreSql, Oracle, MySqlMariaDb }
public sealed class ServerProfile
{
 public string Name{get;set;}=""; public DatabaseEngine Engine{get;set;}=DatabaseEngine.SqlServer;
 public string Host{get;set;}="localhost"; public int? Port{get;set;} public string DatabaseOrService{get;set;}="";
 public string Environment{get;set;}="PROD"; public string Authentication{get;set;}="Windows";
 public string? Username{get;set;} [JsonIgnore] public string? Password{get;set;}
 public bool RememberPassword{get;set;}=false;
 public bool UseSshTunnel{get;set;}=false; public string? SshHost{get;set;} public int? SshPort{get;set;}=22; public string? SshUsername{get;set;} public string? SshPrivateKeyPath{get;set;} [JsonIgnore] public string? SshKeyPassphrase{get;set;}
 public bool TrustCertificate{get;set;}=true;
 public string Display=>$"{Name} | {Engine} | {Host}{(Port is null?"":$":{Port}")} | {Environment}";
}