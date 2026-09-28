using DBACheck2.ControlPlane.Models;

namespace DBACheck2.ControlPlane.Services;

public sealed class ControlPlaneAuth
{
    private readonly ControlPlaneStore _store;
    private readonly string _bootstrapToken=(Environment.GetEnvironmentVariable("DBACHECK2_CONTROL_PLANE_BOOTSTRAP_TOKEN")??"").Trim();
    private readonly string _serviceToken=(Environment.GetEnvironmentVariable("DBACHECK2_CONTROL_PLANE_SERVICE_TOKEN")??"").Trim();

    public ControlPlaneAuth(ControlPlaneStore store)=>_store=store;
    public bool BootstrapConfigured=>_bootstrapToken.Length>=32;
    public bool ServiceTokenConfigured=>_serviceToken.Length>=32;

    public bool ValidateBootstrap(HttpRequest request)
    {
        var supplied=request.Headers["X-DBACHECK-Bootstrap-Token"].ToString();
        return BootstrapConfigured && string.Equals(supplied,_bootstrapToken,StringComparison.Ordinal);
    }

    public bool ValidateService(HttpRequest request)
    {
        var supplied=request.Headers["X-DBACHECK-Service-Token"].ToString();
        return ServiceTokenConfigured && string.Equals(supplied,_serviceToken,StringComparison.Ordinal);
    }

    public async Task<bool> ValidateOrganizationAsync(HttpRequest request,string organizationId)
    {
        var key=request.Headers["X-DBACHECK-Enterprise-Key"].ToString();
        return await _store.AuthenticateAsync(organizationId,key);
    }

    public static string Actor(HttpRequest request)
    {
        var actor=request.Headers["X-DBACHECK-Actor"].ToString();
        return string.IsNullOrWhiteSpace(actor)?"CONTROL PLANE":actor.Trim();
    }
}
