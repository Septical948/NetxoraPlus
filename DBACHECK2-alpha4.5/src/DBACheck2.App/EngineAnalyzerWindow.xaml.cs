using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Providers;
using DBACheck2.App.Services;

namespace DBACheck2.App;
public partial class EngineAnalyzerWindow:Window
{
    readonly IDatabaseProvider provider; readonly string module;
    bool En=>LocalizationService.Current==AppLanguage.En;
    public EngineAnalyzerWindow(IDatabaseProvider provider,string module,string title){InitializeComponent();this.provider=provider;this.module=module;TitleText.Text=title;RefreshButton.Content=LocalizationService.Current==AppLanguage.En?"REFRESH":"ACTUALIZAR";Loaded+=async(_,__)=>await LoadAsync();}
    async Task LoadAsync(){try{ContextText.Text=En?$"{provider.DisplayName} | querying...":$"{provider.DisplayName} | consultando...";var all=await provider.QuickCheckAsync();var rows=Filter(all,module).ToList();GridData.ItemsSource=rows;ContextText.Text=$"{provider.DisplayName} | {rows.Count} check(s) | {LocalizationService.T("Common.ReadOnly")}";}catch(Exception ex){ContextText.Text="ERROR: "+ex.Message;}}
    static IEnumerable<HealthItem> Filter(IEnumerable<HealthItem> x,string m){var a=m switch{"transactions"=>new[]{"TRANSACTIONS","LONG QUERIES"},"blocking"=>new[]{"BLOCKING","LOCKS"},"temp"=>new[]{"TEMP","TEMP USAGE","VACUUM","UNDO"},"log"=>new[]{"LOG","WAL","ARCHIVELOG","REDO","BINLOG"},"backup"=>new[]{"BACKUPS","ARCHIVELOG","MAINTENANCE"},"ha"=>new[]{"REPLICATION","DATAGUARD","ARCHIVELOG"},"performance"=>new[]{"SESSIONS","LONG QUERIES","PERFORMANCE","WAITS","DATABASE SIZE","TABLESPACE","INDEXES","VACUUM"},_=>Array.Empty<string>()};return x.Where(i=>a.Contains(i.Area,StringComparer.OrdinalIgnoreCase));}
    async void Refresh_Click(object s,RoutedEventArgs e)=>await LoadAsync();
    void GridData_SelectionChanged(object s,SelectionChangedEventArgs e){if(GridData.SelectedItem is HealthItem i)DetailText.Text=$"{i.Status} | {i.Area}\n{i.Summary}\n\n{i.Detail}";}
    private void ExpandDetailButton_Click(object sender,RoutedEventArgs e)=>DetailPanelService.Toggle(DetailRow,ExpandDetailButton);
}
