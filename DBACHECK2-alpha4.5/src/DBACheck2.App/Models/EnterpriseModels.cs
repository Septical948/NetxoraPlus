namespace DBACheck2.App.Models;

public enum EnterpriseRole { Viewer, Dba, LeadDba, Administrator }
public enum EnterpriseMemberState { Invited, Active, Suspended }
public enum EnterpriseRequestState { Draft, Requested, InProgress, Delivered, Rejected }
public enum SharedOperationState { New, Assigned, Investigating, Waiting, Resolved }
public enum EnterpriseSsoMode { Disabled, Oidc, SamlGateway }

public sealed class EnterpriseOrganization
{
    public string OrganizationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "DBACHECK2 Enterprise Workspace";
    public string Domain { get; set; } = "";
    public int SeatLimit { get; set; } = 10;
    public EnterpriseSsoMode SsoMode { get; set; } = EnterpriseSsoMode.Disabled;
    public string SsoIssuer { get; set; } = "";
    public string SsoClientId { get; set; } = "";
    public bool RequireSso { get; set; }
    public EnterpriseRole DefaultRole { get; set; } = EnterpriseRole.Viewer;
    public bool SharedOperationsEnabled { get; set; } = true;
    public bool ReadOnlyByDefault { get; set; } = true;
    public bool RequireProtectedActionConfirmation { get; set; } = true;
    public bool RequireIncidentBeforeProtectedAction { get; set; } = true;
    public int AuditRetentionDays { get; set; } = 365;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class EnterpriseMember
{
    public string MemberId { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public EnterpriseRole Role { get; set; } = EnterpriseRole.Viewer;
    public EnterpriseMemberState State { get; set; } = EnterpriseMemberState.Invited;
    public bool SeatAssigned { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastSeenAt { get; set; }
}

public sealed class EnterpriseIntegrationRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string IntegrationType { get; set; } = "";
    public string EndpointOrProduct { get; set; } = "";
    public string Notes { get; set; } = "";
    public EnterpriseRequestState State { get; set; } = EnterpriseRequestState.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class SharedOperation
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Priority { get; set; } = "P3";
    public string Host { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Owner { get; set; } = "";
    public SharedOperationState State { get; set; } = SharedOperationState.New;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class EnterpriseSlaRule
{
    public string Priority { get; set; } = "P1";
    public int ResponseTargetMinutes { get; set; } = 15;
    public int ResolutionTargetMinutes { get; set; } = 240;
    public string EscalationContact { get; set; } = "";
}

public sealed class EnterpriseAuditEvent
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Detail { get; set; } = "";
}
