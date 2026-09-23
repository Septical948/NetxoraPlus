using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;
public partial class LogAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private readonly IncidentCorrelationEngine _correlation;
    private readonly IncidentHistoryService _history=new();
    private bool En=>LocalizationService.Current==AppLanguage.En;
    public LogAnalyzerWindow(SqlHealthService service) { InitializeComponent(); _service=service; _correlation=new IncidentCorrelationEngine(service); Loaded += async (_,__)=>await LoadAsync(); }
    private async Task LoadAsync()
    {
        try {
            RefreshButton.IsEnabled=false; AnalyzerStatus.Text=En?"Querying Transaction Logs...":"Consultando Transaction Logs...";
            var data=await _service.GetLogDatabasesAsync(); LogGrid.ItemsSource=data;
            var c=data.Count(x=>x.Status=="CRITICAL"); var w=data.Count(x=>x.Status=="WARNING");
            SummaryText.Text=$"{data.Count} database(s) | Critical {c} | Warning {w} | Max used {(data.Count==0?0:data.Max(x=>x.UsedPct)):0.0}%";
            AnalyzerStatus.Text=En?"Transaction Log Analyzer updated · Alpha 4.6 Diagnosis Engine.":"Transaction Log Analyzer actualizado · Alpha 4.6 Diagnosis Engine.";
        } catch(Exception ex) { AnalyzerStatus.Text="ERROR: "+ex.Message; DetailText.Text=ex.ToString(); }
        finally { RefreshButton.IsEnabled=true; }
    }
    private LogDatabaseInfo? Selected => LogGrid.SelectedItem as LogDatabaseInfo;
    private void LogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var on=Selected!=null; DiagnosisButton.IsEnabled=on; FilesButton.IsEnabled=on; TransactionsButton.IsEnabled=on; BackupsButton.IsEnabled=on; EvidenceButton.IsEnabled=on;
        if(Selected is { } x) DetailText.Text=IncidentDiagnosisEngine.DiagnoseLog(x).ToString();
    }
    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();
    private async void DiagnosisButton_Click(object sender,RoutedEventArgs e) { if(Selected is not { } x)return; try { AnalyzerStatus.Text=En?"Diagnosing and correlating...":"Diagnosticando y correlacionando..."; var d=IncidentDiagnosisEngine.DiagnoseLog(x); var correlation=await _correlation.CorrelateLogAsync(x); var server=await _service.TestAsync(); var count=await _history.RecurrenceCountAsync(server,x.DatabaseName,"Transaction Log",d.ProbableCause); DetailText.Text=d+"\n\n"+correlation+(count>0?(En?$"\n\nRECURRENCE\n{count} similar previous incident(s) found.":$"\n\nRECURRENCIA\nSe encontraron {count} incidente(s) previo(s) similar(es)."):""); AnalyzerStatus.Text=En?$"Alpha 4.7 diagnosis generated | previous recurrences: {count}":$"Diagnóstico Alpha 4.7 generado | recurrencias previas: {count}"; } catch(Exception ex) { AnalyzerStatus.Text="ERROR correlación: "+ex.Message; DetailText.Text=ex.ToString(); } }
    private async void FilesButton_Click(object sender,RoutedEventArgs e) { if(Selected is { } x) await ShowAsync(()=>_service.GetLogFilesAsync(x),"Files"); }
    private async void TransactionsButton_Click(object sender,RoutedEventArgs e) { if(Selected is { } x) await ShowAsync(()=>_service.GetLogTransactionsAsync(x),En?"Transactions":"Transacciones"); }
    private async void BackupsButton_Click(object sender,RoutedEventArgs e) { if(Selected is { } x) await ShowAsync(()=>_service.GetLogBackupsAsync(x),"Backups"); }
    private async Task ShowAsync(Func<Task<string>> fn,string label) { try { AnalyzerStatus.Text=label+"..."; DetailText.Text=await fn(); AnalyzerStatus.Text=label+(En?" completed.":" finalizado."); } catch(Exception ex) { AnalyzerStatus.Text="ERROR: "+ex.Message; DetailText.Text=ex.ToString(); } }
    private async void EvidenceButton_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not { } x)return;
        try { var t=await _service.BuildLogEvidenceAsync(x); var d=IncidentDiagnosisEngine.DiagnoseLog(x); var server=await _service.TestAsync(); var id=await _history.SaveAsync(server,x.DatabaseName,"Transaction Log",d); var count=await _history.RecurrenceCountAsync(server,x.DatabaseName,"Transaction Log",d.ProbableCause); DetailText.Text=t+"\n\n"+d+(En?$"\n\nINCIDENT HISTORY\nIncident #{id} saved. Recorded occurrences: {count}.":$"\n\nINCIDENT HISTORY\nIncident #{id} guardado. Ocurrencias registradas: {count}."); Clipboard.SetText(DetailText.Text); AnalyzerStatus.Text=En?$"Evidence Snapshot + Incident #{id} saved.":$"Evidence Snapshot + Incident #{id} guardado."; }
        catch(Exception ex){ AnalyzerStatus.Text="ERROR: "+ex.Message; }
    }
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);
}