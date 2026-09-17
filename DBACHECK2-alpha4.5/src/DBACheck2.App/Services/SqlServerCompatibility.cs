using Microsoft.Data.SqlClient;

namespace DBACheck2.App.Services;

public enum SqlCompatibilityProfile
{
    Legacy,
    Modern
}

public sealed record SqlServerCapabilities(
    string ServerName,
    string ProductVersion,
    string Edition,
    int MajorVersion,
    SqlCompatibilityProfile Profile)
{
    public string VersionLabel => MajorVersion switch
    {
        10 => "SQL Server 2008 R2",
        11 => "SQL Server 2012",
        12 => "SQL Server 2014",
        13 => "SQL Server 2016",
        14 => "SQL Server 2017",
        15 => "SQL Server 2019",
        16 => "SQL Server 2022",
        17 => "SQL Server 2025+",
        _ => $"SQL Server {ProductVersion}"
    };

    public bool SupportsAvailabilityGroups => MajorVersion >= 11;
    public bool SupportsDmExecInputBuffer => MajorVersion >= 13;
    public bool SupportsStringAgg => MajorVersion >= 14;
    public bool SupportsDmDbLogSpaceUsage => MajorVersion >= 11;
}

public static class SqlServerCompatibility
{
    public static async Task<SqlServerCapabilities> DetectAsync(string connectionString)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT
 CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),
 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
 CAST(SERVERPROPERTY('Edition') AS nvarchar(128));";

        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        var server = Convert.ToString(r.GetValue(0)) ?? "";
        var version = Convert.ToString(r.GetValue(1)) ?? "0";
        var edition = Convert.ToString(r.GetValue(2)) ?? "";
        var majorText = version.Split('.')[0];
        _ = int.TryParse(majorText, out var major);

        // LEGACY intentionally covers 2008 R2 through 2014.
        // MODERN starts at SQL Server 2016, but individual feature flags
        // still protect functions introduced later (for example STRING_AGG in 2017).
        var profile = major <= 12 ? SqlCompatibilityProfile.Legacy : SqlCompatibilityProfile.Modern;
        return new SqlServerCapabilities(server, version, edition, major, profile);
    }

    public static string LegacyConcat(string selectExpression, string fromAndWhere)
    {
        // SQL Server 2008 R2+ compatible replacement for STRING_AGG.
        return $@"STUFF((SELECT '; ' + CAST({selectExpression} AS nvarchar(max)) {fromAndWhere} FOR XML PATH(''), TYPE).value('.','nvarchar(max)'),1,2,'')";
    }
}
