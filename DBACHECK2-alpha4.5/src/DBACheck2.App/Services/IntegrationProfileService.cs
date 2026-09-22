using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class IntegrationProfileService
{
    private sealed class Stored
    {
        public string Name { get; set; } = "";
        public IntegrationSource Source { get; set; }
        public string Endpoint { get; set; } = "";
        public bool RememberToken { get; set; }
        public bool VerifyTls { get; set; } = true;
        public string? ProtectedToken { get; set; }
    }

    private readonly string _path=Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Netxora","DBACHECK2","integration-profiles.json");

    public string PathName=>_path;

    public async Task<List<IntegrationConnectionProfile>> LoadAsync()
    {
        try
        {
            if(!File.Exists(_path)) return new();
            var json=await File.ReadAllTextAsync(_path);
            var stored=JsonSerializer.Deserialize<List<Stored>>(json)??new();
            return stored.Select(x=>new IntegrationConnectionProfile {
                Name=x.Name, Source=x.Source, Endpoint=x.Endpoint,
                RememberToken=x.RememberToken, VerifyTls=x.VerifyTls,
                Token=x.RememberToken?Unprotect(x.ProtectedToken):null
            }).ToList();
        }
        catch { return new(); }
    }

    public async Task SaveAsync(IEnumerable<IntegrationConnectionProfile> profiles)
    {
        var dir=Path.GetDirectoryName(_path)!; Directory.CreateDirectory(dir);
        var stored=profiles.Select(x=>new Stored {
            Name=x.Name, Source=x.Source, Endpoint=x.Endpoint,
            RememberToken=x.RememberToken, VerifyTls=x.VerifyTls,
            ProtectedToken=x.RememberToken&&!string.IsNullOrEmpty(x.Token)?Protect(x.Token):null
        }).ToList();
        await File.WriteAllTextAsync(_path,JsonSerializer.Serialize(stored,new JsonSerializerOptions{WriteIndented=true}));
    }

    public async Task DeleteAsync(string name)
    {
        var list=await LoadAsync();
        list.RemoveAll(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        await SaveAsync(list);
    }

    private static string Protect(string value)
    {
        var raw=Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser));
    }

    private static string? Unprotect(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)) return null;
        try {
            var raw=ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        } catch { return null; }
    }
}
