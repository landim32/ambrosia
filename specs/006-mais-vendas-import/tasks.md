---

description: "Lista de tarefas da feature 006-mais-vendas-import"
---

# Tasks: Importação do +Vendas pelo frontend (pedidos, clientes e fotos)

**Input**: Documentos de design em `specs/006-mais-vendas-import/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/imports-api.md, quickstart.md

**Tests**: INCLUÍDOS. A constituição §VII exige testes de API para todo endpoint novo no mesmo ciclo (`Ambrosio.ApiTests` contra a homologação com `MaisVendas:Collector=Fake`), e o plan.md prevê ~30 testes de API e ~12 testes unitários de frontend (Vitest + RTL). Pela §VIII, **nenhum** teste unitário novo no backend e **nenhum** teste Playwright/E2E de frontend.

**Organization**: tarefas agrupadas por história de usuário para permitir implementação e validação independentes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história de usuário à qual a tarefa pertence (US1…US7)
- Caminhos relativos à raiz do repositório (`backend/`, `frontend/`, `bruno/`, `deploy/`)

## Skills obrigatórias (constituição §I)

- `dotnet-architecture`: entidades, repositórios, AppServices, DTOs, Fluent API, migração, DI no `Startup`
- `dotnet-fluent-validation`: `ImportRunStartInfoValidator`, `ImportRunListQueryValidator`
- `dotnet-env`: appsettings, `.env*.example`, compose, deploy
- `dotnet-test-api`: `ImportsControllerTests`, `UnauthorizedAccessTests`, `IsolationTests`
- `react-architecture`: `types/import.ts`, `Services/importService.ts`, `Contexts/ImportContext.tsx`, `hooks/useImport.ts`, registro em `main.tsx`
- `wsl-docker`: subir/reconstruir a homologação

---

## Phase 1: Setup (infraestrutura compartilhada)

**Purpose**: dependência nova, configuração `MaisVendas` e ambientes

- [X] T001 Adicionar `PackageReference` de `Microsoft.Playwright` com **versão fixa** (1.x, sem curinga) em `backend/Ambrosio.Infra/Ambrosio.Infra.csproj` e rodar `dotnet restore backend/Ambrosio.sln`
- [X] T002 [P] Criar `MaisVendasSettings` em `backend/Ambrosio.DTO/Settings/MaisVendasSettings.cs` com as chaves e padrões do research §13: `Collector = "Playwright"` (`Playwright` | `Fake`), `BaseUrl = "https://maisvendas.somalabs.com.br"`, `BlockDays = 7`, `MaxRangeDays = 366`, `MaxConcurrentRuns = 2`, `MaxFailedLogins = 3`, `LockoutMinutes = 15`, `MinDelayMs = 300`, `MaxDelayMs = 900` e subclasse `Fake { DelayMs = 50 }`
- [X] T003 [P] Adicionar a seção `MaisVendas` em `backend/Ambrosio.API/appsettings.json` (padrões do §13), `backend/Ambrosio.API/appsettings.Development.json` (`"Collector": "Playwright"`), `backend/Ambrosio.API/appsettings.Homolog.json` (`"Collector": "Fake"`) e `backend/Ambrosio.API/appsettings.Production.json` (`"Collector": "Playwright"`), seguindo a skill `dotnet-env`
- [X] T004 [P] Adicionar `MAIS_VENDAS_COLLECTOR=Fake` em `.env.example` e `MAIS_VENDAS_COLLECTOR=Playwright` em `.env.prod.example` (com comentário: `Fake` é recusado em Production)
- [X] T005 [P] Em `docker-compose.yml`, adicionar `MaisVendas__Collector: ${MAIS_VENDAS_COLLECTOR:-Fake}` no bloco `x-api-environment` e `shm_size: 512m` no serviço `api`; em `docker-compose-prod.yml`, `MaisVendas__Collector: ${MAIS_VENDAS_COLLECTOR:-Playwright}` e `shm_size: 512m` no serviço `api` (estágio `migrator` inalterado)
- [X] T006 [P] Adicionar a seção `MaisVendas` em `backend/Ambrosio.ApiTests/appsettings.Test.json` com os valores esperados pela suíte: `BlockDays: 7`, `MaxRangeDays: 366`, `MaxFailedLogins: 3`, `LockoutMinutes: 15`, `RunTimeoutSeconds: 180`

---

## Phase 2: Foundational (pré-requisitos bloqueantes)

**Purpose**: modelo de dados, migração, contratos internos, fila/worker e coletor Fake base, dos quais todas as histórias dependem

**⚠️ CRITICAL**: nenhuma história começa antes do fim desta fase

### Enums e DTOs (`Ambrosio.DTO`)

- [X] T007 [P] Criar os enums em `backend/Ambrosio.DTO/Enums/`: `OrderSource.cs` (`Manual = 1`, `MaisVendas = 2`), `ImportRunMode.cs` (`DryRun = 1`, `Apply = 2`), `ImportRunStatus.cs` (`Queued = 1`, `Running = 2`, `Succeeded = 3`, `Failed = 4`, `Partial = 5`, `Cancelled = 6`), `ImportBlockStatus.cs` (`Pending = 1`, `Running = 2`, `Succeeded = 3`, `Failed = 4`, `NotExecuted = 5`), `ImportChangeAction.cs` (`Inserted = 1`, `Updated = 2`, `Unchanged = 3`, `Missing = 4`, `Conflict = 5`, `Error = 6`); serialização camelCase pelo conversor global
- [X] T008 [P] Criar os DTOs da API em `backend/Ambrosio.DTO/Imports/` com `[JsonPropertyName]` camelCase e namespace file-scoped: `ImportRunStartInfo.cs` (`login`, `password` com `[DataType(DataType.Password)]`, `startDate`, `endDate` como `DateOnly`, `dryRun`), `ImportDefaultsInfo.cs` (`suggestedStartDate?`, `suggestedEndDate`, `blockDays`, `maxRangeDays`, `lockedUntil?`), `ImportRunCountsInfo.cs` (`inserted`, `updated`, `unchanged`, `missing`, `failed`, `conflicts`, `customersCreated`, `imagesStored`, `imagesFailed`), `ImportRunInfo.cs` (`id`, `mode`, `status`, `startDate`, `endDate`, `blockDays`, `blocksTotal`, `blocksFinished`, `currentBlockNo?`, `cancelRequested`, `requestedAt`, `startedAt?`, `finishedAt?`, `durationSeconds?`, `counts`, `errorMessage?`), `ImportRunBlockInfo.cs`, `ImportRunChangeInfo.cs` (`orderId?`, `orderNumber`, `orderDate`, `kind`, `channel?`, `origin?`, `action`, `changedFields[]?`, `errorMessage?`), `ImportRunBreakdownInfo.cs` (`channel?`, `origin?`, `kind`, `action`, `count`), `ImportRunDetailInfo.cs` (herda `ImportRunInfo` + `blocks[]`, `changes[]`, `breakdown[]`, `previousRunOrdersCount?`, `countDropWarning`) e `ImportRunListQuery.cs` (`page = 1`, `pageSize = 10`). **Nenhum** DTO de saída tem `login`, `password` ou `accountHash` (contrato, garantia transversal)
- [X] T009 [P] Criar os DTOs de transporte do coletor em `backend/Ambrosio.DTO/Imports/Collector/`: `MaisVendasCredentials.cs` (`Login`, `Password`; sem `[JsonPropertyName]`, `ToString()` sobrescrito para não expor valores), `MaisVendasLoginResult.cs` (`Outcome`: `Accepted`/`Refused`/`UnsupportedStep`; `AccountIdentifier?`), `CollectedBlock.cs` (`Orders[]`, `TabCounters` aba→n, `TabTotals` aba→decimal), `CollectedOrder.cs` (`OrderNumber`, `OrderDate`, `Kind`, `Channel`, `Origin`, `Status`, `Total`, `OriginalSaleOn?`, `CancelledOn?`, `ExternalId?`, `CustomerName`, `CpfDigits`, `Items[]`; `ToString()` sem CPF), `CollectedOrderItem.cs` (`LineNo`, `ProductName`, `SkuRef`, `Size`, `Price`, `ImageUrl?`) e `CollectedImage.cs` (`Content` stream, `MimeType`, `SizeBytes`)
- [X] T010 [P] Acrescentar `source` (`OrderSource`) em `backend/Ambrosio.DTO/Orders/OrderInfo.cs` e `backend/Ambrosio.DTO/Orders/OrderDetailInfo.cs` e `imageId` (`long?`) em `backend/Ambrosio.DTO/Orders/OrderItemInfo.cs`, e preencher o mapeamento em `backend/Ambrosio.Domain/Services/OrderService.cs`

### Modelos de domínio (`Ambrosio.Domain/Models`)

- [X] T011 [P] Criar `backend/Ambrosio.Domain/Models/ImportRun.cs` conforme data-model §4: propriedades (`Id`, `UserId`, `Mode`, `Status`, `StartDate`, `EndDate`, `BlockDays`, `BlocksTotal`, `CancelRequested`, `RequestedAt`, `StartedAt?`, `FinishedAt?`, as 9 contagens, `ErrorMessage?`, `CollectorVersion`, `Blocks`, `User?`), `static Create(userId, mode, startDate, endDate, blockDays, collectorVersion, now)` (status `Queued`, blocos `Pending` gerados por `ImportBlockPlanner`), `Start(now)`, `RequestCancel()` (só em `Queued`/`Running`), `AccumulateBlock(block)`, `FailBeforeBlocks(reason, now)` (blocos → `NotExecuted`), `Finish(now)` aplicando a FR-047a exatamente como research §18 (`cancelled` se cancelado; `failed` se algum bloco `failed` ou falha antes dos blocos; `partial` se `failed_count + conflict_count + images_failed_count > 0`; senão `succeeded`; blocos `Pending` → `NotExecuted`) e `DurationSeconds` calculado; `ErrorMessage` truncado a 500 caracteres
- [X] T012 [P] Criar `backend/Ambrosio.Domain/Models/ImportRunBlock.cs` (`Id`, `ImportRunId`, `BlockNo`, `StartDate`, `EndDate`, `Status`, 9 contagens, `ErrorMessage?`, `StartedAt?`, `FinishedAt?`; métodos `Start(now)`, `Succeed(now)`, `Fail(reason, now)`, `MarkNotExecuted()`, `Record(action)`) e `backend/Ambrosio.Domain/Models/ImportRunChange.cs` (`Id`, `ImportRunId`, `ImportRunBlockId`, `OrderId?`, `OrderNumber`, `OrderDate`, `Kind`, `Channel?`, `Origin?`, `Action`, `ChangedFields?`, `ErrorMessage?`, `CreatedAt`) — **nunca** CPF nem nome do cliente (FR-052)
- [X] T013 [P] Criar `backend/Ambrosio.Domain/Models/MaisVendasAccount.cs` (`Id`, `UserId`, `AccountHash?`, `LinkedAt?`, `FailedLoginCount`, `LockedUntil?`, `CreatedAt`, `UpdatedAt`; métodos `IsLocked(now)`, `Link(hash, now)`, `Matches(hash)`, `RegisterRefusedLogin(now, max, lockoutMinutes)` — na `max`-ésima recusa seguida define `LockedUntil = now + lockoutMinutes` —, `RegisterAcceptedLogin(now)` zera a contagem)
- [X] T014 Alterar `backend/Ambrosio.Domain/Models/Order.cs` (`Source` com padrão `OrderSource.Manual`, `SourceHash?`, `CapturedAt?`, `SourceChangedAt?`, fábrica `CreateImported(...)` com `Source = MaisVendas`, `CapturedAt = SourceChangedAt = now`, e `Touch(now)` que altera só `CapturedAt`) e `backend/Ambrosio.Domain/Models/OrderItem.cs` (`ImageId?` + navegação `Image?` para `StoredImage`)

### Persistência

- [X] T015 Configurar em `backend/Ambrosio.Infra/Context/AmbrosioContext.cs` (Fluent API, constituição §V) os 4 `DbSet` novos e as colunas exatamente como data-model §1/§2: `orders.source integer NOT NULL DEFAULT 1`, `orders.source_hash varchar(64)` nulo, `orders.captured_at`/`source_changed_at timestamp without time zone` nulos, índice `ix_orders_user_source_date (user_id, source, order_date DESC)`; `order_items.image_id bigint` nulo com FK `fk_stored_image_order_item` `ClientSetNull` e índice `ix_order_items_image`; `import_runs` (PK `import_runs_pkey`, FK `fk_user_import_run`, `status integer DEFAULT 1`, `start_date`/`end_date date`, 9 contagens `integer DEFAULT 0`, `cancel_requested boolean DEFAULT false`, `error_message varchar(500)`, `collector_version varchar(40) NOT NULL`, índices `ix_import_runs_user_requested (user_id, requested_at DESC)`, `ux_import_runs_user_active (user_id) UNIQUE WHERE status IN (1, 2)`, `ix_import_runs_user_period (user_id, start_date, end_date, finished_at DESC)`); `import_run_blocks` (FK `fk_import_run_import_run_block`, `status integer DEFAULT 1`, `error_message varchar(500)`, índice único `ux_import_run_blocks_run_no (import_run_id, block_no)`); `import_run_changes` (FKs `fk_import_run_import_run_change`, `fk_import_run_block_import_run_change`, `fk_order_import_run_change` todas `ClientSetNull`, `order_number varchar(60) NOT NULL`, `channel varchar(60)`, `changed_fields varchar(500)`, `error_message varchar(500)`, índices `ix_import_run_changes_run`, `ix_import_run_changes_block`); `mais_vendas_accounts` (FK `fk_user_mais_vendas_account`, `account_hash varchar(64)` nulo, `failed_login_count integer DEFAULT 0`, `locked_until` nulo, único `ux_mais_vendas_accounts_user`). Nenhuma coluna de login ou senha
- [X] T016 Gerar a migração `AddMaisVendasImport` com `dotnet ef migrations add AddMaisVendasImport --project backend/Ambrosio.Infra --startup-project backend/Ambrosio.API` em `backend/Ambrosio.Infra/Migrations/`, conferindo no arquivo gerado o filtro `status IN (1, 2)` do índice parcial, o `DEFAULT 1` de `orders.source` para linhas existentes e a ausência de cascata

### Regras puras do Domain (`Ambrosio.Domain/Common`)

- [X] T017 [P] Criar `backend/Ambrosio.Domain/Common/ImportBlockPlanner.cs` com `Split(start, end, blockDays)` gerando blocos consecutivos `[start, min(start + blockDays - 1, end)]`, numerados a partir de 1, sem lacuna nem sobreposição, último menor, um dia → um bloco (FR-009, research §11)
- [X] T018 [P] Criar `backend/Ambrosio.Domain/Common/MaisVendasNormalizer.cs`: `R$ 1.234,56`/`-R$ 1.234,56` → `decimal` 2 casas; CPF só dígitos validado pelo `Cpf` existente (`backend/Ambrosio.Domain/ValueObjects/Cpf.cs`); data `24 Out` → ano deduzido pelo intervalo do bloco (inclusive virada de ano) e confirmado pelo `AAAA-MM-DD` da URL de detalhe, lançando exceção de divergência sem CPF na mensagem (FR-031, research §10)
- [X] T019 [P] Criar `backend/Ambrosio.Domain/Common/SourceHashCalculator.cs`: JSON canônico com chaves em ordem fixa (`orderNumber`, `orderDate`, `kind`, `channel`, `origin`, `status`, `total`, `originalSaleOn`, `cancelledOn`, `externalId`, `customer.cpfHash`, `customer.name`, `items[]` com `lineNo`, `productName`, `skuRef`, `size`, `price`, `imageUrl` ordenados por `lineNo`), decimais 2 casas `InvariantCulture`, datas ISO, strings com `Trim()`; SHA-256 hex minúsculo; **sem** `captured_at`, `image_id` ou ids internos (FR-022, research §6)
- [X] T020 [P] Criar `backend/Ambrosio.Domain/Common/ImportJob.cs` (`RunId`, `UserId`, `Mode`, `StartDate`, `EndDate`, `Login`, `Password`, `Clear()` que zera as referências; `ToString()` sem credenciais) — nunca registrado em DI, log, exceção ou DTO de saída (research §4)
- [X] T021 [P] Criar as exceções em `backend/Ambrosio.Domain/Exceptions/`: `ImportLockedException.cs` (carrega `LockedUntil`), `ImportRunNotCancellableException.cs` ("A execução já foi finalizada.") e `MaisVendasSourceException.cs` (origem indisponível após as retentativas; mensagem sem login, senha ou CPF)

### Contratos (interfaces)

- [X] T022 [P] Criar `backend/Ambrosio.Infra.Interfaces/Repository/IImportRunRepository.cs` com as assinaturas do data-model §7 (`GetByIdAsync(runId, userId)`, `GetCurrentAsync(userId)`, `ListAsync(userId, page, pageSize)`, `ListChangesAsync(runId, userId)`, `GetBreakdownAsync(runId, userId)`, `GetPreviousRunOrdersCountAsync(userId, runId, start, end)`, `InsertAsync`, `UpdateAsync`, `UpdateBlockAsync`, `AddChangeAsync`, `RequestCancelAsync(runId, userId)`, `MarkInterruptedAsync(now)` → `Task<int>` (**somente escrita em lote** via `ExecuteUpdateAsync`, sem materializar entidades nem devolver dados de usuário; não é método de leitura, §VI), `GetSuggestedStartDateAsync(userId)`, `ListImportedKeysInRangeAsync(userId, from, to)`, `UpsertImportedOrderAsync(userId, CollectedOrder, cpfHash, cpfEncrypted, cpfMasked, sourceHash, imageIdsByUrl, now)` → `UpsertOutcome` (`Action`, `OrderId?`, `ChangedFields`, `CustomerCreated`), `PreviewImportedOrderAsync(userId, CollectedOrder, cpfHash, sourceHash)` → `UpsertOutcome` sem gravar, `GetLinkedImageIdsAsync(userId, orderNumber, orderDate, kind)` → mapa `imageUrl → imageId`)
- [X] T023 [P] Criar `backend/Ambrosio.Infra.Interfaces/Repository/IMaisVendasAccountRepository.cs` (`GetOrCreateAsync(userId, now)`, `UpdateAsync(entity)`)
- [X] T024 [P] Criar `backend/Ambrosio.Infra.Interfaces/AppServices/IMaisVendasCollectorAppService.cs` (`CollectorVersion`, `OpenSessionAsync(MaisVendasCredentials, ct)` → `IMaisVendasCollectorSession : IAsyncDisposable` com `LoginAsync(ct)` → `MaisVendasLoginResult`, `CollectBlockAsync(from, to, ct)` → `CollectedBlock`, `DownloadImageAsync(url, maxBytes, ct)` → `CollectedImage`) e `backend/Ambrosio.Infra.Interfaces/AppServices/IAccountHashAppService.cs` (`Hash(accountIdentifier)`)
- [X] T025 [P] Criar `backend/Ambrosio.Domain/Interfaces/IImportRunService.cs` (`GetDefaultsAsync`, `StartAsync`, `GetCurrentAsync`, `GetByIdAsync`, `ListAsync`, `CancelAsync`), `backend/Ambrosio.Domain/Interfaces/IImportExecutionService.cs` (`ExecuteAsync(ImportJob, CancellationToken)`) e `backend/Ambrosio.Domain/Interfaces/IImportRunQueue.cs` (`Enqueue(ImportJob)`, `ReadAllAsync(ct)`)

### Implementações base (Infra / Application / API)

- [X] T026 Criar `backend/Ambrosio.Infra/Repository/ImportRunRepository.cs` com os métodos base: `InsertAsync` (captura `23505` em `ux_import_runs_user_active` → `DuplicateException("Já existe importação em andamento.")`), `UpdateAsync`, `UpdateBlockAsync`, `AddChangeAsync`, `GetByIdAsync` e `GetCurrentAsync` (com blocos; `user_id` como primeira cláusula, §VI) e `MarkInterruptedAsync(now)` como **somente escrita em lote**: dois `ExecuteUpdateAsync` — o primeiro em `import_run_blocks` pendentes das execuções com `status IN (1,2)` (subconsulta) → `notExecuted`; o segundo em `import_runs` com `status IN (1,2)` → `failed` com "Execução interrompida pela reinicialização do servidor." e `finished_at = now`; nenhum `ToListAsync`/`FirstAsync`/materialização de entidade; retorna só o número de linhas afetadas (§VI: não é método de leitura); demais métodos lançam `NotImplementedException` até as histórias
- [X] T027 [P] Criar `backend/Ambrosio.Infra/Repository/MaisVendasAccountRepository.cs` (`GetOrCreateAsync` com corrida em `ux_mais_vendas_accounts_user` relida uma vez, `UpdateAsync`)
- [X] T028 [P] Criar `backend/Ambrosio.Infra/AppServices/AccountHashAppService.cs`: HMAC-SHA256 hex minúsculo de `"mais-vendas-account:" + identificador` com o pepper de `CpfProtection:Pepper` (research §8); identificador normalizado com `Trim().ToLowerInvariant()`
- [X] T029 Criar `backend/Ambrosio.Infra/AppServices/MaisVendas/FakeScenarios.cs` e `backend/Ambrosio.Infra/AppServices/MaisVendas/FakeMaisVendasCollectorAppService.cs` (`CollectorVersion = "fake-1.0"`), determinístico e sem rede: parse do login `conta+cenario` (parte antes do `+` = `AccountIdentifier`), senha `recusada` → `Refused`, demais senhas = versão do conteúdo (`v1` base); cenário `ok` com o conjunto fixo de **49 pedidos entre 26/09 e 25/10/2026, total líquido R$ 123.300,58**, canais Cota e Omni, tipos venda/cancelamento (inclusive uma venda cancelada → dois registros, e o número `41900001` em 28/09 e 05/10), `TabCounters`/`TabTotals` coerentes; atraso `MaisVendas:Fake:DelayMs` entre pedidos; os demais cenários entram nas histórias
- [X] T030 [P] Criar `backend/Ambrosio.Application/Imports/ImportRunQueue.cs` implementando `IImportRunQueue` sobre `Channel<ImportJob>` ilimitado (singleton)
- [X] T031 Criar `backend/Ambrosio.Application/HostedServices/ImportRunWorker.cs` (`BackgroundService`, no padrão de `PendingImageCleanupService.cs`): no `StartAsync` chama `MarkInterruptedAsync(now)` (FR-020); lê a fila; `SemaphoreSlim(MaxConcurrentRuns)` (execução fica `queued` enquanto aguarda, FR-017); um `IServiceScopeFactory.CreateAsyncScope()` por job chamando `IImportExecutionService.ExecuteAsync`; registro `ConcurrentDictionary<long, CancellationTokenSource>` por execução; `finally` com `job.Clear()`; logs só com `BeginScope(new { RunId })`, nunca com o job
- [X] T032 Acrescentar `MaisVendas` em `backend/Ambrosio.Application/Settings/AmbrosioSettings.cs` e registrar em `backend/Ambrosio.Application/Startup.cs`: `IOptions<MaisVendasSettings>`, `ImportRunRepository`, `MaisVendasAccountRepository`, `AccountHashAppService`, coletor por `MaisVendas:Collector` (`Fake` → `FakeMaisVendasCollectorAppService`, `Playwright` → `PlaywrightMaisVendasCollectorAppService`, registrado quando a T105 existir), `ImportRunQueue` singleton, `ImportRunWorker` como hosted service, `IImportRunService`/`IImportExecutionService` scoped e os validators novos
- [X] T033 Validar a seção `MaisVendas` em `backend/Ambrosio.API/Program.cs` (`ValidateSettings`): faixas do research §13 (`BlockDays` 1–31, `MaxRangeDays` 1–3660, `MaxConcurrentRuns` 1–8, `MaxFailedLogins` 1–10, `LockoutMinutes` 1–1440, `MinDelayMs`/`MaxDelayMs` 100–5000 com min ≤ max, `Fake:DelayMs` 0–5000, `BaseUrl` https absoluta) e **recusa** de `Collector=Fake` com `ASPNETCORE_ENVIRONMENT=Production`; acrescentar a descrição das rotas de importação no Swagger
- [X] T034 Acrescentar em `backend/Ambrosio.API/Controllers/ApiControllerBase.cs` os helpers `ImportLockedError(lockedUntil)` (429 + cabeçalho `Retry-After` só com segundos + mensagem "Muitas tentativas com credenciais recusadas. Tente novamente às HH:mm." no fuso America/Sao_Paulo) e `NotCancellableError()` (409)
- [X] T035 [P] Criar `backend/Ambrosio.ApiTests/Helpers/ImportRunWaiter.cs` (`WaitUntilFinishedAsync(runId, token)` com polling de `GET /imports/mais-vendas/runs/{id}` até status final, timeout `RunTimeoutSeconds`) e acrescentar em `backend/Ambrosio.ApiTests/Helpers/TestDataHelper.cs` os atalhos `StartImportAsync(token, login, password, start, end, dryRun)` e `NewSellerAsync()` (cadastro + login de vendedora nova por teste)

**Checkpoint**: `dotnet build backend/Ambrosio.sln` verde, migração aplicada na homolog, worker sobe sem erro

---

## Phase 3: User Story 1 — Importar um período informando as credenciais na hora (Priority: P1) 🎯 MVP

**Goal**: a vendedora abre `/importacao`, vê as datas sugeridas, informa login e senha, inicia a importação, acompanha o progresso bloco a bloco e lê o relatório final

**Independent Test**: com `s482+ok` / `v1`, importar 26/09–25/10/2026 e conferir 49 pedidos com total líquido R$ 123.300,58 em `/pedidos`, 5 blocos com progresso automático e relatório final com as mesmas contagens (quickstart §4 itens 1–2)

### Tests for User Story 1 ⚠️

> Escrever primeiro e confirmar que falham antes da implementação

- [X] T036 [US1] Criar `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs` (skill `dotnet-test-api`) com: `Defaults_NewSeller_ReturnsNullStartAndToday`, `Defaults_AfterImport_ReturnsLastImportedOrderDate`, `Start_Invalid_Returns400` (data inicial > final, final no futuro, login vazio, senha vazia — mensagens do data-model §5, sem eco dos valores), `Start_RangeAboveLimit_Returns400WithLimit` (367 dias → "O período não pode ultrapassar 366 dias."), `Start_Valid_Returns202QueuedWith5Blocks`, `Run_OkScenario_Succeeds_With49Orders` (relatório `succeeded`, blocos `succeeded`, `breakdown` por canal × origem × tipo × ação, `changes[].orderId` preenchido, `GET /orders` somando R$ 123.300,58), `Run_CancelledSale_CreatesSaleAndCancellation`, `Run_SameNumberDifferentDates_CreatesTwoOrders`, `Run_SingleDayPeriod_CreatesOneBlock`
- [X] T037 [P] [US1] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/UnauthorizedAccessTests.cs` as rotas `GET /imports/mais-vendas/defaults`, `POST /imports/mais-vendas/runs`, `GET /imports/mais-vendas/runs/current` e `GET /imports/mais-vendas/runs/{id}` → 401 sem token
- [X] T038 [P] [US1] Criar `frontend/src/lib/importForm.test.ts`: data inicial obrigatória, inicial > final bloqueia, final no futuro bloqueia (America/Sao_Paulo), período > `maxRangeDays` bloqueia com o limite, login/senha vazios bloqueiam
- [X] T039 [P] [US1] Criar `frontend/src/components/imports/ImportForm.test.tsx`: datas sugeridas preenchidas, data inicial vazia quando `suggestedStartDate = null`, envio bloqueado com mensagem no campo, `onSubmit` chamado com os valores válidos
- [X] T040 [P] [US1] Criar `frontend/src/components/imports/ImportProgress.test.tsx`: mostra bloco em andamento, blocos concluídos/total e contagens parciais
- [X] T041 [P] [US1] Criar `frontend/src/components/imports/ImportReport.test.tsx`: identificador, modo, período, duração, resultado, tabela de blocos com contagens, agrupamento canal × tipo, links `/pedidos/{orderId}`

