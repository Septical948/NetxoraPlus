### ALL TO 1EXCEL V1.1 ###
### Author: Nicolas Demaria ###
### Creado: 13/10/2025      ###
### ult mod: 02/25/2026     ###

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
Write-Host "   CONSOLIDACION + RESUMEN AUTOMATICO"
Write-Host "==============================================="

Get-ChildItem -Path $rootFolder -Directory | ForEach-Object {

    $serverFolder = $_.FullName
    $serverName = $_.Name

    $outputFolder = Join-Path $serverFolder "output"
    $excelFolder  = Join-Path $serverFolder "excel"

    Write-Host ">>> Servidor: $serverName" -ForegroundColor Cyan

    if (!(Test-Path $outputFolder)) { return }

    $files = Get-ChildItem -Path $outputFolder -Filter *.xlsx -File
    if (!$files) { return }

    if (!(Test-Path $excelFolder)) {
        New-Item -ItemType Directory -Path $excelFolder | Out-Null
    }

    $outputFile = Join-Path $excelFolder ($serverName + "Consolidado.xlsx")

    if (Test-Path $outputFile) {
        Remove-Item $outputFile -Force
    }

    $usedNames = @{}
    $summary = @()

    foreach ($file in $files) {

        try {
            Write-Host "    - Procesando: $($file.Name)"

            $sheetName = $file.BaseName.Substring(0,[Math]::Min(31,$file.BaseName.Length))

            # Evitar duplicados
            $baseName = $sheetName
            $i = 1
            while ($usedNames.ContainsKey($sheetName)) {
                $sheetName = ($baseName.Substring(0,[Math]::Min(28,$baseName.Length))) + "_$i"
                $i++
            }
            $usedNames[$sheetName] = $true

            $data = Import-Excel $file.FullName

            # === RESUMEN ===
            $rowCount = ($data | Measure-Object).Count
            $colCount = if ($rowCount -gt 0) { ($data[0].PSObject.Properties.Name).Count } else { 0 }

            $summary += [PSCustomObject]@{
                Hoja      = $sheetName
                Filas     = $rowCount
                Columnas  = $colCount
                Archivo   = $file.Name
                Estado    = if ($rowCount -eq 0) { "VACIO" } else { "OK" }
            }

            # Exportar hoja
            $data | Export-Excel $outputFile `
                -WorksheetName $sheetName `
                -AutoSize `
                -Append
        }
        catch {
            Write-Host "      ERROR en archivo $($file.Name): $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    # Crear hoja resumen
    if ($summary.Count -gt 0) {
        $summary | Export-Excel $outputFile `
            -WorksheetName "RESUMEN" `
            -AutoSize `
            -BoldTopRow `
            -FreezeTopRow `
            -AutoFilter

        Write-Host "    ✔ Hoja RESUMEN generada" -ForegroundColor Green
    }

    Write-Host "    OK -> $outputFile" -ForegroundColor Green
    Write-Host ""
}

Write-Host "==============================================="
Write-Host "   CONSOLIDACION FINALIZADA"
Write-Host "==============================================="