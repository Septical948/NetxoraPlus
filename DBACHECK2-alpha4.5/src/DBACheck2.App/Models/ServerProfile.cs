namespace DBACheck2.App.Models;
public enum DatabaseEngine { SqlServer, PostgreSql, Oracle, MySqlMariaDb }
public sealed class ServerProfile
{
 public string Name{get;set;}=""; public DatabaseEngine Engine{get;set;}=DatabaseEngine.SqlServer;
 public string Host{get;set;}="localhost"; public int? Port{get;set;} public string DatabaseOrService{get;set;}="";
 public string Environment{get;set;}="PROD"; public string Authentication{get;set;}="Windows";
 public string? Username{get;set;} public string? Password{get;set;}
 public bool TrustCertificate{get;set;}=true;
 public string Display=>$"{Name} | {Engine} | {Host}{(Port is null?"":$":{Port}")} | {Environment}";
}