[CmdletBinding()]
param(
    [string]$TenantSlug = "lavixa",
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [ValidateRange(1024, 65515)]
    [int]$Port = 5004,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$frontend = Join-Path $root "frontend"
$apiProject = Join-Path $root "backend\src\Lavanderia.Api\Lavanderia.Api.csproj"
$apiWwwroot = Join-Path $root "backend\src\Lavanderia.Api\wwwroot"
$distBrowser = Join-Path $frontend "dist\lavanderia-demo\browser"
$buildRoot = Join-Path $root ".qa-build"
$publishDir = Join-Path $buildRoot "publish"
$migraciones = @(
    (Join-Path $root "backend\db\scripts\036_endurecimiento_saas_izipay.sql"),
    (Join-Path $root "backend\db\scripts\037_pedido_fotos.sql"),
    (Join-Path $root "backend\db\scripts\038_refresh_token_sede.sql")
)
$urlFile = Join-Path $buildRoot "ultima-url.txt"
$apiOut = Join-Path $buildRoot "api.out.log"
$apiErr = Join-Path $buildRoot "api.err.log"
$tunnelOut = Join-Path $buildRoot "tunnel.out.log"
$tunnelErr = Join-Path $buildRoot "tunnel.err.log"
$cloudflared = "C:\Users\Caleb\cloudflared.exe"
$localState = Join-Path $env:LOCALAPPDATA "Lavixa"

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontro '$Name' en PATH. Instala el requisito antes de continuar."
    }
}

function Remove-WorkspaceDirectory([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $root.TrimEnd('\') + '\'
    if (-not $full.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Se rechazo eliminar una ruta fuera del proyecto: $full"
    }
    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

function Get-OrCreateSecret([string]$Name, [int]$Bytes = 48) {
    New-Item -ItemType Directory -Path $localState -Force | Out-Null
    $path = Join-Path $localState $Name
    if (-not (Test-Path -LiteralPath $path)) {
        $buffer = New-Object byte[] $Bytes
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
        [Convert]::ToBase64String($buffer) | Set-Content -LiteralPath $path -Encoding ascii -NoNewline
    }
    return (Get-Content -LiteralPath $path -Raw).Trim()
}

function Wait-Ready([string]$Url, [int]$Seconds = 90, [bool]$Required = $true) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $true }
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)
    if ($Required) { throw "El servicio no quedo listo a tiempo: $Url" }
    return $false
}

function Get-AvailablePort([int]$PreferredPort, [int]$Attempts = 20) {
    for ($offset = 0; $offset -lt $Attempts; $offset++) {
        $candidate = $PreferredPort + $offset
        $listener = Get-NetTCPConnection -LocalPort $candidate -State Listen -ErrorAction SilentlyContinue
        if (-not $listener) { return $candidate }
    }
    throw "No se encontro un puerto libre entre $PreferredPort y $($PreferredPort + $Attempts - 1)."
}

Assert-Command "dotnet"
Assert-Command "node"
Assert-Command "npm.cmd"
Assert-Command "sqlcmd"
if (-not (Test-Path -LiteralPath $cloudflared)) {
    throw "No se encontro cloudflared en $cloudflared"
}

$requestedPort = $Port
$Port = Get-AvailablePort $requestedPort
if ($Port -ne $requestedPort) {
    Write-Host "El puerto $requestedPort esta ocupado; se usara automaticamente el puerto $Port." -ForegroundColor Yellow
}

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
Remove-Item -LiteralPath $apiOut, $apiErr, $tunnelOut, $tunnelErr, $urlFile -Force -ErrorAction SilentlyContinue

Write-Host "[1/6] Aplicando migraciones SQL pendientes..." -ForegroundColor Cyan
foreach ($sql in $migraciones) {
    & sqlcmd -S $SqlServer -E -b -C -I -d Lavanderia -i $sql
    if ($LASTEXITCODE -ne 0) { throw "La migracion SQL fallo ($sql) con codigo $LASTEXITCODE." }
}

Write-Host "[2/6] Compilando Angular para produccion..." -ForegroundColor Cyan
Push-Location $frontend
try {
    if (-not (Test-Path -LiteralPath (Join-Path $frontend "node_modules"))) {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci fallo con codigo $LASTEXITCODE." }
    }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "La compilacion Angular fallo con codigo $LASTEXITCODE." }
} finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath (Join-Path $distBrowser "index.html"))) {
    throw "Angular no genero $distBrowser\index.html"
}

