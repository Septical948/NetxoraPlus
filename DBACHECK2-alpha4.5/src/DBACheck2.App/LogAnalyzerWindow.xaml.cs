using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;
public partial class LogAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private readonly IncidentCorrelationEngine _correlation;
    public LogAnalyzerWindow(SqlHealthService service) { InitializeComponent(); _service=service; _correlation=new IncidentCorrelationEngine(service); Loaded += async (_,__)=>await LoadAsync(); }
    private async Task LoadAsync()
    {
        try {
            RefreshButton.IsEnabled=false; AnalyzerStatus.Text="Consultando Transaction Logs...";
            var data=await _service.GetLogDatabasesAsync(); LogGrid.ItemsSource=data;
            var c=data.Count(x=>x.Status=="CRITICAL"); var w=data.Count(x=>x.Status=="WARNING");
            SummaryText.Text=$"{data.Count} database(s) | Critical {c} | Warning {w} | Max used {(data.Count==0?0:data.Max(x=>x.UsedPct)):0.0}%";
            AnalyzerStatus.Text="Transaction Log Analyzer actualizado · Alpha 4.6 Diagnosis Engine.";
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
    private async void DiagnosisButton_Click(object sender,RoutedEventArgs e) { if(Selected is not { } x)return; try { AnalyzerStatus.Text="Diagnosticando y correlacionando..."; var diagnosis=IncidentDiagnosisEngine.DiagnoseLog(x).ToString(); var correlation=await _correlation.CorrelateLogAsync(x); DetailText.Text=diagnosis+"\n\n"+correlation; AnalyzerStatus.Text="Diagnóstico + correlación Alpha 4.6.1 generado."; } catch(Exception ex) { AnalyzerStatus.Text="ERROR correlación: "+ex.Message; DetailText.Text=ex.ToString(); } }
    private async void FilesButton_Click(object sender,RoutedEventArgs e) { if(Selected is { } x) await ShowAsync(()=>_service.GetLogFilesAsync(x),"Files"); }
    private async void TransactionsButton_Click(object sender,RoutedEventArgs e) { if(Selected is { } x) await ShowAsync(()=>_service.GetLogTransactionsAsync(x),"Transacciones"); }
    private async void BackupsButton_Click(object sender,RoutedEventArgs e) { if(Selected is { } x) await ShowAsync(()=>_service.GetLogBackupsAsync(x),"Backups"); }
    private async Task ShowAsync(Func<Task<string>> fn,string label) { try { AnalyzerStatus.Text=label+"..."; DetailText.Text=await fn(); AnalyzerStatus.Text=label+" finalizado."; } catch(Exception ex) { AnalyzerStatus.Text="ERROR: "+ex.Message; DetailText.Text=ex.ToString(); } }
    private async void EvidenceButton_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not { } x)return;
        try { var t=await _service.BuildLogEvidenceAsync(x); DetailText.Text=t+"\n\n"+IncidentDiagnosisEngine.DiagnoseLog(x); Clipboard.SetText(DetailText.Text); AnalyzerStatus.Text="Evidence Snapshot LOG + diagnóstico copiado."; }
        catch(Exception ex){ AnalyzerStatus.Text="ERROR: "+ex.Message; }
    }
}