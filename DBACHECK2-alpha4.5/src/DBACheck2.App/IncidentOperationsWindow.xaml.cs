using System.Windows;using System.Windows.Controls;using DBACheck2.App.Models;using DBACheck2.App.Services;
namespace DBACheck2.App;
public partial class IncidentOperationsWindow:Window
{
 private readonly IncidentOperationsService _ops; private readonly SqlHealthService _sql;
 public IncidentOperationsWindow(SqlHealthService sql){InitializeComponent();_sql=sql;_ops=new(sql);Loaded+=async(_,__)=>await LoadAsync();}
 private IncidentOperationSummary? Selected=>GridIncidents.SelectedItem as IncidentOperationSummary;
 private async Task LoadAsync(){var x=await _ops.OpenAsync();GridIncidents.ItemsSource=x;SummaryText.Text=$"Alpha 5.0 · {x.Count} incidente(s) OPEN · Incident Center + Evidence + Safe Playbooks";}
 private void GridIncidents_SelectionChanged(object s,SelectionChangedEventArgs e){var on=Selected!=null;AnalyzeButton.IsEnabled=ReportButton.IsEnabled=VerifyButton.IsEnabled=on;JobButton.IsEnabled=on&&Selected!.Module=="Transaction Log";}
 private async void AnalyzeButton_Click(object s,RoutedEventArgs e){if(Selected is{}x)DetailText.Text=await _ops.BuildIncidentCenterAsync(x);}
 private async void ReportButton_Click(object s,RoutedEventArgs e){if(Selected is{}x){var t=await _ops.BuildTechnicalReportAsync(x);DetailText.Text=t;Clipboard.SetText(t);StatusText.Text="Reporte técnico copiado al portapapeles.";}}
 private async void VerifyButton_Click(object s,RoutedEventArgs e){if(Selected is not{}x)return;if(x.Module=="Transaction Log"){var logs=await _sql.GetLogDatabasesAsync();var z=logs.FirstOrDefault(a=>a.DatabaseName.Equals(x.DatabaseName,StringComparison.OrdinalIgnoreCase));DetailText.Text=z==null?"Database no encontrada.":$"REVALIDACIÓN - {z.DatabaseName}\nStatus: {z.Status}\nUsed: {z.UsedPct:0.0}%\nReuse Wait: {z.ReuseWait}\n\nREAD - no se ejecutó acción.";}}
 private void JobButton_Click(object s,RoutedEventArgs e){if(Selected is not{}x)return;DetailText.Text=$"SAFE PLAYBOOK - SQL AGENT JOB\nDatabase: {x.DatabaseName}\n\nAlpha 5.0 no ejecuta un job ambiguo automáticamente.\n1. Revisar correlación Job/Step.\n2. Seleccionar explícitamente el job correcto en Backups / Jobs.\n3. Capturar evidencia.\n4. Confirmar ejecución manual/controlada.\n5. Revalidar el incidente.\n\nSAFETY: no se ejecutó ningún job.";StatusText.Text="Playbook preparado; ninguna acción ejecutada.";}
}