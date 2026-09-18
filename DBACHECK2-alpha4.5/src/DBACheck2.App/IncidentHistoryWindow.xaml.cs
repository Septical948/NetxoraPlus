using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;
namespace DBACheck2.App;
public partial class IncidentHistoryWindow:Window
{
 private readonly IncidentHistoryService _history;
 public IncidentHistoryWindow(IncidentHistoryService history){InitializeComponent();_history=history;Loaded+=async(_,__)=>await LoadAsync();}
 private async Task LoadAsync(){var data=await _history.ListAsync();IncidentGrid.ItemsSource=data;SummaryText.Text=$"{data.Count} incidente(s) almacenado(s) | Alpha 4.7.1 | SQLite local: {_history.DatabasePath}";}
 private void IncidentGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
 {
   if(IncidentGrid.SelectedItem is not IncidentRecord x){ResolveButton.IsEnabled=false;return;}
   ResolveButton.IsEnabled=x.Status=="OPEN"; ActionBox.Text=x.ActionTaken; VerificationBox.Text=x.Verification;
   DetailText.Text=$"INCIDENT #{x.Id}\n{x.CreatedAt:yyyy-MM-dd HH:mm:ss}\nServidor: {x.ServerName}\nDatabase: {x.DatabaseName}\nMódulo: {x.Module}\nSeveridad: {x.Severity}\nEstado: {x.Status}\nRecurrencias: {x.RecurrenceCount}\n\nPROBLEMA\n{x.Problem}\n\nCAUSA\n{x.Cause}\n\nEVIDENCIA\n{x.Evidence}\n\nACCIÓN\n{x.ActionTaken}\n\nVERIFICACIÓN\n{x.Verification}";
 }
 private async void ResolveButton_Click(object sender,RoutedEventArgs e)
 {
   if(IncidentGrid.SelectedItem is not IncidentRecord x || x.Status!="OPEN") return;
   var action=ActionBox.Text.Trim(); var verification=VerificationBox.Text.Trim();
   if(string.IsNullOrWhiteSpace(action)||string.IsNullOrWhiteSpace(verification)){MessageBox.Show("Ingresá Acción realizada y Verificación antes de resolver el incidente.","DBACHECK",MessageBoxButton.OK,MessageBoxImage.Information);return;}
   var ok=MessageBox.Show($"Resolver Incident #{x.Id}?\n\nLa operación sólo actualiza el historial local; no ejecuta acciones sobre SQL Server.","Confirmar resolución",MessageBoxButton.YesNo,MessageBoxImage.Question);
   if(ok!=MessageBoxResult.Yes)return;
   await _history.ResolveAsync(x.Id,action,verification); await LoadAsync(); DetailText.Text=$"Incident #{x.Id} marcado RESOLVED.\n\nACCIÓN\n{action}\n\nVERIFICACIÓN\n{verification}"; ActionBox.Clear();VerificationBox.Clear();ResolveButton.IsEnabled=false;
 }
}