# Implementation Plan: Credenciais do +Vendas guardadas, importação pelo plugin e "Atualizar base"

**Branch**: `008-import-credentials` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/008-import-credentials/spec.md`

## Summary

Login e senha do +Vendas passam a ficar guardados, cifrados com AES-256-GCM e chave dedicada, em `mais_vendas_accounts`, e são editados numa tela nova de configuração que nunca exibe a senha. A tela de importação deixa de pedir credenciais; o worker as decifra só no instante do login no +Vendas. O período máximo passa a 3 meses corridos. Uma rota nova "Atualizar base" calcula o período no servidor (último pedido importado até hoje) e recusa intervalos maiores que 3 meses. No plugin, `iniciar_importacao` passa a iniciar de fato e ganha a companheira `atualizar_base`; nenhuma tool recebe ou mostra credenciais.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (backend: API, host do MCP, worker); TypeScript 5.x / React 18 + Vite 6 (frontend)
**Primary Dependencies**: as existentes (EF Core 9 + Npgsql, FluentValidation, OpenIddict/ModelContextProtocol no host do MCP, Playwright no coletor); `System.Security.Cryptography.AesGcm` (BCL) para a cifra. Nenhum pacote novo
**Storage**: PostgreSQL, `AmbrosioContext`. Migração `AddMaisVendasCredentials`: 5 colunas em `mais_vendas_accounts` e 2 em `import_runs`
**Testing**: testes de API (`Ambrosio.ApiTests`, skill `dotnet-test-api`) com o coletor Fake; testes unitários do frontend (Vitest). Sem testes de unidade novos no backend (Princípio VIII)
**Target Platform**: containers Linux (homolog local no WSL2, produção via GitHub Actions); SPA no nginx do `emagine-deploy`
**Project Type**: web-service + SPA (aplicação existente)
**Performance Goals**: iniciar a importação em até 3 interações na tela (SC-001); execução iniciada pelo plugin visível na tela em até 5 s (SC-002, polling já existente)
**Constraints**: senha do +Vendas nunca devolvida, logada nem aceita pela conversa (FR-003/FR-018); cifra com chave fora da base (FR-002, SC-004); limite de 3 meses idêntico em tela, servidor e plugin (FR-010, SC-005)
**Scale/Scope**: 1 credencial por vendedora; 3 rotas novas de credenciais, 1 rota nova de importação, 1 tela nova, 1 tool nova e 2 tools alteradas

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Avaliação | Situação |
|---|---|---|
| I. Skills obrigatórias | `dotnet-architecture` (DTOs, AppService de cifra, serviço de credenciais), `dotnet-fluent-validation` (validators de credenciais e período), `dotnet-env` (chave nova nos ambientes), `dotnet-test-api` (testes), `react-architecture` (entidade de credenciais), `react-modal` e `react-alert` (confirmações e avisos), `wsl-docker` | PASS |
| II. Stack fixa | Sem dependência nova; AES-GCM da BCL | PASS |
| III. Casing | Pastas novas seguem o casing existente (`src/pages`, `src/services`, `src/contexts`, `src/hooks`, `src/types`) | PASS |
| IV. Convenções | DTOs com `[JsonPropertyName]` camelCase; nomes de tools e argumentos em pt-BR | PASS |
| V. Banco | Colunas novas em snake_case, `timestamp without time zone`, sem cascata | PASS |
| VI. Autenticação e segurança | Rotas `[Authorize]`, usuário do `sub`, leitura por `user_id`; chave de cifra por variável de ambiente com recusa no startup. **Ponto novo**: guardar credencial de terceiro de forma recuperável não está regulado | **Esclarecimento por emenda** ([constitution-amendment.md](constitution-amendment.md), 3.4.0 → 3.4.1) |
| VII. Ambientes e testes de API | Variável nova nos composes, no `.env.example`, no `.env.prod.example` e no pipeline; testes de API para todas as rotas novas e alteradas | PASS |
| VIII. Testes | Só testes de API no backend e unitários no frontend | PASS |

**Resultado**: segue com a emenda proposta (PATCH, esclarece o Princípio VI sem afrouxar regra existente).

**Re-check pós-design**: o desenho (data-model, contratos) não acrescenta violação. A emenda continua sendo o único ponto; ela formaliza o que R2/R3 já aplicam (cifra autenticada, chave dedicada, nunca devolver, decifrar só no uso, nenhuma tool de credenciais).

## Project Structure

### Documentation (this feature)

```text
specs/008-import-credentials/
├── plan.md                    # Este arquivo
├── research.md                # Fase 0: decisões (R1–R12)
├── data-model.md              # Fase 1: colunas novas e DTOs
├── quickstart.md              # Fase 1: configuração e verificação
├── constitution-amendment.md  # Emenda 3.4.1 (credenciais de terceiros)
├── contracts/
│   ├── api.md                 # Rotas de credenciais e importação
│   ├── mcp-tools.md           # iniciar_importacao, atualizar_base, sugestao_periodo_importacao
│   └── ui.md                  # Tela de configuração e tela de importação
├── checklists/requirements.md
└── tasks.md                   # Fase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosio.DTO/
│   ├── Imports/                 # MaisVendasCredentialsInfo, MaisVendasCredentialsUpdateInfo, ImportUpdateBaseInfo;
│   │                            # ImportRunStartInfo, ImportDefaultsInfo e ImportRunInfo alterados
│   ├── Enums/                   # MaisVendasCredentialsStatus, ImportRunStartedVia, ImportRunKind
│   └── Settings/MaisVendasSettings.cs   # CredentialsKey, MaxRangeMonths (sai MaxRangeDays)
├── Ambrosio.Domain/
│   ├── Models/MaisVendasAccount.cs      # credenciais cifradas e transições de status
│   ├── Models/ImportRun.cs              # StartedVia, Kind
│   ├── Services/MaisVendasCredentialService.cs (+ interface)   # GET/PUT/DELETE
│   ├── Services/ImportRunService.cs     # início sem credenciais no DTO, Atualizar base, 3 meses
│   ├── Services/ImportExecutionService.cs  # decifra no login; marca needsUpdate na recusa
│   ├── Common/ImportJob.cs              # sem Login/Password
│   └── Validators/                      # ImportRunStartInfoValidator (3 meses), credenciais
├── Ambrosio.Infra.Interfaces/AppServices/IMaisVendasCredentialCipherAppService.cs
├── Ambrosio.Infra/
│   ├── AppServices/MaisVendasCredentialCipherAppService.cs   # AES-256-GCM, AAD por usuário e campo
│   ├── Context/AmbrosioContext.cs + Migrations/AddMaisVendasCredentials
│   └── Repository/ImportRunRepository.cs  # data do último pedido importado (já existe para a sugestão)
├── Ambrosio.API/
│   ├── Controllers/ImportsController.cs   # credentials (GET/PUT/DELETE), runs/update-base
│   └── Program.cs                         # validação da chave no startup
├── Ambrosio.McpServer/
│   ├── Mcp/Tools/ImportTools.cs           # iniciar_importacao inicia; atualizar_base; sugestão com credenciais
│   └── Program.cs                         # validação da chave no startup
└── Ambrosio.ApiTests/
    ├── Controllers/MaisVendasCredentialsTests.cs, ImportsControllerTests.cs (ajustes)
    └── McpServer/ImportToolsTests.cs (ajustes), ParityTests.cs (exceções de credenciais)

