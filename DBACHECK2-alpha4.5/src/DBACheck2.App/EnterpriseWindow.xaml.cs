using System.Windows;
using DBACheck2.App.Models;
using DBACheck2.App.Services;

namespace DBACheck2.App;

public partial class EnterpriseWindow:Window
{
    private readonly EnterpriseWorkspaceService _service=new();
    private EnterpriseOrganization _organization=new();
    private bool En=>LocalizationService.Current==AppLanguage.En;

    public EnterpriseWindow()
    {
        InitializeComponent();
        MemberRoleBox.ItemsSource=Enum.GetValues<EnterpriseRole>();
        MemberRoleBox.SelectedItem=EnterpriseRole.Dba;
        DefaultRoleBox.ItemsSource=Enum.GetValues<EnterpriseRole>();
        SsoModeBox.ItemsSource=Enum.GetValues<EnterpriseSsoMode>();
        OperationPriorityBox.ItemsSource=new[]{"P1","P2","P3","P4"};
        OperationPriorityBox.SelectedIndex=2;
        ApplyLanguage();
        Loaded+=async(_,__)=>await LoadAllAsync();
    }

    private void ApplyLanguage()
    {
        TitleText.Text=En?"ENTERPRISE CENTER":"CENTRO ENTERPRISE";
        SubtitleText.Text=En?"Organization, team operations, access, policies and governance":"Organización, operaciones de equipo, acceso, políticas y gobierno";
        OrganizationTab.Header=En?"ORGANIZATION":"ORGANIZACIÓN";
        MembersTab.Header=En?"MEMBERS":"MIEMBROS";
        OperationsTab.Header=En?"SHARED OPERATIONS":"OPERACIONES COMPARTIDAS";
        AccessTab.Header=En?"SSO / RBAC / POLICIES":"SSO / RBAC / POLÍTICAS";
        OnboardingTab.Header=En?"ONBOARDING / SLA":"ONBOARDING / SLA";
        AuditTab.Header=En?"AUDIT":"AUDITORÍA";
        RefreshAllButton.Content=En?"REFRESH":"ACTUALIZAR";
        SaveOrganizationButton.Content=En?"SAVE ORGANIZATION":"GUARDAR ORGANIZACIÓN";
        AddMemberButton.Content=En?"ADD MEMBER":"AGREGAR MIEMBRO";
        AddOperationButton.Content=En?"ADD":"AGREGAR";
        SaveAccessButton.Content=En?"SAVE ACCESS / POLICIES":"GUARDAR ACCESO / POLÍTICAS";
        AddIntegrationRequestButton.Content=En?"ADD REQUEST":"AGREGAR SOLICITUD";
        SaveSlaButton.Content=En?"SAVE SLA":"GUARDAR SLA";
        RefreshAuditButton.Content=En?"REFRESH AUDIT":"ACTUALIZAR AUDITORÍA";
        SsoNoticeText.Text=En
            ?"Beta 2 stores Enterprise access policy configuration locally. Production SSO enforcement and central RBAC require the Enterprise backend."
            :"Beta 2 guarda localmente la configuración de acceso Enterprise. La aplicación real de SSO y RBAC central requiere el backend Enterprise.";
    }

    private async Task LoadAllAsync()
    {
        _organization=await _service.LoadOrganizationAsync();
        OrganizationNameBox.Text=_organization.Name;
        DomainBox.Text=_organization.Domain;
        SeatLimitBox.Text=_organization.SeatLimit.ToString();
        SsoModeBox.SelectedItem=_organization.SsoMode;
        SsoIssuerBox.Text=_organization.SsoIssuer;
        SsoClientIdBox.Text=_organization.SsoClientId;
        RequireSsoBox.IsChecked=_organization.RequireSso;
        DefaultRoleBox.SelectedItem=_organization.DefaultRole;
        ReadOnlyDefaultBox.IsChecked=_organization.ReadOnlyByDefault;
        ProtectedConfirmBox.IsChecked=_organization.RequireProtectedActionConfirmation;
        RequireIncidentBox.IsChecked=_organization.RequireIncidentBeforeProtectedAction;
        SharedOperationsBox.IsChecked=_organization.SharedOperationsEnabled;
        AuditRetentionBox.Text=_organization.AuditRetentionDays.ToString();

        await LoadMembersAsync();
        OperationsGrid.ItemsSource=await _service.LoadSharedOperationsAsync();
        IntegrationRequestsGrid.ItemsSource=await _service.LoadIntegrationRequestsAsync();
        SlaGrid.ItemsSource=await _service.LoadSlaAsync();
        await LoadAuditAsync();

        BackendStatusText.Text=_service.EnterpriseBackendConfigured
            ? (En?$"Enterprise API configured: {_service.EnterpriseBackend}":$"API Enterprise configurada: {_service.EnterpriseBackend}")
            : (En?"LOCAL BETA WORKSPACE · Enterprise backend not configured":"WORKSPACE BETA LOCAL · backend Enterprise no configurado");
        WorkspaceStatusText.Text=$"{_organization.Name} · {_service.DatabasePath}";
    }

