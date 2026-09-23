using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Integrations;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class IntegrationWindow:Window
{
    private readonly IntegrationProfileService _profiles=new();
    private readonly ExternalEventDiagnosisService _diagnosis=new();
    private readonly IncidentHistoryService _history=new();
    private List<IntegrationConnectionProfile> _items=new();
    private ExternalDiagnosisResult? _lastDiagnosis;
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public IntegrationWindow()
    {
        InitializeComponent();
        SourceBox.ItemsSource=Enum.GetValues<IntegrationSource>();
        SourceBox.SelectedItem=IntegrationSource.Zabbix;
        ApplyLanguage();
        Loaded+=async(_,__)=>await RefreshProfilesAsync();
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"MONITORING INTEGRATIONS":"INTEGRACIONES DE MONITOREO";
        SubtitleText.Text=En?"Beta 2 · External monitoring → DB evidence → correlation · Read-only":"Beta 2 · Monitoreo externo → evidencia DB → correlación · Solo lectura";
        SavedLabel.Text=En?"SAVED INTEGRATION":"INTEGRACIÓN GUARDADA";
        ProfileLabel.Text=En?"PROFILE":"PERFIL"; SourceLabel.Text=En?"SOURCE":"ORIGEN";
        EndpointLabel.Text="API URL";TokenLabel.Text="API TOKEN";
        RememberTokenBox.Content=En?"Remember token":"Recordar token";
        VerifyTlsBox.Content=En?"Verify TLS certificate":"Verificar certificado TLS";
        SaveButton.Content=En?"SAVE":"GUARDAR";DeleteButton.Content=En?"DELETE":"ELIMINAR";
        TestButton.Content=En?"TEST API":"PROBAR API";LoadProblemsButton.Content=En?"LOAD OPEN PROBLEMS":"CARGAR PROBLEMAS ABIERTOS";
        DiagnoseButton.Content=En?"DIAGNOSE SELECTED":"DIAGNOSTICAR SELECCIONADO";CreateIncidentButton.Content=En?"CREATE INCIDENT":"CREAR INCIDENTE";
        SeverityColumn.Header=En?"Severity":"Severidad";NativeSeverityColumn.Header=En?"Native":"Nativa";HostColumn.Header="Host";
        ProblemColumn.Header=En?"Problem":"Problema";TimeColumn.Header=En?"Time":"Hora";AckColumn.Header=En?"Ack":"Recon.";
        DetailTitleText.Text=En?"CORRELATION / EVIDENCE":"CORRELACIÓN / EVIDENCIA";
        ExpandDetailButton.Content=En?"EXPAND":"EXPANDIR";
        DetailText.Text=En?"Select a monitoring problem to inspect its DBACHECK correlation hint.":"Seleccioná un problema de monitoreo para inspeccionar su correlación DBACHECK.";
    }

    private IntegrationConnectionProfile Current()=>new(){
        Name=ProfileNameBox.Text.Trim(),
        Source=(IntegrationSource)(SourceBox.SelectedItem??IntegrationSource.Zabbix),
        Endpoint=EndpointBox.Text.Trim(),
        Token=TokenBox.Password,
        RememberToken=RememberTokenBox.IsChecked==true,
        VerifyTls=VerifyTlsBox.IsChecked!=false
    };

    private async Task RefreshProfilesAsync()
    {
        _items=await _profiles.LoadAsync();
        ProfilesBox.ItemsSource=null;ProfilesBox.ItemsSource=_items;
        StatusText.Text=En?$"{_items.Count} integration profile(s).":"{_items.Count} perfil(es) de integración.";
    }

    private void Apply(IntegrationConnectionProfile p)
    {
        ProfileNameBox.Text=p.Name;SourceBox.SelectedItem=p.Source;EndpointBox.Text=p.Endpoint;
        TokenBox.Password=p.Token??"";RememberTokenBox.IsChecked=p.RememberToken;VerifyTlsBox.IsChecked=p.VerifyTls;
    }

    private void ProfilesBox_SelectionChanged(object s,SelectionChangedEventArgs e)
    { if(ProfilesBox.SelectedItem is IntegrationConnectionProfile p) Apply(p); }

    private async void Save_Click(object s,RoutedEventArgs e)
    {
        var p=Current();
        if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Endpoint)){StatusText.Text=En?"Profile name and API URL are required.":"Nombre del perfil y API URL son obligatorios.";return;}
        var old=_items.FirstOrDefault(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));
        if(p.RememberToken&&string.IsNullOrEmpty(p.Token)&&old?.RememberToken==true)p.Token=old.Token;
        _items.RemoveAll(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));_items.Add(p);
        await _profiles.SaveAsync(_items);await RefreshProfilesAsync();
        ProfilesBox.SelectedItem=_items.FirstOrDefault(x=>x.Name==p.Name);
        StatusText.Text=En?"Integration profile saved.":"Perfil de integración guardado.";
    }

    private async void Delete_Click(object s,RoutedEventArgs e)
    {
        if(ProfilesBox.SelectedItem is not IntegrationConnectionProfile p)return;
        await _profiles.DeleteAsync(p.Name);await RefreshProfilesAsync();
        ProfileNameBox.Clear();EndpointBox.Clear();TokenBox.Clear();RememberTokenBox.IsChecked=false;
        StatusText.Text=En?$"Integration profile '{p.Name}' deleted.":$"Perfil de integración '{p.Name}' eliminado.";
    }

    private IIntegrationProvider Provider()=>IntegrationProviderFactory.Create(Current());

    private async void Test_Click(object s,RoutedEventArgs e)
    {
        try{SetBusy(true);StatusText.Text=En?"Testing monitoring API...":"Probando API de monitoreo...";StatusText.Text=await Provider().TestAsync();}
        catch(Exception ex){StatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);}
    }

    private async void LoadProblems_Click(object s,RoutedEventArgs e)
    {
        try{
            SetBusy(true);StatusText.Text=En?"Loading open monitoring problems...":"Cargando problemas abiertos de monitoreo...";
            var rows=await Provider().GetOpenEventsAsync(100);EventsGrid.ItemsSource=rows;
            var critical=rows.Count(x=>x.Severity=="CRITICAL");var warnings=rows.Count(x=>x.Severity=="WARNING");
            StatusText.Text=En?$"{rows.Count} open problem(s) | Critical {critical} | Warning {warnings}":$"{rows.Count} problema(s) abierto(s) | Críticos {critical} | Warning {warnings}";
        }catch(Exception ex){StatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);}
    }

    private void EventsGrid_SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        _lastDiagnosis=null;CreateIncidentButton.IsEnabled=false;
        DiagnoseButton.IsEnabled=EventsGrid.SelectedItem is IntegrationEvent;
        if(EventsGrid.SelectedItem is not IntegrationEvent x)return;
        DetailText.Text=$"{x.Source} | {x.Severity} ({x.NativeSeverity})\nHost: {x.Host}\nTime: {x.Timestamp:yyyy-MM-dd HH:mm:ss}\nEvent ID: {x.ExternalId}\nAcknowledged: {x.Acknowledged} | Suppressed: {x.Suppressed}\n\nPROBLEM\n{x.Name}\n\nTAGS\n{x.Tags}\n\nMONITOR DETAIL\n{x.RawDetail}\n\nDBACHECK CORRELATION HINT\n{x.CorrelationHint}";
    }

    private async void Diagnose_Click(object s,RoutedEventArgs e)
    {
        if(EventsGrid.SelectedItem is not IntegrationEvent ev)return;
        try
        {
            SetBusy(true);StatusText.Text=En?"Matching host to DBACHECK profile and running targeted diagnosis...":"Buscando perfil DBACHECK y ejecutando diagnóstico dirigido...";
            _lastDiagnosis=await _diagnosis.DiagnoseAsync(ev);
            DetailText.Text=_lastDiagnosis.BuildEvidence();
            CreateIncidentButton.IsEnabled=true;
            StatusText.Text=En
                ?$"Matched {_lastDiagnosis.Profile.Name} | {_lastDiagnosis.Profile.Engine} | {_lastDiagnosis.Category} | {_lastDiagnosis.RelevantChecks.Count} correlated check(s)"
                :$"Perfil {_lastDiagnosis.Profile.Name} | {_lastDiagnosis.Profile.Engine} | {_lastDiagnosis.Category} | {_lastDiagnosis.RelevantChecks.Count} check(s) correlacionado(s)";
        }
        catch(Exception ex)
        {
            _lastDiagnosis=null;CreateIncidentButton.IsEnabled=false;StatusText.Text="ERROR: "+ex.Message;
        }
        finally{SetBusy(false);DiagnoseButton.IsEnabled=EventsGrid.SelectedItem is IntegrationEvent;}
    }

    private async void CreateIncident_Click(object s,RoutedEventArgs e)
    {
        if(_lastDiagnosis is null)return;
        try
        {
            SetBusy(true);
            var d=_lastDiagnosis.ToIncidentDiagnosis();
            var id=await _history.SaveAsync(_lastDiagnosis.Profile.Host,_lastDiagnosis.Profile.DatabaseOrService??"","External Monitoring",d);
            StatusText.Text=En?$"Incident #{id} created from {_lastDiagnosis.Event.Source} event {_lastDiagnosis.Event.ExternalId}.":$"Incidente #{id} creado desde evento {_lastDiagnosis.Event.Source} {_lastDiagnosis.Event.ExternalId}.";
        }
        catch(Exception ex){StatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);CreateIncidentButton.IsEnabled=_lastDiagnosis is not null;}
    }

    private void ExpandDetailButton_Click(object s,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);
    private void SetBusy(bool busy){SaveButton.IsEnabled=!busy;DeleteButton.IsEnabled=!busy;TestButton.IsEnabled=!busy;LoadProblemsButton.IsEnabled=!busy;DiagnoseButton.IsEnabled=!busy&&EventsGrid.SelectedItem is IntegrationEvent;CreateIncidentButton.IsEnabled=!busy&&_lastDiagnosis is not null;}
}