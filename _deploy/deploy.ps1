# ============================================================
#  Despliegue LaviSystem desde la carpeta oficial
#  (ejecutar en PowerShell COMO ADMINISTRADOR)
#  Hace: respaldo -> detener servicio -> copiar DLL + wwwroot -> iniciar.
# ============================================================
$ErrorActionPreference = 'Stop'
try { Start-Transcript -Path 'C:\LaviSystem\_deploy-log.txt' -Force | Out-Null } catch {}

$appDir = 'C:\LaviSystem\app'
$dllSrc = 'C:\Fuentes Lavanderia\lavanderia\backend\src\Lavanderia.Api\bin\Release\net9.0'
$wwwSrc = 'C:\Fuentes Lavanderia\lavanderia\frontend\dist\lavanderia-demo\browser'
$stamp  = Get-Date -Format 'yyyy-MM-dd_HHmm'
$backup = "C:\LaviSystem\app-deploy-backup-$stamp"

Write-Host "== 0) Comprobaciones ==" -ForegroundColor Cyan
if (-not (Test-Path "$dllSrc\Lavanderia.Api.dll")) { throw "No existe el DLL: $dllSrc" }
if (-not (Test-Path "$wwwSrc\index.html"))         { throw "No existe wwwroot: $wwwSrc" }

Write-Host "== 1) Respaldo (DLL + wwwroot actuales) -> $backup ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $backup | Out-Null
Copy-Item "$appDir\Lavanderia.Api.dll" "$backup\Lavanderia.Api.dll" -Force
robocopy "$appDir\wwwroot" "$backup\wwwroot" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Fallo respaldo wwwroot (robocopy $LASTEXITCODE)" }
Write-Host "   respaldo OK" -ForegroundColor Green

Write-Host "== 2) Detener servicio LaviSystem ==" -ForegroundColor Cyan
Stop-Service -Name 'LaviSystem'
(Get-Service LaviSystem).WaitForStatus('Stopped','00:00:30')
Write-Host "   detenido" -ForegroundColor Green

try {
    Write-Host "== 3) Copiar DLL nuevo ==" -ForegroundColor Cyan
    Copy-Item "$dllSrc\Lavanderia.Api.dll" "$appDir\Lavanderia.Api.dll" -Force
    if (Test-Path "$dllSrc\Lavanderia.Api.pdb") { Copy-Item "$dllSrc\Lavanderia.Api.pdb" "$appDir\Lavanderia.Api.pdb" -Force }

    Write-Host "== 4) Copiar wwwroot nuevo (con /flags) ==" -ForegroundColor Cyan
    robocopy $wwwSrc "$appDir\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Fallo copia wwwroot (robocopy $LASTEXITCODE)" }
}
finally {
    Write-Host "== 5) Iniciar servicio LaviSystem ==" -ForegroundColor Cyan
    Start-Service -Name 'LaviSystem'
    (Get-Service LaviSystem).WaitForStatus('Running','00:00:30')
}

Start-Sleep -Seconds 3
Write-Host "== 6) Verificacion ==" -ForegroundColor Cyan
"Servicio: " + (Get-Service LaviSystem).Status
try { $r = Invoke-WebRequest 'http://localhost:5004' -UseBasicParsing -TimeoutSec 15; "HTTP 5004: $($r.StatusCode) (len $($r.Content.Length))" }
catch { "AVISO: 5004 no respondio: $($_.Exception.Message)" }

Write-Host ""
Write-Host "LISTO. Respaldo en: $backup" -ForegroundColor Green
try { Stop-Transcript | Out-Null } catch {}