### Implementation for User Story 1

- [X] T042 [P] [US1] Criar `backend/Ambrosio.Domain/Validators/ImportRunStartInfoValidator.cs` (skill `dotnet-fluent-validation`, depende de `IClock` e `IOptions<MaisVendasSettings>`): `login` não vazio e ≤ 120 → "Informe o login do +Vendas."; `password` não vazio e ≤ 200 → "Informe a senha do +Vendas."; `startDate` obrigatória e ≤ `endDate` → "A data inicial deve ser anterior ou igual à data final."; `endDate` obrigatória e ≤ hoje America/Sao_Paulo → "A data final não pode estar no futuro."; `endDate - startDate + 1 ≤ MaxRangeDays` → "O período não pode ultrapassar {MaxRangeDays} dias." (no campo `endDate`); nenhuma mensagem usa `{PropertyValue}`
- [X] T043 [US1] Implementar em `backend/Ambrosio.Infra/Repository/ImportRunRepository.cs`: `GetSuggestedStartDateAsync` (`MAX(order_date) WHERE user_id = ? AND source = 2`), `ListChangesAsync`, `GetBreakdownAsync` (agrupamento canal × origem × tipo × ação) e `UpsertImportedOrderAsync` em transação própria (`BeginTransactionAsync`) com os ramos **inexistente → insere** (`Order.CreateImported`, cliente buscado por `cpf_hash` ou criado com hash/cifra/máscara) e **mesmo `source_hash` → `Touch(now)`** (`unchanged`); os demais ramos entram na US3
- [X] T044 [US1] Criar `backend/Ambrosio.Domain/Services/ImportRunService.cs`: `GetDefaultsAsync` (sugestão + hoje SP + `blockDays` + `maxRangeDays`), `StartAsync` (valida, `ImportRun.Create`, `InsertAsync`, `Enqueue(new ImportJob(...))`, devolve `ImportRunInfo` `queued`), `GetCurrentAsync` (ativa ou `null`), `GetByIdAsync` (404 se de outra vendedora) e o mapeamento para `ImportRunInfo`/`ImportRunDetailInfo` (`blocksFinished`, `currentBlockNo`)
- [X] T045 [US1] Criar `backend/Ambrosio.Domain/Services/ImportExecutionService.cs` com o fluxo principal: `Start(now)`; `OpenSessionAsync` + `LoginAsync` (`Refused` → `FailBeforeBlocks("Credenciais recusadas pelo +Vendas.")`); para cada bloco: `Start`, `CollectBlockAsync`, normalização, proteção do CPF via `ICpfProtectionAppService`, `SourceHashCalculator`, gravação **pedido a pedido em escopo de DI próprio** (`IServiceScopeFactory`) via `UpsertImportedOrderAsync`, `AddChangeAsync` + `Record(action)`, `Succeed`, `AccumulateBlock`, `UpdateBlockAsync` após cada pedido (progresso visível em ≤ 5 s); `Finish(now)`; `DisposeAsync` da sessão no `finally`
- [X] T046 [US1] Criar `backend/Ambrosio.API/Controllers/ImportsController.cs` (`[Authorize]`, rota base `imports/mais-vendas`, `userId` só do `sub` via `ClaimsPrincipalExtensions`): `GET defaults` (200), `POST runs` (202 `ImportRunInfo`; `ValidationException` → 400, `DuplicateException` → 409), `GET runs/current` (200 ou 204), `GET runs/{runId}` (200 ou 404); `try/catch` no padrão existente e **sem logar o corpo**
- [X] T047 [P] [US1] Criar `bruno/Imports/folder.bru`, `bruno/Imports/Defaults.bru`, `bruno/Imports/Iniciar Importacao.bru` (corpo com `{{maisVendasLogin}}`/`{{maisVendasPassword}}`, nunca valores reais), `bruno/Imports/Execucao Atual.bru` e `bruno/Imports/Relatorio.bru`
- [X] T048 [P] [US1] Criar `frontend/src/types/import.ts` (skill `react-architecture`; só `interface`, sem `any`): `ImportDefaults`, `ImportRunStartRequest`, `ImportRunCounts`, `ImportRun`, `ImportRunBlock`, `ImportRunChange`, `ImportRunBreakdown`, `ImportRunDetail`, uniões de string para `ImportRunStatus`, `ImportRunMode`, `ImportBlockStatus`, `ImportChangeAction`
- [X] T049 [P] [US1] Acrescentar `source` (`'manual' | 'maisVendas'`) e `imageId` (`number | null`) em `frontend/src/types/order.ts`
- [X] T050 [P] [US1] Acrescentar as chaves `import.*` em `frontend/src/i18n/locales/pt-BR.ts` (título, campos, botões, status `queued` = "Aguardando", `running`, `succeeded`, `failed`, `partial`, `cancelled`, ações do relatório, mensagens de validação) e o link `nav.import` = "Importar"
- [X] T051 [P] [US1] Criar `frontend/src/lib/importForm.ts` (padrão de `lib/authForm.ts`): `validateImportForm(values, defaults, today)` devolvendo erros por campo com chaves i18n e `todayInSaoPaulo()`; constante `IMPORT_POLL_MS = 3000`
- [X] T052 [US1] Criar `frontend/src/Services/importService.ts` (classe + singleton sobre `apiRequest` de `Services/apiClient.ts`): `getDefaults`, `startRun`, `getCurrent` (204 → `null`), `getRun`
- [X] T053 [US1] Criar `frontend/src/Contexts/ImportContext.tsx` (`defaults`, `currentRun`, `lastRun`, `loading`, `error`, `loadDefaults`, `startRun`, `loadRun`, com `useCallback`; polling com `setInterval(IMPORT_POLL_MS)` enquanto `queued`/`running`, parado no status final e no unmount). A senha **nunca** entra no estado do Context
- [X] T054 [US1] Criar `frontend/src/hooks/useImport.ts` e registrar o `ImportProvider` em `frontend/src/main.tsx` depois do `OrderProvider`
- [X] T055 [P] [US1] Criar `frontend/src/components/imports/ImportStatusBadge.tsx` (badge shadcn por status, textos do `pt-BR.ts`)
- [X] T056 [US1] Criar `frontend/src/components/imports/ImportForm.tsx`: login, senha (`type="password"`, `autoComplete="off"`), data inicial, data final, validação de `lib/importForm.ts` com mensagem no campo
- [X] T057 [US1] Criar `frontend/src/components/imports/ImportProgress.tsx`: bloco em andamento, `blocksFinished/blocksTotal`, contagens parciais e status por bloco
- [X] T058 [US1] Criar `frontend/src/components/imports/ImportReport.tsx`: cabeçalho (id, modo, período, duração, resultado), tabela de blocos com as 9 contagens, `breakdown` canal × tipo, lista de `changes` com link `/pedidos/{orderId}`
- [X] T059 [US1] Criar `frontend/src/pages/ImportPage.tsx`: carrega `defaults` e execução atual; senha em `useState` local; mostra `ImportProgress` quando há execução ativa, senão `ImportForm` + último relatório; toast sonner na confirmação do `202`
- [X] T060 [US1] Registrar a rota `/importacao` dentro de `RequireAuth` + `AppShell` em `frontend/src/App.tsx` e o link "Importar" em `frontend/src/components/layout/AppHeader.tsx`

