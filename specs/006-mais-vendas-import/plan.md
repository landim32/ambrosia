# Implementation Plan: Importação do +Vendas pelo frontend (pedidos, clientes e fotos)

**Branch**: `006-mais-vendas-import` | **Date**: 2026-10-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-mais-vendas-import/spec.md` (HU `docs/historias/004-bot-migracao-mais-vendas.md`)

## Summary

Entregar a tela `/importacao` e os endpoints `/imports/mais-vendas/*` para a vendedora importar os pedidos do +Vendas informando login e senha **a cada execução**. O coletor roda **dentro da API** (`Microsoft.Playwright`, Chromium headless, somente leitura com allowlist e ritmo ≤ 2 req/s) num `BackgroundService` com fila em memória: `POST /runs` grava a execução sem credenciais, enfileira o job (credenciais só no objeto em memória) e responde `202`; a tela acompanha por polling de 3 s. O período é dividido em blocos de 7 dias; cada bloco é coletado, validado contra os contadores e totais da origem e só então gravado **pedido a pedido**, cada um em transação própria com upsert por chave natural `(user_id, order_number, order_date, kind)` e impressão digital `source_hash`, cliente sob demanda por `cpf_hash` e fotos via `IImageService.StoreFromServerAsync(…, ImageSource.Bot)`. Novas tabelas `import_runs`, `import_run_blocks`, `import_run_changes` e `mais_vendas_accounts` (vínculo por hash com pepper + bloqueio de 3 recusas/15 min); `orders` ganha `source`, `source_hash`, `captured_at`, `source_changed_at` e `order_items` ganha `image_id`. Para cumprir a constituição §VIII (backend só com testes de API), a Infra traz um coletor **Fake** determinístico usado na homologação, escolhido por configuração e proibido em produção.

Decisões detalhadas em [research.md](./research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 + Vite 6 (frontend)

**Primary Dependencies**:
- Backend (existentes): ASP.NET Core 8, EF Core 9.x + Npgsql, FluentValidation 11, Swashbuckle 8, `AWSSDK.S3` via `IImageService` (feature 004), `Microsoft.Extensions.Hosting.Abstractions` (BackgroundService)
- Backend (**novo**): `Microsoft.Playwright` 1.x (versão fixa; só na Infra) + Chromium headless instalado na imagem Docker da API (research §2)
- Frontend (existentes, sem pacote novo): React Router 6, Tailwind 4 + shadcn/ui, i18next 25, sonner, Fetch via `apiRequest`
- Testes: xUnit + Flurl.Http + FluentAssertions (`Ambrosia.ApiTests`); Vitest + Testing Library (frontend)

**Storage**: PostgreSQL — migração `AddMaisVendasImport` (4 tabelas novas, 2 alteradas, research §6/§8, [data-model.md](./data-model.md)); storage S3 da feature 004 para as fotos (`ImageSource.Bot`, dedup por SHA-256). **Nenhuma** credencial do +Vendas em banco, arquivo, log ou storage.

**Testing**: `Ambrosia.ApiTests` contra Homolog (`http://localhost:8080/api`) com `MaisVendas:Collector=Fake` (cenários por login/senha, research §3/§16); testes unitários Vitest no frontend; nenhum teste unitário novo no backend (§VIII); coletor real validado manualmente em modo simulação (quickstart §7).

**Target Platform**: API Linux em container (Debian `aspnet:8.0` + Chromium) atrás do gateway nginx (`/api`); frontend SPA estático

**Project Type**: web application (`backend/` + `frontend/`), mais Dockerfile/compose/deploy

**Performance Goals**: `202` em ≤ 3 s após o envio (SC-006); progresso com atraso ≤ 5 s (polling 3 s, FR-015); mês de cota (~50 pedidos, com fotos) em ≤ 15 min respeitando ≤ 2 req/s na origem (SC-007); até 2 execuções simultâneas por servidor (FR-017)

**Constraints**:
- Credenciais só em memória durante a execução; nunca em banco/log/resposta/Swagger/`localStorage` (FR-005–FR-008, SC-004)
- Coleta somente leitura: allowlist de hosts, rotas e elementos; nunca "Novo carrinho", "Sair" ou BOX (FR-042, SC-012); sessão nova por execução, sem `storageState` (FR-044)
- Validação de contadores/totais **antes** de gravar o bloco (FR-010/011, SC-009); gravação atômica por pedido (FR-012)
- Uma execução ativa por vendedora (índice único parcial, FR-016); interrupção do servidor → `failed` (FR-020)
- Toda leitura filtra por `user_id` do `sub`; outra vendedora → 404 (FR-053, §VI)
- API não sobe com `MaisVendas` fora das faixas nem com `Collector=Fake` em Production (research §13)
- Fila em memória: **uma réplica** da API (ver Complexity Tracking)

**Scale/Scope**: 4 tabelas novas + 2 alteradas, 1 migração; 4 entidades novas + 2 alteradas, 5 enums; ~12 DTOs (API) + 6 de transporte do coletor; 2 validators; 1 controller com 6 rotas; 3 serviços de domínio (`ImportRunService`, `ImportExecutionService`, utilitários `ImportBlockPlanner`/`SourceHashCalculator`/`MaisVendasNormalizer`/`BlockValidator`); 2 repositórios; 3 AppServices (coletor Playwright, coletor Fake, hash de conta); 1 `BackgroundService` + fila; frontend: 1 página, 5 componentes, 1 context, 1 hook, 1 service, 1 types, 1 lib, ~40 chaves i18n; ~30 testes de API; ~12 testes unitários de frontend; Dockerfile, compose (homolog/prod), `.env.example`, `deploy/README.md`, Bruno

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Princípio | Como o plano atende | Status |
|---|---|---|---|
| I | Skills obrigatórias | `dotnet-architecture` para entidades, repositórios, AppServices, DTOs, Fluent API, migração e DI no `Startup`; `dotnet-fluent-validation` para `ImportRunStartInfoValidator`/`ImportRunListQueryValidator`; `react-architecture` para `types/import.ts`, `Services/importService.ts`, `Contexts/ImportContext.tsx`, `hooks/useImport.ts` e registro em `main.tsx`; `dotnet-env` para appsettings, `.env.example`, compose e deploy; `wsl-docker` para subir a homolog; `dotnet-test-api` para `ImportsControllerTests`. `dotnet-graphql` não se aplica (sem GraphQL). | ✅ |
| II | Stack fixa | Nenhuma tecnologia proibida. `Microsoft.Playwright` é pacote novo restrito à Infra e **não substitui** nada da stack; EF Core continua o único ORM; frontend sem dependência nova (Fetch via `apiRequest`, Context API, Tailwind/shadcn). Playwright aqui é **funcionalidade de produto no servidor** (coleta de um sistema externo), não ferramenta de teste de frontend — a proibição do §II/§VIII é sobre testes E2E (ver §VIII). | ✅ |
| III | Case sensitivity | Frontend: `src/Contexts/ImportContext.tsx`, `src/Services/importService.ts`, `src/hooks/useImport.ts`, `src/types/import.ts`, `src/components/imports/`. Backend em PascalCase (`Imports/`, `HostedServices/`). | ✅ |
| IV | Convenções de código | DTOs `ImportRunStartInfo`/`ImportRunInfo`/`ImportRunDetailInfo`/`ImportDefaultsInfo` com `[JsonPropertyName]`; enums camelCase pelo conversor global; `ErrorResult` em português; namespaces file-scoped; `_camelCase` em campos; frontend com `interface`, arrow functions, `IMPORT_POLL_MS` em UPPER_CASE; strings em `pt-BR.ts`. | ✅ |
| V | Banco de dados | Tabelas snake_case plural, PKs `{entidade}_id` identity, `{tabela}_pkey`, FKs `fk_{pai}_{filho}` com `ClientSetNull` (inclusive `order_items.image_id`), `timestamp without time zone`, `varchar(n)`, enums `integer` com default. Sem cascata: blocos/alterações são removidos só por exclusão explícita futura. | ✅ |
| VI | Autenticação e segurança | `ImportsController` com `[Authorize]`, sem `[AllowAnonymous]`; `userId` só do `sub` (o DTO de entrada carrega login/senha **do +Vendas**, nunca `userId`); repositórios filtram `user_id` como primeira cláusula; 404 para outra vendedora. Credenciais do +Vendas: só em memória, não logadas, `format: password` no Swagger, nenhuma coluna. Hash de conta com pepper do ambiente. Nenhuma rota de token sem credencial. **Exceção justificada**: `MarkInterruptedAsync` lê sem `userId` (recuperação no startup, nunca por endpoint). | ✅ (1 exceção justificada) |
| VII | Ambientes e testes de API | Os três ambientes pela `dotnet-env`: Homolog com `MaisVendas__Collector=Fake` e `shm_size`; Production com `Playwright`; Development alterna por `appsettings.Development.json`. Endpoints novos ganham testes de API no mesmo ciclo; homolog reconstruída e suíte completa verde ao fim (quickstart §3/§9). Testes criam seus usuários por cadastro + login. | ✅ |
| VIII | Estratégia de testes | Backend só por testes de API (o coletor Fake existe para isso); frontend só com Vitest/RTL; nenhum Playwright/E2E **de teste** é adicionado — o pacote `Microsoft.Playwright` é dependência de runtime do backend para a coleta do +Vendas, decisão já antecipada na spec (Assumptions, "Alinhamento com a constituição"). | ✅ |
| — | Variáveis de ambiente | Novas: `MaisVendas__*` (research §13) no backend; `MAIS_VENDAS_COLLECTOR` no `.env` de homolog; nada novo em `VITE_*`. | ✅ |
| — | Tratamento de erros | `try/catch` no padrão existente: `ValidationException` → 400, `DuplicateException` (execução ativa) → 409, `ImportLockedException` → 429 + `Retry-After`, `ImportRunNotCancellableException` → 409, `NotFoundException` → 404, `Exception` → 500 com `ex.Message` — e por isso nenhuma exceção da coleta carrega login, senha ou CPF na mensagem. | ✅ |

**Resultado do gate (pré-Phase 0)**: APROVADO, com uma exceção justificada (§VI, leitura sem `userId` restrita à recuperação no startup).

**Re-avaliação pós-Phase 1**: o data-model mantém §V (índice único parcial `ux_import_runs_user_active`, FKs `ClientSetNull`); o contrato mantém §VI (6 rotas `[Authorize]`, 404 para outra dona, nenhum campo de credencial em saída, `Retry-After` só com segundos); o quickstart fecha o ciclo com homolog reconstruída + ApiTests (§VII) e frontend só unitário (§VIII). Desvios registrados em Complexity Tracking: fila em memória (uma réplica), coletor Fake selecionável por configuração, Chromium na imagem da API. APROVADO.

## Project Structure

### Documentation (this feature)

```text
specs/006-mais-vendas-import/
├── plan.md                 # Este arquivo
├── spec.md                 # Especificação (Clarifications 2026-10-27)
├── decisions.md            # Decisões do autopiloto (clarify)
├── research.md             # Phase 0: decisões técnicas (§1–§18)
├── data-model.md           # Phase 1: tabelas, enums, entidades, DTOs, interfaces
├── quickstart.md           # Phase 1: validação ponta a ponta
├── contracts/
│   └── imports-api.md      # Rotas /imports/mais-vendas, DTOs, códigos, contrato de UI
├── checklists/requirements.md
└── tasks.md                # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosia.DTO/
│   ├── Enums/OrderSource.cs, ImportRunMode.cs, ImportRunStatus.cs, ImportBlockStatus.cs, ImportChangeAction.cs   # NOVOS
│   ├── Imports/ImportRunStartInfo.cs, ImportDefaultsInfo.cs, ImportRunInfo.cs, ImportRunDetailInfo.cs,
│   │   ImportRunBlockInfo.cs, ImportRunChangeInfo.cs, ImportRunBreakdownInfo.cs, ImportRunCountsInfo.cs, ImportRunListQuery.cs   # NOVOS (API)
│   ├── Imports/Collector/MaisVendasCredentials.cs, MaisVendasLoginResult.cs, CollectedBlock.cs, CollectedOrder.cs,
│   │   CollectedOrderItem.cs, CollectedImage.cs                                                      # NOVOS (transporte)
│   ├── Orders/OrderInfo.cs, OrderDetailInfo.cs, OrderItemInfo.cs                                    # + source / imageId
│   └── Settings/MaisVendasSettings.cs                                                                # NOVO
├── Ambrosia.Infra.Interfaces/
│   ├── Repository/IImportRunRepository.cs, IMaisVendasAccountRepository.cs                           # NOVOS
│   └── AppServices/IMaisVendasCollectorAppService.cs, IAccountHashAppService.cs                      # NOVOS
├── Ambrosia.Domain/
│   ├── Models/ImportRun.cs, ImportRunBlock.cs, ImportRunChange.cs, MaisVendasAccount.cs              # NOVOS
│   ├── Models/Order.cs, OrderItem.cs                                                                 # + Source/SourceHash/CapturedAt/SourceChangedAt, ApplyImported; ImageId
│   ├── Common/ImportBlockPlanner.cs, SourceHashCalculator.cs, MaisVendasNormalizer.cs, BlockValidator.cs, ImportJob.cs   # NOVOS
│   ├── Interfaces/IImportRunService.cs, IImportExecutionService.cs, IImportRunQueue.cs               # NOVOS
│   ├── Services/ImportRunService.cs, ImportExecutionService.cs                                       # NOVOS
│   ├── Exceptions/ImportLockedException.cs, ImportRunNotCancellableException.cs                      # NOVOS
│   └── Validators/ImportRunStartInfoValidator.cs, ImportRunListQueryValidator.cs                     # NOVOS
├── Ambrosia.Infra/
│   ├── Ambrosia.Infra.csproj                                                                         # + Microsoft.Playwright
│   ├── AppServices/MaisVendas/PlaywrightMaisVendasCollectorAppService.cs, NavigationGuard.cs, OriginRateLimiter.cs   # NOVOS
│   ├── AppServices/MaisVendas/FakeMaisVendasCollectorAppService.cs, FakeScenarios.cs                 # NOVOS (homolog)
│   ├── AppServices/AccountHashAppService.cs                                                          # NOVO
│   ├── Repository/ImportRunRepository.cs, MaisVendasAccountRepository.cs                             # NOVOS
│   ├── Context/AmbrosiaContext.cs                                                                    # + 4 DbSets, Fluent API, colunas novas
│   └── Migrations/*_AddMaisVendasImport.cs                                                           # NOVO (dotnet ef)
├── Ambrosia.Application/
│   ├── Settings/AmbrosiaSettings.cs                                                                  # + MaisVendas
│   ├── Imports/ImportRunQueue.cs                                                                     # NOVO (Channel)
│   ├── HostedServices/ImportRunWorker.cs                                                             # NOVO
│   └── Startup.cs                                                                                    # settings, repos, AppServices (coletor por config), services, fila, worker
├── Ambrosia.API/
│   ├── Controllers/ImportsController.cs                                                              # NOVO: 6 rotas [Authorize]
│   ├── Controllers/ApiControllerBase.cs                                                              # + ImportLockedError (429), NotCancellableError (409)
│   ├── Program.cs                                                                                    # lê/valida MaisVendas; descrição do Swagger
│   ├── appsettings*.json                                                                             # seção MaisVendas por ambiente
│   └── Dockerfile                                                                                    # Chromium + deps (research §2)
├── Ambrosia.ApiTests/
│   ├── Controllers/ImportsControllerTests.cs                                                         # NOVO
│   ├── Controllers/UnauthorizedAccessTests.cs, IsolationTests.cs                                     # + rotas /imports
│   ├── Helpers/ImportRunWaiter.cs                                                                    # NOVO (polling até status final)
│   └── appsettings.Test.json                                                                         # + MaisVendas:* (prazos/limites esperados)
└── README.md                                                                                         # seção de importação

frontend/src/
├── types/import.ts                                                                                   # NOVO
├── Services/importService.ts                                                                         # NOVO
├── Contexts/ImportContext.tsx                                                                        # NOVO (polling 3 s)
├── hooks/useImport.ts                                                                                # NOVO
├── lib/importForm.ts (+ .test.ts)                                                                    # NOVO: regras de data/credenciais
├── pages/ImportPage.tsx                                                                              # NOVO
├── components/imports/ImportForm.tsx, ImportProgress.tsx, ImportReport.tsx, ImportHistory.tsx, ImportStatusBadge.tsx (+ testes)   # NOVOS
├── components/layout/AppHeader.tsx                                                                   # + link "Importar"
├── App.tsx, main.tsx                                                                                 # rota /importacao; ImportProvider
├── types/order.ts                                                                                    # + source / imageId
└── i18n/locales/pt-BR.ts                                                                             # + import.*

docker-compose.yml / docker-compose-prod.yml   # MaisVendas__Collector, shm_size no serviço api
.env.example / .env.prod.example               # MAIS_VENDAS_COLLECTOR
deploy/README.md                               # memória/shm do Chromium, checklist de produção
bruno/Imports/*.bru                            # NOVOS: defaults, iniciar, atual, relatório, histórico, cancelar
docs/historias/004-bot-migracao-mais-vendas.md # Spec → 006-mais-vendas-import; status
specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml   # + /imports/mais-vendas (contrato consolidado)
```

**Structure Decision**: aplicação web existente, sem projetos novos. O coletor é um **AppService da Infra** atrás de `IMaisVendasCollectorAppService` (como `ObjectStorageAppService`), com duas implementações escolhidas no `Startup` por `MaisVendas:Collector`; o Domain só vê `CollectedBlock`/`CollectedOrder` e concentra as regras (planejamento de blocos, validação de contadores, normalização, hash, upsert, resultado final). A fila (`Channel`) e o `ImportRunWorker` ficam na Application, como o `PendingImageCleanupService`, porque só o composition root conhece todas as dependências e abre um escopo por execução. A gravação por pedido fica no repositório (`UpsertImportedOrderAsync`) para a transação curta ser explícita e única. No frontend, o padrão da 002 é seguido à letra (service singleton sobre `apiRequest`, Context com `useCallback`, hook, provider em `main.tsx`), com a senha restrita ao estado local da página.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| §VI: `IImportRunRepository.MarkInterruptedAsync` lê/escreve sem `userId` | Ao reiniciar, o worker precisa marcar como `failed` toda execução `queued`/`running` de **qualquer** vendedora (FR-020) | Iterar por usuário não acrescenta isolamento: o método roda no `StartAsync` do worker, não atende requisição nem expõe dados; nenhum endpoint o chama (a revisão verifica). |
| Pacote `Microsoft.Playwright` + Chromium na imagem da API (~300 MB a mais, memória por execução) | O +Vendas é uma SPA sem API documentada; a coleta exige navegador e precisa rodar no servidor para a senha não sair do processo (research §1/§2) | Serviço Node separado duplicaria a stack e faria a senha trafegar entre processos; `HttpClient` puro depende do contrato não inspecionado de `client-data` (fica como otimização futura dentro do mesmo AppService). Limite de 2 execuções simultâneas e `shm_size` mitigam o custo. |
| Coletor **Fake** selecionável por configuração, dentro da Infra de produção | Constituição §VIII só permite testar o backend por testes de API contra a homologação, e o +Vendas real não pode ser manipulado (falhas, divergências, fotos com erro) | Um site falso raspado pelo Playwright duplicaria seletores e testaria HTML inventado; condicional de compilação esconderia o código da revisão. `Program.ValidateSettings` recusa `Fake` em Production. |
| Fila em memória → **uma réplica** da API; cancelamento e semáforo locais ao processo | Guardar o job fora do processo exigiria persistir ou transmitir a senha (FR-005/FR-006) | Hangfire/Quartz serializam os argumentos do job em banco; fila externa leva a senha para outro serviço. O índice único parcial garante a regra "uma execução por vendedora" mesmo assim; escalar horizontalmente fica para uma HU futura com outro desenho de credenciais. |
| Nova superfície de configuração obrigatória (`MaisVendas__*`) e Dockerfile mais pesado também em Homolog | A mesma imagem serve homolog e produção; a validação no startup evita rodar com limites inválidos | Imagem separada sem Chromium para homolog quebraria a paridade de ambientes (§VII). |
