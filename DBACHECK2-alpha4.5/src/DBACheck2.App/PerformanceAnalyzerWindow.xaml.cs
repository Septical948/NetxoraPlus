using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class PerformanceAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private List<PerformanceRequest> _items = new();

    public PerformanceAnalyzerWindow(SqlHealthService service)
    {
        InitializeComponent();
        _service=service;
        Loaded += async (_,__) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            RefreshButton.IsEnabled=false;
            AnalyzerStatus.Text="Consultando requests activos...";
            _items=await _service.GetPerformanceRequestsAsync();
            RequestGrid.ItemsSource=_items;
            var critical=_items.Count(x=>x.Status=="CRITICAL");
            var warning=_items.Count(x=>x.Status=="WARNING");
            var blocked=_items.Count(x=>x.BlockingSessionId>0);
            SummaryText.Text=$"{_items.Count} request(s) activo(s) | Critical {critical} | Warning {warning} | Blocked {blocked}";
            AnalyzerStatus.Text="Performance Analyzer actualizado.";
            if(_items.Count==0) DetailText.Text="No se detectaron requests de usuario activos en esta muestra.";
        }
        catch(Exception ex) { AnalyzerStatus.Text="ERROR: "+ex.Message; DetailText.Text=ex.ToString(); }
        finally { RefreshButton.IsEnabled=true; }
    }

    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();

    private void RequestGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        var enabled=RequestGrid.SelectedItem is PerformanceRequest;
        DiagnosticButton.IsEnabled=enabled; SqlButton.IsEnabled=enabled; RequestButton.IsEnabled=enabled; EvidenceButton.IsEnabled=enabled;
    }

    private void DiagnosticButton_Click(object sender,RoutedEventArgs e)
    {
        if(RequestGrid.SelectedItem is not PerformanceRequest x) return;
        DetailText.Text=SqlHealthService.BuildPerformanceDiagnostic(x);
        AnalyzerStatus.Text=$"Diagnóstico SPID {x.SessionId} finalizado.";
    }

    private void SqlButton_Click(object sender,RoutedEventArgs e)
    {
        if(RequestGrid.SelectedItem is not PerformanceRequest x) return;
        DetailText.Text=$"SQL / CURRENT REQUEST - SPID {x.SessionId}\r\nDatabase: {x.DatabaseName}\r\nLogin: {x.LoginName}\r\nHost: {x.HostName}\r\nApplication: {x.ProgramName}\r\n\r\n{x.SqlText}\r\n\r\nRead-only inspection.";
        AnalyzerStatus.Text=$"SQL SPID {x.SessionId}.";
    }

    private void RequestButton_Click(object sender,RoutedEventArgs e)
    {
        if(RequestGrid.SelectedItem is not PerformanceRequest x) return;
        DetailText.Text=SqlHealthService.BuildPerformanceRequestDetail(x);
        AnalyzerStatus.Text=$"Detalle request SPID {x.SessionId}.";
    }

    private async void WaitsButton_Click(object sender,RoutedEventArgs e)
    {
        try { DetailText.Text=await _service.GetPerformanceWaitsAsync(); AnalyzerStatus.Text="Waits actuales consultados."; }
        catch(Exception ex){ AnalyzerStatus.Text="ERROR waits: "+ex.Message; }
    }

    private async void MemoryButton_Click(object sender,RoutedEventArgs e)
    {
        try { DetailText.Text=await _service.GetMemoryGrantsAsync(); AnalyzerStatus.Text="Memory Grants consultados."; }
        catch(Exception ex){ AnalyzerStatus.Text="ERROR memory grants: "+ex.Message; }
    }

    private void EvidenceButton_Click(object sender,RoutedEventArgs e)
    {
        if(RequestGrid.SelectedItem is not PerformanceRequest x) return;
        var snapshot=SqlHealthService.BuildPerformanceEvidence(x);
        DetailText.Text=snapshot; Clipboard.SetText(snapshot);
        AnalyzerStatus.Text=$"Evidence Snapshot SPID {x.SessionId} copiado.";
    }
}