<#
.SYNOPSIS
    Garante que o PostgreSQL (container ambrosio-postgres no Docker do WSL2) esteja de pé e
    acessível em localhost:5432.
.DESCRIPTION
    Nesta máquina o Docker Engine roda dentro da distro WSL2 (skill `wsl-docker`) e o WSL
    DESLIGA a VM quando nenhum processo Windows mantém a distro aberta — o que derruba o proxy da
    porta 5432 no meio de uma rodada de testes (sintoma: `Failed to connect to 127.0.0.1:5432`,
    HTTP 500 em qualquer endpoint que toque o banco).

    O script sobe o container com restart=unless-stopped e deixa um `wsl.exe -e sleep` vivo,
    gravando o PID em backend/.wsl-keepalive.pid para que release-postgres.ps1 o encerre.

    Uso:
        powershell -File scripts/ensure-postgres.ps1          # prepara o banco
        powershell -File scripts/ensure-postgres.ps1 -Release # derruba o keep-alive
#>
param(
    [int]$Port = 5432,
    [string]$Container = 'ambrosio-postgres',
    [int]$TimeoutSeconds = 30,
    [switch]$Release
)

$backend = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $backend '.wsl-keepalive.pid'

if ($Release) {
    if (Test-Path $pidFile) {
        $old = (Get-Content $pidFile -ErrorAction SilentlyContinue | Out-String).Trim()
        if ($old) {
            Get-Process -Id $old -ErrorAction SilentlyContinue | ForEach-Object {
                Stop-Process -Id $_.Id -Force
                Write-Output "keep-alive WSL finalizado (pid $($_.Id))"
            }
        }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'banco liberado (o WSL pode desligar quando quiser)'
    exit 0
}

$existing = $null
if (Test-Path $pidFile) {
    $keepPid = (Get-Content $pidFile -ErrorAction SilentlyContinue | Out-String).Trim()
    if ($keepPid) { $existing = Get-Process -Id $keepPid -ErrorAction SilentlyContinue }
}

if ($null -eq $existing) {
    $proc = Start-Process -FilePath 'wsl.exe' -ArgumentList @('-e', 'sleep', '3600') `
        -WindowStyle Hidden -PassThru
    Set-Content -Path $pidFile -Value $proc.Id -NoNewline
    Write-Output "keep-alive WSL iniciado (pid $($proc.Id))"
    Start-Sleep -Seconds 5
} else {
    Write-Output "keep-alive WSL já ativo (pid $($existing.Id))"
}

wsl -e docker start $Container 2>&1 | Out-Null
wsl -e docker update --restart unless-stopped $Container 2>&1 | Out-Null

for ($i = 0; $i -lt $TimeoutSeconds; $i++) {
    Start-Sleep -Seconds 1
    $listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($listening) {
        Write-Output "postgres pronto em localhost:$Port"
        exit 0
    }
}

Write-Output "FALHA: nada escutando na porta $Port em ${TimeoutSeconds}s."
wsl -e docker logs --tail 20 $Container 2>&1
exit 1
