using DBACheck2.App.Services;

namespace DBACheck2.App.Models;

public sealed class IncidentDiagnosis
{
    public string Severity { get; init; } = "INFO";
    public string Problem { get; init; } = "";
    public string ProbableCause { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
    public string DbaAction { get; init; } = "";
    public string Verification { get; init; } = "";
    public string Safety { get; init; } = "READ";

    public override string ToString()
    {
        var en=LocalizationService.Current==AppLanguage.En;
        return $@"{(en?"SEVERITY":"SEVERIDAD")}
{Severity}

{(en?"PROBLEM":"PROBLEMA")}
{Problem}

{(en?"PROBABLE CAUSE":"CAUSA PROBABLE")}
{ProbableCause}

{(en?"EVIDENCE":"EVIDENCIA")}
{Evidence}

{(en?"RECOMMENDED ACTION":"ACCIÓN RECOMENDADA")}
{RecommendedAction}

{(en?"DBA ACTION":"ACCIÓN DBA")}
{DbaAction}

{(en?"VERIFICATION":"VERIFICACIÓN")}
{Verification}

{(en?"SAFETY":"SEGURIDAD")}
{Safety}";
    }
}