frontend/src/
├── types/maisVendasCredentials.ts
├── services/maisVendasCredentialsService.ts
├── contexts/MaisVendasCredentialsContext.tsx
├── hooks/useMaisVendasCredentials.ts
├── pages/MaisVendasSettingsPage.tsx (+ .test.tsx)
├── pages/ImportPage.tsx, lib/importForm.ts (+ testes)   # sem credenciais, 3 meses, Atualizar base
└── App.tsx, components/ (menu)                          # rota /configuracoes/mais-vendas

docker-compose.yml, docker-compose-prod.yml, .env.example, .env.prod.example, .github/workflows/deploy-prod.yml
bruno/Imports/                                           # credenciais e Atualizar base
```

**Structure Decision**: aplicação existente (backend .NET em Clean Architecture + SPA React). Nenhum projeto novo; a feature estende a importação (006) e o plugin (007) nos mesmos projetos.

## Complexity Tracking

| Violação | Por que é necessária | Alternativa mais simples descartada porque |
|---|---|---|
| Guardar a senha do +Vendas de forma recuperável (Princípio VI regula só senhas do Ambrosio, que são hash) | A spec pede importar sem redigitar a senha e iniciar pelo plugin, onde a senha não pode passar; o +Vendas só aceita a senha em claro no login | Hash não permite logar no +Vendas; continuar pedindo a senha a cada importação contraria a spec. A emenda limita o risco: cifra autenticada, chave dedicada, nunca devolver, decifrar só no uso |