**Checkpoint**: US1 funcional e testável isoladamente (T036–T041 verdes)

---

## Phase 4: User Story 2 — As credenciais do +Vendas nunca ficam guardadas (Priority: P1)

**Goal**: login e senha só em memória; vínculo da conta por hash com pepper; bloqueio após 3 recusas; login com 2FA/captcha falha com mensagem clara

**Independent Test**: importar com login `s482+ok` e senha `SENHA-TESTE-XYZ123` e não encontrar esses valores em nenhuma resposta, no banco, nos logs nem no navegador (quickstart §5); `recusada` ×3 → 429; `s999+ok` após vínculo → `failed`

### Tests for User Story 2 ⚠️

- [X] T061 [US2] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`: `Responses_NeverContainCredentials` (corpos de todas as rotas de importação, inclusive 400/409/429, sem o login, a senha nem `accountHash`), `Run_RefusedPassword_FailsWithMessageAndWritesNothing`, `Start_AfterThreeRefusals_Returns429WithRetryAfter` (+ `GET /defaults` com `lockedUntil`), `Run_AcceptedLogin_ResetsRefusalCount`, `Run_DifferentAccount_FailsBeforeWriting` (vínculo com `s482`, depois `s999+ok` → "Conta do +Vendas diferente da vinculada.", contagens de pedidos inalteradas), `DryRun_DifferentAccount_AlsoFails`, `Run_TwoFactor_FailsWithUnsupportedStep`
- [X] T062 [P] [US2] Acrescentar em `frontend/src/components/imports/ImportForm.test.tsx` e criar `frontend/src/pages/ImportPage.test.tsx`: campo de senha com `autocomplete="off"`, senha limpa após envio com sucesso **e** com erro, `localStorage`/`sessionStorage`/`document.cookie` sem a senha, envio desabilitado com horário de liberação quando `lockedUntil` está no futuro

### Implementation for User Story 2

- [X] T063 [US2] Acrescentar em `backend/Ambrosio.Infra/AppServices/MaisVendas/FakeScenarios.cs` o cenário `2fa` (`UnsupportedStep`) e garantir `AccountIdentifier` = parte do login antes do `+`
- [X] T064 [US2] Acrescentar em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs` o vínculo e o bloqueio: `Refused` → `RegisterRefusedLogin(now, MaxFailedLogins, LockoutMinutes)`; `Accepted` → `RegisterAcceptedLogin`, hash via `IAccountHashAppService`, conta vinculada com hash diferente → `FailBeforeBlocks("Conta do +Vendas diferente da vinculada.")` antes de qualquer gravação (inclusive simulação), sem vínculo e modo `Apply` → `Link(hash, now)`; `UnsupportedStep` → `FailBeforeBlocks("O login do +Vendas exige uma etapa não suportada (código ou captcha).")` (FR-008a, FR-008b, FR-046)
- [X] T065 [US2] Acrescentar em `backend/Ambrosio.Domain/Services/ImportRunService.cs` a checagem `IsLocked(now)` no `StartAsync` → `ImportLockedException(LockedUntil)` e `lockedUntil` no `GetDefaultsAsync`
- [X] T066 [US2] Tratar `ImportLockedException` → `ImportLockedError` (429) em `backend/Ambrosio.API/Controllers/ImportsController.cs` e conferir no Swagger (`/api/swagger`) `password` com `format: password` e ausência de credenciais nos exemplos
- [X] T067 [US2] Revisar `backend/Ambrosio.Domain/Services/ImportExecutionService.cs` e `backend/Ambrosio.Application/HostedServices/ImportRunWorker.cs` para que toda exceção da coleta vire `error_message` genérico sem login, senha ou CPF, os logs usem `BeginScope(new { RunId, BlockNo })` com rota, contagens e tempos (FR-052), e `job.Clear()` + `DisposeAsync` da sessão rodem em sucesso, falha e cancelamento (FR-005)
- [X] T068 [US2] Tratar 429 em `frontend/src/Services/importService.ts` (mensagem do servidor) e, em `frontend/src/components/imports/ImportForm.tsx` e `frontend/src/pages/ImportPage.tsx`, desabilitar o envio com o horário de `lockedUntil` e limpar a senha logo após o envio, com sucesso ou erro (FR-007)

