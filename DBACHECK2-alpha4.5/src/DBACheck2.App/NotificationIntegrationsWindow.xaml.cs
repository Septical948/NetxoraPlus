using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class NotificationIntegrationsWindow:Window
{
    private readonly NotificationIntegrationStore _store=new();
    private readonly NotificationDispatcher _dispatcher=new();
    private List<NotificationDestination> _destinations=new();
    private List<NotificationRoute> _routes=new();

    public NotificationIntegrationsWindow()
    {
        InitializeComponent();
        ProviderBox.ItemsSource=Enum.GetValues<NotificationProvider>();ProviderBox.SelectedItem=NotificationProvider.MicrosoftTeams;
        PriorityBox.ItemsSource=Enum.GetValues<IncidentPriority>();PriorityBox.SelectedItem=IncidentPriority.P2;
        Loaded+=async(_,__)=>await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        (_destinations,_routes)=await _store.LoadAsync();
        DestinationsGrid.ItemsSource=null;DestinationsGrid.ItemsSource=_destinations;
        RouteDestinationsBox.ItemsSource=null;RouteDestinationsBox.ItemsSource=_destinations;
        StatusText.Text=$"{_destinations.Count} destination(s) · {_routes.Count} route(s).";
    }

    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        var provider=(NotificationProvider)(ProviderBox.SelectedItem??NotificationProvider.MicrosoftTeams);
        var dialog=new NotificationDestinationDialog(provider){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        var d=dialog.Destination;
        d.Name=string.IsNullOrWhiteSpace(NameBox.Text)?d.Name:NameBox.Text.Trim();
        if(!string.IsNullOrWhiteSpace(WorkspaceBox.Text))d.TenantOrWorkspaceName=WorkspaceBox.Text.Trim();
        if(!string.IsNullOrWhiteSpace(ChannelBox.Text))d.ChannelName=ChannelBox.Text.Trim();
        _destinations.RemoveAll(x=>x.Name.Equals(d.Name,StringComparison.OrdinalIgnoreCase));_destinations.Add(d);
        await _store.SaveAsync(_destinations,_routes);await ReloadAsync();
        StatusText.Text=$"{d.Provider} destination '{d.Name}' saved. Use TEST NOTIFICATION before enabling automatic routing.";
    }

    private void DestinationsGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(DestinationsGrid.SelectedItem is not NotificationDestination d)return;
        ProviderBox.SelectedItem=d.Provider;NameBox.Text=d.Name;WorkspaceBox.Text=d.TenantOrWorkspaceName;ChannelBox.Text=d.ChannelName;
    }

    private async void Test_Click(object sender,RoutedEventArgs e)
    {
        if(DestinationsGrid.SelectedItem is not NotificationDestination d){StatusText.Text="Select a destination first.";return;}
        try{StatusText.Text="Sending test notification...";await _dispatcher.TestAsync(d);StatusText.Text=$"✓ Test delivered to {d.Display}.";}
        catch(Exception ex){StatusText.Text="ERROR: "+ex.Message;}
    }

    private async void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(DestinationsGrid.SelectedItem is not NotificationDestination d)return;
        _destinations.Remove(d);foreach(var r in _routes)r.DestinationNames.RemoveAll(x=>x.Equals(d.Name,StringComparison.OrdinalIgnoreCase));
        await _store.SaveAsync(_destinations,_routes);await ReloadAsync();
    }

    private async void SaveRoute_Click(object sender,RoutedEventArgs e)
    {
        var selected=RouteDestinationsBox.SelectedItems.Cast<NotificationDestination>().Select(x=>x.Name).ToList();
        if(selected.Count==0){StatusText.Text="Select at least one destination for the route.";return;}
        var route=new NotificationRoute{Name="Default",HostPattern=string.IsNullOrWhiteSpace(HostPatternBox.Text)?"*":HostPatternBox.Text.Trim(),MinimumPriority=(IncidentPriority)(PriorityBox.SelectedItem??IncidentPriority.P2),DestinationNames=selected};
        _routes.RemoveAll(x=>x.Name.Equals("Default",StringComparison.OrdinalIgnoreCase));_routes.Add(route);
        await _store.SaveAsync(_destinations,_routes);await ReloadAsync();
        StatusText.Text=$"✓ Default route saved: {route.HostPattern} · {route.MinimumPriority} and above · {string.Join(", ",selected)}";
    }
}
