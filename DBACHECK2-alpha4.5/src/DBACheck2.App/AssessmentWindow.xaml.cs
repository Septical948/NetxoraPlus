using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class AssessmentWindow:Window
{
    private readonly ServerProfileService _profiles=new();
    private readonly AssessmentService _assessment=new();
    private readonly AssessmentHistoryService _history=new();
    private List<ServerProfile> _profileItems=new();
    private AssessmentRun? _current;
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public AssessmentWindow()
    {
        InitializeComponent();
        ModeBox.ItemsSource=Enum.GetValues<AssessmentMode>();
        ModeBox.SelectedItem=AssessmentMode.Full;
        ApplyLanguage();
        Loaded+=async(_,__)=>{await LoadProfilesAsync();await LoadHistoryAsync();};
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"ASSESSMENT":"EVALUACIÓN";
        SubtitleText.Text=En?"Multi-engine health assessment · Read-only baseline":"Evaluación de salud multi-motor · Baseline de solo lectura";
        RunTab.Header=En?"RUN ASSESSMENT":"EJECUTAR EVALUACIÓN";
        HistoryTab.Header=En?"HISTORY":"HISTORIAL";
        ProfileLabel.Text=En?"PROFILE":"PERFIL";
        ModeLabel.Text=En?"MODE":"MODO";
        RunButton.Content=En?"RUN ASSESSMENT":"EJECUTAR EVALUACIÓN";
        DetailTitleText.Text=En?"ASSESSMENT EVIDENCE":"EVIDENCIA DE EVALUACIÓN";
        ExpandDetailButton.Content=En?"EXPAND":"EXPANDIR";
        DetailText.Text=En?"Select a check to inspect evidence and remediation guidance.":"Seleccioná un check para revisar evidencia y guía de remediación.";
        RefreshHistoryButton.Content=En?"REFRESH HISTORY":"ACTUALIZAR HISTORIAL";
        HistoryDetailText.Text=En?"Select a previous assessment.":"Seleccioná una evaluación previa.";
    }

    private async Task LoadProfilesAsync()
    {
        _profileItems=await _profiles.LoadAsync();
        ProfileBox.ItemsSource=null;ProfileBox.ItemsSource=_profileItems;
        if(_profileItems.Count>0)ProfileBox.SelectedIndex=0;
        StatusText.Text=En?$"{_profileItems.Count} saved profile(s).":$"{_profileItems.Count} perfil(es) guardado(s).";
    }

    private async void RunButton_Click(object sender,RoutedEventArgs e)
    {
        if(ProfileBox.SelectedItem is not ServerProfile profile){StatusText.Text=En?"Select a saved profile first.":"Seleccioná primero un perfil guardado.";return;}
        var mode=(AssessmentMode)(ModeBox.SelectedItem??AssessmentMode.Full);
        try
        {
            SetBusy(true);StatusText.Text=En?"Running read-only assessment...":"Ejecutando evaluación de solo lectura...";
            _current=await _assessment.RunAsync(profile,mode);
            await _history.SaveAsync(_current);
            ChecksGrid.ItemsSource=_current.Checks;
            OverallText.Text=_current.Overall;CriticalText.Text=_current.Critical.ToString();WarningText.Text=_current.Warning.ToString();OkText.Text=_current.Ok.ToString();InfoText.Text=_current.Info.ToString();
            StatusText.Text=En
                ?$"{_current.Engine} | {_current.Checks.Count} check(s) | {_current.DurationSeconds:0.0}s | {_current.PackVersion}"
                :$"{_current.Engine} | {_current.Checks.Count} check(s) | {_current.DurationSeconds:0.0}s | {_current.PackVersion}";
            await LoadHistoryAsync();
        }
        catch(Exception ex){StatusText.Text="ERROR: "+ex.Message;}
        finally{SetBusy(false);}
    }

    private void ChecksGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ChecksGrid.SelectedItem is not AssessmentCheck x)return;
        DetailText.Text=$@"{x.Status} | {x.Engine} | {x.Category}
CHECK
{x.Title}

SUMMARY
{x.Summary}

EVIDENCE
{x.Evidence}

WHY IT MATTERS
{x.WhyItMatters}

RECOMMENDED ACTION
{x.RecommendedAction}

VERIFICATION
{x.Verification}

CAPABILITY
{x.Capability}

SAFETY
{(x.ReadOnly?"READ-ONLY":"IMPACT")}";
    }

    private async Task LoadHistoryAsync()
    {
        var rows=await _history.ListAsync(100);
        HistoryGrid.ItemsSource=rows;
        HistoryStatusText.Text=En?$"{rows.Count} assessment run(s) | {_history.DatabasePath}":$"{rows.Count} evaluación(es) | {_history.DatabasePath}";
    }

    private async void RefreshHistoryButton_Click(object sender,RoutedEventArgs e)=>await LoadHistoryAsync();

    private void HistoryGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(HistoryGrid.SelectedItem is not AssessmentRun r)return;
        var findings=r.Checks.OrderByDescending(x=>x.Severity).Take(12);
        HistoryDetailText.Text=$@"{r.Overall} | {r.Engine} | {r.ProfileName}
Host: {r.Host}
Started: {r.StartedAt:yyyy-MM-dd HH:mm:ss}
Mode: {r.Mode}
Critical: {r.Critical} | Warning: {r.Warning} | OK: {r.Ok} | Info: {r.Info}
Provider: {r.ProviderInfo}

TOP FINDINGS
{string.Join(Environment.NewLine,findings.Select(x=>$"[{x.Status}] {x.Category} / {x.Title}: {x.Summary}"))}";
    }

    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(DetailRow,ExpandDetailButton,125);
    private void SetBusy(bool busy){RunButton.IsEnabled=!busy;ProfileBox.IsEnabled=!busy;ModeBox.IsEnabled=!busy;}
}
