using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DBACheck2.App.Models;
using DBACheck2.App.Services;
using DBACheck2.App.Providers;

namespace DBACheck2.App;

public partial class MainWindow : Window
{
    private Window? _hostedAnalyzer;
    private DatabaseEngine _activeEngine=DatabaseEngine.SqlServer;
    private string _activeProfileName="Manual";
    private ServerProfile? _activeProfile;

    public MainWindow()
    {
        InitializeComponent();
        LanguageBox.SelectedIndex=LocalizationService.Current==AppLanguage.Es?0:1;
        ApplyLanguage();
        ServerBox.TextChanged += (_,__) => {
            TargetContextText.Text=string.IsNullOrWhiteSpace(ServerBox.Text)?"Sin destino":ServerBox.Text.Trim();
            SetConnectionState("NO VERIFICADA","#263244","#B7C3D7");
        };
    }

    private SqlHealthService Service()=>new(ServerBox.Text.Trim(),TrustCertBox.IsChecked==true);
    private SqlCompatibilityCollectorService Compat()=>new(ServerBox.Text.Trim(),TrustCertBox.IsChecked==true);

    private bool ValidateTarget()
    {
        if(!string.IsNullOrWhiteSpace(ServerBox.Text)) return true;
        StatusText.Text="ERROR: indicá servidor, instancia o IP antes de continuar."; ServerBox.Focus();
        SetConnectionState("SIN DESTINO","#5A2A2A","#FFD1D1"); return false;
    }

    private async void TestButton_Click(object sender,RoutedEventArgs e)
    {
        if(!ValidateTarget()) return;
        try {
            SetBusy(true,$"Probando conexión con {ServerBox.Text.Trim()}..."); SetConnectionState("CONECTANDO","#4A4120","#FFE69A");
            if(_activeProfile is not null && _activeProfile.Engine!=DatabaseEngine.SqlServer) {
                var provider=DatabaseProviderFactory.Create(_activeProfile);
                var info=await provider.TestAsync();
                StatusText.Text=$"✓ {_activeProfile.Name} | {provider.DisplayName} | {info.Replace("\n"," | ")}";
                SetConnectionState($"CONECTADO · {provider.DisplayName.ToUpperInvariant()}","#173D35","#77E6CE");
            } else {
                var caps=await Compat().DetectAsync();
                StatusText.Text=$"✓ {caps.ServerName} | {caps.VersionLabel} ({caps.ProductVersion}) | {caps.Edition} | Perfil {caps.Profile.ToString().ToUpperInvariant()}";
                SetConnectionState($"CONECTADO · {caps.Profile.ToString().ToUpperInvariant()}","#173D35","#77E6CE");
            }
        } catch(Exception ex) { StatusText.Text=$"✗ Destino: {ServerBox.Text.Trim()} | {ex.Message}"; SetConnectionState("ERROR","#5A2A2A","#FFD1D1"); }
        finally { SetBusy(false); }
    }

    private async void QuickButton_Click(object sender,RoutedEventArgs e)
    {
        if(!ValidateTarget()) return; ShowQuickCheck(); var sw=Stopwatch.StartNew();
        try {
            SetBusy(true,$"Ejecutando Quick Check en {ServerBox.Text.Trim()}...");
            List<HealthItem> data; string engineInfo;
            if(_activeProfile is not null && _activeProfile.Engine!=DatabaseEngine.SqlServer) {
                var provider=DatabaseProviderFactory.Create(_activeProfile); data=await provider.QuickCheckAsync(); engineInfo=provider.DisplayName;
            } else {
                var compat=Compat(); var caps=await compat.DetectAsync(); data=await compat.QuickCheckAsync(); engineInfo=$"{caps.VersionLabel} | {caps.Profile.ToString().ToUpperInvariant()}";
            }
            HealthGrid.ItemsSource=data;
            var critical=data.Count(x=>x.Status=="CRITICAL"); var warnings=data.Count(x=>x.Status=="WARNING"); var errors=data.Count(x=>x.Status=="ERROR"); var ok=data.Count(x=>x.Status=="OK");
            StatusText.Text=$"Destino: {ServerBox.Text.Trim()} | {engineInfo} | {sw.Elapsed.TotalSeconds:0.0}s | {data.Count} áreas | OK {ok} | Warning {warnings} | Critical {critical} | Error {errors}";
        } catch(Exception ex) { StatusText.Text=$"✗ Destino: {ServerBox.Text.Trim()} | Quick Check: {ex.Message}"; }
        finally { sw.Stop(); SetBusy(false); }
    }

