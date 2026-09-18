using System.Text;
using DBACheck2.App.Models;
using DBACheck2.App.Providers;

namespace DBACheck2.App.Services;

public sealed class DbaAssistantService
{
    private enum Intent { General, Log, Backup, Blocking, Transactions, TempDb, Performance, Ha, Jobs }

    public async Task<string> AskAsync(ServerProfile profile,string question)
    {
        if(string.IsNullOrWhiteSpace(question)) return "Ingresá una pregunta DBA.";
        var provider=DatabaseProviderFactory.Create(profile);
        var sb=new StringBuilder();
        sb.AppendLine($"DBA ASSISTANT - {provider.DisplayName}");
        sb.AppendLine($"Target: {profile.Host} | Environment: {profile.Environment}");
        sb.AppendLine();

        if(profile.Engine!=DatabaseEngine.SqlServer)
        {
            sb.AppendLine($"Provider {provider.DisplayName}: estructura multi-engine disponible; collectors específicos aún no habilitados.");
            sb.AppendLine("No se ejecutó ninguna consulta contra el motor.");
            return sb.ToString();
        }

        var intent=DetectIntent(question);
        var health=await provider.QuickCheckAsync();
        var rows=Route(health,intent).ToList();
        var alerts=rows.Where(IsAlert).ToList();

        sb.AppendLine($"INTENT: {IntentLabel(intent)}");
        sb.AppendLine();
        sb.AppendLine("PROBLEMA");
        if(alerts.Count==0) sb.AppendLine("No se detectó una condición de alerta en los checks relacionados con la consulta.");
        else foreach(var x in alerts) sb.AppendLine($"[{x.Status}] {x.Area}: {x.Summary}");

        sb.AppendLine();
        sb.AppendLine("CAUSA PROBABLE");
        if(alerts.Count==0) sb.AppendLine("Sin causa operativa identificada en esta muestra.");
        else foreach(var x in alerts) sb.AppendLine($"- {CauseFor(x)}");

        sb.AppendLine();
        sb.AppendLine("EVIDENCIA");
        foreach(var x in rows) sb.AppendLine($"[{x.Status}] {x.Area}: {x.Summary}{(string.IsNullOrWhiteSpace(x.Detail)?"":$" | {Limit(x.Detail,650)}")}");

        sb.AppendLine();
        sb.AppendLine("ACCIÓN RECOMENDADA");
        if(alerts.Count==0) sb.AppendLine("Sin acción correctiva inmediata. Mantener observación si el síntoma continúa.");
        else foreach(var x in alerts) sb.AppendLine($"- {ActionFor(x)}");

        sb.AppendLine();
        sb.AppendLine("VERIFICACIÓN");
        sb.AppendLine(VerificationFor(intent));
        sb.AppendLine();
        sb.AppendLine("SAFETY: sólo lectura. El Assistant no ejecutó cambios.");
        return sb.ToString();
    }

    private static Intent DetectIntent(string question)
    {
        var q=question.ToLowerInvariant();
        if(q.Contains("tempdb")||q.Contains("version store")||q.Contains("temporal")) return Intent.TempDb;
        if(q.Contains("bloq")||q.Contains("blocking")||q.Contains("lock")||q.Contains("deadlock")) return Intent.Blocking;
        if(q.Contains("transacci")||q.Contains("transaction")) return Intent.Transactions;
        if(q.Contains("backup")||q.Contains("respaldo")||q.Contains("copia")) return Intent.Backup;
        if(q.Contains("job")||q.Contains("agent")) return Intent.Jobs;
        if(q.Contains("alwayson")||q.Contains("availability")||q.Contains("replica")||q.Contains("cluster")||q.Contains(" ha ")) return Intent.Ha;
        if(q.Contains("performance")||q.Contains("rendimiento")||q.Contains("cpu")||q.Contains("memoria")||q.Contains("wait")) return Intent.Performance;
        if(q.Contains("log")||q.Contains("vlf")) return Intent.Log;
        return Intent.General;
    }

    private static IEnumerable<HealthItem> Route(IEnumerable<HealthItem> all,Intent intent)
    {
        var areas=intent switch {
            Intent.Log => new[]{"LOG"},
            Intent.Backup => new[]{"BACKUPS"},
            Intent.Blocking => new[]{"BLOCKING"},
            Intent.Transactions => new[]{"TRANSACTIONS","BLOCKING"},
            Intent.TempDb => new[]{"TEMPDB","VERSION STORE","TRANSACTIONS"},
            Intent.Performance => new[]{"WAITS","BLOCKING","TRANSACTIONS"},
            Intent.Ha => new[]{"ALWAYSON"},
            Intent.Jobs => new[]{"JOBS"},
            _ => Array.Empty<string>()
        };
        var source=all.ToList();
        if(intent==Intent.General) return source.OrderByDescending(SeverityRank);
        return source.Where(x=>areas.Contains(x.Area,StringComparer.OrdinalIgnoreCase)).OrderByDescending(SeverityRank);
    }

