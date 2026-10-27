<#
.SYNOPSIS
    Finaliza a API Ambrosio iniciada por run-api.ps1.
.DESCRIPTION
    run-api.ps1 dá Start-Process em `dotnet run`, que por sua vez inicia o host
    `Ambrosio.API.exe` como filho. Matar apenas o pai (o PID gravado em .api.pid) deixaria o
    host vivo, segurando bin\Debug\*.dll e fazendo o próximo `dotnet build` falhar com MSB3027.
    Por isso o script também encerra os processos chamados Ambrosio.API* e, em último caso,
    qualquer dotnet/Ambrosio dono da porta da API.
#>
param(
    [string]$BaseUrl = 'http://localhost:5000'
)

$backend = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $backend '.api.pid'
$stopped = $false

if (Test-Path $pidFile) {
    $pidValue = (Get-Content $pidFile -ErrorAction SilentlyContinue | Out-String).Trim()
    if ($pidValue) {
        Get-Process -Id $pidValue -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force
            Write-Output "API finalizada (pid $($_.Id))"
            $stopped = $true
        }
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

# O host filho do `dotnet run`: é ele que trava as DLLs do build.
Get-Process -Name 'Ambrosio.API' -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.Id -Force
    Write-Output "host Ambrosio.API finalizado (pid $($_.Id))"
    $stopped = $true
}

# Fallback: qualquer processo pendurado na porta da API.
$port = ([System.Uri]$BaseUrl).Port
$conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
foreach ($conn in $conns) {
    $owner = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
    if ($owner -and ($owner.ProcessName -like 'dotnet*' -or $owner.ProcessName -like 'Ambrosio*')) {
        Stop-Process -Id $owner.Id -Force
        Write-Output "processo órfão na porta finalizado (pid $($owner.Id))"
        $stopped = $true
    }
}

if (-not $stopped) { Write-Output 'nenhuma API em execução' }