    private void ShowQuickCheck(){ReleaseHostedAnalyzer();ModuleHost.Visibility=Visibility.Collapsed;QuickCheckView.Visibility=Visibility.Visible;}
    private void HostAnalyzer(Window analyzer){ReleaseHostedAnalyzer();QuickCheckView.Visibility=Visibility.Collapsed;ModuleHost.Visibility=Visibility.Visible;var content=analyzer.Content as UIElement;analyzer.Content=null;_hostedAnalyzer=analyzer;ModuleHost.Content=content;analyzer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));}
    private void ReleaseHostedAnalyzer(){ModuleHost.Content=null;_hostedAnalyzer=null;}

    private bool IsSql=>_activeProfile is null||_activeProfile.Engine==DatabaseEngine.SqlServer;
    private IDatabaseProvider ActiveProvider()=>DatabaseProviderFactory.Create(_activeProfile??new ServerProfile{Engine=DatabaseEngine.SqlServer,Host=ServerBox.Text.Trim(),TrustCertificate=TrustCertBox.IsChecked==true});
    private void OpenEngine(string module,string title,Func<Window> sql){if(!ValidateTarget())return;if(IsSql)HostAnalyzer(sql());else HostAnalyzer(new EngineAnalyzerWindow(ActiveProvider(),module,title));}
    private void IncidentButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("transactions",IncidentButton.Content.ToString()!,()=>new TransactionAnalyzerWindow(Service(),Compat()));
    private void BlockingButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("blocking",BlockingButton.Content.ToString()!,()=>new BlockingAnalyzerWindow(Service(),Compat()));
    private void TempDbButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("temp",TempDbButton.Content.ToString()!,()=>new TempDbAnalyzerWindow(Service()));
    private void LogButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("log",LogButton.Content.ToString()!,()=>new LogAnalyzerWindow(Service()));
    private void BackupJobsButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("backup",BackupJobsButton.Content.ToString()!,()=>new BackupJobsAnalyzerWindow(Service()));
    private void AlwaysOnButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("ha",AlwaysOnButton.Content.ToString()!,()=>new AlwaysOnAnalyzerWindow(Service()));
    private void PerformanceButton_Click(object sender,RoutedEventArgs e)=>OpenEngine("performance",PerformanceButton.Content.ToString()!,()=>new PerformanceAnalyzerWindow(Service(),Compat()));
    private void HistoryButton_Click(object sender,RoutedEventArgs e){HostAnalyzer(new IncidentHistoryWindow(new IncidentHistoryService()));}
    private void OperationsButton_Click(object sender,RoutedEventArgs e){if(!ValidateTarget())return;HostAnalyzer(new IncidentOperationsWindow(Service()));}
    private void AssistantButton_Click(object sender,RoutedEventArgs e)
    {
        var window=new AssistantProfilesWindow(ServerBox.Text.Trim());
        window.ProfileActivated+=ActivateProfile;
        HostAnalyzer(window);
    }

    private void ActivateProfile(ServerProfile profile)
    {
        _activeProfile=profile;
        _activeEngine=profile.Engine;
        _activeProfileName=string.IsNullOrWhiteSpace(profile.Name)?"Manual":profile.Name;
        ServerBox.Text=profile.Host;
        TrustCertBox.IsChecked=profile.TrustCertificate;
        TargetContextText.Text=$"{_activeProfileName} | {profile.Engine} | {profile.Host}";
        StatusText.Text=$"Perfil global activo: {_activeProfileName} | {profile.Engine} | {profile.Host}";
        ApplyEngineNavigation(profile.Engine);
        if(profile.Engine==DatabaseEngine.SqlServer) SetConnectionState("NO VERIFICADA","#263244","#B7C3D7");
        else SetConnectionState($"{profile.Engine} · NO VERIFICADA","#263244","#B7C3D7");
    }

    private void LanguageBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!IsLoaded && LanguageBox.SelectedItem is null)return;
        var lang=LanguageBox.SelectedIndex==1?AppLanguage.En:AppLanguage.Es;
        LocalizationService.Set(lang); ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        if(SubtitleText is null)return;
        SubtitleText.Text=LocalizationService.T("App.Subtitle"); TargetLabelText.Text=LocalizationService.T("Connection.Target");
        TestButton.Content=LocalizationService.T("Connection.Test"); TrustCertBox.Content=LocalizationService.T("Connection.Trust");
        OperationLabelText.Text=LocalizationService.T("Nav.Operation"); QuickButton.Content=LocalizationService.T("Nav.Quick");
        HistoryButton.Content=LocalizationService.T("Nav.History"); OperationsButton.Content=LocalizationService.T("Nav.Operations"); AssistantButton.Content=LocalizationService.T("Nav.Assistant");
        CurrentContextLabelText.Text=LocalizationService.T("Context.Current"); AuthenticationText.Text=LocalizationService.T("Context.WindowsAuth");
        QuickTitleText.Text=LocalizationService.T("Quick.Title"); QuickSubtitleText.Text=LocalizationService.T("Quick.Subtitle");
        StatusColumn.Header=LocalizationService.T("Grid.Status"); AreaColumn.Header=LocalizationService.T("Grid.Area"); SummaryColumn.Header=LocalizationService.T("Grid.Summary"); EvidenceColumn.Header=LocalizationService.T("Grid.Evidence");
        EvidenceTitleText.Text=LocalizationService.T("Grid.EvidenceTitle"); LanguageLabelText.Text=LocalizationService.T("Language.Label");
        FooterText.Text=$"DBACHECK 2 Beta 1 | Multi-engine | {LocalizationService.T("Common.ReadOnly")}";
        ApplyEngineNavigation(_activeEngine);
    }

    private void ApplyEngineNavigation(DatabaseEngine e)
    {
        IncidentButton.Content=LocalizationService.T("Nav.LongTransactions"); BlockingButton.Content=LocalizationService.T("Nav.Blocking");
        if(e==DatabaseEngine.PostgreSql){TempDbButton.Content=LocalizationService.T("Nav.TempPg");LogButton.Content=LocalizationService.T("Nav.LogPg");BackupJobsButton.Content=LocalizationService.T("Nav.BackupPg");AlwaysOnButton.Content=LocalizationService.T("Nav.HaPg");PerformanceButton.Content=LocalizationService.T("Nav.PerfPg");}
        else if(e==DatabaseEngine.Oracle){TempDbButton.Content=LocalizationService.T("Nav.TempOracle");LogButton.Content=LocalizationService.T("Nav.LogOracle");BackupJobsButton.Content=LocalizationService.T("Nav.BackupOracle");AlwaysOnButton.Content=LocalizationService.T("Nav.HaOracle");PerformanceButton.Content=LocalizationService.T("Nav.PerfOracle");}
        else if(e==DatabaseEngine.MySqlMariaDb){TempDbButton.Content=LocalizationService.T("Nav.TempMySql");LogButton.Content=LocalizationService.T("Nav.LogMySql");BackupJobsButton.Content=LocalizationService.T("Nav.BackupMySql");AlwaysOnButton.Content=LocalizationService.T("Nav.HaMySql");PerformanceButton.Content=LocalizationService.T("Nav.PerfMySql");}
        else {TempDbButton.Content=LocalizationService.T("Nav.TempSql");LogButton.Content=LocalizationService.T("Nav.LogSql");BackupJobsButton.Content=LocalizationService.T("Nav.BackupSql");AlwaysOnButton.Content=LocalizationService.T("Nav.HaSql");PerformanceButton.Content=LocalizationService.T("Nav.PerfSql");}
    }

    private void HealthGrid_SelectionChanged(object sender,SelectionChangedEventArgs e){if(HealthGrid.SelectedItem is HealthItem item)DetailText.Text=$"{item.Status} | {item.Area}\n{item.Summary}\n\n{item.Detail}";}
    private void SetBusy(bool busy,string? text=null){TestButton.IsEnabled=!busy;QuickButton.IsEnabled=!busy;IncidentButton.IsEnabled=!busy;BlockingButton.IsEnabled=!busy;TempDbButton.IsEnabled=!busy;LogButton.IsEnabled=!busy;BackupJobsButton.IsEnabled=!busy;AlwaysOnButton.IsEnabled=!busy;PerformanceButton.IsEnabled=!busy;HistoryButton.IsEnabled=!busy;OperationsButton.IsEnabled=!busy;AssistantButton.IsEnabled=!busy;if(text!=null)StatusText.Text=text;}
    private void SetConnectionState(string text,string background,string foreground){ConnectionStateText.Text=text;ConnectionBadge.Background=(Brush)new BrushConverter().ConvertFromString(background)!;ConnectionStateText.Foreground=(Brush)new BrushConverter().ConvertFromString(foreground)!;}
}