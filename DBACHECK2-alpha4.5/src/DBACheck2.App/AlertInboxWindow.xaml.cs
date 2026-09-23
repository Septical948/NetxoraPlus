using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class AlertInboxWindow:Window
{
    private readonly AlertInboxService _inbox=new();
    private readonly ExternalEventDiagnosisService _diagnosis=new();
    private readonly IncidentHistoryService _history=new();
    private AlertInboxLoadResult _last=new();
    private ExternalDiagnosisResult? _lastDiagnosis;
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public AlertInboxWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        Loaded+=async(_,__)=>await LoadAsync();
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"ALERT INBOX":"BANDEJA DE ALERTAS";
        SubtitleText.Text=En
            ?"Cross-monitor operational queue · No database connection required to load alerts"
            :"Cola operativa multi-monitor · No requiere conexión a bases para cargar alertas";
        OpenLabel.Text=En?"ACTIVE GROUPS":"GRUPOS ACTIVOS";
        UnmatchedLabel.Text=En?"UNMATCHED":"SIN PERFIL";
        RefreshButton.Content=En?"REFRESH ALL MONITORS":"ACTUALIZAR MONITORES";
        DiagnoseButton.Content=En?"DIAGNOSE SELECTED":"DIAGNOSTICAR SELECCIONADO";
        CreateIncidentButton.Content=En?"CREATE INCIDENT":"CREAR INCIDENTE";
        DetailTitleText.Text=En?"PRIORITY / CORRELATION DETAIL":"DETALLE DE PRIORIDAD / CORRELACIÓN";
        ExpandDetailButton.Content=En?"EXPAND":"EXPANDIR";
        DetailText.Text=En
            ?"Alert Inbox loads monitoring alerts first. Database collectors run only when you choose DIAGNOSE SELECTED."
            :"La bandeja carga primero las alertas de monitoreo. Los collectors de base sólo se ejecutan al elegir DIAGNOSTICAR SELECCIONADO.";
    }

    private async Task LoadAsync()
    {
        try
        {
            SetBusy(true);
            StatusText.Text=En?"Reading configured monitoring sources...":"Leyendo fuentes de monitoreo configuradas...";
            _last=await _inbox.LoadAsync();
            InboxGrid.ItemsSource=null;InboxGrid.ItemsSource=_last.Items;
            UpdateCards();

            var sourceText=_last.SourceStatus.Count==0
                ? (En?"No integration profiles configured.":"No hay perfiles de integración configurados.")
                : string.Join(" | ",_last.SourceStatus);

            StatusText.Text=En
                ?$"{_last.Items.Count} operational group(s) | Sources OK {_last.SourcesOk} | Failed {_last.SourcesFailed}"
                :$"{_last.Items.Count} grupo(s) operativo(s) | Fuentes OK {_last.SourcesOk} | Fallidas {_last.SourcesFailed}";
            DetailText.Text=sourceText;
        }
        catch(Exception ex)
        {
            StatusText.Text="ERROR: "+ex.Message;
            DetailText.Text=ex.ToString();
        }
        finally{SetBusy(false);}
    }

    private void UpdateCards()
    {
        P1Text.Text=_last.Items.Count(x=>x.Priority=="P1").ToString();
        P2Text.Text=_last.Items.Count(x=>x.Priority=="P2").ToString();
        OpenText.Text=_last.Items.Count.ToString();
        UnmatchedText.Text=_last.Items.Count(x=>!x.ProfileMatched).ToString();
    }

    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();

    private void InboxGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        _lastDiagnosis=null;
        CreateIncidentButton.IsEnabled=false;
        DiagnoseButton.IsEnabled=InboxGrid.SelectedItem is AlertInboxItem item && item.ProfileMatched;
        if(InboxGrid.SelectedItem is not AlertInboxItem x)return;
        DetailText.Text=BuildDetail(x);
    }

    private string BuildDetail(AlertInboxItem x)
    {
        var events=string.Join(Environment.NewLine+Environment.NewLine,x.Events.Select(e=>
$@"[{e.Source}] {e.Severity} ({e.NativeSeverity})
Event ID: {e.ExternalId}
Time: {e.Timestamp:yyyy-MM-dd HH:mm:ss}
Problem: {e.Name}
Acknowledged: {e.Acknowledged} | Suppressed: {e.Suppressed}
Tags: {e.Tags}
Detail: {e.RawDetail}"));

        return En
            ? $@"{x.Priority} | SCORE {x.PriorityScore}/100 | {x.State}
Host: {x.Host}
Environment: {x.Environment}
Category: {x.Category}
Sources: {x.Sources}
Alerts grouped: {x.AlertCount}
Age: {x.AgeText}
Matched profile: {(x.ProfileMatched?x.ProfileName:"NO")}
Match reason: {x.MatchReason}

WHY THIS PRIORITY
{x.PriorityReason}

MONITORING EVENTS
{events}

NEXT STEP
{(x.ProfileMatched
    ?"Select DIAGNOSE SELECTED to connect only to this matched database target and collect targeted DB evidence."
    :"Create or map a DBACHECK server profile before database diagnosis.")}"
            : $@"{x.Priority} | SCORE {x.PriorityScore}/100 | {x.State}
Host: {x.Host}
Ambiente: {x.Environment}
Categoría: {x.Category}
Fuentes: {x.Sources}
Alertas agrupadas: {x.AlertCount}
Antigüedad: {x.AgeText}
Perfil asociado: {(x.ProfileMatched?x.ProfileName:"NO")}
Motivo del match: {x.MatchReason}

POR QUÉ TIENE ESTA PRIORIDAD
{x.PriorityReason}

ALERTAS DE MONITOREO
{events}

SIGUIENTE PASO
{(x.ProfileMatched
    ?"Elegí DIAGNOSTICAR SELECCIONADO para conectar únicamente al destino asociado y recolectar evidencia DB dirigida."
    :"Creá o asociá un perfil DBACHECK antes del diagnóstico de base.")}";
    }

    private async void DiagnoseButton_Click(object sender,RoutedEventArgs e)
    {
        if(InboxGrid.SelectedItem is not AlertInboxItem item || !item.ProfileMatched)return;
        try
        {
            SetBusy(true);
            StatusText.Text=En?"Connecting to matched target and running targeted diagnosis...":"Conectando al destino asociado y ejecutando diagnóstico dirigido...";
            _lastDiagnosis=await _diagnosis.DiagnoseAsync(item.PrimaryEvent);
            item.State="DIAGNOSED";
            RefreshGridSelection(item);
            DetailText.Text=BuildDetail(item)+"\n\n"+_lastDiagnosis.BuildEvidence();
            CreateIncidentButton.IsEnabled=true;
            StatusText.Text=En
                ?$"{item.Priority} | {item.ProfileName} | {_lastDiagnosis.Category} | {_lastDiagnosis.RelevantChecks.Count} correlated DB check(s)"
                :$"{item.Priority} | {item.ProfileName} | {_lastDiagnosis.Category} | {_lastDiagnosis.RelevantChecks.Count} check(s) DB correlacionado(s)";
        }
        catch(Exception ex)
        {
            _lastDiagnosis=null;
            StatusText.Text="ERROR: "+ex.Message;
        }
        finally
        {
            SetBusy(false);
            DiagnoseButton.IsEnabled=InboxGrid.SelectedItem is AlertInboxItem x && x.ProfileMatched;
            CreateIncidentButton.IsEnabled=_lastDiagnosis is not null;
        }
    }

    private async void CreateIncidentButton_Click(object sender,RoutedEventArgs e)
    {
        if(_lastDiagnosis is null || InboxGrid.SelectedItem is not AlertInboxItem item)return;
        try
        {
            SetBusy(true);
            var baseDiagnosis=_lastDiagnosis.ToIncidentDiagnosis();
            var diagnosis=new IncidentDiagnosis {
                Severity=baseDiagnosis.Severity,
                Problem=baseDiagnosis.Problem,
                ProbableCause=baseDiagnosis.ProbableCause,
                Evidence=baseDiagnosis.Evidence+"\n\nALERT INBOX GROUP\n"+BuildDetail(item),
                RecommendedAction=baseDiagnosis.RecommendedAction,
                DbaAction=baseDiagnosis.DbaAction,
                Verification=baseDiagnosis.Verification,
                Safety=baseDiagnosis.Safety
            };
            var id=await _history.SaveAsync(_lastDiagnosis.Profile.Host,_lastDiagnosis.Profile.DatabaseOrService??"","Alert Inbox",diagnosis);
            item.State="INCIDENT";
            RefreshGridSelection(item);
            StatusText.Text=En
                ?$"Incident #{id} created from {item.AlertCount} correlated monitoring alert(s)."
                :$"Incidente #{id} creado desde {item.AlertCount} alerta(s) de monitoreo correlacionada(s).";
        }
        catch(Exception ex){StatusText.Text="ERROR: "+ex.Message;}
        finally
        {
            SetBusy(false);
            CreateIncidentButton.IsEnabled=_lastDiagnosis is not null;
        }
    }

    private void RefreshGridSelection(AlertInboxItem item)
    {
        InboxGrid.Items.Refresh();
        InboxGrid.SelectedItem=item;
    }

    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);

    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled=!busy;
        DiagnoseButton.IsEnabled=!busy && InboxGrid.SelectedItem is AlertInboxItem x && x.ProfileMatched;
        CreateIncidentButton.IsEnabled=!busy && _lastDiagnosis is not null;
    }
}
