# Arquitectura

## Original
`DBACHECK.exe/DBACHECK.ps1 -> Runprod_ExportCSV.CMD -> SQLCMD -> CSV -> convertCSVtoEXCEL.ps1 -> consolidaEXCEL.ps1 -> XLSX`.

## DBACHECK 2
`WPF UI -> Collector SQL -> Rule/Severity -> Incident Analyzer -> Evidence -> Approved Action -> Verification -> Report`.

La alpha implementa las tres primeras piezas para Quick Check. Incident Analyzer, acciones, verificación e informes son siguientes etapas.

## Seguridad
- Windows Authentication en alpha.
- No persistir credenciales SQL.
- Consultas Quick Check de solo lectura.
- Futuras acciones SAFE/IMPACT/CRITICAL exigirán confirmación según riesgo.
- Antes de KILL u otra acción de impacto se guardará evidencia de sesión, login, host, app, SQL, transacción, wait y bloqueo.
