using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class PerformanceAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private readonly SqlCompatibilityCollectorService _compat;
    private List<PerformanceRequest> _items=new();
    private bool En=>LocalizationService.Current==AppLanguage.En;
    public PerformanceAnalyzerWindow(SqlHealthService service,SqlCompatibilityCollectorService compat){InitializeComponent();_service=service;_compat=compat;ApplyLanguage();Loaded+=async(_,__)=>await LoadAsync();}
    private void ApplyLanguage(){var en=LocalizationService.Current==AppLanguage.En;RefreshButton.Content=en?"REFRESH":"ACTUALIZAR";DiagnosticButton.Content=en?"DIAGNOSIS":"DIAGNÓSTICO";SqlButton.Content=en?"VIEW SQL":"VER SQL";RequestButton.Content=en?"VIEW REQUEST":"VER REQUEST";WaitsButton.Content=en?"VIEW WAITS":"VER WAITS";EvidenceButton.Content=en?"CAPTURE EVIDENCE":"CAPTURAR EVIDENCIA";StatusColumn.Header=en?"Status":"Estado";DetailText.Text=en?"Select a request to investigate.":"Selecciona un request para investigar.";}
    private async Task LoadAsync(){try{RefreshButton.IsEnabled=false;AnalyzerStatus.Text=En?"Querying active requests...":"Consultando requests activos...";_items=await _compat.GetPerformanceRequestsAsync();RequestGrid.ItemsSource=_items;var critical=_items.Count(x=>x.Status=="CRITICAL");var warning=_items.Count(x=>x.Status=="WARNING");var blocked=_items.Count(x=>x.BlockingSessionId>0);var caps=await _compat.DetectAsync();SummaryText.Text=En?$"{_items.Count} active request(s) | Critical {critical} | Warning {warning} | Blocked {blocked}":$"{_items.Count} request(s) activo(s) | Critical {critical} | Warning {warning} | Bloqueados {blocked}";AnalyzerStatus.Text=En?$"Performance updated | {caps.VersionLabel} | {caps.Profile.ToString().ToUpperInvariant()}":$"Performance actualizado | {caps.VersionLabel} | {caps.Profile.ToString().ToUpperInvariant()}";if(_items.Count==0)DetailText.Text=En?"No active user requests were detected in this sample.":"No se detectaron requests de usuario activos en esta muestra.";}catch(Exception ex){AnalyzerStatus.Text="ERROR: "+ex.Message;DetailText.Text=ex.ToString();}finally{RefreshButton.IsEnabled=true;}}
    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();
    private void RequestGrid_SelectionChanged(object sender,SelectionChangedEventArgs e){var enabled=RequestGrid.SelectedItem is PerformanceRequest;DiagnosticButton.IsEnabled=enabled;SqlButton.IsEnabled=enabled;RequestButton.IsEnabled=enabled;EvidenceButton.IsEnabled=enabled;}
    private void DiagnosticButton_Click(object sender,RoutedEventArgs e){if(RequestGrid.SelectedItem is not PerformanceRequest x)return;DetailText.Text=SqlHealthService.BuildPerformanceDiagnostic(x);AnalyzerStatus.Text=En?$"Diagnosis for SPID {x.SessionId} completed.":$"Diagnóstico SPID {x.SessionId} finalizado.";}
    private void SqlButton_Click(object sender,RoutedEventArgs e){if(RequestGrid.SelectedItem is not PerformanceRequest x)return;DetailText.Text=$"SQL / CURRENT REQUEST - SPID {x.SessionId}\r\nDatabase: {x.DatabaseName}\r\nLogin: {x.LoginName}\r\nHost: {x.HostName}\r\nApplication: {x.ProgramName}\r\n\r\n{x.SqlText}\r\n\r\nRead-only inspection.";AnalyzerStatus.Text=$"SQL SPID {x.SessionId}.";}
    private void RequestButton_Click(object sender,RoutedEventArgs e){if(RequestGrid.SelectedItem is not PerformanceRequest x)return;DetailText.Text=SqlHealthService.BuildPerformanceRequestDetail(x);AnalyzerStatus.Text=En?$"Request detail for SPID {x.SessionId}.":$"Detalle request SPID {x.SessionId}.";}
    private async void WaitsButton_Click(object sender,RoutedEventArgs e){try{DetailText.Text=await _service.GetPerformanceWaitsAsync();AnalyzerStatus.Text=En?"Current waits queried.":"Waits actuales consultados.";}catch(Exception ex){AnalyzerStatus.Text="ERROR waits: "+ex.Message;}}
    private async void MemoryButton_Click(object sender,RoutedEventArgs e){try{DetailText.Text=await _service.GetMemoryGrantsAsync();AnalyzerStatus.Text=En?"Memory Grants queried.":"Memory Grants consultados.";}catch(Exception ex){AnalyzerStatus.Text="ERROR memory grants: "+ex.Message;}}
    private void EvidenceButton_Click(object sender,RoutedEventArgs e){if(RequestGrid.SelectedItem is not PerformanceRequest x)return;var snapshot=SqlHealthService.BuildPerformanceEvidence(x);DetailText.Text=snapshot;Clipboard.SetText(snapshot);AnalyzerStatus.Text=En?$"Evidence Snapshot for SPID {x.SessionId} copied.":$"Evidence Snapshot SPID {x.SessionId} copiado.";}
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);
}