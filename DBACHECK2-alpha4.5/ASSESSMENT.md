# DBACHECK2 Assessment Engine

Status: Beta 2 foundation

## Purpose

The Assessment Engine is the multi-engine evolution path for DBAHEALTCHECK.

DBAHEALTCHECK was designed for SQL Server. DBACHECK2 Assessment must preserve that operational knowledge while exposing a common assessment model that also supports PostgreSQL, Oracle and MySQL/MariaDB.

The current implementation is the first functional baseline:
- reads saved DBACHECK server profiles
- uses the existing engine provider
- runs read-only provider checks
- normalizes results into `AssessmentCheck`
- adds Why it matters / Recommended action / Verification
- stores assessment history in SQLite
- supports SQL Server, PostgreSQL, Oracle and MySQL/MariaDB through the same UI

This baseline does **not** claim that the historical DBAHEALTCHECK SQL Server script library has already been migrated. The legacy source/scripts must be imported and validated separately.

## Assessment check contract

Each check is normalized to:

- CheckId
- Engine
- Category
- Title
- Status
- Severity
- Summary
- Evidence
- WhyItMatters
- RecommendedAction
- Verification
- Capability
- ReadOnly
- Duration
- Timestamp

Statuses include OK, WARNING, CRITICAL, ERROR, NO PERMISSION, NOT ENABLED, UNSUPPORTED and UNAVAILABLE.

## Categories

Common categories:
- Platform
- Capacity
- Temporary
- Transactions
- Logs
- Maintenance
- Backup
- High Availability
- Performance
- Indexes
- Configuration
- Security
- Other

The category is common, but evidence remains engine-native.

## Engine direction

### SQL Server

Target pack:
- version / edition / support context
- database status and recovery models
- data/log capacity and autogrowth
- TempDB layout and usage
- Version Store
- open and long transactions
- transaction log usage / reuse waits
- backup coverage and age
- SQL Agent job failures
- AlwaysOn / replication / log shipping
- memory / waits / I/O
- Query Store
- top SQL
- index usage / duplicate / missing-index evidence
- configuration and security exposure

Historical DBAHEALTCHECK scripts should be migrated into this pack only after output equivalence is verified.

### PostgreSQL

Target pack:
- version / role
- database and relation size
- sessions / max_connections
- long transactions / idle in transaction
- locks and blocking
- autovacuum / analyze health
- dead tuples / bloat indicators
- XID age / wraparound risk
- WAL generation / retention / archive failures
- replication slots
- streaming replication / lag
- checkpoint behavior
- temp usage
- sequential scans / index usage
- pg_stat_statements capability
- backup evidence

### Oracle

Target pack:
- version / edition / connection capability
- tablespaces / datafiles / autoextend
- TEMP / UNDO
- redo log groups and switch frequency
- archive log / FRA
- sessions / processes
- blocking chains / long transactions
- invalid objects
- unusable indexes
- stale statistics
- scheduler jobs
- waits / top SQL when available
- Data Guard
- RMAN evidence
- ASM/FRA when capability exists

Old Oracle releases must degrade by capability and return UNSUPPORTED / UNAVAILABLE / NO PERMISSION instead of failing the complete assessment.

### MySQL / MariaDB

Target pack:
- version / engine context
- connection usage / max_connections
- InnoDB transactions
- locks / deadlocks
- buffer pool
- dirty pages / redo
- binlog
- replication / lag
- slow/long SQL
- performance_schema capability
- temp tables / disk temp tables
- database/table growth
- index evidence
- event scheduler
- backup evidence

## Modes

Current:
- Quick: findings plus key platform/HA context
- Full: all checks currently available from the provider baseline

Planned:
- Incident Assessment
- Custom categories
- Scheduled Assessment (Plus)

## History

Assessment runs are stored in:

`%LOCALAPPDATA%\Netxora\DBACHECK2\assessments.db`

Planned use:
- compare assessments
- baseline and delta
- trend
- technical report / Excel / PDF
- correlate historical risk with current Alert Inbox incidents

## Migration rule for DBAHEALTCHECK

Do not rewrite historical SQL Server checks from memory.

For each legacy check:
1. import the original script/query
2. document its purpose and required permissions
3. run it against a known SQL Server
4. compare legacy output with DBACHECK2 output
5. map it to an Assessment category
6. preserve raw evidence
7. add remediation and verification guidance
8. only then mark the legacy check as migrated

This prevents knowledge loss while allowing DBACHECK2 to become truly multi-engine.
