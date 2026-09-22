using System.Windows;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;
public partial class TempDbAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private TempDbSnapshot? _snapshot;
    private bool En=>LocalizationService.Current==AppLanguage.En;
    public TempDbAnalyzerWindow(SqlHealthService service) { InitializeComponent(); _service=service; Loaded += async (_,__)=>await LoadAsync(); }
    private async Task LoadAsync() { try { RefreshButton.IsEnabled=false; AnalyzerStatus.Text=En?"Querying TempDB...":"Consultando TempDB..."; _snapshot=await _service.GetTempDbSnapshotAsync(); FilesGrid.ItemsSource=_snapshot.Files; SummaryText.Text=En?$"{_snapshot.Status} | Total {_snapshot.TotalMb} MB | Used {_snapshot.UsedMb} MB ({_snapshot.UsedPct}%) | Free {_snapshot.FreeMb} MB | Version Store {_snapshot.VersionStoreMb} MB":$"{_snapshot.Status} | Total {_snapshot.TotalMb} MB | Usado {_snapshot.UsedMb} MB ({_snapshot.UsedPct}%) | Libre {_snapshot.FreeMb} MB | Version Store {_snapshot.VersionStoreMb} MB"; DetailText.Text=SqlHealthService.BuildTempDbDiagnosis(_snapshot); AnalyzerStatus.Text=En?"TempDB Analyzer updated.":"TempDB Analyzer actualizado."; } catch(Exception ex) { AnalyzerStatus.Text="ERROR: "+ex.Message; DetailText.Text=ex.ToString(); } finally { RefreshButton.IsEnabled=true; } }
    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();
    private void DiagnosisButton_Click(object sender,RoutedEventArgs e) { if(_snapshot!=null) DetailText.Text=SqlHealthService.BuildTempDbDiagnosis(_snapshot); }
    private void FilesButton_Click(object sender,RoutedEventArgs e) { if(_snapshot!=null) DetailText.Text=SqlHealthService.BuildTempDbFiles(_snapshot); }
    private void VersionButton_Click(object sender,RoutedEventArgs e) { if(_snapshot!=null) DetailText.Text=SqlHealthService.BuildVersionStoreDetail(_snapshot); }
    private void EvidenceButton_Click(object sender,RoutedEventArgs e) { if(_snapshot==null)return; var t=SqlHealthService.BuildTempDbEvidence(_snapshot); DetailText.Text=t; Clipboard.SetText(t); AnalyzerStatus.Text=En?"TempDB Evidence Snapshot copied to clipboard.":"Evidence Snapshot TempDB copiado al portapapeles."; }
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(DetailRow,ExpandDetailButton);
}