**Checkpoint**: US1 + US2 entregam o MVP liberável (P1)

---

## Phase 5: User Story 3 — Repetir importação ou bloco que falhou sem duplicar nada (Priority: P2)

**Goal**: idempotência completa (inalterado, atualizado com campos, não encontrado, conflito com manual), validação de contadores antes de gravar, parada em bloco falho e "Repetir este bloco"

**Independent Test**: `s482+falha-bloco3` → blocos 1–2 ok, bloco 3 `failed` com 14 gravados, 4–5 `notExecuted`; repetir o bloco 3 e o período com `s482+ok` → estado idêntico à execução limpa (quickstart §4 itens 3–8)

### Tests for User Story 3 ⚠️

- [X] T069 [US3] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`: `Reimport_SameContent_AllUnchanged` (SC-002, sem linhas novas em pedidos/clientes), `Reimport_V2_UpdatesWithChangedFields`, `Reimport_AfterManualEdit_RevertsToSource` (`PUT /orders/{id}` e reimportação → `updated` com `total`), `Run_FailureInBlock3_Keeps14OrdersAndStops`, `RepeatBlock3_ThenFullPeriod_MatchesCleanRun` (SC-003, 0 duplicatas), `Run_CounterMismatch_FailsBlockWithoutWriting` e `Run_TotalMismatch_FailsBlockWithoutWriting` (SC-009), `Reimport_V3_ReportsMissingWithoutDeleting`, `Run_ManualOrderSameKey_ReportsConflictAndPartial`, `Run_YearlessDates_ResolvedByBlock`, `Run_UnstableSource_RetriesAndSucceeds`, `Run_EmptyPeriod_SucceedsWithZeroCounts`
- [X] T070 [P] [US3] Acrescentar em `frontend/src/components/imports/ImportReport.test.tsx`: bloco falho com motivo e "N pedidos gravados", botão "Repetir este bloco" navegando para `/importacao?inicio=AAAA-MM-DD&fim=AAAA-MM-DD`, `changedFields` exibidos, destaque visual de `failed`/`partial`
- [X] T071 [P] [US3] Acrescentar em `frontend/src/lib/importForm.test.ts` a leitura de `inicio`/`fim` da query string (válidas pré-preenchem, inválidas são ignoradas)

### Implementation for User Story 3

- [X] T072 [US3] Acrescentar em `backend/Ambrosio.Infra/AppServices/MaisVendas/FakeScenarios.cs`: senha `v2` (altera valor/status de alguns pedidos), `v3` (omite um pedido do bloco 2 para provar `missing`), `falha-bloco3` (no 3º bloco, o 15º pedido vem com `order_number` acima de 60 caracteres e a gravação estoura — cai em "falha inesperada de gravação → `block.Fail`", T076; os 14 pedidos anteriores ficam gravados; o 15º pedido traz foto, gravada antes da falha, para o teste da FR-041), `contador-divergente`, `total-divergente`, `sem-ano` (datas `24 Out` confirmadas pela URL de detalhe), `instavel` (erro transitório superado na 2ª tentativa) e `vazio` (nenhum pedido, contadores zero)
- [X] T073 [P] [US3] Criar `backend/Ambrosio.Domain/Common/BlockValidator.cs`: confere `TabCounters` × linhas lidas por aba e `TabTotals` × soma dos pedidos por aba; devolve o motivo da divergência sem CPF (FR-010/FR-011)
- [X] T074 [US3] Acrescentar `ApplyImported(snapshot, hash, now)` em `backend/Ambrosio.Domain/Models/Order.cs`: sobrescreve os campos vindos da origem, substitui os itens (preservando `ImageId` de itens que mantiveram `image_url`), define `SourceHash` e `SourceChangedAt = now` e devolve a lista de campos alterados (`total`, `status`, `items`…) comparando com o conteúdo gravado; campos só do Ambrosio preservados (clarificação 2)
- [X] T074a [US3] Em `backend/Ambrosio.Domain/Services/OrderService.cs` (atualização de pedido e de itens), quando `order.Source == OrderSource.MaisVendas`, definir `SourceHash = null` antes de salvar, para que a reimportação seguinte reverta a edição manual (FR-023)
- [X] T075 [US3] Completar `UpsertImportedOrderAsync` em `backend/Ambrosio.Infra/Repository/ImportRunRepository.cs` com os ramos **`source = manual` → não toca, `conflict`** e **hash diferente ou `source_hash` nulo → `ApplyImported`, `updated` com `changed_fields`** (lista separada por vírgula, ≤ 500 caracteres), e implementar `ListImportedKeysInRangeAsync(userId, from, to)` (só `source = 2`)
- [X] T076 [US3] Acrescentar em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs`: `BlockValidator` **antes** de gravar o bloco; `MaisVendasSourceException` ou falha inesperada de gravação → `block.Fail(motivo, now)` com contagem de inseridos preservada, blocos seguintes `NotExecuted`, execução para; pedido com dado inválido → `error` sem parar; após bloco sem falha, chaves de `ListImportedKeysInRangeAsync` ausentes do bloco → `AddChangeAsync(missing)` sem alterar o pedido (FR-027); contagem de `conflict`
- [X] T077 [US3] Em `frontend/src/components/imports/ImportReport.tsx`, exibir motivo e pedidos gravados do bloco falho, botão "Repetir este bloco" (`navigate('/importacao?inicio=…&fim=…')`), `changedFields`, ações `missing`/`conflict` e destaque de `failed`/`partial`; em `frontend/src/lib/importForm.ts` e `frontend/src/pages/ImportPage.tsx`, pré-preencher as datas a partir de `inicio`/`fim` com login e senha vazios (FR-026)

