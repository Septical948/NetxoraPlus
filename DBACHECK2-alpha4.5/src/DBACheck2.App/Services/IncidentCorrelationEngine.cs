using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public sealed class IncidentCorrelationEngine
{
    private readonly SqlHealthService _service;
    public IncidentCorrelationEngine(SqlHealthService service) => _service=service;

    public async Task<string> CorrelateLogAsync(LogDatabaseInfo x)
    {
        var lines=new List<string>
        {
            "CORRELACIÓN AUTOMÁTICA - Alpha 4.6.1",
            $"Database: {x.DatabaseName}",
            $"Reuse Wait: {x.ReuseWait}",
            ""
        };

        var backups=await _service.GetBackupDatabasesAsync();
        var backup=backups.FirstOrDefault(b=>b.DatabaseName.Equals(x.DatabaseName,StringComparison.OrdinalIgnoreCase));
        lines.Add("BACKUPS");
        if(backup is null) lines.Add("- No se obtuvo registro correlacionado de backup para la base.");
        else
        {
            lines.Add($"- FULL: {(backup.LastFull.HasValue?backup.LastFull.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}");
            lines.Add($"- DIFF: {(backup.LastDiff.HasValue?backup.LastDiff.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}");
            lines.Add($"- LOG : {(backup.LastLog.HasValue?backup.LastLog.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}");
        }

        if(x.ReuseWait.Equals("LOG_BACKUP",StringComparison.OrdinalIgnoreCase))
        {
            var jobs=await _service.GetAgentJobsAsync();
            var candidates=jobs.Where(j =>
                j.JobName.Contains(x.DatabaseName,StringComparison.OrdinalIgnoreCase) ||
                j.JobName.Contains("backup",StringComparison.OrdinalIgnoreCase) ||
                j.JobName.Contains("log",StringComparison.OrdinalIgnoreCase))
                .OrderBy(j=>j.Outcome=="FAILED"?0:1).ThenBy(j=>j.JobName).Take(8).ToList();

            lines.Add("");
            lines.Add("SQL AGENT - CANDIDATOS RELACIONADOS");
            if(candidates.Count==0) lines.Add("- No se identificaron jobs candidatos por nombre. Esto no demuestra que no exista un proceso externo de backup.");
            foreach(var j in candidates)
                lines.Add($"- {j.JobName} | Enabled: {(j.Enabled?"YES":"NO")} | Last: {j.Outcome} | {(j.LastRun.HasValue?j.LastRun.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}");

            lines.Add("");
            lines.Add("INTERPRETACIÓN");
            var failed=candidates.Where(j=>j.Enabled && j.Outcome=="FAILED").ToList();
            if(failed.Count>0)
                lines.Add("- Hay job(s) candidato(s) habilitado(s) cuya última ejecución falló. Revisar su historial antes de ejecutar manualmente un backup.");
            else
                lines.Add("- No se encontró un fallo concluyente entre los jobs candidatos. Correlación por nombre es evidencia contextual, no prueba de causalidad.");
        }

        if(x.ReuseWait.Equals("ACTIVE_TRANSACTION",StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("");
            lines.Add("TRANSACCIÓN ABIERTA MÁS ANTIGUA");
            lines.Add(await _service.GetLogTransactionsAsync(x));
            lines.Add("");
            lines.Add("INTERPRETACIÓN");
            lines.Add("- ACTIVE_TRANSACTION está confirmado a nivel de base; la sesión mostrada debe investigarse antes de atribuirle causalidad o considerar KILL.");
        }

        lines.Add("");
        lines.Add("SEGURIDAD");
        lines.Add("READ - sólo correlación. No se ejecutó backup, job, KILL, SHRINK ni cambio de recovery model.");
        return string.Join(Environment.NewLine,lines);
    }
}
