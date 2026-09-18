using System.Windows;using System.Windows.Controls;using DBACheck2.App.Models;using DBACheck2.App.Providers;using DBACheck2.App.Services;
namespace DBACheck2.App;
public partial class AssistantProfilesWindow:Window
{
 private readonly ServerProfileService _profiles=new();private readonly DbaAssistantService _assistant=new();private List<ServerProfile> _items=new();
 public AssistantProfilesWindow(string currentHost){InitializeComponent();HostBox.Text=currentHost;EngineBox.ItemsSource=Enum.GetValues<DatabaseEngine>();EngineBox.SelectedItem=DatabaseEngine.SqlServer;Loaded+=async(_,__)=>await Refresh();}
 private ServerProfile Current()=>new(){Name=ProfileName.Text.Trim(),Engine=(DatabaseEngine)(EngineBox.SelectedItem??DatabaseEngine.SqlServer),Host=HostBox.Text.Trim(),Environment="PROD",Authentication="Windows",TrustCertificate=true};
 private async Task Refresh(){_items=await _profiles.LoadAsync();ProfilesBox.ItemsSource=null;ProfilesBox.ItemsSource=_items;OutputText.Text=$"Profiles: {_items.Count}\nStorage: {_profiles.PathName}\n\nLas credenciales no se almacenan en esta primera integración.";}
 private async void Save_Click(object s,RoutedEventArgs e){var p=Current();if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Host)){OutputText.Text="Nombre y Host son obligatorios.";return;}_items.RemoveAll(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));_items.Add(p);await _profiles.SaveAsync(_items);await Refresh();ProfilesBox.SelectedItem=_items.FirstOrDefault(x=>x.Name==p.Name);}
 private void Load_Click(object s,RoutedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
 private void ProfilesBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(ProfilesBox.SelectedItem is ServerProfile p)Apply(p);}
 private void Apply(ServerProfile p){ProfileName.Text=p.Name;EngineBox.SelectedItem=p.Engine;HostBox.Text=p.Host;}
 private async void Test_Click(object s,RoutedEventArgs e){try{OutputText.Text="Probando conexión...";OutputText.Text=await DatabaseProviderFactory.Create(Current()).TestAsync();}catch(Exception ex){OutputText.Text=$"ERROR: {ex.Message}";}}
 private async void Ask_Click(object s,RoutedEventArgs e){try{OutputText.Text="Recolectando evidencia...";OutputText.Text=await _assistant.AskAsync(Current(),QuestionBox.Text);}catch(Exception ex){OutputText.Text=$"ERROR: {ex.Message}";}}
}