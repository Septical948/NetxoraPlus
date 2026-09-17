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

    public override string ToString() =>
$@"SEVERIDAD
{Severity}

PROBLEMA
{Problem}

CAUSA PROBABLE
{ProbableCause}

EVIDENCIA
{Evidence}

ACCIÓN RECOMENDADA
{RecommendedAction}

ACCIÓN DBA
{DbaAction}

VERIFICACIÓN
{Verification}

SEGURIDAD
{Safety}";
}
