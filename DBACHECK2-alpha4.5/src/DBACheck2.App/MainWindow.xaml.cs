using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;
public partial class MainWindow : Window
{
    public MainWindow()=>InitializeComponent();
    private SqlHealthService Service()=>new(ServerBox.Text.Trim(),TrustCertBox.IsChecked==true);

    private async void TestButton_Click(object sender,RoutedEventArgs e)
    {
        try { SetBusy(true,"Probando conexión..."); StatusText.Text="✓ "+await Service().TestAsync(); }
        catch(Exception ex) { StatusText.Text="✗ "+ex.Message; }
        finally { SetBusy(false); }
    }

    private async void QuickButton_Click(object sender,RoutedEventArgs e)
    {
        var sw=Stopwatch.StartNew();
        try {
            SetBusy(true,"Ejecutando Quick Check...");
            var data=await Service().QuickCheckAsync();
            HealthGrid.ItemsSource=data;
            var critical=data.Count(x=>x.Status=="CRITICAL");
            var warnings=data.Count(x=>x.Status=="WARNING");
            var errors=data.Count(x=>x.Status=="ERROR");
            var ok=data.Count(x=>x.Status=="OK");
            StatusText.Text=$"Quick Check finalizado en {sw.Elapsed.TotalSeconds:0.0}s | {data.Count} áreas | OK {ok} | Warning {warnings} | Critical {critical} | Error {errors}";
        }
        catch(Exception ex) { StatusText.Text="✗ Quick Check: "+ex.Message; }
        finally { sw.Stop(); SetBusy(false); }
    }

    private void IncidentButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new TransactionAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void BlockingButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new BlockingAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void TempDbButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new TempDbAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void LogButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new LogAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void BackupJobsButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new BackupJobsAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void AlwaysOnButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new AlwaysOnAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void PerformanceButton_Click(object sender,RoutedEventArgs e)
    {
        var w = new PerformanceAnalyzerWindow(Service()) { Owner = this };
        w.ShowDialog();
    }

    private void HealthGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(HealthGrid.SelectedItem is HealthItem item)
            DetailText.Text=$"{item.Status} | {item.Area}\n{item.Summary}\n\n{item.Detail}";
    }

    private void SetBusy(bool busy,string? text=null)
    {
        TestButton.IsEnabled=!busy; QuickButton.IsEnabled=!busy; IncidentButton.IsEnabled=!busy; BlockingButton.IsEnabled=!busy; TempDbButton.IsEnabled=!busy; LogButton.IsEnabled=!busy; BackupJobsButton.IsEnabled=!busy; AlwaysOnButton.IsEnabled=!busy; PerformanceButton.IsEnabled=!busy;
        if(text!=null) StatusText.Text=text;
    }
}