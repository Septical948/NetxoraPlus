using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public static class IncidentDiagnosisEngine
{
    public static IncidentDiagnosis DiagnoseLog(LogDatabaseInfo x)
    {
        var wait=(x.ReuseWait ?? "").ToUpperInvariant();
        var severity=x.UsedPct>=90 ? "CRITICAL" : x.UsedPct>=75 ? "WARNING" : wait=="NOTHING" ? "OK" : "INFO";
        var cause=wait switch {
            "LOG_BACKUP"=>"SQL Server necesita un backup de transaction log antes de poder reutilizar VLFs.",
            "ACTIVE_TRANSACTION"=>"Una transacción activa mantiene parte del transaction log requerido.",
            "AVAILABILITY_REPLICA"=>"Una réplica de Availability Group puede estar reteniendo truncamiento del log.",
            "REPLICATION"=>"Replicación puede estar reteniendo registros del transaction log.",
            "CHECKPOINT"=>"SQL Server está esperando un CHECKPOINT para reutilizar espacio.",
            "NOTHING"=>"No existe actualmente una espera de reutilización del transaction log.",
            _=>$"SQL Server reporta log_reuse_wait_desc = {x.ReuseWait}."
        };
        var action=wait switch {
            "LOG_BACKUP"=>"Verificar el job/política de backup de LOG y confirmar por qué no se ejecutó o quedó atrasado.",
            "ACTIVE_TRANSACTION"=>"Identificar la transacción abierta más antigua antes de considerar cualquier KILL.",
            "AVAILABILITY_REPLICA"=>"Revisar estado de sincronización, send queue y conectividad de las réplicas.",
            "REPLICATION"=>"Revisar Log Reader/replicación y latencia antes de modificar el log.",
            "CHECKPOINT"=>"Revisar actividad y ejecutar CHECKPOINT sólo si corresponde operacionalmente.",
            _=>x.UsedPct>=75 ? "Revisar crecimiento, espacio disponible y causa de retención antes de ampliar o reducir el archivo." : "Sin acción correctiva inmediata."
        };
        return new IncidentDiagnosis {
            Severity=severity,
            Problem=x.UsedPct>=75 ? $"Transaction log de {x.DatabaseName} en {x.UsedPct:0.0}%." : $"Estado del transaction log de {x.DatabaseName}.",
            ProbableCause=cause,
            Evidence=$"Database: {x.DatabaseName}\nRecovery: {x.RecoveryModel}\nLog: {x.LogSizeMb:0.0} MB\nUsed: {x.UsedMb:0.0} MB ({x.UsedPct:0.0}%)\nReuse Wait: {x.ReuseWait}\nLast LOG backup: {(x.LastLogBackup.HasValue?x.LastLogBackup.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}",
            RecommendedAction=action,
            DbaAction=wait=="LOG_BACKUP" ? "Validar primero el job de backup. No ejecutar SHRINK como respuesta automática." : wait=="ACTIVE_TRANSACTION" ? "Usar Long Transactions para capturar evidencia y evaluar la sesión responsable." : "Mantener la acción en modo diagnóstico hasta confirmar la causa.",
            Verification=$"Volver a consultar {x.DatabaseName}: porcentaje usado y log_reuse_wait_desc. Confirmar que la causa desaparezca o el uso deje de crecer.",
            Safety="READ - diagnóstico automático; ninguna acción ejecutada."
        };
    }

    public static IncidentDiagnosis DiagnoseBlocking(BlockingIncident x, IReadOnlyCollection<BlockingIncident> all)
    {
        var root=x.RootBlockerId;
        var victims=all.Count(i=>i.BlockingSessionId>0 && i.RootBlockerId==root);
        var rootItem=all.FirstOrDefault(i=>i.SessionId==root);
        return new IncidentDiagnosis {
            Severity=x.WaitSeconds>=300 ? "CRITICAL" : x.WaitSeconds>=60 ? "WARNING" : "INFO",
            Problem=$"Cadena de blocking con root blocker SPID {root} y {victims} víctima(s).",
            ProbableCause=$"SPID {root} mantiene recursos requeridos por otras sesiones. El SQL mostrado es evidencia contextual y no demuestra por sí solo qué statement adquirió el lock.",
            Evidence=$"Selected SPID: {x.SessionId}\nRoot blocker: {root}\nBlocked by: {x.BlockingSessionId}\nWait: {x.WaitType}\nWait sec: {x.WaitSeconds}\nDatabase: {x.DatabaseName}\nRoot login: {rootItem?.LoginName}\nRoot host: {rootItem?.HostName}\nRoot app: {rootItem?.ProgramName}\nOpen transactions: {rootItem?.OpenTransactions}",
            RecommendedAction="Investigar primero el root blocker: SQL, locks, transacción abierta, aplicación y duración. Evitar matar una víctima de la cadena.",
            DbaAction=$"Capturar Evidence Snapshot del root blocker SPID {root}. Si se confirma que debe interrumpirse, realizar la acción únicamente mediante el flujo protegido.",
            Verification="Actualizar Blocking Analyzer y confirmar 0 víctimas para esa cadena; revisar además rollback y nuevas cadenas.",
            Safety="IMPACT si se considera KILL - requiere evidencia, revalidación y confirmación explícita."
        };
    }

    public static IncidentDiagnosis DiagnoseLongTransaction(TransactionIncident x)
    {
        var blocked=x.BlockingSessionId>0;
        return new IncidentDiagnosis {
            Severity=x.Minutes>=240 || blocked ? "CRITICAL" : x.Minutes>=60 ? "WARNING" : "INFO",
            Problem=$"Transacción abierta SPID {x.SessionId} desde hace {x.Minutes} minutos.",
            ProbableCause=blocked ? $"La sesión además está bloqueada por SPID {x.BlockingSessionId}; una transacción abierta puede retener locks/log/versiones." : "La sesión mantiene una transacción abierta durante un período prolongado. Debe determinarse si es trabajo legítimo, sesión sleeping con transacción pendiente o proceso atascado.",
            Evidence=$"SPID: {x.SessionId}\nDatabase: {x.DatabaseName}\nLogin: {x.LoginName}\nHost: {x.HostName}\nApp: {x.ProgramName}\nStatus: {x.SessionStatus}\nBegin: {x.TransactionBeginTime:yyyy-MM-dd HH:mm:ss}\nAge: {x.Minutes} min\nOpen tran: {x.OpenTransactions}\nBlocked by: {x.BlockingSessionId}\nWait: {x.WaitType}\nCPU: {x.CpuMs} ms\nReads: {x.Reads}\nWrites: {x.Writes}",
            RecommendedAction="Revisar SQL/input buffer, locks y contexto de aplicación. Confirmar con el responsable si la transacción es esperada antes de interrumpirla.",
            DbaAction=$"Capturar Evidence Snapshot. Sólo si se autoriza y la identidad sigue coincidiendo, utilizar KILL protegido para SPID {x.SessionId}.",
            Verification="Actualizar Long Transactions. Si hubo KILL, consultar ROLLBACK STATUS hasta finalizar y comprobar liberación de locks/log.",
            Safety="CRITICAL para KILL - evidencia + revalidación + confirmación explícita."
        };
    }
}
