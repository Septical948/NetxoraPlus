using System.Windows;
using System.Windows.Controls;

namespace DBACheck2.App.Services;

/// <summary>
/// Applies live localization to hosted legacy analyzer views.
/// This keeps older XAML modules language-aware without reopening them.
/// Only known UI labels are translated; data/evidence returned by the engine is never modified.
/// </summary>
public static class UiLocalizationService
{
    private static readonly (string Es,string En)[] Pairs =
    {
        ("ACTUALIZAR","REFRESH"),
        ("DIAGNÓSTICO","DIAGNOSIS"),
        ("VER CADENA","VIEW CHAIN"),
        ("VER SQL","VIEW SQL"),
        ("VER LOCKS","VIEW LOCKS"),
        ("VER TRANSACCIÓN","VIEW TRANSACTION"),
        ("VER TRANSACCIONES","VIEW TRANSACTIONS"),
        ("VER FILES","VIEW FILES"),
        ("VER VERSION STORE","VIEW VERSION STORE"),
        ("VER BACKUPS","VIEW BACKUPS"),
        ("VER HISTORIAL","VIEW HISTORY"),
        ("VER RÉPLICA","VIEW REPLICA"),
        ("VER DATABASE","VIEW DATABASE"),
        ("VER LISTENER","VIEW LISTENER"),
        ("VER REQUEST","VIEW REQUEST"),
        ("VER WAITS","VIEW WAITS"),
        ("CAPTURAR EVIDENCIA","CAPTURE EVIDENCE"),
        ("KILL PROTEGIDO","PROTECTED KILL"),
        ("RESOLVER INCIDENTE","RESOLVE INCIDENT"),
        ("CORRELACIONAR","CORRELATE"),
        ("REPORTE TÉCNICO","TECHNICAL REPORT"),
        ("REVALIDAR","REVALIDATE"),
        ("EXPANDIR","EXPAND"),
        ("CONTRAER","COLLAPSE"),
        ("INTEGRACIONES DE MONITOREO","MONITORING INTEGRATIONS"),
        ("INTEGRACIÓN GUARDADA","SAVED INTEGRATION"),
        ("PERFIL","PROFILE"),
        ("ORIGEN","SOURCE"),
        ("Recordar token","Remember token"),
        ("Verificar certificado TLS","Verify TLS certificate"),
        ("GUARDAR","SAVE"),
        ("ELIMINAR","DELETE"),
        ("PROBAR API","TEST API"),
        ("CARGAR PROBLEMAS ABIERTOS","LOAD OPEN PROBLEMS"),
        ("Severidad","Severity"),
        ("Nativa","Native"),
        ("Problema","Problem"),
        ("Hora","Time"),
        ("Recon.","Ack"),
        ("CORRELACIÓN / EVIDENCIA","CORRELATION / EVIDENCE"),
        ("Seleccioná un problema de monitoreo para inspeccionar su correlación DBACHECK.","Select a monitoring problem to inspect its DBACHECK correlation hint."),
        ("Estado","Status"),
        ("Área","Area"),
        ("Resumen","Summary"),
        ("Evidencia","Evidence"),
        ("Fecha","Date"),
        ("Servidor","Server"),
        ("Módulo","Module"),
        ("Veces","Count"),
        ("Problema","Problem"),
        ("EVIDENCIA / ANÁLISIS DEL SPID SELECCIONADO","EVIDENCE / SELECTED SPID ANALYSIS"),
        ("EVIDENCIA / DIAGNÓSTICO DEL SPID SELECCIONADO","EVIDENCE / SELECTED SPID DIAGNOSIS"),
        ("EVIDENCIA / DIAGNÓSTICO TEMPDB","EVIDENCE / TEMPDB DIAGNOSIS"),
        ("EVIDENCIA / DIAGNÓSTICO DE LA BASE SELECCIONADA","EVIDENCE / SELECTED DATABASE DIAGNOSIS"),
        ("EVIDENCIA / DIAGNÓSTICO","EVIDENCE / DIAGNOSIS"),
        ("EVIDENCIA / DIAGNÓSTICO PERFORMANCE","EVIDENCE / PERFORMANCE DIAGNOSIS"),
        ("EVIDENCIA / DIAGNÓSTICO ALWAYSON","EVIDENCE / ALWAYSON DIAGNOSIS"),
        ("Selecciona una fila para investigar la cadena de bloqueo.","Select a row to investigate the blocking chain."),
        ("Selecciona una transacción para investigar.","Select a transaction to investigate."),
        ("Selecciona una base para investigar su Transaction Log.","Select a database to investigate its Transaction Log."),
        ("Selecciona una base o job para investigar.","Select a database or job to investigate."),
        ("Selecciona un request para investigar.","Select a request to investigate."),
        ("Selecciona una réplica/base para investigar.","Select a replica/database to investigate."),
        ("Seleccioná una fila para ver evidencia.","Select a row to view evidence."),
        ("Consultando blocking actual...","Querying current blocking..."),
        ("Consultando TempDB...","Querying TempDB..."),
        ("Consultando logs...","Querying logs..."),
        ("Cargando...","Loading..."),
        ("Beta 1 · Espacio · Files · Version Store · Correlación contextual · Solo lectura","Beta 1 · Space · Files · Version Store · Context correlation · Read-only"),
        ("Beta 1 · Diagnosis + Correlation · Reuse Wait · Backups · Jobs · Transacciones · Solo lectura","Beta 1 · Diagnosis + Correlation · Reuse Wait · Backups · Jobs · Transactions · Read-only"),
        ("Beta 1 · Diagnosis Engine · SQL · Locks · Transacción · Evidence Snapshot · KILL protegido","Beta 1 · Diagnosis Engine · SQL · Locks · Transaction · Evidence Snapshot · Protected KILL"),
        ("Beta 1 ·| Root blocker + diagnóstico | Evidence Snapshot | Acciones de impacto requieren flujo protegido.","Beta 1 · Root blocker + diagnosis | Evidence Snapshot | Impact actions require a protected flow."),
        ("Beta 1 ·| Diagnosis Engine | Version Store es contexto GLOBAL | KILL requiere evidencia + revalidación + confirmación explícita.","Beta 1 · Diagnosis Engine | Version Store is GLOBAL context | KILL requires evidence + revalidation + explicit confirmation.")
    };

