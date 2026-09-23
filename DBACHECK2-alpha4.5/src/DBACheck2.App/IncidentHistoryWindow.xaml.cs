using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;
namespace DBACheck2.App;
public partial class IncidentHistoryWindow:Window
{
 private readonly IncidentHistoryService _history;
 private bool En=>LocalizationService.Current==AppLanguage.En;
 public IncidentHistoryWindow(IncidentHistoryService history){InitializeComponent();_history=history;ApplyLanguage();Loaded+=async(_,__)=>await LoadAsync();}
 private void ApplyLanguage()
 {
   TitleText.Text=En?"INCIDENT HISTORY":"HISTORIAL DE INCIDENTES";
   DateColumn.Header=En?"Date":"Fecha";ServerColumn.Header=En?"Server":"Servidor";DatabaseColumn.Header=En?"Database":"Base";
   ModuleColumn.Header=En?"Module":"Módulo";SeverityColumn.Header=En?"Severity":"Sev";StateColumn.Header=En?"Status":"Estado";CountColumn.Header=En?"Count":"Veces";ProblemColumn.Header=En?"Problem":"Problema";
   ActionBox.ToolTip=En?"Action performed by the DBA":"Acción realizada por el DBA";VerificationBox.ToolTip=En?"Post-action verification":"Verificación posterior";
   ResolveButton.Content=En?"RESOLVE INCIDENT":"RESOLVER INCIDENTE";DetailText.Text=En?"Select an incident.":"Seleccioná un incidente.";
 }
 private async Task LoadAsync(){var data=await _history.ListAsync();IncidentGrid.ItemsSource=data;SummaryText.Text=En?$"{data.Count} stored incident(s) | Beta 1 | Local SQLite: {_history.DatabasePath}":$"{data.Count} incidente(s) almacenado(s) | Beta 1 | SQLite local: {_history.DatabasePath}";}
 private void IncidentGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
 {
   if(IncidentGrid.SelectedItem is not IncidentRecord x){ResolveButton.IsEnabled=false;return;}
   ResolveButton.IsEnabled=x.Status=="OPEN"; ActionBox.Text=x.ActionTaken; VerificationBox.Text=x.Verification;
   DetailText.Text=En
    ?$"INCIDENT #{x.Id}\n{x.CreatedAt:yyyy-MM-dd HH:mm:ss}\nServer: {x.ServerName}\nDatabase: {x.DatabaseName}\nModule: {x.Module}\nSeverity: {x.Severity}\nStatus: {x.Status}\nRecurrences: {x.RecurrenceCount}\n\nPROBLEM\n{x.Problem}\n\nCAUSE\n{x.Cause}\n\nEVIDENCE\n{x.Evidence}\n\nACTION\n{x.ActionTaken}\n\nVERIFICATION\n{x.Verification}"
    :$"INCIDENTE #{x.Id}\n{x.CreatedAt:yyyy-MM-dd HH:mm:ss}\nServidor: {x.ServerName}\nBase: {x.DatabaseName}\nMódulo: {x.Module}\nSeveridad: {x.Severity}\nEstado: {x.Status}\nRecurrencias: {x.RecurrenceCount}\n\nPROBLEMA\n{x.Problem}\n\nCAUSA\n{x.Cause}\n\nEVIDENCIA\n{x.Evidence}\n\nACCIÓN\n{x.ActionTaken}\n\nVERIFICACIÓN\n{x.Verification}";
 }
 private async void ResolveButton_Click(object sender,RoutedEventArgs e)
 {
   if(IncidentGrid.SelectedItem is not IncidentRecord x || x.Status!="OPEN") return;
   var action=ActionBox.Text.Trim(); var verification=VerificationBox.Text.Trim();
   if(string.IsNullOrWhiteSpace(action)||string.IsNullOrWhiteSpace(verification)){MessageBox.Show(En?"Enter the action performed and verification before resolving the incident.":"Ingresá Acción realizada y Verificación antes de resolver el incidente.","DBACHECK",MessageBoxButton.OK,MessageBoxImage.Information);return;}
   var msg=En?$"Resolve Incident #{x.Id}?\n\nThis only updates local history; it does not execute actions on the database.":$"¿Resolver Incidente #{x.Id}?\n\nLa operación sólo actualiza el historial local; no ejecuta acciones sobre la base.";
   var ok=MessageBox.Show(msg,En?"Confirm resolution":"Confirmar resolución",MessageBoxButton.YesNo,MessageBoxImage.Question);
   if(ok!=MessageBoxResult.Yes)return;
   await _history.ResolveAsync(x.Id,action,verification); await LoadAsync(); DetailText.Text=En?$"Incident #{x.Id} marked RESOLVED.\n\nACTION\n{action}\n\nVERIFICATION\n{verification}":$"Incidente #{x.Id} marcado RESOLVED.\n\nACCIÓN\n{action}\n\nVERIFICACIÓN\n{verification}"; ActionBox.Clear();VerificationBox.Clear();ResolveButton.IsEnabled=false;
 }
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(ListRow,DetailRow,ExpandDetailButton,125,105);
}