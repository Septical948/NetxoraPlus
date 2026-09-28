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
    private string _domainFilter="ALL";
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public AlertInboxWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        UpdateDomainFilterButtons();
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
        FilterLabelText.Text=En?"FILTER:":"FILTRAR:";
        ExpandDetailButton.Content=En?"EXPAND":"EXPANDIR";
        UpdateDomainFilterButtons();
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
            ApplyDomainFilter();

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

    private void UpdateCards(IEnumerable<AlertInboxItem> items)
    {
        var list=items.ToList();
        P1Text.Text=list.Count(x=>x.Priority=="P1").ToString();
        P2Text.Text=list.Count(x=>x.Priority=="P2").ToString();
        OpenText.Text=list.Count.ToString();
        UnmatchedText.Text=list.Count(x=>!x.ProfileMatched).ToString();
    }

    private void DomainFilterButton_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button || button.Tag is not string key)return;
        _domainFilter=key;
        ApplyDomainFilter();
        UpdateDomainFilterButtons();
    }

    private void ApplyDomainFilter()
    {
        if(InboxGrid is null)return;
        var rows=_domainFilter=="ALL"
            ? _last.Items
            : _last.Items.Where(x=>string.Equals(x.Domain,_domainFilter,StringComparison.OrdinalIgnoreCase)).ToList();

        InboxGrid.ItemsSource=null;
        InboxGrid.ItemsSource=rows;
        UpdateCards(rows);
        UpdateDomainFilterButtons();

        if(_last.Items.Count>0)
        {
            StatusText.Text=En
                ?$"{rows.Count}/{_last.Items.Count} shown | DB {_last.Items.Count(x=>x.Domain=="DATABASE")} | OS {_last.Items.Count(x=>x.Domain=="OS")} | Network {_last.Items.Count(x=>x.Domain=="NETWORK")} | App {_last.Items.Count(x=>x.Domain=="APPLICATION")} | Other {_last.Items.Count(x=>x.Domain=="OTHER")}"
                :$"{rows.Count}/{_last.Items.Count} visibles | BD {_last.Items.Count(x=>x.Domain=="DATABASE")} | SO {_last.Items.Count(x=>x.Domain=="OS")} | Red {_last.Items.Count(x=>x.Domain=="NETWORK")} | App {_last.Items.Count(x=>x.Domain=="APPLICATION")} | Otros {_last.Items.Count(x=>x.Domain=="OTHER")}";
        }
    }

    private void UpdateDomainFilterButtons()
    {
        if(FilterAllButton is null)return;
        SetFilterButton(FilterAllButton,"ALL",En?"ALL":"TODOS",_last.Items.Count);
        SetFilterButton(FilterDbButton,"DATABASE",En?"DB":"BD",_last.Items.Count(x=>x.Domain=="DATABASE"));
        SetFilterButton(FilterOsButton,"OS",En?"OS":"SO",_last.Items.Count(x=>x.Domain=="OS"));
        SetFilterButton(FilterNetworkButton,"NETWORK",En?"NETWORK":"RED",_last.Items.Count(x=>x.Domain=="NETWORK"));
        SetFilterButton(FilterAppButton,"APPLICATION","APP",_last.Items.Count(x=>x.Domain=="APPLICATION"));
        SetFilterButton(FilterOtherButton,"OTHER",En?"OTHER":"OTROS",_last.Items.Count(x=>x.Domain=="OTHER"));
    }

    private void SetFilterButton(Button button,string key,string label,int count)
    {
        button.Content=$"{label} {count}";
        var selected=string.Equals(_domainFilter,key,StringComparison.OrdinalIgnoreCase);
        button.Background=selected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39,59,91))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26,41,66));
        button.Foreground=selected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56,232,208))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232,237,247));
        button.BorderBrush=selected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56,232,208))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(53,80,111));
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
Domain: {MonitoringDomainClassifier.Display(x.Domain,true)}
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
Dominio: {MonitoringDomainClassifier.Display(x.Domain,false)}
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
