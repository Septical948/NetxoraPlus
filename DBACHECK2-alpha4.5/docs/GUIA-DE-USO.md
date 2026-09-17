# DBACHECK 2 — Guía de uso

## Requisitos de esta alpha
- Windows 10/11 o Windows Server compatible con .NET 8.
- Visual Studio 2022 con **.NET Desktop Development** para compilar desde código fuente.
- Acceso de red a la instancia SQL Server.
- Usuario Windows con permisos suficientes para consultar las DMV utilizadas.

## Primera ejecución desde Visual Studio
1. Abrir `DBACHECK2/DBACHECK2.sln`.
2. Esperar la restauración del paquete `Microsoft.Data.SqlClient`.
3. Compilar la solución.
4. Ejecutar `DBACheck2.App`.

## Probar conexión
Escribir el servidor en formato `SERVIDOR`, `SERVIDOR\\INSTANCIA` o `tcp:SERVIDOR,PUERTO`. DBACHECK2 utiliza la identidad Windows del proceso. `Trust server certificate` está disponible para laboratorios/entornos con certificados no confiables; en producción se recomienda una cadena TLS correctamente validada.

Pulsar **Probar conexión**. La aplicación mostrará servidor, versión y edición cuando la conexión sea correcta.

## Quick Check
Pulsar **QUICK CHECK**. La alpha consulta:
- estado de databases;
- blocking actual;
- transacciones activas de más de 30 minutos;
- ocupación de tempdb;
- Version Store;
- máximo uso de transaction log;
- backups full de bases de usuario en las últimas 24 horas;
- último resultado de jobs habilitados;
- principal wait acumulativo desde el arranque;
- salud local de AlwaysOn, cuando HADR está habilitado.

Los resultados se ordenan por severidad: `CRITICAL`, `WARNING`, `INFO`, `OK`.

## Importante
Esta alpha es **solo diagnóstico**. No ejecuta KILL, failover, shrink, rebuild ni cambios de configuración. Esas acciones se incorporarán posteriormente con evidencia previa, confirmación explícita y validación posterior.

## DBACHECK original
No eliminarlo. Los scripts históricos y el pipeline de Excel continúan siendo la referencia del assessment completo hasta que esa función sea migrada a DBACHECK2.


## Alpha 2 - Quick Check
Cada collector se ejecuta de forma independiente. Si uno falla, aparece como ERROR y los demás continúan.
Selecciona una fila para ver la evidencia completa. TEMPDB muestra porcentaje usado, MB totales/libres y
distribución de Version Store, objetos de usuario y objetos internos. Quick Check continúa siendo solo lectura.

## Alpha 3 - Incident Analyzer
Use **INCIDENT ANALYZER** to inspect transactions open for 30 minutes or more. Select a SPID to see its Evidence Snapshot. **CAPTURAR EVIDENCIA** refreshes the snapshot text and copies it to the Windows clipboard for ticket/email/RCA use. Alpha 3 is read-only: it does not execute KILL or corrective commands.

## Alpha 3.3 - Long Transaction Incident Analyzer
Al seleccionar una transacción larga se puede consultar Diagnóstico, SQL/Input Buffer, Locks y detalle de Transacción sin ejecutar acciones correctivas. El Version Store mostrado es global de tempdb y no implica que el SPID seleccionado sea su causa. CAPTURAR EVIDENCIA continúa siendo requisito para habilitar KILL PROTEGIDO; el KILL revalida SPID/login/host/inicio de transacción y requiere confirmación explícita.

## Alpha 4 - Blocking Incident Analyzer
Desde la pantalla principal usar **BLOCKING ANALYZER**. El módulo lista root blocker, SPID bloqueado, blocked-by, wait actual, base, login, host, aplicación y transacciones abiertas. **VER CADENA** reconstruye el árbol de bloqueo; **VER SQL** y **VER LOCKS** permiten investigar sin modificar SQL Server. **CAPTURAR EVIDENCIA** copia un snapshot al portapapeles. En la primera validación Alpha 4 el módulo Blocking no ejecuta KILL.
