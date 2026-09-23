using System.Diagnostics;
using DBACheck2.App.Models;
using DBACheck2.App.Providers;

namespace DBACheck2.App.Services;

public sealed class AssessmentService
{
    public async Task<AssessmentRun> RunAsync(ServerProfile profile,AssessmentMode mode)
    {
        var started=DateTime.Now;
        var provider=DatabaseProviderFactory.Create(profile);
        var info=await provider.TestAsync();
        List<AssessmentCheck> checks;
        string packVersion;

        if(mode==AssessmentMode.Full)
        {
            switch(profile.Engine)
            {
                case DatabaseEngine.SqlServer:
                    // FULL on SQL Server executes the original DBAHEALTCHECK SQL packs.
                    checks=await new LegacySqlHealthCheckService(profile).RunFullAsync();
                    packVersion="DBAHEALTCHECK SQL Pack 1";
                    break;
                case DatabaseEngine.PostgreSql:
                    checks=await new PostgreSqlAssessmentPackService(profile).RunFullAsync();
                    packVersion="PostgreSQL Full Pack 1";
                    break;
                case DatabaseEngine.Oracle:
                    checks=await new OracleAssessmentPackService(profile).RunFullAsync();
                    packVersion="Oracle Full Pack 1";
                    break;
                case DatabaseEngine.MySqlMariaDb:
                    checks=await new MySqlAssessmentPackService(profile).RunFullAsync();
                    packVersion="MySQL/MariaDB Full Pack 1";
                    break;
                default:
                    throw new NotSupportedException($"Full Assessment is not available for {profile.Engine}.");
            }
        }
        else
        {
            var sw=Stopwatch.StartNew();
            var health=await provider.QuickCheckAsync();
            sw.Stop();
            checks=health.Select((x,i)=>FromHealth(profile.Engine,x,i,sw.ElapsedMilliseconds)).ToList()
                .Where(x=>x.Status!="OK" || x.Category is AssessmentCategory.Platform or AssessmentCategory.HighAvailability)
                .ToList();
            packVersion="Core Quick Baseline 1";
        }

        return new AssessmentRun {
            StartedAt=started,
            CompletedAt=DateTime.Now,
            ProfileName=profile.Name,
            Host=profile.Host,
            Engine=profile.Engine,
            Mode=mode,
            ProviderInfo=info.Replace("\n"," | "),
            PackVersion=packVersion,
            Checks=checks
        };
    }

    private static AssessmentCheck FromHealth(DatabaseEngine engine,HealthItem item,int index,long totalMs)
    {
        var category=Category(item.Area);
        var capability=item.Status switch {
            "UNSUPPORTED"=>"UNSUPPORTED",
            "NOT ENABLED"=>"NOT ENABLED",
            "NO PERMISSION"=>"NO PERMISSION",
            "UNAVAILABLE"=>"UNAVAILABLE",
            "ERROR"=>"ERROR",
            _=>"AVAILABLE"
        };

        return new AssessmentCheck {
            CheckId=$"{engine}:{item.Area}:{index}".ToUpperInvariant().Replace(" ","_"),
            Engine=engine,
            Category=category,
            Title=item.Area,
            Status=item.Status,
            Severity=item.Severity,
            Summary=item.Summary,
            Evidence=item.Detail,
            WhyItMatters=Why(engine,category,item),
            RecommendedAction=Action(engine,category,item),
            Verification=Verification(engine,category,item),
            Capability=capability,
            ReadOnly=true,
            DurationMs=totalMs,
            Timestamp=DateTime.Now
        };
    }

    private static AssessmentCategory Category(string area)
    {
        var a=(area??"").ToUpperInvariant();
        if(Has(a,"VERSION","ENGINE","ROLE","EDITION"))return AssessmentCategory.Platform;
        if(Has(a,"DATABASE SIZE","TABLESPACE","DISK","DATAFILE","FRA","GROWTH"))return AssessmentCategory.Capacity;
        if(Has(a,"TEMPDB","TEMP USAGE","TEMP ","UNDO"))return AssessmentCategory.Temporary;
        if(Has(a,"TRANSACTION","IDLE IN","XID","INNODB"))return AssessmentCategory.Transactions;
        if(Has(a,"LOG","WAL","XLOG","REDO","BINLOG","ARCHIVE"))return AssessmentCategory.Logs;
        if(Has(a,"BACKUP","RMAN"))return AssessmentCategory.Backup;
        if(Has(a,"JOB","MAINTENANCE","VACUUM","ANALYZE","SCHEDULER","EVENT"))return AssessmentCategory.Maintenance;
        if(Has(a,"ALWAYSON","REPLICATION","DATAGUARD","STREAMING","HA"))return AssessmentCategory.HighAvailability;
        if(Has(a,"INDEX","BLOAT","SCAN"))return AssessmentCategory.Indexes;
        if(Has(a,"WAIT","PERFORMANCE","TOP QUER","SESSION","BLOCKING","LOCK","MEMORY","CPU","I/O","IO "))return AssessmentCategory.Performance;
        if(Has(a,"CONFIG","SETTING","PARAMETER"))return AssessmentCategory.Configuration;
        if(Has(a,"SECURITY","LOGIN","PRIVILEGE","PERMISSION"))return AssessmentCategory.Security;
        return AssessmentCategory.Other;
    }

