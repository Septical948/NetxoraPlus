using DBACheck2.ControlPlane.Models;
using DBACheck2.ControlPlane.Services;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ControlPlaneStore>();
builder.Services.AddSingleton<ControlPlaneAuth>();

var app=builder.Build();

app.MapGet("/health",(ControlPlaneStore store,ControlPlaneAuth auth)=>Results.Ok(new {
    service="DBACHECK2 Enterprise Control Plane",
    utc=DateTime.UtcNow,
    database=Path.GetFileName(store.DatabasePath),
    bootstrap=auth.BootstrapConfigured,
    service_token=auth.ServiceTokenConfigured
}));

app.MapPost("/v1/organizations/bootstrap",async(BootstrapOrganizationRequest body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!auth.ValidateBootstrap(request))return Results.Unauthorized();
    if(string.IsNullOrWhiteSpace(body.Name))return Results.BadRequest(new{error="Organization name is required."});
    var created=await store.BootstrapAsync(body);
    return Results.Ok(new BootstrapOrganizationResponse {
        OrganizationId=created.Org.OrganizationId,
        EnterpriseKey=created.Key
    });
});

app.MapGet("/v1/organizations/{orgId}",async(string orgId,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    var org=await store.GetOrganizationAsync(orgId);
    return org is null?Results.NotFound():Results.Ok(org);
});

app.MapPut("/v1/organizations/{orgId}",async(string orgId,EnterpriseOrganization body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    await store.SaveOrganizationAsync(orgId,body,ControlPlaneAuth.Actor(request));
    return Results.Ok(body);
});

app.MapGet("/v1/organizations/{orgId}/members",async(string orgId,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    return Results.Ok(await store.GetMembersAsync(orgId));
});

app.MapPost("/v1/organizations/{orgId}/members",async(string orgId,MemberCreateRequest body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    if(string.IsNullOrWhiteSpace(body.Email))return Results.BadRequest(new{error="Member email is required."});
    try{return Results.Ok(await store.AddMemberAsync(orgId,body,ControlPlaneAuth.Actor(request)));}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapPut("/v1/organizations/{orgId}/members/{memberId}",async(string orgId,string memberId,MemberUpdateRequest body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    try{return Results.Ok(await store.UpdateMemberAsync(orgId,memberId,body,ControlPlaneAuth.Actor(request)));}
    catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapDelete("/v1/organizations/{orgId}/members/{memberId}",async(string orgId,string memberId,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    await store.DeleteMemberAsync(orgId,memberId,ControlPlaneAuth.Actor(request));
    return Results.NoContent();
});

app.MapGet("/v1/organizations/{orgId}/operations",async(string orgId,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    return Results.Ok(await store.GetOperationsAsync(orgId));
});

app.MapPost("/v1/organizations/{orgId}/operations",async(string orgId,SharedOperation body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    await store.SaveOperationAsync(orgId,body,ControlPlaneAuth.Actor(request));
    return Results.Ok(body);
});

app.MapGet("/v1/organizations/{orgId}/integrations",async(string orgId,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    return Results.Ok(await store.GetIntegrationRequestsAsync(orgId));
});

app.MapPost("/v1/organizations/{orgId}/integrations",async(string orgId,EnterpriseIntegrationRequest body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    await store.SaveIntegrationRequestAsync(orgId,body,ControlPlaneAuth.Actor(request));
    return Results.Ok(body);
});

app.MapGet("/v1/organizations/{orgId}/sla",async(string orgId,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    return Results.Ok(await store.GetSlaAsync(orgId));
});

app.MapPut("/v1/organizations/{orgId}/sla",async(string orgId,List<EnterpriseSlaRule> body,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    await store.SaveSlaAsync(orgId,body,ControlPlaneAuth.Actor(request));
    return Results.Ok(body);
});

app.MapGet("/v1/organizations/{orgId}/audit",async(string orgId,int? limit,HttpRequest request,ControlPlaneStore store,ControlPlaneAuth auth)=>{
    if(!await auth.ValidateOrganizationAsync(request,orgId))return Results.Unauthorized();
    return Results.Ok(await store.GetAuditAsync(orgId,limit??500));
});

app.Run();