    private static bool IsAlert(HealthItem x)=>x.Status is "WARNING" or "CRITICAL" or "ERROR";
    private static int SeverityRank(HealthItem x)=>x.Status switch{"CRITICAL"=>4,"ERROR"=>3,"WARNING"=>2,"INFO"=>1,_=>0};
    private static string IntentLabel(Intent x)=>x switch{Intent.Log=>"TRANSACTION LOG",Intent.Backup=>"BACKUPS",Intent.Blocking=>"BLOCKING",Intent.Transactions=>"TRANSACTIONS",Intent.TempDb=>"TEMPDB / VERSION STORE",Intent.Performance=>"PERFORMANCE",Intent.Ha=>"HA / ALWAYSON",Intent.Jobs=>"SQL AGENT JOBS",_=>"GENERAL HEALTH"};

    private static string CauseFor(HealthItem x)=>x.Area switch {
        "LOG"=>"Uso elevado o condición de reutilización del transaction log. Correlacionar log_reuse_wait_desc antes de concluir causa.",
        "BACKUPS"=>"La política o ejecución reciente de backups no cumple el umbral observado por DBACHECK.",
        "JOBS"=>"Uno o más SQL Agent Jobs habilitados finalizaron con error en su última ejecución.",
        "BLOCKING"=>"Existe espera entre sesiones en la muestra actual; identificar blocker raíz y recurso antes de actuar.",
        "TRANSACTIONS"=>"Existen transacciones abiertas por encima del umbral; pueden retener locks, log o versionado.",
        "TEMPDB"=>"Uso de TempDB por encima de la condición evaluada por el collector.",
        "VERSION STORE"=>"Version Store presenta una condición que requiere correlación con transacciones/snapshot.",
        "ALWAYSON"=>"El estado local de disponibilidad/sincronización presenta una condición reportable.",
        "WAITS"=>"El patrón de waits observado requiere correlación con carga y requests activos.",
        _=>"La evidencia del collector reporta una condición fuera del estado esperado."
    };

    private static string ActionFor(HealthItem x)=>x.Area switch {
        "LOG"=>"Revisar porcentaje usado, log_reuse_wait_desc, backups LOG y transacciones abiertas. No ejecutar SHRINK como respuesta automática.",
        "BACKUPS"=>"Revisar política, último backup y job responsable; confirmar que el destino y la retención sean correctos.",
        "JOBS"=>"Abrir historial del job fallido y revisar el step/error exacto antes de reejecutarlo.",
        "BLOCKING"=>"Identificar blocker raíz, SQL, transacción y locks; preservar evidencia antes de cualquier KILL.",
        "TRANSACTIONS"=>"Identificar SPID, login, aplicación, SQL y locks; validar con el owner de la aplicación antes de intervenir.",
        "TEMPDB"=>"Revisar consumo por archivos y sesiones; correlacionar con Version Store y transacciones.",
        "VERSION STORE"=>"Identificar transacciones antiguas y aislamiento que puedan sostener versiones.",
        "ALWAYSON"=>"Reportar estado, réplica, base, sincronización y colas. DBACHECK no modifica AG ni cluster.",
        "WAITS"=>"Correlacionar waits con requests activos, CPU, I/O, memoria y blocking; no cambiar configuración por una sola muestra.",
        _=>"Revisar la evidencia indicada y validar nuevamente después de la acción operativa."
    };

    private static string VerificationFor(Intent x)=>x switch {
        Intent.Log=>"Volver a consultar uso del log y log_reuse_wait_desc después de la acción del DBA.",
        Intent.Backup=>"Confirmar nuevo backup válido y revisar historial del job correspondiente.",
        Intent.Blocking=>"Revalidar cadena de bloqueo y confirmar que no queden sesiones bloqueadas.",
        Intent.Transactions=>"Revalidar antigüedad/transacciones abiertas y el recurso que estaban reteniendo.",
        Intent.TempDb=>"Revalidar uso de TempDB, Version Store y transacciones relacionadas.",
        Intent.Ha=>"Revalidar synchronization_state/health y colas. Sólo reporte; sin cambios de AG/cluster.",
        Intent.Jobs=>"Revisar el resultado y mensaje de la siguiente ejecución del job.",
        Intent.Performance=>"Tomar una nueva muestra y comparar waits/requests antes de concluir mejora.",
        _=>"Ejecutar nuevamente Quick Check y comparar las áreas que estaban en WARNING/CRITICAL."
    };

    private static string Limit(string value,int max)=>value.Length<=max?value:value[..max]+" ...";
}