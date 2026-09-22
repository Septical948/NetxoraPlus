using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public static class IntegrationCorrelationService
{
    public static IntegrationCategory Category(IntegrationEvent e)
    {
        var text=(e.Name+" "+e.Tags+" "+e.RawDetail).ToLowerInvariant();
        if(Has(text,"disk","filesystem","space","free space","storage")) return IntegrationCategory.Storage;
        if(Has(text,"transaction log","log space","wal","xlog","redo","binlog")) return IntegrationCategory.LogWal;
        if(Has(text,"blocking","blocked","lock","deadlock")) return IntegrationCategory.Locking;
        if(Has(text,"replication","replica","alwayson","availability group","dataguard","streaming")) return IntegrationCategory.HaReplication;
        if(Has(text,"backup","rman","dump")) return IntegrationCategory.Backup;
        if(Has(text,"cpu","load","latency","slow","performance","i/o","io wait")) return IntegrationCategory.Performance;
        if(Has(text,"connection","connections","unreachable","down","availability")) return IntegrationCategory.Availability;
        return IntegrationCategory.General;
    }

    public static string Hint(IntegrationEvent e)=>Category(e) switch {
        IntegrationCategory.Storage=>"STORAGE: correlate filesystem pressure with DB files, transaction/WAL/redo/binlog, backups and temp usage.",
        IntegrationCategory.LogWal=>"LOG/WAL: inspect log usage, retention/reuse wait, long transactions, replication and backup/archive health.",
        IntegrationCategory.Locking=>"LOCKING: inspect blockers, root blocker, transaction age, waits and current SQL.",
        IntegrationCategory.HaReplication=>"HA/REPLICATION: inspect role, synchronization state, queues/lag and transport/apply health.",
        IntegrationCategory.Backup=>"BACKUP: validate last successful backup, failures, duration and storage availability.",
        IntegrationCategory.Performance=>"PERFORMANCE: correlate active requests, waits, CPU/I/O, long SQL, sessions and memory pressure.",
        IntegrationCategory.Availability=>"AVAILABILITY: validate network endpoint, engine service, connection capacity and database state.",
        _=>"GENERAL: correlate this monitoring event with the target DB profile and run the engine-specific Quick Check."
    };

    private static bool Has(string text,params string[] values)=>values.Any(text.Contains);
}
