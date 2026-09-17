# Alpha 4.5 - Performance Incident Analyzer

- Active user requests with CPU, reads, logical reads, writes, waits and blocking.
- Current SQL/request inspection.
- Actionable current waiting tasks view.
- Query Memory Grants inspection.
- Contextual point-in-time diagnostic rules.
- Evidence Snapshot.
- Read-only: no KILL, cache clear, index, MAXDOP or memory changes.

# Alpha 4.4 - AlwaysOn / HA Analyzer

- Added read-only Availability Group analyzer.
- Local replica/database synchronization state and health.
- Send/redo queues and rates.
- Replica, database and listener detail.
- Evidence Snapshot and contextual diagnosis.
- No FAILOVER, RESUME, REMOVE DATABASE or AG changes.

# Changelog

## 2.0.0-alpha.3.2 - 2026-09-16
- Long Transaction Incident Analyzer: diagnóstico operativo de locks, LOG y TempDB Version Store.
- Detecta visualmente/diagnósticamente sleeping sessions con transacción abierta.
- Evidence Snapshot obligatorio antes de habilitar KILL.
- KILL protegido revalida SPID + login + host + transaction begin antes de ejecutar.
- Confirmación explícita del operador y verificación post-acción.
- Rollback Status mediante KILL <spid> WITH STATUSONLY.
- Integra correcciones Alpha 3.1 de tipos SQL y ORDER BY con DISTINCT.

# Registro de cambios

## 2.0.0-alpha.1 — 2026-09-15
### Agregado
- Estructura independiente `DBACHECK2/`; DBACHECK original no se modifica.
- Proyecto WPF sobre .NET 8.
- Windows Authentication integrada.
- Prueba de conexión SQL Server.
- Quick Check directo, sin CSV intermedio.
- Checks iniciales: databases, blocking, transacciones >30 min, tempdb, Version Store, uso de log, backups full >24h, último estado de jobs habilitados, top wait acumulativo y AlwaysOn.
- Clasificación inicial OK / INFO / WARNING / CRITICAL.
- Wiki, arquitectura, roadmap y guía de uso.

### Cambio de arquitectura
El flujo histórico `CMD -> SQLCMD -> CSV -> PowerShell -> Excel` se conserva en DBACHECK original, pero DBACHECK2 comienza a consultar SQL Server directamente para diagnóstico interactivo.

### Pendiente de validar
- Primer build en Windows con Visual Studio 2022/.NET 8.
- Compatibilidad de las consultas con todas las versiones SQL Server objetivo.
- Permisos mínimos reales por check para reducir dependencia de `sysadmin`.


## 2.0.0-alpha.2 — 2026-09-16
- Quick Check validated against a real SQL Server 2019 test environment.
- Fixed collation conflicts in all STRING_AGG collectors.
- Fixed TEMPDB percentage calculation using tempdb.sys.dm_db_file_space_usage.
- Isolated collectors: one failed check no longer aborts the full Quick Check.
- Added ERROR status with collector-specific technical evidence.
- Added elapsed time and OK/Warning/Critical/Error counters.
- Improved DataGrid header contrast and added a full detail panel.
- Filtered SOS_WORK_DISPATCHER from the basic cumulative wait summary.
- Quick Check remains read-only.

## 2.0.0-alpha.3 - 2026-09-16
- Added Long Transaction Incident Analyzer.
- Added read-only DMV investigation for transactions >= 30 minutes.
- Shows SPID, duration, database, login, host, application, status, open transactions, blocker, wait, CPU/reads/writes and input buffer.
- Added Evidence Snapshot generation and clipboard copy.
- Explicitly no KILL/corrective execution in Alpha 3.
- Preserves Alpha 2 Quick Check and isolated collectors.

## 2.0.0-alpha.3.3 - 2026-09-16
- Long Transaction Analyzer: agrega VER SQL, VER LOCKS y VER TRANSACCIÓN.
- Diagnóstico separa evidencia observada de causalidad: Version Store se etiqueta como valor GLOBAL de tempdb y no se atribuye automáticamente al SPID.
- LOG usa sys.master_files para tamaño total y sys.databases.log_reuse_wait_desc para contexto de reutilización.
- Clasificación explícita SLEEPING WITH OPEN TRANSACTION e impacto actual (blocking, locks, log, Version Store).
- Mantiene Evidence Snapshot obligatorio, revalidación de identidad y confirmación explícita antes de KILL.

## 2.0.0-alpha.4 - 2026-09-16
- Nuevo Blocking Incident Analyzer.
- Reconstrucción de root blocker y cadena de bloqueo multinivel.
- Vista de SQL/input buffer y locks por SPID.
- Evidence Snapshot de blocking con sesiones afectadas y longest wait.
- Primera validación del módulo Blocking es solo lectura; no ejecuta KILL.
- Long Transaction Analyzer Alpha 3.3 se conserva sin cambios funcionales.

## 2.0.0-alpha.4.1 - 2026-09-16
- Added TempDB & Version Store Incident Analyzer.
- Read-only TempDB summary, per-file size/used/free/growth, Version Store context, oldest active snapshot transaction context and Evidence Snapshot.
- Explicitly avoids causal attribution of global Version Store to a specific SPID without supporting evidence.
