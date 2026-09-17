using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class BlockingAnalyzerWindow : Window
{
    private readonly SqlHealthService _service;
    private readonly SqlCompatibilityCollectorService _compat;
    private List<BlockingIncident> _items=new();
    public BlockingAnalyzerWindow(SqlHealthService service,SqlCompatibilityCollectorService compat){InitializeComponent();_service=service;_compat=compat;Loaded+=async(_,__)=>await LoadAsync();}
    private async Task LoadAsync(){try{RefreshButton.IsEnabled=false;AnalyzerStatus.Text="Consultando blocking...";_items=await _compat.GetBlockingIncidentsAsync();BlockingGrid.ItemsSource=_items;var roots=_items.Where(x=>x.IsRootBlocker).Select(x=>x.SessionId).Distinct().Count();var victims=_items.Count(x=>x.BlockingSessionId>0);var caps=await _compat.DetectAsync();AnalyzerStatus.Text=$"{victims} sesión(es) bloqueada(s) | {roots} root blocker(s) | {caps.VersionLabel} | {caps.Profile.ToString().ToUpperInvariant()} | Alpha 4.6";}catch(Exception ex){AnalyzerStatus.Text="ERROR: "+ex.Message;}finally{RefreshButton.IsEnabled=true;}}
    private async void RefreshButton_Click(object sender,RoutedEventArgs e)=>await LoadAsync();
    private BlockingIncident? Selected()=>BlockingGrid.SelectedItem as BlockingIncident;
    private void BlockingGrid_SelectionChanged(object sender,SelectionChangedEventArgs e){EvidenceButton.IsEnabled=Selected()!=null;if(Selected() is{}x)DetailText.Text=IncidentDiagnosisEngine.DiagnoseBlocking(x,_items).ToString();}
    private void ChainButton_Click(object sender,RoutedEventArgs e){if(Selected() is{}x)DetailText.Text=SqlHealthService.BuildBlockingChain(x.RootBlockerId,_items)+"\n\n"+IncidentDiagnosisEngine.DiagnoseBlocking(x,_items);}
    private void SqlButton_Click(object sender,RoutedEventArgs e){if(Selected() is{}x)DetailText.Text=$"SQL / INPUT BUFFER - SPID {x.SessionId}\nDatabase: {x.DatabaseName}\nLogin: {x.LoginName}\nHost: {x.HostName}\nApplication: {x.ProgramName}\n\n{x.SqlText}\n\nLegacy profile uses the most recent SQL handle when dm_exec_input_buffer is unavailable. Evidence only; not proof of causality.";}
    private async void LocksButton_Click(object sender,RoutedEventArgs e){if(Selected() is not{}x)return;try{AnalyzerStatus.Text=$"Consultando locks SPID {x.SessionId}...";DetailText.Text=await _service.GetBlockingLocksAsync(x.SessionId);AnalyzerStatus.Text=$"Locks SPID {x.SessionId} finalizado.";}catch(Exception ex){AnalyzerStatus.Text="ERROR locks: "+ex.Message;}}
    private void EvidenceButton_Click(object sender,RoutedEventArgs e){if(Selected() is not{}x)return;var text=SqlHealthService.BuildBlockingEvidence(x,_items)+"\n\n"+IncidentDiagnosisEngine.DiagnoseBlocking(x,_items);DetailText.Text=text;Clipboard.SetText(text);AnalyzerStatus.Text=$"Evidence Snapshot + diagnóstico SPID {x.SessionId} copiado.";}
}