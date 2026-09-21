using DBACheck2.App.Models;

namespace DBACheck2.App.Providers;
public sealed class OracleAutoProvider : IDatabaseProvider
{
    private readonly ServerProfile p;
    public OracleAutoProvider(ServerProfile profile)=>p=profile;
    public DatabaseEngine Engine=>DatabaseEngine.Oracle;
    public string DisplayName=>"Oracle Auto";

    public async Task<string> TestAsync()
    {
        try { return "Provider: Oracle Modern\n"+await new OracleProvider(p).TestAsync(); }
        catch(Exception modern)
        {
            try { return "Provider: Oracle Legacy (OraOLEDB)\n"+await new OracleLegacyProvider(p).TestAsync(); }
            catch(Exception legacy){throw new InvalidOperationException($"Oracle Auto could not connect. Modern: {modern.Message} | Legacy: {legacy.Message}");}
        }
    }

    public async Task<List<HealthItem>> QuickCheckAsync()
    {
        try { return await new OracleProvider(p).QuickCheckAsync(); }
        catch
        {
            return await new OracleLegacyProvider(p).QuickCheckAsync();
        }
    }
}