**Checkpoint**: US3 testável isoladamente; reimportações seguras

---

## Phase 6: User Story 4 — Simular a importação sem gravar nada (Priority: P2)

**Goal**: modo "Simular (não gravar)" com as mesmas validações e relatório do que *seria* feito

**Independent Test**: anotar contagens de pedidos, clientes e imagens; simular `s482+ok`; contagens idênticas e relatório identificado como simulação (quickstart §4 item 9, SC-011)

### Tests for User Story 4 ⚠️

- [X] T078 [US4] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`: `DryRun_NewSeller_WritesNothing` (contagens de `GET /orders`, `GET /customers` e imagens iguais antes e depois), `DryRun_ReportShowsWouldInsertCustomersAndImages` (`mode = dryRun`, `changes[].orderId = null`), `DryRun_CounterMismatch_FailsLikeApply`, `DryRun_DoesNotLinkAccount`
- [X] T079 [P] [US4] Acrescentar em `frontend/src/components/imports/ImportForm.test.tsx` o checkbox "Simular (não gravar)" enviando `dryRun: true` e em `frontend/src/components/imports/ImportReport.test.tsx` o rótulo de simulação com o texto "seriam inseridos"

### Implementation for User Story 4

- [X] T080 [US4] Implementar `PreviewImportedOrderAsync` em `backend/Ambrosio.Infra/Repository/ImportRunRepository.cs`: somente leitura (`AsNoTracking`), mesma decisão de ação do upsert (`inserted`/`unchanged`/`updated` com campos/`conflict`) e `CustomerCreated` quando o `cpf_hash` não existe
- [X] T081 [US4] Acrescentar o ramo `DryRun` em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs`: usa `PreviewImportedOrderAsync`, não baixa nem grava fotos, `images_stored` conta URLs distintas ainda não ligadas a item com a mesma URL, `customers_created` conta `cpf_hash` distintos inexistentes (conjunto em memória da execução), `changes` sem `order_id`, mesmo `BlockValidator` (FR-032)
- [X] T082 [US4] Acrescentar o checkbox "Simular (não gravar)" em `frontend/src/components/imports/ImportForm.tsx` e o rótulo "Simulação"/textos "seriam…" em `frontend/src/components/imports/ImportReport.tsx` e `frontend/src/components/imports/ImportProgress.tsx`, com chaves novas em `frontend/src/i18n/locales/pt-BR.ts`

