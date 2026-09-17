using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;
namespace DBACheck2.App;
public partial class IncidentHistoryWindow:Window
{
 private readonly IncidentHistoryService _history;
 public IncidentHistoryWindow(IncidentHistoryService history){InitializeComponent();_history=history;Loaded+=async(_,__)=>await LoadAsync();}
 private async Task LoadAsync(){var data=await _history.ListAsync();IncidentGrid.ItemsSource=data;SummaryText.Text=$"{data.Count} incidente(s) almacenado(s) | SQLite local: {_history.DatabasePath}";}
 private void IncidentGrid_SelectionChanged(object sender,SelectionChangedEventArgs e){if(IncidentGrid.SelectedItem is not IncidentRecord x)return;DetailText.Text=$"INCIDENT #{x.Id}\n{x.CreatedAt:yyyy-MM-dd HH:mm:ss}\nServidor: {x.ServerName}\nDatabase: {x.DatabaseName}\nMódulo: {x.Module}\nSeveridad: {x.Severity}\nEstado: {x.Status}\nRecurrencias: {x.RecurrenceCount}\n\nPROBLEMA\n{x.Problem}\n\nCAUSA\n{x.Cause}\n\nEVIDENCIA\n{x.Evidence}\n\nACCIÓN\n{x.ActionTaken}\n\nVERIFICACIÓN\n{x.Verification}";}
}