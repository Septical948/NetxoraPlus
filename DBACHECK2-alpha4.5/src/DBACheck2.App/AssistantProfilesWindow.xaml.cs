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
        ApplyLanguage();
        HostBox.Text=currentHost;
        EngineBox.ItemsSource=Enum.GetValues<DatabaseEngine>();
        EngineBox.SelectedItem=DatabaseEngine.SqlServer;
        PortBox.TextChanged+=(_,__)=>UpdateHints(); DatabaseBox.TextChanged+=(_,__)=>UpdateHints(); UsernameBox.TextChanged+=(_,__)=>UpdateHints();
        EngineBox.SelectionChanged+=(_,__)=>UpdateHints();
        Loaded+=async(_,__)=>{UpdateHints();await Refresh();};
    }

    private void ApplyLanguage(){TitleText.Text=LocalizationService.T("Profiles.Title");SubtitleText.Text=$"Beta 1 · Multi-engine · {LocalizationService.T("Common.ReadOnly")}";SaveButton.Content=LocalizationService.T("Profiles.Save");LoadButton.Content=LocalizationService.T("Profiles.Load");UseButton.Content=LocalizationService.T("Profiles.Use");TestProfileButton.Content=LocalizationService.T("Profiles.Test");AskButton.Content=LocalizationService.T("Profiles.Ask");QuestionBox.Text=LocalizationService.T("Profiles.Question");PasswordHint.Text=LocalizationService.T("Profiles.PasswordRuntime");}
    private void UpdateHints(){PortHint.Visibility=string.IsNullOrWhiteSpace(PortBox.Text)?Visibility.Visible:Visibility.Collapsed;DatabaseHint.Text=(EngineBox.SelectedItem is DatabaseEngine.Oracle)?LocalizationService.T("Profiles.ServiceSid"):"Database";DatabaseHint.Visibility=string.IsNullOrWhiteSpace(DatabaseBox.Text)?Visibility.Visible:Visibility.Collapsed;UsernameHint.Visibility=string.IsNullOrWhiteSpace(UsernameBox.Text)?Visibility.Visible:Visibility.Collapsed;}

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
        if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Host)){OutputText.Text=LocalizationService.T("Profiles.Required");return;}
        _items.RemoveAll(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));_items.Add(p);
        await _profiles.SaveAsync(_items);await Refresh();ProfilesBox.SelectedItem=_items.FirstOrDefault(x=>x.Name==p.Name);
    }

    private void Load_Click(object s,RoutedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
    private void ProfilesBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
    private void Apply(ServerProfile p){ProfileName.Text=p.Name;EngineBox.SelectedItem=p.Engine;HostBox.Text=p.Host;PortBox.Text=p.Port?.ToString()??"";DatabaseBox.Text=p.DatabaseOrService;UsernameBox.Text=p.Username??"";PasswordBox.Clear();}

    private void UseProfile_Click(object s,RoutedEventArgs e)
    {
        var p=Current();
        if(string.IsNullOrWhiteSpace(p.Host)){OutputText.Text=LocalizationService.T("Profiles.HostRequired");return;}
        ProfileActivated?.Invoke(p);
        OutputText.Text=$"{LocalizationService.T("Profiles.GlobalActive")}\n{p.Display}\n\n{LocalizationService.T("Profiles.ContextUpdated")}";
    }

    private async void Test_Click(object s,RoutedEventArgs e){try{OutputText.Text=LocalizationService.T("Profiles.Testing");OutputText.Text=await DatabaseProviderFactory.Create(Current()).TestAsync();}catch(Exception ex){OutputText.Text=$"ERROR: {ex.Message}";}}
    private async void Ask_Click(object s,RoutedEventArgs e){try{OutputText.Text=LocalizationService.T("Profiles.Collecting");OutputText.Text=await _assistant.AskAsync(Current(),QuestionBox.Text);}catch(Exception ex){OutputText.Text=$"ERROR: {ex.Message}";}}
}