    private static string Why(DatabaseEngine engine,AssessmentCategory category,HealthItem item)
    {
        if(item.Status=="OK")return "Current evidence does not show an active risk for this check.";
        return category switch {
            AssessmentCategory.Capacity=>"Capacity pressure can lead to failed writes, unavailable databases or emergency growth operations.",
            AssessmentCategory.Temporary=>engine switch {
                DatabaseEngine.SqlServer=>"TempDB pressure can affect sorting, hashing, row-versioning and many concurrent workloads.",
                DatabaseEngine.PostgreSql=>"Temporary file growth can indicate memory pressure or expensive sort/hash workloads.",
                DatabaseEngine.Oracle=>"TEMP/UNDO pressure can interrupt SQL execution or long-running transactional work.",
                _=>"Temporary/InnoDB pressure can increase disk I/O and degrade transactional workloads."
            },
            AssessmentCategory.Transactions=>"Long or retained transactions can hold locks, retain logs/WAL/UNDO and delay maintenance.",
            AssessmentCategory.Logs=>"Log/WAL/redo/binlog health is fundamental for recovery, replication and sustained write activity.",
            AssessmentCategory.Backup=>"Backup gaps increase recovery exposure and can also affect log/archive retention.",
            AssessmentCategory.HighAvailability=>"Replica or transport problems can reduce resilience and increase recovery risk.",
            AssessmentCategory.Maintenance=>"Maintenance gaps can allow statistics, vacuum, jobs or housekeeping tasks to fall behind.",
            AssessmentCategory.Performance=>"Current workload evidence may indicate blocking, waits or resource pressure requiring investigation.",
            AssessmentCategory.Indexes=>"Index health and access patterns influence read cost, write overhead and plan quality.",
            AssessmentCategory.Security=>"Permissions and exposure should be reviewed because they directly affect access and operational risk.",
            _=>"This check contributes to the operational health baseline for the selected database engine."
        };
    }

    private static string Action(DatabaseEngine engine,AssessmentCategory category,HealthItem item)
    {
        if(item.Status=="OK")return "No immediate corrective action. Keep this value as baseline evidence.";
        if(item.Status=="NO PERMISSION")return "Grant only the minimum read permissions required for this assessment check, or document the limitation.";
        if(item.Status=="UNSUPPORTED")return "No action on the database. This capability is not supported by the detected version/provider.";
        if(item.Status=="NOT ENABLED")return "Review whether the feature should be enabled for this environment before relying on the check.";
        return category switch {
            AssessmentCategory.Capacity=>"Identify the consuming database/file and validate growth rate, free space and retention before resizing.",
            AssessmentCategory.Temporary=>"Identify the workload creating temporary pressure and correlate it with active SQL/transactions before changing capacity.",
            AssessmentCategory.Transactions=>"Identify the oldest transaction/session, owner and application before considering any interruption.",
            AssessmentCategory.Logs=>"Review retention/reuse cause, backup/archive health and replication before growing or shrinking log-related files.",
            AssessmentCategory.Backup=>"Validate the backup policy, last successful execution and recoverability requirements.",
            AssessmentCategory.HighAvailability=>"Review role, synchronization/lag, transport health and replica connectivity.",
            AssessmentCategory.Maintenance=>engine==DatabaseEngine.PostgreSql?"Review autovacuum/analyze progress, dead tuples and XID age.":"Review failed/delayed maintenance tasks and their operational impact.",
            AssessmentCategory.Performance=>"Open the corresponding DBACHECK diagnostic module and correlate current SQL, waits, blocking and resource usage.",
            AssessmentCategory.Indexes=>"Validate workload evidence before creating, dropping or rebuilding indexes.",
            _=>"Review the evidence and validate the condition with the corresponding DBACHECK diagnostic module."
        };
    }

    private static string Verification(DatabaseEngine engine,AssessmentCategory category,HealthItem item)
    {
        if(item.Status=="OK")return "Re-run the assessment later and compare against this baseline.";
        return category switch {
            AssessmentCategory.Backup=>"Re-run the check after the next successful backup and confirm the expected recovery point is available.",
            AssessmentCategory.HighAvailability=>"Re-run the check and confirm healthy/synchronized state with acceptable lag or queues.",
            AssessmentCategory.Logs=>"Re-run the check and confirm usage/retention cause returns to a healthy state and no abnormal growth continues.",
            AssessmentCategory.Transactions=>"Confirm the long transaction is gone and that locks/log/WAL/UNDO retention has been released.",
            AssessmentCategory.Capacity=>"Confirm free space is stable and growth no longer threatens the filesystem or database.",
            AssessmentCategory.Performance=>"Repeat the diagnostic sample and confirm waits/blocking/resource pressure has normalized.",
            _=>"Re-run this assessment check after remediation and compare status and evidence."
        };
    }

    private static bool Has(string value,params string[] tokens)=>tokens.Any(value.Contains);
}
