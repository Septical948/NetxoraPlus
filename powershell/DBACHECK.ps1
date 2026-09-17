Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ====== CONFIG ======
$basePath = "C:\netxora"
$psPath   = "$basePath\powershell"
$sqlCmd   = "$basePath\Runprod_ExportCSV.CMD"

# ====== FORM ======
$form = New-Object System.Windows.Forms.Form
$form.Text = "DBACHECK - SQL Assessment Tool"
$form.Size = New-Object System.Drawing.Size(500,220)
$form.StartPosition = "CenterScreen"

# ====== LABEL ======
$label = New-Object System.Windows.Forms.Label
$label.Text = "Iniciando proceso..."
$label.AutoSize = $true
$label.Location = New-Object System.Drawing.Point(20,20)
$form.Controls.Add($label)

# ====== PROGRESS BAR ======
$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Location = New-Object System.Drawing.Point(20,60)
$progress.Size = New-Object System.Drawing.Size(440,30)
$progress.Minimum = 0
$progress.Maximum = 100
$form.Controls.Add($progress)

# ====== BOTON ======
$button = New-Object System.Windows.Forms.Button
$button.Text = "Ejecutar"
$button.Location = New-Object System.Drawing.Point(180,110)
$form.Controls.Add($button)

# ====== FUNCION PROGRESO ======
function Update-Progress($value, $text) {
    $progress.Value = $value
    $label.Text = $text
    $form.Refresh()
}

# ====== LOGICA ======
$button.Add_Click({

    try {
        $button.Enabled = $false

        # STEP 1
        Update-Progress 10 "Generando CSV desde SQL Server..."
        Start-Process -FilePath $sqlCmd -Wait -NoNewWindow

        # STEP 2
        Update-Progress 50 "Convirtiendo CSV a Excel..."
        powershell -NoProfile -ExecutionPolicy Bypass -File "$psPath\convertCSVtoEXCEL.ps1"

        # STEP 3
        Update-Progress 80 "Consolidando archivos Excel..."
        powershell -NoProfile -ExecutionPolicy Bypass -File "$psPath\consolidaEXCEL.ps1"

        # FIN
        Update-Progress 100 "Proceso finalizado correctamente"

        [System.Windows.Forms.MessageBox]::Show("Proceso completado con éxito","DBACHECK")

    }
    catch {
        [System.Windows.Forms.MessageBox]::Show("Error en ejecución: $_","DBACHECK ERROR")
    }

    $button.Enabled = $true
})

$form.Topmost = $true
$form.Add_Shown({$form.Activate()})
[void]$form.ShowDialog()