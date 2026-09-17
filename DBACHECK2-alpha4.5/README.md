# DBACHECK 2

Nueva generación de DBACHECK. La versión original permanece intacta en la raíz del repositorio.

## Estado actual — 2.0.0-alpha.1
- Aplicación WPF .NET 8 para Windows.
- Conexión directa a SQL Server con Windows Authentication.
- Prueba de conexión.
- Quick Check de: databases, blocking, transacciones largas, tempdb, Version Store, log, backups, jobs, waits y AlwaysOn.
- Quick Check de solo lectura: no ejecuta acciones correctivas.
- No depende de `C:\netxora`, CSV, SQLCMD ni PowerShell para este flujo interactivo.

## Ejecutar
Abrir `DBACHECK2.sln` con Visual Studio 2022 + .NET 8 Desktop Development, restaurar NuGet y ejecutar `DBACheck2.App`.

> Esta alpha no fue compilada dentro del entorno de generación porque allí no está instalado el SDK .NET. El primer build debe validarse en Windows/Visual Studio antes de considerarla release.


## Alpha 4
Incluye Blocking Incident Analyzer de solo lectura: root blocker, cadena de bloqueo, SQL, locks y Evidence Snapshot.