    public static void Apply(DependencyObject root)
    {
        var en=LocalizationService.Current==AppLanguage.En;
        Walk(root,en);
    }

    private static void Walk(DependencyObject obj,bool en)
    {
        switch(obj)
        {
            case Button b when b.Content is string s: b.Content=Translate(s,en); break;
            case CheckBox c when c.Content is string s: c.Content=Translate(s,en); break;
            case TextBlock t: t.Text=Translate(t.Text,en); break;
            case TextBox t when t.IsReadOnly: t.Text=Translate(t.Text,en); break;
            case DataGrid g:
                foreach(var col in g.Columns)
                    if(col.Header is string h) col.Header=Translate(h,en);
                break;
            case TabControl tabs:
                foreach(var item in tabs.Items)
                    if(item is TabItem tab && tab.Header is string h) tab.Header=Translate(h,en);
                break;
        }

        switch(obj)
        {
            case Panel p:
                foreach(UIElement child in p.Children) Walk(child,en);
                break;
            case Decorator d when d.Child is not null:
                Walk(d.Child,en);
                break;
            case ContentControl cc when cc.Content is DependencyObject child:
                Walk(child,en);
                break;
            case ItemsControl ic when obj is not DataGrid:
                foreach(var item in ic.Items)
                    if(item is DependencyObject child) Walk(child,en);
                break;
        }
    }

    private static string Translate(string value,bool en)
    {
        if(string.IsNullOrWhiteSpace(value)) return value;
        foreach(var pair in Pairs)
        {
            if(en && string.Equals(value,pair.Es,StringComparison.OrdinalIgnoreCase)) return pair.En;
            if(!en && string.Equals(value,pair.En,StringComparison.OrdinalIgnoreCase)) return pair.Es;
        }
        return value;
    }
}
