using System.Text;
using System.Text.RegularExpressions;
using DBACheck2.App.Models;
using DBACheck2.App.Providers;

namespace DBACheck2.App.Services;

public sealed class DbaAssistantService
{
    private enum Intent { General, Log, Backup, Blocking, Transactions, TempDb, Performance, Ha, Jobs, Capacity, Vacuum }

    public async Task<string> AskAsync(ServerProfile profile,string question)
    {
        if(string.IsNullOrWhiteSpace(question)) return "Ingresá una pregunta DBA.";
        var provider=DatabaseProviderFactory.Create(profile);
        var sb=new StringBuilder();
        sb.AppendLine($"DBA ASSISTANT - {provider.DisplayName}");
        sb.AppendLine($"Target: {profile.Host} | Environment: {profile.Environment}");
        sb.AppendLine();

        var intent=DetectIntent(question);
        var health=await provider.QuickCheckAsync();
        var rows=Route(health,intent).ToList();
        var alerts=rows.Where(IsAlert).ToList();
        var en=LocalizationService.Current==AppLanguage.En;

        sb.Clear();
        sb.AppendLine($"DBA ASSISTANT - {provider.DisplayName}");
        sb.AppendLine($"{(en?"Target":"Destino")}: {profile.Host} | {(en?"Environment":"Ambiente")}: {profile.Environment}");
        sb.AppendLine();
        sb.AppendLine($"INTENT: {IntentLabel(intent)}");

        if(intent==Intent.Capacity) {
            var size=rows.FirstOrDefault(x=>x.Area.Equals("DATABASE SIZE",StringComparison.OrdinalIgnoreCase));
            var largest=size is null?null:LargestDatabase(size.Detail);
            sb.AppendLine(); sb.AppendLine(en?"DIRECT ANSWER":"RESPUESTA DIRECTA");
            sb.AppendLine(largest is null?(en?"Database-size evidence is unavailable.":"No hay evidencia disponible de tamaño de bases."):(en?$"Largest database: {largest}":$"Base de datos más grande: {largest}"));
        }
        sb.AppendLine();
        sb.AppendLine(en?"PROBLEM":"PROBLEMA");
        if(alerts.Count==0) sb.AppendLine(en?"No alert condition was detected in the checks related to the question.":"No se detectó una condición de alerta en los checks relacionados con la consulta.");
        else foreach(var x in alerts) sb.AppendLine($"[{x.Status}] {x.Area}: {x.Summary}");

        sb.AppendLine();
        sb.AppendLine(en?"PROBABLE CAUSE":"CAUSA PROBABLE");
        if(alerts.Count==0) sb.AppendLine(en?"No operational cause was identified in this sample.":"Sin causa operativa identificada en esta muestra.");
        else foreach(var x in alerts) sb.AppendLine($"- {CauseFor(x,en)}");

        sb.AppendLine();
        sb.AppendLine(en?"EVIDENCE":"EVIDENCIA");
        foreach(var x in rows) sb.AppendLine($"[{x.Status}] {x.Area}: {x.Summary}{(string.IsNullOrWhiteSpace(x.Detail)?"":$" | {Limit(x.Detail,650)}")}");

        sb.AppendLine();
        sb.AppendLine(en?"RECOMMENDED ACTION":"ACCIÓN RECOMENDADA");
        if(alerts.Count==0) sb.AppendLine(en?"No immediate corrective action. Continue observation if the symptom persists.":"Sin acción correctiva inmediata. Mantener observación si el síntoma continúa.");
        else foreach(var x in alerts) sb.AppendLine($"- {ActionFor(x,en)}");

        sb.AppendLine();
        sb.AppendLine(en?"VERIFICATION":"VERIFICACIÓN");
        sb.AppendLine(VerificationFor(intent,en));
        sb.AppendLine();
        sb.AppendLine(en?"SAFETY: read-only. The Assistant made no changes.":"SAFETY: sólo lectura. El Assistant no ejecutó cambios.");
        return sb.ToString();
    }

