<#
.SYNOPSIS
    Marca (ou desmarca) tarefas como concluídas em specs/<feature>/tasks.md sem reescrever o
    arquivo inteiro.
.DESCRIPTION
    tasks.md é UTF-8 com BOM e contém texto acentuado: sobrescrevê-lo a partir de conteúdo
    "lido" por outra ferramenta corromperia os acentos. Aqui só se troca `- [ ] Txxx` por
    `- [X] Txxx` nas tarefas informadas, preservando BOM e demais bytes.

    Aceita `-Ids 27,28,29` e `-Ids 27 28 29`: chamado via `powershell -File`, o `-File` agrupa a
    lista separada por vírgula em um único argumento, então ela é re-dividida aqui.
.PARAMETER Ids
    Identificadores com ou sem prefixo/zero: 27, T028, T046 ...
.EXAMPLE
    powershell -File backend/scripts/mark-tasks.ps1 -Ids 27,28,29,30
.EXAMPLE
    powershell -File backend/scripts/mark-tasks.ps1 -Ids 46 -Unmark
#>
param(
    [string]$Feature = '001-api-cadastro-clientes-pedidos',
    [Parameter(Mandatory = $true)][string[]]$Ids,
    [switch]$Unmark,
    [switch]$Report
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $root "specs\$Feature\tasks.md"
if (-not (Test-Path $path)) { throw "tasks.md não encontrado em $path" }

$bytes = [System.IO.File]::ReadAllBytes($path)
$hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)

if ($Report) {
    $open = ([regex]::Matches($text, '- \[ \] T')).Count
    $done = ([regex]::Matches($text, '- \[[xX]\] T')).Count
    Write-Output "concluídas=$done em aberto=$open total=$($open + $done)"
    exit 0
}

$target = if ($Unmark) { '- [ ]' } else { '- [X]' }
$from   = if ($Unmark) { '- [X]' } else { '- [ ]' }

# Normaliza "27,28,29" (um único argumento via -File) e "27 28 29" (array).
$tokens = ($Ids -join ',') -split '[,\s]+' | Where-Object { $_ }

$changed = @()
foreach ($raw in $tokens) {
    $digits = ($raw -replace '\D', '')
    if (-not $digits) { Write-Output "AVISO: identificador inválido '$raw'"; continue }
    $id = 'T{0:d3}' -f [int]$digits

    if ($text.Contains("$from $id")) {
        $text = $text.Replace("$from $id", "$target $id")
        $changed += $id
    } elseif ($text.Contains("$target $id")) {
        $changed += "$id (já estava)"
    } else {
        Write-Output "AVISO: tarefa $id não encontrada"
    }
}

if ($changed.Count -gt 0) {
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    [System.IO.File]::WriteAllText($path, $text, $encoding)
}

Write-Output ($changed -join ', ')
