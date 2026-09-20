using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Providers;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class AssistantProfilesWindow:Window
{
    private readonly ServerProfileService _profiles=new();
    private readonly DbaAssistantService _assistant=new();
    private List<ServerProfile> _items=new();
    public event Action<ServerProfile>? ProfileActivated;

    public AssistantProfilesWindow(string currentHost)
    {
        InitializeComponent();
        HostBox.Text=currentHost;
        EngineBox.ItemsSource=Enum.GetValues<DatabaseEngine>();
        EngineBox.SelectedItem=DatabaseEngine.SqlServer;
        PortBox.TextChanged+=(_,__)=>UpdateHints(); DatabaseBox.TextChanged+=(_,__)=>UpdateHints(); UsernameBox.TextChanged+=(_,__)=>UpdateHints();
        EngineBox.SelectionChanged+=(_,__)=>UpdateHints();
        Loaded+=async(_,__)=>{UpdateHints();await Refresh();};
    }

    private void UpdateHints(){PortHint.Visibility=string.IsNullOrWhiteSpace(PortBox.Text)?Visibility.Visible:Visibility.Collapsed;DatabaseHint.Text=(EngineBox.SelectedItem is DatabaseEngine.Oracle)?"Service Name / SID":"Database";DatabaseHint.Visibility=string.IsNullOrWhiteSpace(DatabaseBox.Text)?Visibility.Visible:Visibility.Collapsed;UsernameHint.Visibility=string.IsNullOrWhiteSpace(UsernameBox.Text)?Visibility.Visible:Visibility.Collapsed;}

    private void PasswordBox_PasswordChanged(object s,RoutedEventArgs e)=>PasswordHint.Visibility=string.IsNullOrEmpty(PasswordBox.Password)?Visibility.Visible:Visibility.Collapsed;

    private ServerProfile Current()=>new(){Name=ProfileName.Text.Trim(),Engine=(DatabaseEngine)(EngineBox.SelectedItem??DatabaseEngine.SqlServer),Host=HostBox.Text.Trim(),Port=int.TryParse(PortBox.Text,out var p)?p:null,DatabaseOrService=DatabaseBox.Text.Trim(),Username=UsernameBox.Text.Trim(),Password=PasswordBox.Password,Environment="PROD",Authentication=(DatabaseEngine)(EngineBox.SelectedItem??DatabaseEngine.SqlServer)==DatabaseEngine.SqlServer?"Windows":"Database",TrustCertificate=true};

    private async Task Refresh()
    {
        _items=await _profiles.LoadAsync();
        ProfilesBox.ItemsSource=null;ProfilesBox.ItemsSource=_items;
        OutputText.Text=$"Profiles: {_items.Count}\nStorage: {_profiles.PathName}\n\nSeleccioná USAR PERFIL para convertirlo en el contexto global de DBACHECK.";
    }

    private async void Save_Click(object s,RoutedEventArgs e)
    {
        var p=Current();
        if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Host)){OutputText.Text="Nombre y Host son obligatorios.";return;}
        _items.RemoveAll(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));_items.Add(p);
        await _profiles.SaveAsync(_items);await Refresh();ProfilesBox.SelectedItem=_items.FirstOrDefault(x=>x.Name==p.Name);
    }

    private void Load_Click(object s,RoutedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
    private void ProfilesBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
    private void Apply(ServerProfile p){ProfileName.Text=p.Name;EngineBox.SelectedItem=p.Engine;HostBox.Text=p.Host;PortBox.Text=p.Port?.ToString()??"";DatabaseBox.Text=p.DatabaseOrService;UsernameBox.Text=p.Username??"";PasswordBox.Clear();}

    private void UseProfile_Click(object s,RoutedEventArgs e)
    {
        var p=Current();
        if(string.IsNullOrWhiteSpace(p.Host)){OutputText.Text="Host obligatorio.";return;}
        ProfileActivated?.Invoke(p);
        OutputText.Text=$"PERFIL GLOBAL ACTIVO\n{p.Display}\n\nEl contexto superior de DBACHECK fue actualizado.";
    }

    private async void Test_Click(object s,RoutedEventArgs e){try{OutputText.Text="Probando conexión...";OutputText.Text=await DatabaseProviderFactory.Create(Current()).TestAsync();}catch(Exception ex){OutputText.Text=$"ERROR: {ex.Message}";}}
    private async void Ask_Click(object s,RoutedEventArgs e){try{OutputText.Text="Recolectando evidencia...";OutputText.Text=await _assistant.AskAsync(Current(),QuestionBox.Text);}catch(Exception ex){OutputText.Text=$"ERROR: {ex.Message}";}}
}