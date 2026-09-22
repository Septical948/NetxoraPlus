# DBACHECK2 Integration Layer

Status: Beta 2 integration phase (first adapter: Zabbix)

## Goal

DBACHECK2 does not try to replace monitoring platforms. Monitoring systems detect conditions and expose events/metrics; DBACHECK2 consumes those signals and correlates them with database-specific evidence.

Flow:

Monitoring source -> Integration provider -> Normalized IntegrationEvent -> Correlation hint -> DB provider diagnostics -> Incident workflow

## Common integration model

All external adapters must map source-specific data to `IntegrationEvent`:

- Source
- External event ID
- Host
- Problem name
- Normalized severity
- Native severity
- Timestamp
- Acknowledged / suppressed state
- Tags
- Raw detail
- DBACHECK correlation hint

The provider contract is `IIntegrationProvider` and adapters are created through `IntegrationProviderFactory`.

Planned adapters:
- Zabbix: enabled
- Nagios: planned
- Prometheus / Alertmanager: planned
- Datadog: planned

## Zabbix adapter

Current read-only capabilities:
- API reachability/version test
- API token authentication
- Current Authorization: Bearer flow
- Legacy `auth` JSON-RPC fallback for older installations
- `problem.get` for unresolved problems
- `trigger.get` to resolve trigger -> host
- Severity normalization
- Tags and opdata collection
- DBACHECK correlation hints

DBACHECK accepts either:
- a Zabbix base URL, e.g. `https://zabbix.example/zabbix`
- the full endpoint, e.g. `https://zabbix.example/zabbix/api_jsonrpc.php`

Integration tokens are optional at rest. If "Remember token" is enabled, the token is encrypted with Windows DPAPI for the current Windows user. It is never stored as plaintext.

## Correlation categories

The first correlation layer recognizes monitoring symptoms related to:
- Storage / filesystem
- Transaction log / WAL / redo / binlog
- Blocking / locks / deadlocks
- HA / replication
- Backup
- Performance / CPU / I/O / latency
- Availability / connections

This first layer produces a diagnostic direction only. The next step is target/profile matching so an external event can automatically invoke the correct SQL Server, PostgreSQL, Oracle or MySQL/MariaDB read-only collectors.

## Next implementation step

1. Map monitoring hosts/tags to DBACHECK server profiles.
2. Run targeted DB collectors from a selected external event.
3. Store external-event evidence in Incident History.
4. Add Nagios adapter.
5. Add Prometheus/Alertmanager adapter.
6. Add webhook/push ingestion in addition to pull/API mode.


## Event -> DB diagnosis vertical slice

The first end-to-end correlation path is now implemented.

1. Load an external monitoring event.
2. Resolve the event host to a saved DBACHECK server profile.
3. Test the matched database provider.
4. Run the provider Quick Check.
5. Filter the returned checks according to the external event category.
6. Show the correlated database evidence.
7. Optionally create an Incident History record from the correlation result.

Profile resolution precedence:
- Zabbix/monitoring tag `dbacheck_profile=<saved profile name>`
- exact external host -> profile Host
- exact external host -> profile Name
- unique short-host match

If the monitoring host name differs from the DBACHECK profile, the recommended explicit mapping is a monitoring tag such as:

```
dbacheck_profile=PROD-SQL01
```

Current correlation categories:
- Storage
- Log/WAL
- Locking
- HA/Replication
- Backup
- Performance
- Availability
- General

No ACK, silence, remote action or database corrective action is executed by this flow.


## Alert Inbox

Alert Inbox is the DBA morning operations queue.

When at least one monitoring integration profile is configured, DBACHECK2 opens the Alert Inbox at startup and reads external monitoring sources before any database connection is required.

Current flow:

1. Read configured monitoring sources.
2. Normalize events to `IntegrationEvent`.
3. Group related alerts by short host + operational category.
4. Resolve the group to a saved DBACHECK server profile without connecting to the database.
5. Calculate an operational priority (P1-P4).
6. Present the queue ordered by priority.
7. Connect to the database only when the DBA selects `DIAGNOSE SELECTED`.
8. Optionally create an Incident History record after database evidence is collected.

Priority currently considers:
- external monitor severity
- PROD / QA / DEV environment when a DB profile is matched
- alert age
- acknowledged state
- monitoring category
- number of independent monitoring sources reporting the condition

The original monitoring severity is preserved separately from DBACHECK operational priority.

### Local snapshot

The last successful Alert Inbox snapshot is stored locally in:

`%LOCALAPPDATA%\Netxora\DBACHECK2\alert-inbox.db`

If all monitoring APIs are temporarily unavailable, DBACHECK can display the last successful snapshot as `CACHED`. This is a fallback view and is not presented as current monitoring state.

### Important boundary

Opening Alert Inbox does not connect to SQL Server, PostgreSQL, Oracle or MySQL/MariaDB. Database access starts only when the operator requests targeted diagnosis.