Write-Host "[3/6] Sincronizando frontend y publicando API Release..." -ForegroundColor Cyan
Remove-WorkspaceDirectory $apiWwwroot
New-Item -ItemType Directory -Path $apiWwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $distBrowser "*") -Destination $apiWwwroot -Recurse -Force

Remove-WorkspaceDirectory $publishDir
& dotnet publish $apiProject -c Release -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo con codigo $LASTEXITCODE." }

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:Jwt__SecretKey = Get-OrCreateSecret "jwt-secret.txt" 64
$env:SeedAdmin__Password = Get-OrCreateSecret "seed-admin.txt" 32
$env:SeedPropietario__Password = Get-OrCreateSecret "seed-propietario.txt" 32
$env:DataProtection__KeysPath = Join-Path $localState "keys"
# Las fotos de evidencia se guardan fuera de la carpeta de publicacion (que se borra en cada
# republicacion) para que sobrevivan a los reinicios del demo.
$env:Fotos__Directorio = Join-Path $localState "fotos"

$apiProcess = $null
$tunnelProcess = $null
try {
    Write-Host "[4/6] Iniciando API endurecida..." -ForegroundColor Cyan
    $apiDll = Join-Path $publishDir "Lavanderia.Api.dll"
    $apiProcess = Start-Process -FilePath "dotnet" -ArgumentList @($apiDll) -WorkingDirectory $publishDir `
        -RedirectStandardOutput $apiOut -RedirectStandardError $apiErr -WindowStyle Hidden -PassThru
    Wait-Ready "http://127.0.0.1:$Port/health/ready" | Out-Null

    Write-Host "[5/6] Creando tunel HTTPS temporal..." -ForegroundColor Cyan
    $tunnelProcess = Start-Process -FilePath $cloudflared `
        -ArgumentList @("tunnel", "--url", "http://127.0.0.1:$Port", "--no-autoupdate", "--loglevel", "info") `
        -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelErr -WindowStyle Hidden -PassThru

    $deadline = (Get-Date).AddSeconds(75)
    $publicUrl = $null
    do {
        Start-Sleep -Seconds 1
        $logs = ""
        if (Test-Path -LiteralPath $tunnelOut) { $logs += Get-Content -LiteralPath $tunnelOut -Raw }
        if (Test-Path -LiteralPath $tunnelErr) { $logs += Get-Content -LiteralPath $tunnelErr -Raw }
        $match = [regex]::Match($logs, 'https://[a-z0-9-]+\.trycloudflare\.com', 'IgnoreCase')
        if ($match.Success) { $publicUrl = $match.Value.TrimEnd('/') }
        if ($tunnelProcess.HasExited) { throw "Cloudflare Tunnel se cerro. Revisa $tunnelErr" }
    } while (-not $publicUrl -and (Get-Date) -lt $deadline)

    if (-not $publicUrl) { throw "Cloudflare no entrego una URL dentro del tiempo esperado." }
    $publicReady = Wait-Ready "$publicUrl/health/ready" 75 $false

    $lavixaUrl = "$publicUrl/$TenantSlug/login"
    $ownerUrl = "$publicUrl/login"
    @(
        "Base: $publicUrl"
        "Lavixa: $lavixaUrl"
        "Propietario: $ownerUrl"
        "Puerto local: $Port"
        "Generada: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ) | Set-Content -LiteralPath $urlFile -Encoding utf8

    try { Set-Clipboard -Value $lavixaUrl } catch { }

    Write-Host "[6/6] LISTO PARA PRUEBAS" -ForegroundColor Green
    Write-Host ""
    Write-Host "Lavixa:     $lavixaUrl" -ForegroundColor Green
    Write-Host "Propietario: $ownerUrl" -ForegroundColor Yellow
    Write-Host "Health:      $publicUrl/health/ready"
    Write-Host ""
    if ($publicReady) {
        Write-Host "El tunel respondio correctamente desde Internet." -ForegroundColor Green
    } else {
        Write-Warning "Cloudflare entrego la URL, pero su DNS aun no responde. Espera 30-60 segundos y vuelve a abrirla."
    }
    Write-Host "La URL de Lavixa fue copiada al portapapeles. Esta URL cambia al reiniciar."
    Write-Host "No cierres esta ventana. Presiona Ctrl+C para detener API y tunel."

    if (-not $NoBrowser) { Start-Process $lavixaUrl }

    while (-not $apiProcess.HasExited -and -not $tunnelProcess.HasExited) {
        Start-Sleep -Seconds 2
    }
    throw "La API o el tunel se detuvo inesperadamente. Revisa los logs de $buildRoot"
}
finally {
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) { Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue }
}