**Checkpoint**: US4 testável isoladamente

---

## Phase 7: User Story 5 — Clientes cadastrados sob demanda a partir dos pedidos (Priority: P2)

**Goal**: um cliente por CPF, com CPF protegido; nome atualizado pela origem preservando o resto; CPF inválido vira pedido "com erro"

**Independent Test**: importar período com um CPF novo e um CPF em três pedidos de dois blocos; conferir cliente novo com CPF mascarado e um único cliente ligado aos três pedidos (quickstart §4 item 4)

### Tests for User Story 5 ⚠️

- [X] T083 [US5] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`: `Run_NewCpf_CreatesProtectedCustomer` (`GET /customers/{id}` com CPF mascarado), `Run_SameCpfAcrossBlocks_SingleCustomer` (três pedidos com o mesmo `customerId`), `Reimport_V2_UpdatesNameAndKeepsManualFields` (telefone/observações via `PUT /customers/{id}` antes, intactos depois), `Run_InvalidCpf_ReportsErrorAndContinues` (ação `error` "CPF ausente ou inválido na origem.", demais pedidos inseridos, execução `partial`), `Responses_NeverContainFullCpf` (regex `\d{3}\.?\d{3}\.?\d{3}-?\d{2}` só casa com máscara)

### Implementation for User Story 5

- [X] T084 [US5] Acrescentar em `backend/Ambrosio.Infra/AppServices/MaisVendas/FakeScenarios.cs`: CPF repetido em três pedidos de blocos 1 e 2, troca de nome de um cliente na versão `v2` e cenário `cpf-invalido` (um pedido sem CPF e um com dígito verificador errado)
- [X] T085 [US5] Em `UpsertImportedOrderAsync` (`backend/Ambrosio.Infra/Repository/ImportRunRepository.cs`): cliente existente → atualiza só o nome quando mudou, preservando os demais campos (FR-035); corrida `23505` em `ux_customers_user_cpf` → relê e repete uma vez (research §7); `CustomerCreated` no `UpsertOutcome`
- [X] T086 [US5] Em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs`: validar o CPF com `Cpf` antes de proteger; ausente/inválido → `AddChangeAsync(error, "CPF ausente ou inválido na origem.")` sem interromper o bloco (FR-036); somar `customers_created`; logs só com `CpfMasked` (FR-052)

**Checkpoint**: US5 testável isoladamente

---

## Phase 8: User Story 6 — Fotos dos produtos copiadas para o storage (Priority: P3)

**Goal**: cada foto de item gravada uma única vez no storage da vendedora (`ImageSource.Bot`), item ligado à imagem, falhas de foto não bloqueiam o pedido

**Independent Test**: `s482+foto-erro` e `s482+foto-gif` → pedidos gravados, itens sem imagem como "imagem com erro"; SKU repetido em três pedidos → 1 objeto no storage (quickstart §4 item 10, SC-010)

### Tests for User Story 6 ⚠️

