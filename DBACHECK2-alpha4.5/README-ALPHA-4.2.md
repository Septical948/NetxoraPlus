# DBACHECK 2 Alpha 4.2

Adds the read-only Transaction Log Analyzer.

Features:
- Log size and used percentage for online databases
- Recovery model and log_reuse_wait_desc
- Last LOG backup
- Log file size, autogrowth, max size, path and volume free space
- Oldest open transaction as contextual evidence
- Reuse-wait interpretation for ACTIVE_TRANSACTION, LOG_BACKUP, AVAILABILITY_REPLICA, REPLICATION and NOTHING
- Evidence Snapshot
- No SHRINK, recovery model changes, file changes, backup execution or KILL

Also normalizes the visible UTF-8 labels in the TempDB Analyzer.
