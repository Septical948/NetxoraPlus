# DBACHECK 2 — Beta 1 Full Platform

## Product contract
DBACHECK detects, correlates, explains, reports and preserves evidence. It does not perform SHRINK, recovery-model changes, AlwaysOn/cluster failover, replica removal/resume, or structural HA changes.

## Engines
- SQL Server: existing validated operational analyzers, compatibility routing, incident history/correlation.
- PostgreSQL: sessions, locks, long transactions, database size, WAL/XLOG, replication/standby.
- Oracle: sessions, blocking, long transactions, tablespace, archive/log mode.
- MySQL/MariaDB: sessions, long queries, database size, replication reporting foundation.

## Platform
- Server Profiles / global context
- Engine provider factory
- DBA Assistant over real collectors
- Quick Check / evidence
- Incident history and recurrence
- Incident Operations / technical reports
- Metrics JSON snapshots for external integrations
- Text technical report export service
- Read-only-first safety model

## Integration contract
Metrics snapshots are deliberately vendor-neutral JSON so adapters/exporters can map them to Prometheus, Zabbix, OpenTelemetry or other monitoring systems without coupling collectors to a monitoring vendor.

## QA gates before sellable release
1. Clean build and self-contained publish.
2. SQL Server 2014 + 2019 regression.
3. PostgreSQL 9.4/9.5 + modern validation.
4. Oracle legacy + modern validation.
5. MySQL/MariaDB validation.
6. Least-privilege permission matrix.
7. Credential security / no plaintext persisted secrets.
8. Timeouts, unavailable-host and partial-permission tests.
9. 24h/7d collector soak tests.
10. Installer, upgrade, rollback, documentation and licensing/package checks.

This branch is intentionally feature-complete-first and QA-driven afterward. Provider queries may require compatibility fixes discovered during QA.
