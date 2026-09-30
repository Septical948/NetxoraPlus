using System.Windows;
using DBACheck2.App.Models;
namespace DBACheck2.App;
public partial class NotificationDestinationDialog:Window
{
    private readonly NotificationProvider _provider;
    public NotificationDestination Destination {get;private set;}=new();
    public NotificationDestinationDialog(NotificationProvider provider)
    {
        InitializeComponent();_provider=provider;TitleText.Text=$"CONFIGURE {provider}";
        HelpText.Text=provider switch {
            NotificationProvider.MicrosoftTeams=>"RC1 supports a Teams Workflow/Incoming Webhook delivery endpoint. Tenant-wide OAuth onboarding will be handled by the DBACHECK2 control plane; no Microsoft client secret is embedded in the desktop app.",
            NotificationProvider.Slack=>"Paste the Slack incoming-webhook endpoint for this workspace/channel. OAuth workspace onboarding is the next control-plane stage.",
            _=>"Paste the Discord channel webhook endpoint. Bot/OAuth installation can be added without changing the dispatcher contract."
        };
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(!Uri.TryCreate(EndpointBox.Text.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"){MessageBox.Show("A valid HTTPS endpoint is required.","DBACHECK2");return;}
        Destination=new NotificationDestination{Name=_provider.ToString(),Provider=_provider,Endpoint=uri.ToString(),Secret=SecretBox.Password,Enabled=true,ConnectedUtc=DateTime.UtcNow};
        DialogResult=true;
    }
    private void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
}