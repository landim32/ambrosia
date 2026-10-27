<#
.SYNOPSIS
    Sobe a API Ambrosio em background e aguarda o Swagger responder.
.DESCRIPTION
    Usado pelo quickstart e pelos testes HTTP de Ambrosio.ApiTests, que exigem a API no ar
    em Development. Escreve o log em backend/logs/api-{EnvironmentName}.log e o PID do
    processo em backend/.api.pid (lido por stop-api.ps1).

    Usa --no-launch-profile: o perfil "http" de launchSettings.json fixa
    ASPNETCORE_ENVIRONMENT=Development e a porta 5000, o que ignoraria tanto o parâmetro
    -EnvironmentName quanto -BaseUrl aqui (e impediria testar o comportamento em Production).
#>
param(
    [string]$BaseUrl = 'http://localhost:5000',
    [string]$EnvironmentName = 'Development',
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$backend = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $backend 'logs'
$logFile = Join-Path $logDir "api-$EnvironmentName.log"
$errFile = Join-Path $logDir "api-$EnvironmentName.err.log"
$pidFile = Join-Path $backend '.api.pid'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Derruba uma instância anterior para não deixar duas APIs disputando a porta.
if (Test-Path $pidFile) {
    $old = (Get-Content $pidFile -ErrorAction SilentlyContinue | Out-String).Trim()
    if ($old) {
        Get-Process -Id $old -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force
            Write-Output "processo anterior finalizado (pid $($_.Id))"
        }
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

$env:ASPNETCORE_ENVIRONMENT = $EnvironmentName

Write-Output "iniciando API ($EnvironmentName) em $BaseUrl ..."
$proc = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', 'Ambrosio.API', '--no-build', '--no-launch-profile', '--url', $BaseUrl) `
    -WorkingDirectory $backend `
    -RedirectStandardOutput $logFile `
    -RedirectStandardError $errFile `
    -WindowStyle Hidden -PassThru

Set-Content -Path $pidFile -Value $proc.Id -NoNewline
Write-Output "pid $($proc.Id) | log $logFile"

$ready = $false
for ($i = 0; $i -lt ($TimeoutSeconds * 2); $i++) {
    Start-Sleep -Milliseconds 500
    try {
        Invoke-WebRequest -Uri "$BaseUrl/swagger/index.html" -UseBasicParsing -TimeoutSec 3 | Out-Null
        $ready = $true
        break
    } catch {
        if ($proc.HasExited) { break }
    }
}

if ($proc.HasExited) {
    Write-Output "FALHA: a API terminou com código $($proc.ExitCode). Últimas linhas do log:"
    Get-Content $logFile -Tail 30 -ErrorAction SilentlyContinue
    Write-Output '--- stderr ---'
    Get-Content $errFile -Tail 30 -ErrorAction SilentlyContinue
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    exit 1
}

if (-not $ready) {
    Write-Output "FALHA: Swagger não respondeu em $BaseUrl dentro de ${TimeoutSeconds}s."
    exit 1
}

Write-Output "API pronta em $BaseUrl (swagger em $BaseUrl/swagger)"
