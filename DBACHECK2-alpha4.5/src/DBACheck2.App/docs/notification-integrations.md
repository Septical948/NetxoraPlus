# DBACHECK2 notification integrations

RC1 foundation for Microsoft Teams, Slack and Discord incident delivery.

## Architecture

DB diagnosis -> IncidentNotification -> NotificationDispatcher -> routing policy -> destination.

Credentials/endpoints saved by the desktop client are protected with Windows DPAPI (CurrentUser). OAuth client secrets must never be embedded in the desktop executable.

## Tenant/workspace onboarding

The production design uses a DBACHECK2 control-plane OAuth callback:

- Microsoft Teams: Microsoft Entra multi-tenant application, least-privilege consent/RSC where supported.
- Slack: OAuth v2 installation, workspace and channel authorization.
- Discord: OAuth2/bot installation or webhook destination.

The control plane owns refresh tokens/client credentials. The desktop stores only the destination identity and a revocable delivery credential/endpoint when required.

## Routing

Routes support wildcard host patterns and P1-P4 minimum priority. A P1 event matches P1/P2/P3/P4 thresholds; P4 only matches P4 thresholds.

The dispatcher has a 15-second HTTP timeout and returns per-destination delivery status. Persistent retry/deduplication/audit queues are the next stage before automatic production alerting.
