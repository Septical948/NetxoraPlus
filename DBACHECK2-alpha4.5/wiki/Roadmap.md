# DBACHECK2 Roadmap

## Completed foundations

### Alpha 1 — Functional base
Connection profiles, Quick Check and documentation.

### Alpha 2 — Incident Analyzer
Blocking, long transactions, TempDB/version store, transaction log, backups/jobs, waits, disk and HA evidence.

### Alpha 3 — Controlled actions
Protected actions with human confirmation and evidence-first safety.

### Alpha 4 — History and reports
Local SQLite history, technical evidence and reports.

### Alpha 5 — DBA Assistant / multi-engine foundation
Provider abstraction for SQL Server, PostgreSQL, Oracle and MySQL/MariaDB.

### Beta 1 — Full operational platform
- text-first single-window UI
- engine-aware diagnostics
- Incident History / Incident Operations
- monitoring integrations
- Alert Inbox / prioritization
- profile security / DPAPI
- update/support/settings shell

### Beta 2 — Assessment + commercial foundation
- DBAHEALTCHECK integrated into Full Assessment
- engine-native PostgreSQL / Oracle / MySQL-MariaDB assessment packs
- capability-aware legacy routing
- Full Assessment history and HTML export
- Standard / Plus / Enterprise catalog
- Stripe Billing API
- Checkout / Customer Portal / webhooks
- local Enterprise workspace foundation
- organization / seats
- onboarding / SLA
- local shared operations
- RBAC policy model
- audit policy foundation

## Current — Beta 3

### 1. Subscription entitlement enforcement
Status: IN PROGRESS

Commercial plans must govern product capabilities without breaking Beta/Development licenses.

- Standard: multi-engine diagnostics, Quick Check, Full Assessment, export and Incident History
- Plus: monitoring integrations, Alert Inbox, correlation and Incident Operations
- Enterprise: organization / seats / Enterprise Center and future central team features

Development licenses remain unrestricted during QA.

### 2. Enterprise Control Plane
Status: NEXT

Convert the current local Enterprise workspace into real multi-user operations:

- central organizations
- members / seats
- shared operations
- shared incident ownership
- centralized SLA
- centralized policy
- organization licensing
- synchronization / offline cache

### 3. SSO / RBAC enforcement
Status: PLANNED

- OIDC first
- optional SAML gateway
- Viewer / DBA / Lead DBA / Administrator
- server-authoritative permissions
- protected-action authorization
- session / token lifecycle

### 4. Central audit
Status: PLANNED

- immutable operational audit events
- actor / action / target / evidence
- subscription and administrative events
- retention by organization policy
- export for Enterprise customers

### 5. Production licensing hardening
Status: PLANNED

- offline grace period
- signed cached entitlement
- webhook lifecycle regression
- past_due / canceled / expired behavior
- Enterprise contract licensing
- seat enforcement
- production secret manager

### 6. Release Candidate QA
Status: PLANNED

- least-privilege permission matrix
- SQL Server old/current regression
- PostgreSQL old/current regression
- Oracle legacy/modern validation
- MySQL/MariaDB validation
- timeouts / partial-permission / unavailable-host tests
- 24h / 7d soak tests
- collector performance budgets
- installer / upgrade / rollback
- self-contained publish
- documentation / licensing / packaging review

## Sellable release gate

DBACHECK2 becomes Release Candidate only when:

1. clean build and signed/self-contained package
2. commercial subscription lifecycle is tested
3. entitlement enforcement is validated
4. secrets are never persisted in plaintext
5. production collectors stay read-only unless a protected action is explicitly confirmed
6. critical legacy/current engine combinations have documented compatibility results
7. installer, update and rollback are tested
8. support / documentation / privacy / licensing material is complete
