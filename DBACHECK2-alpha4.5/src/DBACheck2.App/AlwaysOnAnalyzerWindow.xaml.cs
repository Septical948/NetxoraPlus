using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class AlwaysOnAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private List<AlwaysOnItem> _items = new();
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public AlwaysOnAnalyzerWindow(SqlHealthService service)
    {
        InitializeComponent();
        _service = service; ApplyLanguage();
    Loaded += async (_,__) => await LoadAsync();
    }

    private void ApplyLanguage(){var en=LocalizationService.Current==AppLanguage.En;RefreshButton.Content=en?"REFRESH":"ACTUALIZAR";DiagnosticButton.Content=en?"DIAGNOSIS":"DIAGNÓSTICO";ReplicaButton.Content=en?"VIEW REPLICA":"VER RÉPLICA";DatabaseButton.Content=en?"VIEW DATABASE":"VER DATABASE";ListenerButton.Content=en?"VIEW LISTENER":"VER LISTENER";EvidenceButton.Content=en?"CAPTURE EVIDENCE":"CAPTURAR EVIDENCIA";StatusColumn.Header=en?"Status":"Estado";DetailText.Text=en?"Select a replica/database to investigate.":"Selecciona una réplica/base para investigar."; }

    private async Task LoadAsync()
    {
        try
        {
            RefreshButton.IsEnabled=false;
            AnalyzerStatus.Text=En?"Querying AlwaysOn...":"Consultando AlwaysOn...";
            _items=await _service.GetAlwaysOnItemsAsync();
            AgGrid.ItemsSource=_items;
            var critical=_items.Count(x=>x.Status=="CRITICAL");
            var warning=_items.Count(x=>x.Status=="WARNING");
            var groups=_items.Select(x=>x.AvailabilityGroup).Distinct().Count();
            SummaryText.Text=$"{groups} AG(s) | {_items.Count} database replica(s) | Critical {critical} | Warning {warning}";
            AnalyzerStatus.Text=En?"AlwaysOn Analyzer updated.":"AlwaysOn Analyzer actualizado.";
            if(_items.Count==0) DetailText.Text=En?"No local Availability Groups were detected, or AlwaysOn is not enabled on this instance.":"No se detectaron Availability Groups locales, o AlwaysOn no está habilitado en esta instancia.";
        }
        catch(Exception ex)
        {
            AnalyzerStatus.Text="ERROR: "+ex.Message;
            DetailText.Text=ex.ToString();
        }
        finally { RefreshButton.IsEnabled=true; }
    }

    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();

    private void AgGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        var enabled=AgGrid.SelectedItem is AlwaysOnItem;
        DiagnosticButton.IsEnabled=enabled;
        ReplicaButton.IsEnabled=enabled;
        DatabaseButton.IsEnabled=enabled;
        EvidenceButton.IsEnabled=enabled;
    }

    private async void DiagnosticButton_Click(object sender,RoutedEventArgs e)
    {
        if(AgGrid.SelectedItem is not AlwaysOnItem x) return;
        DetailText.Text=await _service.GetAlwaysOnDiagnosticAsync(x);
        AnalyzerStatus.Text=En?$"Diagnosis {x.AvailabilityGroup}/{x.DatabaseName} completed.":$"Diagnóstico {x.AvailabilityGroup}/{x.DatabaseName} finalizado.";
    }

    private async void ReplicaButton_Click(object sender,RoutedEventArgs e)
    {
        if(AgGrid.SelectedItem is not AlwaysOnItem x) return;
        DetailText.Text=await _service.GetAlwaysOnReplicaDetailAsync(x.AvailabilityGroup);
        AnalyzerStatus.Text=En?$"Replica details for {x.AvailabilityGroup}.":$"Detalle de réplicas {x.AvailabilityGroup}.";
    }

    private async void DatabaseButton_Click(object sender,RoutedEventArgs e)
    {
        if(AgGrid.SelectedItem is not AlwaysOnItem x) return;
        DetailText.Text=await _service.GetAlwaysOnDatabaseDetailAsync(x.AvailabilityGroup,x.DatabaseName);
        AnalyzerStatus.Text=En?$"Database replica detail for {x.DatabaseName}.":$"Detalle database replica {x.DatabaseName}.";
    }

    private async void ListenerButton_Click(object sender,RoutedEventArgs e)
    {
        DetailText.Text=await _service.GetAlwaysOnListenersAsync();
        AnalyzerStatus.Text=En?"Listeners queried.":"Listeners consultados.";
    }

    private void EvidenceButton_Click(object sender,RoutedEventArgs e)
    {
        if(AgGrid.SelectedItem is not AlwaysOnItem x) return;
        var snapshot=SqlHealthService.BuildAlwaysOnEvidence(x);
        DetailText.Text=snapshot;
        Clipboard.SetText(snapshot);
        AnalyzerStatus.Text=En?"Evidence Snapshot captured and copied to clipboard.":"Evidence Snapshot capturado y copiado al portapapeles.";
    }
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);
}