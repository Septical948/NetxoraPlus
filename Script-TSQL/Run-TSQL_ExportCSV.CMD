@echo off
echo *************************************************************************************
echo ***************            SQL SERVER HEALTH CHECK TRIGGERDB          ***************
echo *************************************************************************************

echo  Este script ejecutara varios .sql sobre su servidor MSSQL  " 
echo  Debe ejecutarlo con un usuario de AD con permisos de SYSADMIN en SQL Server y localadmin en Windows

CHOICE /M "Desea continuar (Y/N)"

IF ERRORLEVEL 2 goto salir

cls

echo Procesando Archivos .....................

SET mypath=%cd%
set sqlname =  %computername%
set /p sqlname="Nombre del Servidor SQL Server (o enter para local): " || Set sqlname=%computername%
echo %sqlname%

for /F "delims=/" %%X in ('dir /b /on /ON *.sql') do ( 
  echo %%X 
  SQLCMD -S%sqlname% -s; -d master -E -i"%mypath%\%%X" -o"%mypath%\OUTtsql\%%~nX.csv" -s"|" 
)
pause


:salir
echo bye 



