using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class NotificationIntegrationStore
{
    private sealed class StoredDestination
    {
        public string Name { get; set; }="";
        public NotificationProvider Provider { get; set; }
        public string TenantOrWorkspaceId { get; set; }="";
        public string TenantOrWorkspaceName { get; set; }="";
        public string ChannelId { get; set; }="";
        public string ChannelName { get; set; }="";
        public string Endpoint { get; set; }="";
        public string? ProtectedSecret { get; set; }
        public bool Enabled { get; set; }=true;
        public DateTime? ConnectedUtc { get; set; }
    }
    private sealed class State
    {
        public List<StoredDestination> Destinations { get; set; }=new();
        public List<NotificationRoute> Routes { get; set; }=new();
    }

    private readonly string _path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netxora","DBACHECK2","notification-integrations.json");

    public async Task<(List<NotificationDestination> Destinations,List<NotificationRoute> Routes)> LoadAsync()
    {
        try
        {
            if(!File.Exists(_path)) return (new(),new());
            var state=JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(_path))??new();
            return (state.Destinations.Select(x=>new NotificationDestination {
                Name=x.Name,Provider=x.Provider,TenantOrWorkspaceId=x.TenantOrWorkspaceId,TenantOrWorkspaceName=x.TenantOrWorkspaceName,
                ChannelId=x.ChannelId,ChannelName=x.ChannelName,Endpoint=x.Endpoint,Secret=Unprotect(x.ProtectedSecret),
                Enabled=x.Enabled,ConnectedUtc=x.ConnectedUtc
            }).ToList(),state.Routes);
        }
        catch { return (new(),new()); }
    }

    public async Task SaveAsync(IEnumerable<NotificationDestination> destinations,IEnumerable<NotificationRoute> routes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var state=new State {
            Destinations=destinations.Select(x=>new StoredDestination {
                Name=x.Name,Provider=x.Provider,TenantOrWorkspaceId=x.TenantOrWorkspaceId,TenantOrWorkspaceName=x.TenantOrWorkspaceName,
                ChannelId=x.ChannelId,ChannelName=x.ChannelName,Endpoint=x.Endpoint,
                ProtectedSecret=string.IsNullOrWhiteSpace(x.Secret)?null:Protect(x.Secret),Enabled=x.Enabled,ConnectedUtc=x.ConnectedUtc
            }).ToList(),
            Routes=routes.ToList()
        };
        await File.WriteAllTextAsync(_path,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
    }

    private static string Protect(string value)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));
    private static string? Unprotect(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser));}
        catch{return null;}
    }
}
