using System.Reflection;
using System.Text.Json;

namespace DBACheck2.App.Services;

public enum UpdateState { NotChecked, UpToDate, UpdateAvailable, NoPublishedRelease, CheckFailed }

public sealed class UpdateCheckResult
{
    public UpdateState State { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ReleaseUrl { get; init; }
}

public sealed class UpdateCheckService
{
    private const string LatestReleaseApi="https://api.github.com/repos/Septical948/NetxoraPlus/releases/latest";

    public async Task<UpdateCheckResult> CheckAsync()
    {
        var currentAssembly=Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0,0,0,0);
        var current=$"{currentAssembly.Major}.{currentAssembly.Minor}.{currentAssembly.Build}";
        try
        {
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(8)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DBACHECK2-UpdateChecker/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response=await client.GetAsync(LatestReleaseApi);
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)
                return new(){State=UpdateState.NoPublishedRelease,CurrentVersion=current,Message="No published GitHub release is available yet."};

            response.EnsureSuccessStatusCode();
            var json=await response.Content.ReadAsStringAsync();
            using var doc=JsonDocument.Parse(json);
            var tag=doc.RootElement.TryGetProperty("tag_name",out var t)?t.GetString()??"":"";
            var url=doc.RootElement.TryGetProperty("html_url",out var u)?u.GetString():null;
            if(!TryVersion(tag,out var latest))
                return new(){State=UpdateState.CheckFailed,CurrentVersion=current,LatestVersion=tag,ReleaseUrl=url,Message=$"Unable to parse release tag '{tag}'."};

            var state=latest>currentAssembly?UpdateState.UpdateAvailable:UpdateState.UpToDate;
            return new(){
                State=state,CurrentVersion=current,LatestVersion=tag,ReleaseUrl=url,
                Message=state==UpdateState.UpToDate?"DBACHECK2 is up to date.":$"Update available: {tag}"
            };
        }
        catch(Exception ex)
        {
            return new(){State=UpdateState.CheckFailed,CurrentVersion=current,Message=ex.Message};
        }
    }

    private static bool TryVersion(string value,out Version version)
    {
        var raw=(value??"").Trim().TrimStart('v','V');
        var dash=raw.IndexOf('-');if(dash>0)raw=raw[..dash];
        var plus=raw.IndexOf('+');if(plus>0)raw=raw[..plus];
        if(Version.TryParse(raw,out var parsed) && parsed is not null){version=parsed;return true;}
        version=new Version(0,0,0,0);
        return false;
    }
}
