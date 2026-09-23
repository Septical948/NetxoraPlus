using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;
using Microsoft.Win32;

namespace DBACheck2.App;

public partial class AssessmentWindow:Window
{
    private readonly ServerProfileService _profiles=new();
    private readonly AssessmentService _assessment=new();
    private readonly AssessmentHistoryService _history=new();
    private readonly AssessmentExportService _export=new();
    private List<ServerProfile> _profileItems=new();
    private AssessmentRun? _current;
    private CancellationTokenSource? _assessmentCts;
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
        StopButton.Content=En?"STOP":"DETENER";
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
        _assessmentCts?.Dispose();
        _assessmentCts=new CancellationTokenSource();
        var progress=new Progress<string>(message=>StatusText.Text=message);
        try
        {
            SetBusy(true);
            ResetRunView();
            StatusText.Text=En?"Running read-only assessment...":"Ejecutando evaluación de solo lectura...";
            _current=await _assessment.RunAsync(profile,mode,_assessmentCts.Token,progress);
            await _history.SaveAsync(_current);
            ChecksGrid.ItemsSource=_current.Checks;
            OverallText.Text=_current.Overall;CriticalText.Text=_current.Critical.ToString();WarningText.Text=_current.Warning.ToString();OkText.Text=_current.Ok.ToString();InfoText.Text=_current.Info.ToString();
            StatusText.Text=$"{_current.Engine} | {_current.Checks.Count} check(s) | {_current.DurationSeconds:0.0}s | {_current.PackVersion}";
            await LoadHistoryAsync();
        }
        catch(OperationCanceledException)
        {
            StatusText.Text=En
                ?"Assessment stopped by operator. The interrupted run was not saved to history."
                :"Evaluación detenida por el operador. La ejecución interrumpida no se guardó en el historial.";
        }
        catch(Exception ex)
        {
            StatusText.Text="ERROR: "+ex.Message;
            DetailText.Text=(En?"Assessment failed.\n\n":"La evaluación falló.\n\n")+ex;
        }
        finally
        {
            SetBusy(false);
            _assessmentCts?.Dispose();
            _assessmentCts=null;
        }
    }

    private void StopButton_Click(object sender,RoutedEventArgs e)
    {
        if(_assessmentCts is null || _assessmentCts.IsCancellationRequested)return;
        StopButton.IsEnabled=false;
        StatusText.Text=En
            ?"Stopping assessment and requesting cancellation of the active query..."
            :"Deteniendo evaluación y solicitando la cancelación de la consulta activa...";
        _assessmentCts.Cancel();
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

    private async void DownloadAssessment_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement element || element.DataContext is not AssessmentRun run)return;
        if(!run.CanExport)
        {
            HistoryStatusText.Text=En
                ?"Only completed Full assessments can be exported."
                :"Sólo se pueden exportar assessments Full completos.";
            return;
        }

        var dialog=new SaveFileDialog {
            Title=En?"Download Full Assessment":"Descargar Assessment Full",
            Filter="HTML report (*.html)|*.html",
            FileName=_export.SuggestedFileName(run),
            AddExtension=true,
            DefaultExt=".html",
            OverwritePrompt=true
        };

        if(dialog.ShowDialog()!=true)return;

        try
        {
            await _export.ExportHtmlAsync(run,dialog.FileName);
            HistoryStatusText.Text=En
                ?$"Full Assessment exported: {dialog.FileName}"
                :$"Assessment Full exportado: {dialog.FileName}";
        }
        catch(Exception ex)
        {
            HistoryStatusText.Text="ERROR: "+ex.Message;
        }
    }

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

    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)
        =>DetailPanelService.Toggle(ChecksRow,DetailRow,ExpandDetailButton,125,105);

    private void ResetRunView()
    {
        _current=null;
        ChecksGrid.ItemsSource=null;
        ChecksGrid.SelectedItem=null;
        OverallText.Text=En?"RUNNING":"EJECUTANDO";
        CriticalText.Text="0";WarningText.Text="0";OkText.Text="0";InfoText.Text="0";
        DetailText.Text=En
            ?"Assessment is running. Select a finding after completion to inspect its evidence."
            :"La evaluación está en ejecución. Seleccioná un hallazgo al finalizar para revisar su evidencia.";
        DetailPanelService.Initialize(ChecksRow,DetailRow,ExpandDetailButton,125);
    }

    private void SetBusy(bool busy)
    {
        RunButton.IsEnabled=!busy;
        StopButton.IsEnabled=busy && _assessmentCts is not null && !_assessmentCts.IsCancellationRequested;
        ProfileBox.IsEnabled=!busy;
        ModeBox.IsEnabled=!busy;
    }
}
