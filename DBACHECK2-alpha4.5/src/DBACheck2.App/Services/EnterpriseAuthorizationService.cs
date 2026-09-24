using DBACheck2.App.Models;

namespace DBACheck2.App.Services;

public enum EnterprisePermission
{
    View,
    Diagnose,
    CaptureEvidence,
    CreateIncident,
    ProtectedAction,
    ManageIntegrations,
    ManageMembers,
    ManagePolicies,
    ViewAudit
}

public static class EnterpriseAuthorizationService
{
    public static bool Can(EnterpriseRole role,EnterprisePermission permission)=>role switch
    {
        EnterpriseRole.Viewer=>permission is EnterprisePermission.View or EnterprisePermission.ViewAudit,
        EnterpriseRole.Dba=>permission is EnterprisePermission.View
            or EnterprisePermission.Diagnose
            or EnterprisePermission.CaptureEvidence
            or EnterprisePermission.CreateIncident
            or EnterprisePermission.ViewAudit,
        EnterpriseRole.LeadDba=>permission is EnterprisePermission.View
            or EnterprisePermission.Diagnose
            or EnterprisePermission.CaptureEvidence
            or EnterprisePermission.CreateIncident
            or EnterprisePermission.ProtectedAction
            or EnterprisePermission.ManageIntegrations
            or EnterprisePermission.ViewAudit,
        EnterpriseRole.Administrator=>true,
        _=>false
    };

    public static string Describe(EnterpriseRole role)
    {
        var allowed=Enum.GetValues<EnterprisePermission>().Where(p=>Can(role,p));
        return $"{role}: {string.Join(", ",allowed)}";
    }
}
