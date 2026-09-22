using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public static class IntegrationCorrelationService
{
    public static string Hint(IntegrationEvent e)
    {
        var text=(e.Name+" "+e.Tags+" "+e.RawDetail).ToLowerInvariant();
        if(Has(text,"disk","filesystem","space","free space","storage"))
            return "STORAGE: correlate filesystem pressure with DB files, transaction/WAL/redo/binlog, backups and temp usage.";
        if(Has(text,"transaction log","log space","wal","xlog","redo","binlog"))
            return "LOG/WAL: inspect log usage, retention/reuse wait, long transactions, replication and backup/archive health.";
        if(Has(text,"blocking","blocked","lock","deadlock"))
            return "LOCKING: inspect blockers, root blocker, transaction age, waits and current SQL.";
        if(Has(text,"replication","replica","alwayson","availability group","dataguard","streaming"))
            return "HA/REPLICATION: inspect role, synchronization state, queues/lag and transport/apply health.";
        if(Has(text,"backup","rman","dump"))
            return "BACKUP: validate last successful backup, failures, duration and storage availability.";
        if(Has(text,"cpu","load","latency","slow","performance","i/o","io wait"))
            return "PERFORMANCE: correlate active requests, waits, CPU/I/O, long SQL, sessions and memory pressure.";
        if(Has(text,"connection","connections","unreachable","down","availability"))
            return "AVAILABILITY: validate network endpoint, engine service, connection capacity and database state.";
        return "GENERAL: correlate this monitoring event with the target DB profile and run the engine-specific Quick Check.";
    }

    private static bool Has(string text,params string[] values)=>values.Any(text.Contains);
}
