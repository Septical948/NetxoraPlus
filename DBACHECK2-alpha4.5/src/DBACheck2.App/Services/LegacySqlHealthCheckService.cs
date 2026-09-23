using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DBACheck2.App.Models;
using Microsoft.Data.SqlClient;

namespace DBACheck2.App.Services;

public sealed class LegacySqlHealthCheckService
{
    private readonly ServerProfile _profile;

    public LegacySqlHealthCheckService(ServerProfile profile)=>_profile=profile;

    public async Task<List<AssessmentCheck>> RunFullAsync()
    {
        var scripts=LoadEmbeddedScripts();
        var unique=new List<LegacyScript>();
        var seenNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Preserve original precedence: Script -> Script Index -> Script-TSQL.
        // Duplicate filenames and byte-equivalent SQL are executed only once.
        foreach(var script in scripts.OrderBy(x=>x.Order).ThenBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase))
        {
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Sql.Replace("\r\n","\n").Trim())));
            if(!seenNames.Add(script.FileName) || !seenHashes.Add(hash))continue;
            unique.Add(script);
        }

        var result=new List<AssessmentCheck>();
        await using var cn=new SqlConnection(BuildConnectionString());
        await cn.OpenAsync();

        foreach(var script in unique.OrderBy(x=>x.Order).ThenBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase))
            result.Add(await ExecuteAsync(cn,script));

        return result;
    }

    private string BuildConnectionString()
    {
        var target=_profile.Host;
        if(_profile.Port is not null && !target.Contains(',') && !target.Contains('\\'))
            target+=$",{_profile.Port}";

        var b=new SqlConnectionStringBuilder {
            DataSource=target,
            InitialCatalog="master",
            TrustServerCertificate=_profile.TrustCertificate,
            ConnectTimeout=15,
            ApplicationName="DBACHECK2 Assessment"
        };

        var sqlAuth=!string.IsNullOrWhiteSpace(_profile.Username) &&
                    !_profile.Authentication.Equals("Windows",StringComparison.OrdinalIgnoreCase);

        if(sqlAuth)
        {
            b.IntegratedSecurity=false;
            b.UserID=_profile.Username;
            b.Password=_profile.Password??"";
        }
        else b.IntegratedSecurity=true;

        return b.ConnectionString;
    }

    private async Task<AssessmentCheck> ExecuteAsync(SqlConnection cn,LegacyScript script)
    {
        var started=DateTime.Now;
        try
        {
            var evidence=new StringBuilder();
            var totalRows=0;
            var resultSet=0;

            foreach(var batch in SplitBatches(script.Sql))
            {
                if(string.IsNullOrWhiteSpace(batch))continue;
                await using var cmd=new SqlCommand(batch,cn){CommandTimeout=120};
                await using var reader=await cmd.ExecuteReaderAsync();

                do
                {
                    if(reader.FieldCount<=0)continue;
                    resultSet++;
                    evidence.AppendLine($"RESULT SET {resultSet}");
                    evidence.AppendLine(string.Join(" | ",Enumerable.Range(0,reader.FieldCount).Select(reader.GetName)));

                    var shown=0;
                    while(await reader.ReadAsync())
                    {
                        totalRows++;
                        if(shown<500)
                        {
                            var values=new string[reader.FieldCount];
                            for(var i=0;i<reader.FieldCount;i++)
                            {
                                var value=reader.IsDBNull(i)?"NULL":Convert.ToString(reader.GetValue(i))??"";
                                values[i]=value.Replace("\r"," ").Replace("\n"," ");
                            }
                            evidence.AppendLine(string.Join(" | ",values));
                            shown++;
                        }
                    }
                    if(totalRows>shown)evidence.AppendLine($"... output truncated in UI; total rows seen so far: {totalRows}");
                    evidence.AppendLine();
                } while(await reader.NextResultAsync());
            }

            var status=ClassifyStatus(script.FileName,totalRows);
            return new AssessmentCheck {
                CheckId=$"SQLSERVER:LEGACY:{script.FileName}".ToUpperInvariant(),
                Engine=DatabaseEngine.SqlServer,
                Category=Category(script.FileName),
                Title=Path.GetFileNameWithoutExtension(script.FileName),
                Status=status,
                Severity=Severity(status),
                Summary=Summary(script.FileName,totalRows,status),
                Evidence=$"SOURCE: {script.SourceFolder} / {script.FileName}\nROWS: {totalRows}\n\n{evidence}",
                WhyItMatters=Why(script.FileName),
                RecommendedAction=Action(script.FileName,status),
                Verification="Re-run the Full Assessment after remediation and compare this check with the previous assessment history.",
                Capability="AVAILABLE",
                ReadOnly=true,
                DurationMs=(long)(DateTime.Now-started).TotalMilliseconds,
                Timestamp=DateTime.Now
            };
        }
        catch(Exception ex)
        {
            return new AssessmentCheck {
                CheckId=$"SQLSERVER:LEGACY:{script.FileName}".ToUpperInvariant(),
                Engine=DatabaseEngine.SqlServer,
                Category=Category(script.FileName),
                Title=Path.GetFileNameWithoutExtension(script.FileName),
                Status=ex is SqlException sql && sql.Number==229?"NO PERMISSION":"ERROR",
                Severity=ex is SqlException sql2 && sql2.Number==229?2:4,
                Summary=$"Legacy HealthCheck query failed: {ex.Message}",
                Evidence=$"SOURCE: {script.SourceFolder} / {script.FileName}\n{ex}",
                WhyItMatters="This legacy DBAHEALTCHECK check could not be evaluated, so the assessment is incomplete for this item.",
                RecommendedAction="Review SQL Server version, permissions and query compatibility. Do not change the database only to satisfy the assessment.",
                Verification="Re-run this check after correcting the compatibility or permission issue.",
                Capability=ex is SqlException sql3 && sql3.Number==229?"NO PERMISSION":"ERROR",
                ReadOnly=true,
                DurationMs=(long)(DateTime.Now-started).TotalMilliseconds,
                Timestamp=DateTime.Now
            };
        }
    }

    private static IEnumerable<string> SplitBatches(string sql)=>
        Regex.Split(sql,@"^\s*GO\s*(?:--.*)?$",RegexOptions.Multiline|RegexOptions.IgnoreCase);

    private static List<LegacyScript> LoadEmbeddedScripts()
    {
        var asm=Assembly.GetExecutingAssembly();
        var list=new List<LegacyScript>();
        foreach(var name in asm.GetManifestResourceNames())
        {
            var folder=name.StartsWith("LegacyHealthCheck.Script.",StringComparison.Ordinal)?"Script":
                       name.StartsWith("LegacyHealthCheck.ScriptIndex.",StringComparison.Ordinal)?"Script Index":
                       name.StartsWith("LegacyHealthCheck.ScriptTsql.",StringComparison.Ordinal)?"Script-TSQL":null;
            if(folder is null)continue;

            using var stream=asm.GetManifestResourceStream(name);
            if(stream is null)continue;
            using var sr=new StreamReader(stream,Encoding.UTF8,true);
            var sql=sr.ReadToEnd();
            var file=name[(name.LastIndexOf('.')+1)..];
            // Logical names encode the real filename before the final .sql marker.
            var prefix=folder switch{"Script"=>"LegacyHealthCheck.Script.","Script Index"=>"LegacyHealthCheck.ScriptIndex.",_=>"LegacyHealthCheck.ScriptTsql."};
            var logical=name[prefix.Length..];
            var fileName=logical.EndsWith(".sql",StringComparison.OrdinalIgnoreCase)?logical:logical+".sql";
            list.Add(new LegacyScript(folder,fileName,sql,folder=="Script"?1:folder=="Script Index"?2:3));
        }
        return list;
    }

    private static string ClassifyStatus(string file,int rows)
    {
        if(rows==0)return "OK";
        var f=file.ToLowerInvariant();
        if(ContainsAny(f,"missingindex","duplicate","badindex","disableindex","indexunused","hypothetical","fksinindex","find heap","guid clustered","900_bytes","more 1 column","fill_factor80","convert_implicit","sp recompile","prefix_sp","cursorobject","selectfrom","index hardcode","nocount","errorlog latency"))
            return "WARNING";
        return "INFO";
    }

    private static AssessmentCategory Category(string file)
    {
        var f=file.ToLowerInvariant();
        if(f.StartsWith("10.")||f.Contains("index_")||f.Contains("index "))return AssessmentCategory.Indexes;
        if(f.StartsWith("12.")||f.StartsWith("13.")||f.StartsWith("14."))return AssessmentCategory.Performance;
        if(f.StartsWith("2_")||f.StartsWith("3.")||f.StartsWith("4."))return AssessmentCategory.Configuration;
        if(f.StartsWith("5.")||f.Contains("datafile"))return AssessmentCategory.Capacity;
        if(f.StartsWith("6.")||f.StartsWith("7.")||f.StartsWith("8.")||f.StartsWith("9."))return AssessmentCategory.Performance;
        if(f.StartsWith("11."))return AssessmentCategory.Capacity;
        return AssessmentCategory.Other;
    }

    private static string Summary(string file,int rows,string status)=>status switch {
        "OK"=>$"{Path.GetFileNameWithoutExtension(file)} returned no findings.",
        "WARNING"=>$"{Path.GetFileNameWithoutExtension(file)} returned {rows} finding row(s) requiring review.",
        "INFO"=>$"{Path.GetFileNameWithoutExtension(file)} returned {rows} informational row(s).",
        _=>$"{Path.GetFileNameWithoutExtension(file)} completed."
    };

    private static string Why(string file)
    {
        var f=file.ToLowerInvariant();
        if(f.Contains("missingindex"))return "Missing-index DMV evidence can identify expensive access patterns, but recommendations must be validated against workload and write cost.";
        if(f.Contains("duplicate"))return "Overlapping indexes can increase storage and write overhead without providing proportional read benefit.";
        if(f.Contains("indexunused"))return "Unused indexes may add write and maintenance cost, but usage counters must be interpreted over a representative uptime period.";
        if(f.Contains("statistics"))return "Old statistics can contribute to poor cardinality estimates and suboptimal execution plans.";
        if(f.Contains("heap"))return "Heap tables can be appropriate in some workloads, but forwarding records and access patterns should be reviewed.";
        if(f.Contains("wait"))return "Wait statistics provide cumulative evidence about where SQL Server has spent time waiting since startup or the last reset.";
        if(f.Contains("filestats"))return "File I/O latency and activity help identify database files or storage paths under pressure.";
        if(f.Contains("xp_cmdshell")||f.Contains("sa_status"))return "Server-level security configuration should match the organization's operational and security policy.";
        if(f.Contains("maxmem")||f.Contains("maxdegree")||f.Contains("maxdop"))return "Memory and parallelism configuration can materially affect workload stability and concurrency.";
        return "This is an original DBAHEALTCHECK SQL Server check preserved as part of the Full Assessment.";
    }

    private static string Action(string file,string status)
    {
        if(status=="OK")return "No immediate action. Keep the result as baseline evidence.";
        var f=file.ToLowerInvariant();
        if(f.Contains("missingindex"))return "Review high-impact candidates with query workload and existing indexes before creating anything.";
        if(f.Contains("duplicate")||f.Contains("indexunused"))return "Compare definitions and real usage before dropping or consolidating indexes.";
        if(f.Contains("statistics"))return "Validate stale statistics against table change rate and maintenance policy before updating.";
        if(f.Contains("wait"))return "Correlate top waits with active workload and DBACHECK performance modules; do not treat a wait name alone as root cause.";
        return "Review the returned evidence and validate it with the corresponding DBACHECK diagnostic module before making changes.";
    }

    private static int Severity(string status)=>status switch{"CRITICAL"=>5,"ERROR"=>4,"WARNING"=>3,"NO PERMISSION"=>2,_=>status=="OK"?0:1};
    private static bool ContainsAny(string value,params string[] tokens)=>tokens.Any(value.Contains);

    private sealed record LegacyScript(string SourceFolder,string FileName,string Sql,int Order);
}
