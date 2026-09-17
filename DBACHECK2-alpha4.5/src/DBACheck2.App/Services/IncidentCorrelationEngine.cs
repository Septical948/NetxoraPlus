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
            "CORRELACIÓN AUTOMÁTICA - Alpha 4.6.2",
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
            var direct=await _service.GetJobStepCorrelationsAsync(x.DatabaseName);

            lines.Add("");
            lines.Add("SQL AGENT - CORRELACIÓN POR JOB / STEP");
            if(direct.Count==0)
            {
                lines.Add($"- No se encontró ningún job/step que haga referencia explícita a {x.DatabaseName}.");
                lines.Add("- Esto NO demuestra que no exista backup: puede ejecutarse mediante Maintenance Plan/SSIS, procedimiento dinámico, herramienta externa o script genérico.");
            }
            else
            {
                foreach(var g in direct.GroupBy(d=>d.JobId))
                {
                    var first=g.First();
                    lines.Add($"- DIRECTA | {first.JobName} | Enabled: {(first.Enabled?"YES":"NO")} | Last: {first.Outcome} | {(first.LastRun.HasValue?first.LastRun.Value.ToString("yyyy-MM-dd HH:mm:ss"):"N/A")}");
                    foreach(var step in g.Take(4))
                        lines.Add($"    Step {step.StepId}: {step.StepName} | {step.Subsystem} | referencia explícita a {x.DatabaseName}");
                }
            }

            lines.Add("");
            lines.Add("INTERPRETACIÓN");
            var failedDirect=direct.Where(d=>d.Enabled && d.Outcome=="FAILED").ToList();
            if(failedDirect.Count>0)
                lines.Add("- EVIDENCIA DIRECTA: existe al menos un job habilitado cuyo job/step referencia la base y cuya última ejecución falló. Revisar el historial y step exacto.");
            else if(direct.Count>0)
                lines.Add("- Se encontró relación directa job/step → base, pero la última ejecución del job no figura FAILED. Revisar horario, step y política antes de atribuir causalidad.");
            else
                lines.Add("- Sin relación directa en SQL Agent. No se listan jobs de otras bases para evitar falsos positivos.");
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
