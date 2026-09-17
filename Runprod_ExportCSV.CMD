@echo off
echo *************************************************************************************
echo ***************   SQL SERVER HEALTH CHECK NETXORA   *********************************
echo *************************************************************************************

echo Este script ejecutara varios .sql sobre su servidor MSSQL
echo Debe ejecutarlo con un usuario con permisos SYSADMIN

CHOICE /M "Desea continuar (Y/N)"
IF ERRORLEVEL 2 goto salir

cls
echo Procesando Archivos...

REM === Pedir nombre del SQL Server ===
set "sqlname=%computername%"
set /p sqlname="Nombre del Servidor SQL Server (Enter para local): "
if "%sqlname%"=="" set "sqlname=%computername%"

echo Servidor: %sqlname%

REM === Definir rutas ===
set "basePath=C:\netxora"
set "scriptPath=%basePath%\Script"
set "scriptIndexPath=%basePath%\Scriptindex"
set "outputBase=%basePath%\%sqlname%"

REM === Crear estructura ===
if not exist "%outputBase%" mkdir "%outputBase%"
if not exist "%outputBase%\excel" mkdir "%outputBase%\excel"
if not exist "%outputBase%\output" mkdir "%outputBase%\output"

echo.
echo Ejecutando scripts de carpeta Script...
echo.

REM === Ejecutar scripts de Script ===
for %%X in ("%scriptPath%\*.sql") do (
    echo Ejecutando %%~nxX
    SQLCMD -S %sqlname% -d master -E -i "%%X" -o "%outputBase%\%%~nX.csv" -s"|"
)

echo.
echo Ejecutando scripts de carpeta Scriptindex...
echo.

REM === Ejecutar scripts de Scriptindex ===
for %%X in ("%scriptIndexPath%\*.sql") do (
    echo Ejecutando %%~nxX
    SQLCMD -S %sqlname% -d master -E -i "%%X" -o "%outputBase%\%%~nX.csv" -s"|"
)

echo.
echo Generando info de discos...

wmic volume where driveletter!=null get driveletter,blocksize > "%outputBase%\blocksize.csv"

echo.
echo Proceso finalizado.
pause

:salir
echo bye