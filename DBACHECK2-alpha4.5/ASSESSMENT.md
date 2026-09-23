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


## Capability-first compatibility model

Full Assessment no longer assumes that all supported servers expose the same catalog views, DMVs or functions.

Every engine pack follows this rule:

```
concept -> detect version/capability -> choose compatible collector
                                 -> AVAILABLE
                                 -> NO PERMISSION
                                 -> NOT ENABLED
                                 -> UNSUPPORTED
                                 -> UNAVAILABLE
```

A missing modern view on an old server must not fail the complete assessment.

### SQL Server legacy behavior

SQL Server Full Assessment executes the original DBAHEALTCHECK SQL library. If an original check references a DMV, column, function or object that does not exist on the detected SQL Server release, DBACHECK records that check as `UNSUPPORTED` instead of terminating the run.

Permission failures are recorded as `NO PERMISSION`.

### PostgreSQL Full Pack 1

The first engine-native PostgreSQL pack now covers:

- database capacity
- connection saturation
- long transactions
- idle in transaction
- blocking / lock waits
- XID age
- vacuum / analyze evidence
- dead tuples
- unused index candidates
- invalid indexes
- sequential-scan candidates
- WAL archiver
- replication slots when supported
- streaming replication
- version-aware checkpoint statistics
- core memory/WAL settings
- pg_stat_statements capability
- backup evidence boundary

Version-specific behavior includes old `waiting` lock evidence before PostgreSQL 9.6, XLOG function names before PostgreSQL 10, and `pg_stat_checkpointer` on PostgreSQL 17+.

### Oracle Full Pack 1

Oracle Full Assessment uses the configured provider mode:

- Modern ODP.NET
- Legacy OraOLEDB
- Auto fallback

The pack preserves the same DBA concepts while degrading according to release capability. It currently covers:

- sessions/processes capacity
- transactions
- blocking
- tablespace capacity
- TEMP capacity
- archive mode
- redo configuration and switch frequency
- unusable indexes
- duplicate index definitions
- invalid objects
- statistics recency/staleness
- DBMS_JOB on legacy releases
- Scheduler jobs on modern releases
- core configuration
- database role
- FRA where supported
- RMAN evidence where supported
- UNDO management vs legacy rollback-segment boundary

Oracle 8i/9i-specific gaps are represented as `UNSUPPORTED` or `UNAVAILABLE`; the assessment is not aborted.

### MySQL / MariaDB Full Pack 1

The first MySQL/MariaDB pack covers:

- connection capacity
- long InnoDB transactions
- long SQL
- lock waits
- database size
- duplicate index definitions
- unused index candidates when Performance Schema evidence exists
- tables without primary keys
- large DATA_FREE candidates
- temporary-table disk ratio
- InnoDB buffer/dirty-page evidence
- deadlock counter
- binary-log configuration
- replication
- core InnoDB settings
- Event Scheduler
- backup evidence boundary

Compatibility rules distinguish MariaDB, MySQL 5.x and MySQL 8.x. For example, replication uses legacy `SHOW SLAVE STATUS` on MariaDB/MySQL 5.x and `SHOW REPLICA STATUS` on MySQL 8.x.

### Design rule: preserve concept, not SQL syntax

Checks are not mechanically translated across engines.

Examples:

- SQL Server heap detection does not become an Oracle "heap problem".
- SQL Server 900-byte index checks are SQL Server-specific.
- unused-index evidence is only reported where the engine provides sufficiently meaningful usage instrumentation.
- backup status is not inferred when the engine has no authoritative universal backup history.

The common layer is the DBA question being asked; the implementation and capability boundary remain engine-native.