- [X] T087 [US6] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`: `Run_RepeatedSku_StoresSingleImage` (mesmo `imageId` em três itens via `GET /orders/{id}`, link de leitura de `GET /images/{id}` abre a foto com `StorageHttp`), `Run_PhotoError_SavesOrderWithoutImage` (`imagesFailed = 1`, item com `imageId = null`, execução `partial`), `Run_GifPhoto_IsRejectedAndReported`, `Reimport_SameUrl_DoesNotStoreAgain` (`imagesStored = 0`), `Reimport_AfterPhotoError_RetriesDownload`, `RepeatBlock_AfterOrderFailure_ReusesStoredImage` (com `falha-bloco3`, repetir o bloco 3 com `s482+ok`; o item do 15º pedido fica ligado ao mesmo `imageId` e a contagem de imagens da vendedora não aumenta — FR-041)

### Implementation for User Story 6

- [X] T088 [US6] Embutir fotos de teste em `backend/Ambrosio.Infra/AppServices/MaisVendas/FakeImages/` (`small.jpg`, `small.png`, `small.webp`, `other.jpg` copiados de `backend/Ambrosio.ApiTests/Fixtures/Images/` e um `tiny.gif`) como `EmbeddedResource` em `backend/Ambrosio.Infra/Ambrosio.Infra.csproj`; em `FakeScenarios.cs`, uma URL por SKU, `DownloadImageAsync` devolvendo os bytes e os cenários `foto-erro` (URL de um item falha) e `foto-gif` (`image/gif`)
- [X] T089 [US6] Implementar `GetLinkedImageIdsAsync` e a ligação `order_items.image_id` a partir de `imageIdsByUrl` em `UpsertImportedOrderAsync` (`backend/Ambrosio.Infra/Repository/ImportRunRepository.cs`), mantendo `image_url` com a URL de origem (FR-037)
- [X] T090 [US6] Acrescentar o pipeline de fotos em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs`, **antes** do upsert de cada pedido: pula itens já ligados com a mesma URL (FR-040); cache `url → imageId` por execução; `DownloadImageAsync(url, ObjectStorage:MaxUploadBytes)` e `IImageService.StoreFromServerAsync(userId, stream, mimeType, ImageSource.Bot)`; `ImageRejectedException`, tamanho excedido ou falha de download → item sem imagem, `images_failed` +1, pedido gravado (FR-039); toda falha de download de foto, inclusive `MaisVendasSourceException`, vira "imagem com erro" e não derruba o bloco (FR-039/FR-045)

**Checkpoint**: US6 testável isoladamente

---

## Phase 9: User Story 7 — Acompanhar, retomar, cancelar e consultar o histórico (Priority: P3)

**Goal**: execução continua no servidor, retomada ao reabrir a tela, cancelamento ao fim do bloco, histórico paginado, aviso de queda de contagem, uma execução ativa por vendedora

**Independent Test**: iniciar, fechar e reabrir a aba (progresso volta); segunda execução → 409; cancelar → `cancelled` ao fim do bloco com blocos concluídos preservados (quickstart §4 itens 11, 12 e 16)

### Tests for User Story 7 ⚠️

- [X] T091 [US7] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`: `Start_WhileActive_Returns409` ("Já existe importação em andamento.", primeira continua), `Current_ReturnsActiveRunThen204`, `Cancel_Running_EndsAfterCurrentBlock` (blocos concluídos preservados, status `cancelled`), `Cancel_Queued_CancelsImmediately`, `Cancel_Finished_Returns409`, `List_ReturnsPagedDescending`, `List_PageSizeAbove50_Returns400`, `Report_CountDrop_SetsWarning` (período completo com `s482+ok` e depois `s482+vazio` → `countDropWarning = true`, `previousRunOrdersCount = 49`)
- [X] T092 [P] [US7] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/IsolationTests.cs`: execução da vendedora A → 404 para B em `GET runs/{id}` e `POST runs/{id}/cancel`; `GET runs` de B sem itens de A; `GET runs/current` de B → 204
- [X] T093 [P] [US7] Acrescentar em `backend/Ambrosio.ApiTests/Controllers/UnauthorizedAccessTests.cs` as rotas `GET /imports/mais-vendas/runs` e `POST /imports/mais-vendas/runs/{id}/cancel` → 401
- [X] T094 [P] [US7] Criar `frontend/src/components/imports/ImportHistory.test.tsx` (lista paginada, destaque `failed`/`partial`, abrir relatório) e `frontend/src/Contexts/ImportContext.test.tsx` (retoma a execução atual no mount, polling inicia em `queued`/`running` e para no status final, `cancelRun` atualiza o estado)

### Implementation for User Story 7

- [X] T095 [P] [US7] Criar `backend/Ambrosio.Domain/Validators/ImportRunListQueryValidator.cs` (`page ≥ 1`, `pageSize` 1–50)
- [X] T096 [US7] Implementar em `backend/Ambrosio.Infra/Repository/ImportRunRepository.cs`: `ListAsync` (ordenado por `requested_at DESC`, `PagedResult`), `RequestCancelAsync` e `GetPreviousRunOrdersCountAsync` (última execução `succeeded`/`partial` em modo `Apply` com o **mesmo período**, soma `inserted + updated + unchanged`, research §18)
- [X] T097 [US7] Acrescentar em `backend/Ambrosio.Domain/Services/ImportRunService.cs`: `ListAsync`, `CancelAsync` (`queued` → `cancelled` imediato; `running` → `cancel_requested` + sinal ao registro do worker; final → `ImportRunNotCancellableException`; outra vendedora → 404) e `previousRunOrdersCount`/`countDropWarning` no detalhe
- [X] T098 [US7] Em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs` e `backend/Ambrosio.Application/HostedServices/ImportRunWorker.cs`: checar `cancel_requested`/token entre blocos, terminar o bloco atual e marcar `Cancelled` com blocos restantes `NotExecuted`; execução cancelada enquanto `queued` é descartada ao sair da fila
- [X] T099 [US7] Acrescentar em `backend/Ambrosio.API/Controllers/ImportsController.cs` `GET runs` (200 `PagedResult<ImportRunInfo>`, 400) e `POST runs/{runId}/cancel` (202 detalhe, 404, `ImportRunNotCancellableException` → 409)
- [X] T100 [P] [US7] Criar `bruno/Imports/Historico.bru` e `bruno/Imports/Cancelar.bru`
- [X] T101 [US7] Acrescentar `listRuns` e `cancelRun` em `frontend/src/Services/importService.ts` e `history`, `loadHistory`, `cancelRun` e retomada da execução atual no mount em `frontend/src/Contexts/ImportContext.tsx`
- [X] T102 [US7] Criar `frontend/src/components/imports/ImportHistory.tsx` (usa `components/common/Pagination.tsx`, destaque `failed`/`partial`, abre o relatório via `loadRun`); acrescentar botão "Cancelar" e estado "Aguardando" em `frontend/src/components/imports/ImportProgress.tsx`, aviso de queda de contagem em `frontend/src/components/imports/ImportReport.tsx` e o histórico em `frontend/src/pages/ImportPage.tsx`, com chaves em `frontend/src/i18n/locales/pt-BR.ts`

**Checkpoint**: todas as histórias funcionais de forma independente

---

## Phase 10: Coletor real (Playwright) e imagem da API

**Purpose**: coleta real do +Vendas em produção (serve US1, US3, US6); não coberta por testes automatizados (constituição §VIII) — validada manualmente em simulação (quickstart §7)

- [X] T103 [P] Criar `backend/Ambrosio.Infra/AppServices/MaisVendas/NavigationGuard.cs`: allowlist de hosts `maisvendas.somalabs.com.br` e `api.mais-vendas.somalabs.com.br` (mais a origem das fotos quando igual), rotas `/login*`, `/cotaPiloto*`, `/storeOmni*`, `/detailOrder/*`; `EnsureAllowed(url)` lança e registra rota e tempo; bloqueia `somabox.somalabs.com.br`, analytics e qualquer outro host (FR-042, SC-012)
- [X] T104 [P] Criar `backend/Ambrosio.Infra/AppServices/MaisVendas/OriginRateLimiter.cs`: token-bucket de 1 token a cada 650 ms + pausa aleatória `MinDelayMs`–`MaxDelayMs` antes de cada ação, compartilhado com o download de fotos (FR-043)
- [X] T105 Criar `backend/Ambrosio.Infra/AppServices/MaisVendas/PlaywrightMaisVendasCollectorAppService.cs` (`CollectorVersion = "playwright-1.0"`): Chromium headless, `BrowserContext` novo por execução sem `storageState`, `AcceptDownloads = false`, `IgnoreHTTPSErrors = false`, fechado no `DisposeAsync` (FR-044); `RouteAsync("**/*")` com `NavigationGuard`; cliques só na lista permitida (abas Todos/Vendas/Devoluções/Cancelamentos, filtro de canal, "Ver mais", linhas, seletor de período), nunca "Novo carrinho", "Sair" ou BOX; login com detecção de captcha/2FA → `UnsupportedStep`; leitura de `client-data` via `page.Response` com fallback para o DOM, contadores e totais das abas, venda cancelada → dois `CollectedOrder`; retentativas 2 s → 4 s → 8 s e `MaisVendasSourceException` após 3 falhas (FR-045); fotos por `context.APIRequest.GetAsync` com `Content-Length` conferido e leitura limitada (research §9, §10, §12); depois registrar no `Startup` (T032)
- [X] T106 Alterar `backend/Dockerfile`: no estágio `build`, `dotnet tool install --global PowerShell` e `pwsh .../playwright.ps1 install chromium` com `PLAYWRIGHT_BROWSERS_PATH=/ms-playwright`; no estágio `final` (`aspnet:8.0`), `apt-get install` da lista fixa de dependências do Chromium (libnss3, libatk-bridge2.0-0, libdrm2, libxkbcommon0, libgbm1, libasound2, fonts-liberation etc.), `COPY --from=build /ms-playwright /ms-playwright`, `ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright`, usuário `app` mantido; estágio `migrator` inalterado (research §2)
- [X] T107 [P] Atualizar `deploy/README.md` com `shm_size`/`mem_limit` do serviço `api`, `MaisVendas__Collector=Playwright` em produção, recusa de `Fake` em Production, réplica única da API (fila em memória) e o checklist de liberação (termos de uso da Somalabs, primeira execução em simulação)

