### CONVERT CSV TO EXCEL V1 ###
### Author: Nicolas Demaria ###
### Creado: 12/10/2025      ###
### ult mod: 03/25/2026     ###

# ================= CONFIG =================
$rootFolder = "C:\netxora"
$modulePath = "C:\netxora\modules\ImportExcel"

# =============== VALIDACION ===============
if (!(Test-Path $rootFolder)) {
    Write-Host "ERROR: No existe la carpeta $rootFolder" -ForegroundColor Red
    exit 1
}

# ===== CARGAR MODULO OFFLINE =====
$moduleFile = Get-ChildItem -Path $modulePath -Recurse -Filter ImportExcel.psd1 |
              Where-Object { $_.FullName -notmatch "\\en\\" } |
              Select-Object -First 1

if (!$moduleFile) {
    Write-Host "ERROR: No se encontró ImportExcel.psd1 en $modulePath" -ForegroundColor Red
    exit 1
}

Import-Module $moduleFile.FullName -Force

Write-Host "==============================================="
Write-Host "   PROCESO DE CONVERSION CSV -> EXCEL (OFFLINE)"
Write-Host "==============================================="
Write-Host "Ruta base: $rootFolder"
Write-Host ""

# ========== RECORRER SERVIDORES ===========
Get-ChildItem -Path $rootFolder -Directory | ForEach-Object {

    $serverFolder = $_.FullName
    $serverName = $_.Name

    Write-Host ">>> Servidor: $serverName" -ForegroundColor Cyan

    # Buscar CSV
    $csvFiles = Get-ChildItem -Path $serverFolder -Filter *.csv -File

    if (!$csvFiles) {
        Write-Host "    (Sin archivos CSV)" -ForegroundColor DarkGray
        return
    }

    # Crear output
    $outputFolder = Join-Path $serverFolder "output"
    if (!(Test-Path $outputFolder)) {
        New-Item -ItemType Directory -Path $outputFolder | Out-Null
    }

    foreach ($file in $csvFiles) {

        $csvFile = $file.FullName
        $outputFile = Join-Path $outputFolder ($file.BaseName + ".xlsx")

        # Evitar reproceso
        if (Test-Path $outputFile) {
            Write-Host "    - Omitido (ya existe): $($file.Name)" -ForegroundColor Yellow
            continue
        }

        # Validar archivo vacío
        if ($file.Length -eq 0) {
            Write-Host "    - Archivo vacío: $($file.Name)" -ForegroundColor DarkYellow
            continue
        }

        try {
            Write-Host "    - Procesando: $($file.Name)"

            Import-Csv $csvFile -Delimiter "|" |
                Export-Excel $outputFile `
                    -WorksheetName "Data" `
                    -AutoSize `
                    -TableName "Data"

            Write-Host "      OK -> $outputFile" -ForegroundColor Green
        }
        catch {
            Write-Host "      ERROR en $($file.Name): $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    Write-Host ""
}

Write-Host "==============================================="
Write-Host "        PROCESO FINALIZADO"
Write-Host "==============================================="