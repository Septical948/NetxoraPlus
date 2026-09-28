# DBACHECK2 Enterprise Control Plane

Status: Beta 2 central-control foundation.

The Control Plane converts the local Enterprise workspace into shared organization state for multiple DBAs.

## Architecture

Desktop clients connect over HTTPS to DBACHECK2 Control Plane. The Control Plane centralizes organization/seats, members/roles, shared operations, SLA, SSO/RBAC policy metadata, onboarding requests, and audit.

Billing remains a separate service. Stripe and DBACHECK2 Billing API own commercial subscription state. DBACHECK2 Control Plane owns organization operations and governance.

A later milestone will bind an Enterprise contract from Billing to a Control Plane organization and make the contracted seat limit authoritative.

## Project

src/DBACheck2.ControlPlane

ASP.NET Core 8 + SQLite for Beta 2.

Default database:
src/DBACheck2.ControlPlane/data/control-plane.db

Production must set persistent storage with DBACHECK2_CONTROL_PLANE_DB.

## Bootstrap security

Organization creation is intentionally not public.

Set a long random server-only bootstrap token:

    set DBACHECK2_CONTROL_PLANE_BOOTSTRAP_TOKEN=<at least 32 random characters>

Never place this value inside DBACHECK2 Desktop.

The bootstrap endpoint returns a one-time Enterprise organization key. The Control Plane stores only its SHA-256 hash.

## Run locally

    cd C:\netxora\Netxora+\DBACHECK2-alpha4.5
    set DBACHECK2_CONTROL_PLANE_BOOTSTRAP_TOKEN=REPLACE_WITH_A_LONG_RANDOM_VALUE
    dotnet run --project .\src\DBACheck2.ControlPlane\DBACheck2.ControlPlane.csproj --urls http://localhost:5108

Check:
http://localhost:5108/health

## Create the first Enterprise organization

PowerShell:

    $headers = @{ "X-DBACHECK-Bootstrap-Token" = "REPLACE_WITH_A_LONG_RANDOM_VALUE" }
    $body = @{ name="Example Corp"; domain="example.com"; seatLimit=10; adminEmail="dba@example.com"; adminName="Lead DBA" } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "http://localhost:5108/v1/organizations/bootstrap" -Headers $headers -ContentType "application/json" -Body $body

The response contains organizationId and enterpriseKey. The Enterprise key is shown only at bootstrap. Store it securely.

## Connect DBACHECK2 Desktop

For Beta 2 development:

    set DBACHECK2_ENTERPRISE_API=http://localhost:5108
    set DBACHECK2_ENTERPRISE_ORG_ID=<organizationId>
    set DBACHECK2_ENTERPRISE_KEY=<enterpriseKey>
    set DBACHECK2_ENTERPRISE_ACTOR=ndemaria

Then start DBACHECK2 from the same environment.

The existing Enterprise Center automatically switches from LOCAL BETA to CONTROL PLANE. Organization, Members, Shared Operations, Onboarding/SLA and Audit become shared data.

## Current endpoints

Health:
- GET /health

Bootstrap:
- POST /v1/organizations/bootstrap
- requires X-DBACHECK-Bootstrap-Token

Organization:
- GET /v1/organizations/{orgId}
- PUT /v1/organizations/{orgId}

Members / seats:
- GET /v1/organizations/{orgId}/members
- POST /v1/organizations/{orgId}/members
- DELETE /v1/organizations/{orgId}/members/{memberId}

Shared Operations:
- GET /v1/organizations/{orgId}/operations
- POST /v1/organizations/{orgId}/operations

Custom integrations / onboarding:
- GET /v1/organizations/{orgId}/integrations
- POST /v1/organizations/{orgId}/integrations

SLA:
- GET /v1/organizations/{orgId}/sla
- PUT /v1/organizations/{orgId}/sla

Audit:
- GET /v1/organizations/{orgId}/audit?limit=500

All organization endpoints require X-DBACHECK-Enterprise-Key. Writes also accept X-DBACHECK-Actor and are written to the central audit trail.

## What is centralized now

- organization identity
- configured seat limit
- members and roles
- central access-policy configuration
- shared operations
- integration/onboarding requests
- SLA rules
- append audit events

## SSO / RBAC status

The Control Plane stores Enterprise SSO and RBAC policy configuration, but Beta 2 does not yet treat that configuration as a complete identity provider.

Current roles:
- Viewer
- DBA
- Lead DBA
- Administrator

Production SSO phase must add:
1. OIDC/SAML identity-provider validation
2. user login/session handling
3. verified identity to Enterprise member mapping
4. server-side RBAC enforcement
5. short-lived access tokens
6. key rotation / revocation
7. break-glass administrator flow
8. SSO-required enforcement
9. audit correlation IDs

The Enterprise organization key is therefore a Beta/admin transport credential, not the final user-authentication mechanism.

## Production data layer

SQLite is appropriate for Beta testing. Before multi-customer production move the central store to PostgreSQL or another managed relational service with tenant isolation, encrypted backups, migrations, HA, PITR, retention jobs and structured audit export.

## Billing binding milestone

Enterprise commercial provisioning should ultimately flow from Stripe/contract to Billing API, then to an ACTIVE Enterprise subscription with a contracted seat limit, then to the Control Plane organization.

The Control Plane must not allow a customer to permanently increase a contracted seat limit from the desktop after that binding is enabled.

## Next production milestones

1. compile and run Control Plane locally
2. bootstrap a test organization
3. connect two DBACHECK2 clients to the same organization
4. verify shared members / operations / audit
5. add member update and operation lifecycle actions
6. connect Enterprise Billing contract to organization seat limits
7. implement OIDC server-side authentication
8. enforce RBAC server-side
9. add invitation flow
10. add API-key rotation and revocation
11. migrate central persistence from SQLite
12. add backups, HA, telemetry and rate limiting
