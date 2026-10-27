# Helper de setup: cria a estrutura de pastas exigida pelas skills dotnet-test / dotnet-test-api.
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$tests = Join-Path $Root 'Ambrosio.Tests'
$apiTests = Join-Path $Root 'Ambrosio.ApiTests'

$testDirs = @(
    'Domain/Services', 'Domain/Models', 'Domain/ValueObjects', 'Domain/Validators',
    'Infra/AppServices', 'Infra/Repository'
)
foreach ($d in $testDirs) {
    $full = Join-Path $tests $d
    New-Item -ItemType Directory -Force -Path $full | Out-Null
    Set-Content -Path (Join-Path $full '.gitkeep') -Value '' -NoNewline
}

foreach ($d in @('Fixtures', 'Controllers', 'Helpers')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $apiTests $d) | Out-Null
}

Write-Output "Estrutura de pastas de testes criada em $Root"