---

## Phase 11: Polish & Cross-Cutting Concerns

- [X] T108 [P] Consolidar `/imports/mais-vendas/*` (rotas, DTOs, códigos 202/204/400/401/404/409/429, `password` com `format: password`) e os campos `source`/`imageId` em `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml`
- [X] T109 [P] Acrescentar a seção de importação do +Vendas em `backend/README.md` (configuração `MaisVendas`, cenários do coletor Fake, instalação do Chromium em Development)
- [X] T110 [P] Atualizar `docs/historias/004-bot-migracao-mais-vendas.md` com a referência à spec `006-mais-vendas-import` e o status
- [X] T111 Revisão de segurança (§VI): confirmar com busca no código que `MarkInterruptedAsync` só é chamado por `ImportRunWorker.StartAsync` e é apenas `ExecuteUpdateAsync` (sem `ToListAsync`/`FirstAsync`/`Select` com retorno de dados), que nenhum `ILogger` recebe `ImportJob`/`MaisVendasCredentials`/`ImportRunStartInfo`, que `ImportsController` não tem `[AllowAnonymous]` e que toda consulta do `ImportRunRepository` filtra `user_id` primeiro (`MarkInterruptedAsync` não é leitura)
- [X] T112 Rodar no `frontend/`: `npm run lint`, `npm run type-check`, `npm test`, `npm run build -- --mode production` e `npm run verify:prod`; confirmar nenhum arquivo Playwright/E2E no frontend
- [ ] T113 Reconstruir a homologação (`wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build`, skill `wsl-docker`), conferir a migração no log do `migrator` e rodar `dotnet test backend/Ambrosio.ApiTests` inteiro verde contra `http://localhost:8080/api` (constituição §VII)
- [ ] T114 Executar a validação manual do `specs/006-mais-vendas-import/quickstart.md`: cenários do §4, busca das credenciais e de CPF completo no banco e nos logs (§5, SC-004) e reinicialização no meio da execução (§6, FR-020)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências
- **Foundational (Phase 2)**: depende do Setup — **bloqueia todas as histórias**
- **US1 (Phase 3)**: depende da Phase 2
- **US2 (Phase 4)**: depende da US1 (estende `ImportExecutionService`, `ImportRunService`, `ImportsController`, `ImportForm` e `ImportPage`)
- **US3 (Phase 5)**: depende da US1; independente de US2
- **US4 (Phase 6)**: depende da US1; usa o `BlockValidator` da US3 no teste `DryRun_CounterMismatch_FailsLikeApply`
- **US5 (Phase 7)**: depende da US1 (criação básica de cliente); `Reimport_V2_…` depende do ramo `updated` da US3
- **US6 (Phase 8)**: depende da US1; `RepeatBlock_AfterOrderFailure_ReusesStoredImage` usa o cenário `falha-bloco3` da T072 (US3)
- **US7 (Phase 9)**: depende da US1; `Report_CountDrop_SetsWarning` usa o cenário `vazio` da T072 (US3)
- **Coletor real (Phase 10)**: depende da Phase 2 (contratos do coletor); pode correr em paralelo às histórias
- **Polish (Phase 11)**: depende de todas as fases desejadas

### Within Each User Story

- Testes escritos primeiro e falhando antes da implementação
- Fake/cenários → modelos/regras → repositório → serviço → controller → frontend (types → service → context → hook → componentes → página)
- `ImportsControllerTests.cs`, `ImportRunRepository.cs`, `ImportExecutionService.cs` e `FakeScenarios.cs` são arquivos compartilhados entre histórias: tarefas neles são **sequenciais**

### Parallel Opportunities

- Setup: T002–T006 em paralelo após T001
- Foundational: T007–T013 em paralelo; T017–T025 em paralelo; T027, T028, T030 e T035 em paralelo
- US1: T037–T041 (testes) em paralelo; T042, T047–T051 e T055 em paralelo
- US7: T092–T095 e T100 em paralelo
- Phase 10: T103, T104 e T107 em paralelo, e a fase inteira em paralelo às US3–US7
- Polish: T108–T110 em paralelo

---

## Parallel Example: User Story 1

```text
# Testes da US1 juntos (arquivos diferentes):
Task: "T037 Rotas de importação → 401 em backend/Ambrosio.ApiTests/Controllers/UnauthorizedAccessTests.cs"
Task: "T038 Regras de data/credenciais em frontend/src/lib/importForm.test.ts"
Task: "T039 Formulário em frontend/src/components/imports/ImportForm.test.tsx"
Task: "T040 Progresso em frontend/src/components/imports/ImportProgress.test.tsx"
Task: "T041 Relatório em frontend/src/components/imports/ImportReport.test.tsx"

# Arquivos independentes da US1 juntos:
Task: "T042 ImportRunStartInfoValidator em backend/Ambrosio.Domain/Validators/"
Task: "T047 Bruno em bruno/Imports/"
Task: "T048 Tipos em frontend/src/types/import.ts"
Task: "T050 Chaves import.* em frontend/src/i18n/locales/pt-BR.ts"
Task: "T051 Regras em frontend/src/lib/importForm.ts"
```

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Phase 1 (Setup) → Phase 2 (Foundational)
2. Phase 3 (US1) → **validar**: 49 pedidos, R$ 123.300,58, progresso e relatório
3. Phase 4 (US2) → **validar**: credenciais nunca guardadas, bloqueio, vínculo de conta
4. US1 sozinha já é demonstrável em homolog, mas **só é liberável com a US2** (garantia de credenciais, P1) e com o coletor real (Phase 10)

### Incremental Delivery

1. Setup + Foundational → base pronta
2. US1 → demo em homolog (Fake)
3. US2 → MVP liberável (com Phase 10 para produção)
4. US3 → reimportações e repetição de bloco seguras
5. US4 → simulação
6. US5 → clientes sob demanda completos
7. US6 → fotos
8. US7 → histórico, cancelamento e retomada
9. Polish → homolog reconstruída + suíte completa verde

### Parallel Team Strategy

1. Equipe conclui Setup + Foundational
2. Dev A: US1 → US2; Dev B: Phase 10 (coletor Playwright + Dockerfile)
3. Depois da US1: Dev A US3 → US5; Dev B US6; Dev C US4 → US7 (atenção aos arquivos compartilhados)

---

## Notes

- [P] = arquivos diferentes, sem dependência de tarefa incompleta
- [Story] liga a tarefa à história para rastreabilidade
- Strings visíveis no frontend sempre de `frontend/src/i18n/locales/pt-BR.ts`; pastas `Contexts/` e `Services/` maiúsculas, `hooks/` e `types/` minúsculas (constituição §III)
- Mensagens de commit e comentários de código em português
- Nunca usar valores reais de login/senha do +Vendas em testes, Bruno ou documentação
