using System.Windows; using System.Windows.Controls; using DBACheck2.App.Models; using DBACheck2.App.Services;
namespace DBACheck2.App;
public partial class BackupJobsAnalyzerWindow:Window
{
 bool En=>LocalizationService.Current==AppLanguage.En;
 readonly SqlHealthService _service; public BackupJobsAnalyzerWindow(SqlHealthService s){InitializeComponent();_service=s;ApplyLanguage();Loaded+=async(_,__)=>await LoadAsync();}
 void ApplyLanguage(){var en=LocalizationService.Current==AppLanguage.En;RefreshButton.Content=en?"REFRESH":"ACTUALIZAR";DiagnosisButton.Content=en?"DIAGNOSIS":"DIAGNÓSTICO";HistoryButton.Content=en?"VIEW HISTORY":"VER HISTORIAL";EvidenceButton.Content=en?"CAPTURE EVIDENCE":"CAPTURAR EVIDENCIA";StatusColumn.Header=en?"Status":"Estado";DetailText.Text=en?"Select a database or job to investigate.":"Selecciona una base o job para investigar.";}
 async Task LoadAsync()
 {
     try
     {
         RefreshButton.IsEnabled = false;
         AnalyzerStatus.Text = En?"Querying backups and jobs...":"Consultando backups y jobs...";
         var b = await _service.GetBackupDatabasesAsync();
         var j = await _service.GetAgentJobsAsync();
         BackupGrid.ItemsSource = b;
         JobGrid.ItemsSource = j;
         var backupWarnings = b.Count(x => x.Status == "WARNING");
         var jobWarnings = j.Count(x => x.Status == "WARNING");
         AnalyzerStatus.Text = $"Backups: {backupWarnings} warning | Jobs: {jobWarnings} failed";
     }
     catch(Exception ex)
     {
         AnalyzerStatus.Text = "ERROR: " + ex.Message;
         DetailText.Text = ex.ToString();
     }
     finally
     {
         RefreshButton.IsEnabled = true;
     }
 }
 object? Selected=>ModeTabs.SelectedIndex==0?BackupGrid.SelectedItem:JobGrid.SelectedItem;
 void Update(){var on=Selected!=null;DiagnosisButton.IsEnabled=HistoryButton.IsEnabled=EvidenceButton.IsEnabled=on;if(Selected is BackupDatabaseInfo b)DetailText.Text=SqlHealthService.BuildBackupDiagnosis(b);else if(Selected is AgentJobInfo j)DetailText.Text=SqlHealthService.BuildJobDiagnosis(j);}
 void BackupGrid_SelectionChanged(object s,SelectionChangedEventArgs e)=>Update(); void JobGrid_SelectionChanged(object s,SelectionChangedEventArgs e)=>Update(); void ModeTabs_SelectionChanged(object s,SelectionChangedEventArgs e){if(IsLoaded)Update();}
 async void RefreshButton_Click(object s,RoutedEventArgs e)=>await LoadAsync(); void DiagnosisButton_Click(object s,RoutedEventArgs e)=>Update();
 async void HistoryButton_Click(object s,RoutedEventArgs e){try{if(Selected is BackupDatabaseInfo b)DetailText.Text=await _service.GetBackupHistoryAsync(b);else if(Selected is AgentJobInfo j)DetailText.Text=await _service.GetJobHistoryAsync(j);AnalyzerStatus.Text=En?"History completed.":"Historial finalizado.";}catch(Exception ex){AnalyzerStatus.Text="ERROR: "+ex.Message;}}
 async void EvidenceButton_Click(object s,RoutedEventArgs e){string t="";if(Selected is BackupDatabaseInfo b)t=SqlHealthService.BuildBackupDiagnosis(b)+"\n\nEvidence timestamp: "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");else if(Selected is AgentJobInfo j)t=SqlHealthService.BuildJobDiagnosis(j)+"\n\nEvidence timestamp: "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");if(t.Length>0){DetailText.Text=t;Clipboard.SetText(t);AnalyzerStatus.Text=En?"Evidence Snapshot copied.":"Evidence Snapshot copiado.";}await Task.CompletedTask;}
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);
}
