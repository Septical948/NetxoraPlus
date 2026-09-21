using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Providers;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class AssistantProfilesWindow:Window
{
    private readonly ServerProfileService _profiles=new();
    private List<ServerProfile> _items=new();
    public event Action<ServerProfile>? ConnectRequested;

    public AssistantProfilesWindow(string currentHost)
    {
        InitializeComponent(); ApplyLanguage(); HostBox.Text=currentHost;
        EngineBox.ItemsSource=Enum.GetValues<DatabaseEngine>(); EngineBox.SelectedItem=DatabaseEngine.SqlServer; OracleModeBox.ItemsSource=Enum.GetValues<OracleConnectionMode>(); OracleModeBox.SelectedItem=OracleConnectionMode.Auto;
        PortBox.TextChanged+=(_,__)=>UpdateHints(); DatabaseBox.TextChanged+=(_,__)=>UpdateHints(); UsernameBox.TextChanged+=(_,__)=>UpdateHints();
        EngineBox.SelectionChanged+=(_,__)=>{UpdateHints();UpdateEngineOptions();}; Loaded+=async(_,__)=>{UpdateHints();UpdateEngineOptions();await Refresh();};
    }

    private void ApplyLanguage(){var en=LocalizationService.Current==AppLanguage.En;TitleText.Text=LocalizationService.T("Profiles.ConnectionTitle");SubtitleText.Text=$"Beta 1 · Multi-engine · {LocalizationService.T("Common.ReadOnly")}";SaveButton.Content=LocalizationService.T("Profiles.Save");LoadButton.Content=LocalizationService.T("Profiles.Load");DeleteButton.Content=en?"DELETE":"ELIMINAR";UseButton.Content=LocalizationService.T("Profiles.Connect");TestProfileButton.Content=LocalizationService.T("Profiles.Test");PasswordHint.Text=LocalizationService.T("Profiles.PasswordRuntime");SavedProfileLabel.Text=LocalizationService.T("Profiles.SavedProfile");ProfileLabel.Text=LocalizationService.T("Profiles.Profile");EngineLabel.Text=LocalizationService.T("Profiles.Engine");HostLabel.Text=LocalizationService.T("Profiles.Host");PortLabel.Text=LocalizationService.T("Profiles.Port");DatabaseLabel.Text=LocalizationService.T("Profiles.Database");UsernameLabel.Text=LocalizationService.T("Profiles.Username");PasswordLabel.Text=en?"PASSWORD":"CONTRASEÑA";CredentialHelpText.Text=en?"Credentials are used by the selected profile.":"Las credenciales son utilizadas por el perfil seleccionado.";ConnectionStatusLabel.Text=LocalizationService.T("Profiles.ConnectionStatus");RememberPasswordBox.Content=en?"Remember password on this computer":"Recordar contraseña en este equipo";PasswordSecurityText.Text=en?"Protected with Windows DPAPI for the current Windows user.":"Protegida con Windows DPAPI para el usuario actual.";OracleModeLabel.Text=en?"ORACLE COMPATIBILITY":"COMPATIBILIDAD ORACLE";OracleModeHelp.Text=en?"Auto: modern ODP.NET first, then legacy OraOLEDB.":"Auto: primero ODP.NET moderno y luego OraOLEDB legacy.";}
    private void UpdateHints(){PortHint.Visibility=string.IsNullOrWhiteSpace(PortBox.Text)?Visibility.Visible:Visibility.Collapsed;DatabaseHint.Text=(EngineBox.SelectedItem is DatabaseEngine.Oracle)?LocalizationService.T("Profiles.ServiceSid"):"Database";DatabaseHint.Visibility=string.IsNullOrWhiteSpace(DatabaseBox.Text)?Visibility.Visible:Visibility.Collapsed;UsernameHint.Visibility=string.IsNullOrWhiteSpace(UsernameBox.Text)?Visibility.Visible:Visibility.Collapsed;}
    private void UpdateEngineOptions(){var e=(DatabaseEngine)(EngineBox.SelectedItem??DatabaseEngine.SqlServer);SshPanel.Visibility=e is DatabaseEngine.MySqlMariaDb or DatabaseEngine.PostgreSql?Visibility.Visible:Visibility.Collapsed;OracleModePanel.Visibility=e==DatabaseEngine.Oracle?Visibility.Visible:Visibility.Collapsed;if(e==DatabaseEngine.MySqlMariaDb&&string.IsNullOrWhiteSpace(PortBox.Text))PortBox.Text="3306";else if(e==DatabaseEngine.PostgreSql&&string.IsNullOrWhiteSpace(PortBox.Text))PortBox.Text="5432";else if(e==DatabaseEngine.Oracle&&string.IsNullOrWhiteSpace(PortBox.Text))PortBox.Text="1521";}
    private void PasswordBox_PasswordChanged(object s,RoutedEventArgs e)=>PasswordHint.Visibility=string.IsNullOrEmpty(PasswordBox.Password)?Visibility.Visible:Visibility.Collapsed;
    private void BrowseKey_Click(object s,RoutedEventArgs e){var d=new Microsoft.Win32.OpenFileDialog{Title=LocalizationService.Current==AppLanguage.En?"Select SSH private key":"Seleccionar clave privada SSH",Filter="SSH private keys (*.ppk;*.pem;*.key)|*.ppk;*.pem;*.key|PuTTY keys (*.ppk)|*.ppk|All files (*.*)|*.*",CheckFileExists=true,Multiselect=false};if(d.ShowDialog()==true)SshKeyPathBox.Text=d.FileName;}

    private ServerProfile Current()=>new(){Name=ProfileName.Text.Trim(),Engine=(DatabaseEngine)(EngineBox.SelectedItem??DatabaseEngine.SqlServer),Host=HostBox.Text.Trim(),Port=int.TryParse(PortBox.Text,out var p)?p:null,DatabaseOrService=DatabaseBox.Text.Trim(),Username=UsernameBox.Text.Trim(),Password=PasswordBox.Password,RememberPassword=RememberPasswordBox.IsChecked==true,OracleMode=(OracleConnectionMode)(OracleModeBox.SelectedItem??OracleConnectionMode.Auto),UseSshTunnel=UseSshBox.IsChecked==true,SshHost=SshHostBox.Text.Trim(),SshPort=int.TryParse(SshPortBox.Text,out var sp)?sp:22,SshUsername=SshUserBox.Text.Trim(),SshPrivateKeyPath=SshKeyPathBox.Text.Trim(),SshKeyPassphrase=SshPassphraseBox.Password,Environment="PROD",Authentication=(DatabaseEngine)(EngineBox.SelectedItem??DatabaseEngine.SqlServer)==DatabaseEngine.SqlServer?"Windows":"Database",TrustCertificate=true};

    private async Task Refresh()
    {
        _items=await _profiles.LoadAsync(); ProfilesBox.ItemsSource=null;ProfilesBox.ItemsSource=_items;
        OutputText.Foreground=System.Windows.Media.Brushes.White;
        OutputText.Text=LocalizationService.Current==AppLanguage.En?$"Profiles: {_items.Count}\nStorage: {_profiles.PathName}\n\nSelect a profile to load its connection settings.":$"Perfiles: {_items.Count}\nAlmacenamiento: {_profiles.PathName}\n\nSeleccioná un perfil para cargar sus datos de conexión.";
    }

    private async void Save_Click(object s,RoutedEventArgs e)
    {
        var p=Current(); if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Host)){OutputText.Text=LocalizationService.T("Profiles.Required");return;}
        var old=_items.FirstOrDefault(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));
        if(p.RememberPassword&&string.IsNullOrEmpty(p.Password)&&old?.RememberPassword==true)p.Password=old.Password;
        _items.RemoveAll(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));_items.Add(p);
        await _profiles.SaveAsync(_items);await Refresh();ProfilesBox.SelectedItem=_items.FirstOrDefault(x=>x.Name==p.Name);
        OutputText.Text=LocalizationService.Current==AppLanguage.En?"Profile saved.":"Perfil guardado.";
    }

    private async void Delete_Click(object s,RoutedEventArgs e)
    {
        if(ProfilesBox.SelectedItem is not ServerProfile p)return;
        await _profiles.DeleteAsync(p.Name); ClearEditor(); await Refresh();
        OutputText.Text=LocalizationService.Current==AppLanguage.En?$"Profile '{p.Name}' deleted.":$"Perfil '{p.Name}' eliminado.";
    }
    private void ClearEditor(){ProfilesBox.SelectedItem=null;ProfileName.Clear();HostBox.Text="localhost";PortBox.Clear();DatabaseBox.Clear();UsernameBox.Clear();PasswordBox.Clear();RememberPasswordBox.IsChecked=false;OracleModeBox.SelectedItem=OracleConnectionMode.Auto;UseSshBox.IsChecked=false;SshHostBox.Clear();SshPortBox.Text="22";SshUserBox.Clear();SshKeyPathBox.Clear();SshPassphraseBox.Clear();}
    private void Load_Click(object s,RoutedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
    private void ProfilesBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
    private void Apply(ServerProfile p){ProfileName.Text=p.Name;EngineBox.SelectedItem=p.Engine;HostBox.Text=p.Host;PortBox.Text=p.Port?.ToString()??"";DatabaseBox.Text=p.DatabaseOrService;UsernameBox.Text=p.Username??"";PasswordBox.Password=p.Password??"";RememberPasswordBox.IsChecked=p.RememberPassword;OracleModeBox.SelectedItem=p.OracleMode;UseSshBox.IsChecked=p.UseSshTunnel;SshHostBox.Text=p.SshHost??"";SshPortBox.Text=p.SshPort?.ToString()??"22";SshUserBox.Text=p.SshUsername??"";SshKeyPathBox.Text=p.SshPrivateKeyPath??"";SshPassphraseBox.Clear();UpdateEngineOptions();}

    private void UseProfile_Click(object s,RoutedEventArgs e){var p=Current();if(string.IsNullOrWhiteSpace(p.Host)){OutputText.Text=LocalizationService.T("Profiles.HostRequired");return;}ConnectRequested?.Invoke(p);}
    public void SetConnectionProgress(string message){OutputText.Foreground=System.Windows.Media.Brushes.White;OutputText.Text=message;UseButton.IsEnabled=false;TestProfileButton.IsEnabled=false;}
    public void SetConnectionSuccess(string message){OutputText.Foreground=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#77E6CE")!;OutputText.Text=message;UseButton.IsEnabled=true;TestProfileButton.IsEnabled=true;}
    public void SetConnectionError(string message){OutputText.Foreground=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF7B8A")!;OutputText.Text=message;UseButton.IsEnabled=true;TestProfileButton.IsEnabled=true;}
    private async void Test_Click(object s,RoutedEventArgs e){try{OutputText.Text=LocalizationService.T("Profiles.Testing");OutputText.Text=await DatabaseProviderFactory.Create(Current()).TestAsync();}catch(Exception ex){SetConnectionError($"ERROR: {ex.Message}");}}
}