    private static Intent DetectIntent(string question)
    {
        var q=question.ToLowerInvariant();
        if((q.Contains("base")||q.Contains("database"))&&(q.Contains("grande")||q.Contains("mayor")||q.Contains("tamaño")||q.Contains("size")||q.Contains("espacio")||q.Contains("uso")||q.Contains("largest")||q.Contains("biggest")||q.Contains("used")||q.Contains("longer"))) return Intent.Capacity;
        if(q.Contains("vacuum")||q.Contains("autovacuum")||q.Contains("bloat")||q.Contains("dead tuple")||q.Contains("xid")) return Intent.Vacuum;
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
            Intent.Log => new[]{"LOG","WAL","ARCHIVELOG"},
            Intent.Backup => new[]{"BACKUPS"},
            Intent.Blocking => new[]{"BLOCKING"},
            Intent.Transactions => new[]{"TRANSACTIONS","BLOCKING"},
            Intent.TempDb => new[]{"TEMPDB","VERSION STORE","TRANSACTIONS"},
            Intent.Performance => new[]{"WAITS","BLOCKING","TRANSACTIONS","SESSIONS","LONG QUERIES"},
            Intent.Ha => new[]{"ALWAYSON","REPLICATION","ARCHIVELOG"},
            Intent.Jobs => new[]{"JOBS"},
            Intent.Capacity => new[]{"DATABASE SIZE","TABLESPACE","DISK"},
            Intent.Vacuum => new[]{"VACUUM","TEMP USAGE","TRANSACTIONS"},
            _ => Array.Empty<string>()
        };
        var source=all.ToList();
        if(intent==Intent.General) return source.OrderByDescending(SeverityRank);
        return source.Where(x=>areas.Contains(x.Area,StringComparer.OrdinalIgnoreCase)).OrderByDescending(SeverityRank);
    }

    private static bool IsAlert(HealthItem x)=>x.Status is "WARNING" or "CRITICAL" or "ERROR";
    private static int SeverityRank(HealthItem x)=>x.Status switch{"CRITICAL"=>6,"ERROR"=>5,"WARNING"=>4,"NO PERMISSION"=>3,"NOT ENABLED"=>2,"UNSUPPORTED"=>2,"UNAVAILABLE"=>2,"INFO"=>1,_=>0};
    private static string IntentLabel(Intent x)=>x switch{Intent.Log=>"TRANSACTION LOG",Intent.Backup=>"BACKUPS",Intent.Blocking=>"BLOCKING",Intent.Transactions=>"TRANSACTIONS",Intent.TempDb=>"TEMPDB / VERSION STORE",Intent.Performance=>"PERFORMANCE",Intent.Ha=>"HA / ALWAYSON",Intent.Jobs=>"JOBS / MAINTENANCE",Intent.Capacity=>"CAPACITY / DATABASE SIZE",Intent.Vacuum=>"VACUUM / MAINTENANCE",_=>"GENERAL HEALTH"};

    private static string CauseFor(HealthItem x,bool en)=>en ? x.Area switch { "BLOCKING"=>"Sessions are waiting on locks; identify the root blocker and resource before acting.","TRANSACTIONS"=>"Transactions exceed the configured duration threshold and may retain locks, WAL/log or versions.","TEMP USAGE"=>"Accumulated temporary-file usage is high; correlate with active workload and stats reset time.",_=>"The collector reports a condition outside the expected state." } : x.Area switch {
        "LOG"=>"Uso elevado o condición de reutilización del transaction log. Correlacionar log_reuse_wait_desc antes de concluir causa.",
        "WAL"=>"El estado WAL observado requiere correlación con generación, archivado y/o replay según el rol del servidor.",
        "REPLICATION"=>"El estado de replicación/standby observado requiere revisar rol, conexiones y atraso con muestras adicionales.",
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

    private static string ActionFor(HealthItem x,bool en)=>en ? x.Area switch { "BLOCKING"=>"Identify root blocker, SQL, transaction and locks; preserve evidence before any termination.","TRANSACTIONS"=>"Identify PID/SPID, user, application, SQL and locks; validate with the application owner before intervention.","TEMP USAGE"=>"Identify databases and queries generating temporary files and compare against the statistics reset time.",_=>"Review the reported evidence and validate again after the operational action." } : x.Area switch {
        "LOG"=>"Revisar porcentaje usado, log_reuse_wait_desc, backups LOG y transacciones abiertas.",
        "WAL"=>"Revisar posición WAL/XLOG, archivado y estado de standby/replicación según la versión PostgreSQL.",
        "REPLICATION"=>"Revisar pg_stat_replication/estado recovery y medir lag antes de concluir una falla.",
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

    private static string VerificationFor(Intent x,bool en)=>en ? x switch { Intent.Blocking=>"Recheck the blocking chain and confirm that no sessions remain blocked.",Intent.Transactions=>"Recheck transaction age and the resources retained by them.",Intent.Capacity=>"Run Quick Check again and compare database sizes.",_=>"Run Quick Check again and compare the areas that were WARNING/CRITICAL." } : x switch {
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

    private static string? LargestDatabase(string detail)
    {
        string? best=null; double max=-1;
        foreach(Match m in Regex.Matches(detail,@"([^;=]+)=([0-9.,]+)\s*(bytes|kB|KB|MB|GB|TB)",RegexOptions.IgnoreCase)) {
            if(!double.TryParse(m.Groups[2].Value.Replace(",","."),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n))continue;
            var u=m.Groups[3].Value.ToUpperInvariant(); var bytes=n*(u=="TB"?1099511627776d:u=="GB"?1073741824d:u=="MB"?1048576d:u=="KB"?1024d:1d);
            if(bytes>max){max=bytes;best=$"{m.Groups[1].Value.Trim()} = {m.Groups[2].Value} {m.Groups[3].Value}";}
        }
        return best;
    }

    private static string Limit(string value,int max)=>value.Length<=max?value:value[..max]+" ...";
}