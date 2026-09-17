@echo off
setlocal enabledelayedexpansion

echo ==========================================================
echo        netxora - SQL SERVER HEALTH CHECK
echo ==========================================================
echo.

REM === RUTAS BASE ===
set "basePath=C:\netxora"
set "psPath=%basePath%\powershell"
set "sqlCmd=%basePath%\Runprod_ExportCSV.CMD"

REM === VALIDACIONES ===
if not exist "%basePath%" (
    echo ERROR: No existe %basePath%
    pause
    exit /b
)

if not exist "%sqlCmd%" (
    echo ERROR: No existe Runprod_ExportCSV.CMD
    pause
    exit /b
)

if not exist "%psPath%\convertCSVtoEXCEL.ps1" (
    echo ERROR: No existe convertCSVtoEXCEL.ps1
    pause
    exit /b
)

if not exist "%psPath%\consolidaEXCEL.ps1" (
    echo ERROR: No existe consolidaEXCEL.ps1
    pause
    exit /b
)

echo.
echo [1/3] Generando CSV desde SQL Server...
echo ----------------------------------------------------------

call "%sqlCmd%"

if errorlevel 1 (
    echo ERROR en generacion de CSV
    pause
    exit /b
)

echo.
echo [2/3] Convirtiendo CSV a Excel...
echo ----------------------------------------------------------

powershell -NoProfile -ExecutionPolicy Bypass -File "%psPath%\convertCSVtoEXCEL.ps1"

if errorlevel 1 (
    echo ERROR en conversion CSV -> Excel
    pause
    exit /b
)

echo.
echo [3/3] Consolidando archivos Excel...
echo ----------------------------------------------------------

powershell -NoProfile -ExecutionPolicy Bypass -File "%psPath%\consolidaEXCEL.ps1"

if errorlevel 1 (
    echo ERROR en consolidacion de Excel
    pause
    exit /b
)

echo.
echo ==========================================================
echo        PROCESO COMPLETO FINALIZADO OK
echo ==========================================================
echo.

pause
exit /b