    private async Task LoadMembersAsync()
    {
        var members=await _service.LoadMembersAsync();
        MembersGrid.ItemsSource=members;
        var used=members.Count(x=>x.SeatAssigned && x.State!=EnterpriseMemberState.Suspended);
        SeatSummaryText.Text=En
            ?$"Assigned seats: {used} / {_organization.SeatLimit}\nAvailable: {Math.Max(0,_organization.SeatLimit-used)}"
            :$"Licencias asignadas: {used} / {_organization.SeatLimit}\nDisponibles: {Math.Max(0,_organization.SeatLimit-used)}";
    }

    private async void RefreshAllButton_Click(object sender,RoutedEventArgs e)=>await LoadAllAsync();

    private async void SaveOrganizationButton_Click(object sender,RoutedEventArgs e)
    {
        _organization.Name=OrganizationNameBox.Text.Trim();
        _organization.Domain=DomainBox.Text.Trim();
        if(int.TryParse(SeatLimitBox.Text,out var seats)&&seats>0)_organization.SeatLimit=seats;
        await _service.SaveOrganizationAsync(_organization);
        await LoadMembersAsync();
        WorkspaceStatusText.Text=En?"Organization saved.":"Organización guardada.";
    }

    private async void AddMemberButton_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(MemberEmailBox.Text))return;
        var members=await _service.LoadMembersAsync();
        var used=members.Count(x=>x.SeatAssigned && x.State!=EnterpriseMemberState.Suspended);
        if(used>=_organization.SeatLimit)
        {
            WorkspaceStatusText.Text=En?"Seat limit reached. Increase the Enterprise seat allocation first.":"Se alcanzó el límite de licencias. Aumentá primero la asignación Enterprise.";
            return;
        }
        await _service.SaveMemberAsync(new(){
            Email=MemberEmailBox.Text.Trim(),
            DisplayName=MemberNameBox.Text.Trim(),
            Role=MemberRoleBox.SelectedItem is EnterpriseRole role?role:EnterpriseRole.Dba,
            State=EnterpriseMemberState.Invited,
            SeatAssigned=true
        });
        MemberEmailBox.Clear();MemberNameBox.Clear();
        await LoadMembersAsync();await LoadAuditAsync();
    }

    private async void AddOperationButton_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(OperationSummaryBox.Text))return;
        await _service.SaveSharedOperationAsync(new(){
            Priority=OperationPriorityBox.SelectedItem?.ToString()??"P3",
            Host=OperationHostBox.Text.Trim(),
            Engine=OperationEngineBox.Text.Trim(),
            Summary=OperationSummaryBox.Text.Trim(),
            Owner=OperationOwnerBox.Text.Trim(),
            State=SharedOperationState.New
        });
        OperationSummaryBox.Clear();
        OperationsGrid.ItemsSource=await _service.LoadSharedOperationsAsync();
        await LoadAuditAsync();
    }

    private async void SaveAccessButton_Click(object sender,RoutedEventArgs e)
    {
        _organization.SsoMode=SsoModeBox.SelectedItem is EnterpriseSsoMode sso?sso:EnterpriseSsoMode.Disabled;
        _organization.SsoIssuer=SsoIssuerBox.Text.Trim();
        _organization.SsoClientId=SsoClientIdBox.Text.Trim();
        _organization.RequireSso=RequireSsoBox.IsChecked==true;
        _organization.DefaultRole=DefaultRoleBox.SelectedItem is EnterpriseRole role?role:EnterpriseRole.Viewer;
        _organization.ReadOnlyByDefault=ReadOnlyDefaultBox.IsChecked==true;
        _organization.RequireProtectedActionConfirmation=ProtectedConfirmBox.IsChecked==true;
        _organization.RequireIncidentBeforeProtectedAction=RequireIncidentBox.IsChecked==true;
        _organization.SharedOperationsEnabled=SharedOperationsBox.IsChecked==true;
        if(int.TryParse(AuditRetentionBox.Text,out var days)&&days>0)_organization.AuditRetentionDays=days;
        await _service.SaveOrganizationAsync(_organization);
        WorkspaceStatusText.Text=En?"Access and policy configuration saved.":"Configuración de acceso y políticas guardada.";
        await LoadAuditAsync();
    }

    private async void AddIntegrationRequestButton_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(IntegrationNameBox.Text))return;
        await _service.SaveIntegrationRequestAsync(new(){
            Name=IntegrationNameBox.Text.Trim(),
            IntegrationType=IntegrationTypeBox.Text.Trim(),
            EndpointOrProduct=IntegrationEndpointBox.Text.Trim(),
            Notes=IntegrationNotesBox.Text.Trim(),
            State=EnterpriseRequestState.Requested
        });
        IntegrationNameBox.Clear();IntegrationTypeBox.Clear();IntegrationEndpointBox.Clear();IntegrationNotesBox.Clear();
        IntegrationRequestsGrid.ItemsSource=await _service.LoadIntegrationRequestsAsync();
        await LoadAuditAsync();
    }

    private async void SaveSlaButton_Click(object sender,RoutedEventArgs e)
    {
        var rows=SlaGrid.ItemsSource is IEnumerable<EnterpriseSlaRule> source?source.ToList():new();
        await _service.SaveSlaAsync(rows);
        WorkspaceStatusText.Text=En?"SLA saved.":"SLA guardado.";
        await LoadAuditAsync();
    }

    private async Task LoadAuditAsync()=>AuditGrid.ItemsSource=await _service.LoadAuditAsync();
    private async void RefreshAuditButton_Click(object sender,RoutedEventArgs e)=>await LoadAuditAsync();
}
