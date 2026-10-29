---
description: "Lista de tarefas da feature 008-import-credentials"
---

# Tasks: Credenciais do +Vendas guardadas, importação pelo plugin e "Atualizar base"

**Input**: Documentos de design em `specs/008-import-credentials/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md), [constitution-amendment.md](constitution-amendment.md)

**Tests**: incluídos. A constituição (Princípio VII) exige testes de API para endpoints novos ou alterados no mesmo ciclo (skill `dotnet-test-api`, em `backend/Ambrosio.ApiTests/`, com o coletor Fake). O backend não recebe testes de unidade novos (Princípio VIII); o frontend recebe testes unitários (Vitest).

**Organization**: tarefas agrupadas por história de usuário da spec (US1 a US4).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história de usuário da tarefa (US1…US4)
- Caminhos relativos à raiz do repositório (`C:\repos\ambrosio`)

## Convenções para todas as tarefas

- Backend: skill `dotnet-architecture` (DTO → Domain → Infra.Interfaces → Infra → Application), `dotnet-fluent-validation` para validators, DTOs com `[JsonPropertyName]` camelCase, namespaces file-scoped `Ambrosio.*`.
- Frontend: skill `react-architecture` (types → service → context → hook → provider em `main.tsx`), `react-modal` para confirmações, `react-alert` para avisos; textos pela i18n em `frontend/src/i18n/locales/`.
- A senha do +Vendas NUNCA aparece em DTO de saída, log, mensagem de erro, auditoria ou resposta de tool (FR-003). O login só sai no `GET` de credenciais da própria vendedora.
- Toda leitura de credenciais é por `userId` vindo do `sub` (Princípio VI).
- Testes de API criam os próprios usuários (`POST /auth/register` + `POST /auth/login`).

---

## Phase 1: Setup (infraestrutura compartilhada)

**Purpose**: emenda da constituição, configuração da chave nova e do limite de 3 meses nos ambientes.

- [X] T001 Aplicar a emenda 3.4.0 → 3.4.1 descrita em `specs/008-import-credentials/constitution-amendment.md` a `.specify/memory/constitution.md` (nova regra de credenciais de terceiros no Princípio VI, Sync Impact Report e `Last Amended: 2026-10-28`)
- [X] T002 Em `backend/Ambrosio.DTO/Settings/MaisVendasSettings.cs`, acrescentar `CredentialsKey` (string, base64 de 32 bytes) e `MaxRangeMonths` (int, padrão 3) e remover `MaxRangeDays`; ajustar `backend/Ambrosio.Application/Startup.cs` (cópia das settings) e todas as leituras de `MaxRangeDays` no backend para `MaxRangeMonths`
- [X] T003 [P] Acrescentar a validação de startup de `MaisVendas__CredentialsKey` (obrigatória, base64 de exatamente 32 bytes, mensagem que nomeia a variável sem ecoar o valor) em `backend/Ambrosio.API/Program.cs` e em `backend/Ambrosio.McpServer/Program.cs`, e o repasse da chave pelo `ConfigureServices` do host do MCP
- [X] T004 [P] Configurar a chave nos ambientes (skill `dotnet-env`): `MaisVendas.CredentialsKey` local em `backend/Ambrosio.API/appsettings.Development.json` e `backend/Ambrosio.McpServer/appsettings.Development.json`; `MaisVendas__CredentialsKey: ${AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY}` no `x-api-environment` de `docker-compose.yml` e `docker-compose-prod.yml`; a variável comentada em `.env.example` e `.env.prod.example`; e no `.github/workflows/deploy-prod.yml` a gravação do GitHub Secret `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY` no `.env.prod` e na lista de segredos obrigatórios
- [X] T005 [P] Trocar `MaxRangeDays` por `MaxRangeMonths` nos `appsettings*.json` de `backend/Ambrosio.API/` e `backend/Ambrosio.McpServer/` e em `backend/Ambrosio.ApiTests/appsettings.Test.json` (`MaisVendas:MaxRangeMonths: 3`), expondo o valor em `backend/Ambrosio.ApiTests/Fixtures/ApiTestFixture.cs`

**Checkpoint**: a solução compila e API e host do MCP sobem com a chave; sem ela, recusam o startup.

---

## Phase 2: Foundational (pré-requisitos de todas as histórias)

**Purpose**: cifra, colunas novas e enums usados por todas as histórias.

- [X] T006 Criar os enums `MaisVendasCredentialsStatus` (None = 0, Valid = 1, NeedsUpdate = 2), `ImportRunStartedVia` (Screen = 1, Mcp = 2) e `ImportRunKind` (Period = 1, UpdateBase = 2) em `backend/Ambrosio.DTO/Enums/`
- [X] T007 [P] Criar `backend/Ambrosio.Infra.Interfaces/AppServices/IMaisVendasCredentialCipherAppService.cs` (`string Encrypt(long userId, string field, string plain)` e `string Decrypt(long userId, string field, string payload)`) e `backend/Ambrosio.Infra/AppServices/MaisVendasCredentialCipherAppService.cs`: AES-256-GCM com a chave de `MaisVendasSettings.CredentialsKey`, nonce aleatório de 12 bytes, dados associados `mais-vendas:{userId}:{field}`, formato `v1:` + base64(nonce ‖ tag ‖ cifra), `CryptographicException` em payload adulterado ou de outro usuário/campo (research R2); registrar como singleton em `backend/Ambrosio.Application/Startup.cs`
- [X] T008 Acrescentar a `backend/Ambrosio.Domain/Models/MaisVendasAccount.cs` as propriedades `LoginEncrypted`, `PasswordEncrypted`, `CredentialsSavedAt`, `PasswordChangedAt` e `CredentialsStatus` e os métodos `SetCredentials(loginEncrypted, passwordEncrypted, now)`, `ChangeLogin(loginEncrypted, now)`, `ClearCredentials(now)`, `HasCredentials` e as transições de status de `data-model.md` §1 (recusa → NeedsUpdate; aceite → Valid; senha nova → Valid); `RegisterRefusedLogin`/`RegisterAcceptedLogin` passam a ajustar o status quando há credenciais
- [X] T009 [P] Acrescentar `StartedVia` e `Kind` a `backend/Ambrosio.Domain/Models/ImportRun.cs` (parâmetros novos em `ImportRun.Create`, padrões Screen/Period) e aos DTOs `backend/Ambrosio.DTO/Imports/ImportRunInfo.cs` (`startedVia`, `kind`, como string camelCase) e ao mapeamento em `backend/Ambrosio.Domain/Services/ImportRunService.cs`
- [X] T010 Mapear as colunas novas em `backend/Ambrosio.Infra/Context/AmbrosioContext.cs` conforme `data-model.md` §1 e §2 (`login_encrypted`, `password_encrypted` `text`; `credentials_saved_at`, `password_changed_at` `timestamp without time zone`; `credentials_status`, `started_via` e `kind` `integer` com padrão) e gerar a migração `AddMaisVendasCredentials` em `backend/Ambrosio.Infra/Migrations/` (`dotnet ef migrations add AddMaisVendasCredentials --project Ambrosio.Infra --startup-project Ambrosio.API`), conferindo o SQL (só `ADD COLUMN`, padrões corretos)
- [X] T011 Garantir em `backend/Ambrosio.Infra/Repository/MaisVendasAccountRepository.cs` (e `backend/Ambrosio.Infra.Interfaces/Repository/IMaisVendasAccountRepository.cs`, se preciso) que `GetOrCreateAsync`/`UpdateAsync` persistem as colunas novas, sempre filtrando por `userId`

**Checkpoint**: migração aplicada no banco de Development; cifra e decifra funcionam e recusam payload trocado entre usuários.

---

## Phase 3: User Story 1 - Guardar as credenciais do +Vendas numa tela de configuração (Priority: P1) 🎯 MVP

**Goal**: a vendedora cadastra, troca e remove login e senha do +Vendas; a senha nunca é exibida e fica cifrada na base.

**Independent Test**: `PUT` de login e senha, `GET` devolve o login e `hasPassword: true` sem nenhum campo de senha; a coluna no banco começa com `v1:`; `DELETE` zera tudo; a tela mostra o login e nunca preenche a senha.

### Tests for User Story 1

- [X] T012 [P] [US1] Testes em `backend/Ambrosio.ApiTests/Controllers/MaisVendasCredentialsTests.cs`: `GET` sem credenciais (`status: none`); `PUT` com login e senha → `GET` com login, `hasPassword: true`, `status: valid`, e o corpo bruto nunca contém a senha; troca só de senha atualiza `passwordChangedAt`; troca de login sem senha → 400 "Informe a senha para trocar o login."; `PUT` sem credenciais e sem senha → 400; `DELETE` → 204 e `GET` volta a `none`; vendedora B não vê nem altera as credenciais de A; rotas sem token → 401
- [X] T013 [P] [US1] Testes unitários em `frontend/src/pages/MaisVendasSettingsPage.test.tsx`: estado sem credenciais; com credenciais o login vem preenchido e a senha vazia com "Senha cadastrada em dd/mm/aaaa"; trocar login sem senha mostra o erro; salvar limpa o campo de senha; remover pede confirmação em modal; `status: needsUpdate` mostra o alerta

### Implementation for User Story 1

- [X] T014 [P] [US1] Criar `backend/Ambrosio.DTO/Imports/MaisVendasCredentialsInfo.cs` (`login`, `hasPassword`, `passwordChangedAt`, `status`) e `backend/Ambrosio.DTO/Imports/MaisVendasCredentialsUpdateInfo.cs` (`login`, `password?`) conforme `contracts/api.md`
- [X] T015 [P] [US1] Criar `backend/Ambrosio.Domain/Validators/MaisVendasCredentialsUpdateInfoValidator.cs`: login obrigatório (até 120 caracteres), senha até 200 caracteres; mensagens sem ecoar valores
- [X] T016 [US1] Criar `backend/Ambrosio.Domain/Interfaces/IMaisVendasCredentialService.cs` e `backend/Ambrosio.Domain/Services/MaisVendasCredentialService.cs` com `GetAsync(userId)`, `SaveAsync(userId, info)` (regras do `PUT` em `contracts/api.md`: senha obrigatória sem credenciais ou com login diferente; cifra login e senha com `IMaisVendasCredentialCipherAppService`) e `DeleteAsync(userId)`, mais o método interno de uso do worker `GetDecryptedAsync(userId)` que devolve `(login, password)` só para a execução; registrar em `backend/Ambrosio.Application/Startup.cs`
- [X] T017 [US1] Acrescentar a `backend/Ambrosio.API/Controllers/ImportsController.cs` as rotas `GET`, `PUT` e `DELETE /imports/mais-vendas/credentials` (`[Authorize]`, usuário do `sub`, `ErrorResult` nos erros de validação)
- [X] T018 [P] [US1] Criar a entidade de frontend pela skill `react-architecture`: `frontend/src/types/maisVendasCredentials.ts`, `frontend/src/services/maisVendasCredentialsService.ts` (Fetch API pelo `apiClient`), `frontend/src/contexts/MaisVendasCredentialsContext.tsx`, `frontend/src/hooks/useMaisVendasCredentials.ts` e o provider em `frontend/src/main.tsx`
- [X] T019 [US1] Criar `frontend/src/pages/MaisVendasSettingsPage.tsx` conforme `contracts/ui.md` (login, senha `type="password"` com `autocomplete="new-password"` sempre vazia, salvar, remover com `react-modal`, alerta de `needsUpdate` e toast de sucesso com `react-alert`), a rota `/configuracoes/mais-vendas` em `frontend/src/App.tsx`, o item de menu em `frontend/src/components/layout/navItems.ts` e os textos em `frontend/src/i18n/locales/`

**Checkpoint**: credenciais cadastradas pela tela, cifradas no banco e nunca devolvidas.

---

## Phase 4: User Story 2 - Importar só com o período, usando as credenciais guardadas (Priority: P1)

**Goal**: a importação usa as credenciais guardadas; o período máximo é de 3 meses.

**Independent Test**: com credenciais cadastradas e o coletor Fake, `POST /imports/mais-vendas/runs` só com datas inicia e conclui; sem credenciais é recusado; 26/07 a 26/10 é recusado e 26/07 a 25/10 aceito; com o cenário Fake de senha recusada, a execução falha e as credenciais passam a `needsUpdate`.

### Tests for User Story 2

- [X] T020 [P] [US2] Ajustar `backend/Ambrosio.ApiTests/Controllers/ImportsControllerTests.cs`, `backend/Ambrosio.ApiTests/Helpers/TestDataHelper.cs` (`StartImportAsync`/`StartImportRawAsync` passam a cadastrar as credenciais Fake pelo `PUT` antes e a iniciar só com datas) e os demais testes que iniciam importação (inclusive `IsolationTests`): início sem credenciais → 400 "Cadastre as credenciais do +Vendas na configuração."; período de 3 meses + 1 dia → 400 "O período não pode passar de 3 meses."; período de exatamente 3 meses aceito; `login`/`password` no corpo são ignorados e não gravados; senha Fake recusada → execução `failed` e `GET` de credenciais com `status: needsUpdate`; defaults trazem `hasCredentials`, `credentialsStatus` e `maxRangeMonths`
- [X] T021 [P] [US2] Ajustar `frontend/src/lib/importForm.test.ts`, `frontend/src/components/imports/ImportForm.test.tsx` e `frontend/src/pages/ImportPage.test.tsx`: sem campos de login e senha; sem credenciais aparece o aviso com o botão para a configuração e iniciar fica indisponível; limite de 3 meses com a mensagem única; `needsUpdate` mostra aviso sem bloquear

### Implementation for User Story 2

- [X] T022 [US2] Remover `Login`/`Password` de `backend/Ambrosio.DTO/Imports/ImportRunStartInfo.cs` e de `backend/Ambrosio.Domain/Common/ImportJob.cs` (e do `Clear`), e trocar em `backend/Ambrosio.Domain/Validators/ImportRunStartInfoValidator.cs` as regras de login/senha pela de 3 meses (`endDate <= startDate.AddMonths(MaxRangeMonths).AddDays(-1)`, mensagem "O período não pode passar de 3 meses.")
- [X] T023 [US2] Em `backend/Ambrosio.Domain/Services/ImportRunService.cs`: `StartAsync(userId, info, startedVia)` recusa sem credenciais (`ValidationException` com a mensagem do contrato), cria a execução com `Kind = Period` e enfileira o `ImportJob` sem credenciais; `GetDefaultsAsync` devolve `hasCredentials`, `credentialsStatus` e `maxRangeMonths` em `backend/Ambrosio.DTO/Imports/ImportDefaultsInfo.cs` (sai `maxRangeDays`); ajustar `backend/Ambrosio.Domain/Interfaces/IImportRunService.cs` e o controller
- [X] T024 [US2] Em `backend/Ambrosio.Domain/Services/ImportExecutionService.cs`, obter login e senha por `IMaisVendasCredentialService.GetDecryptedAsync` no início da execução (antes de abrir a sessão do coletor), descartá-los ao terminar em qualquer desfecho, encerrar como falha "credenciais do +Vendas não cadastradas" se tiverem sido removidas antes de a execução começar, e gravar a transição de status das credenciais na recusa e no aceite do login (research R3/R7)
- [X] T025 [US2] Atualizar o frontend da importação: `frontend/src/types/import.ts` (sem credenciais no início; defaults com `hasCredentials`, `credentialsStatus`, `maxRangeMonths`), `frontend/src/services/importService.ts`, `frontend/src/lib/importForm.ts` (limite de 3 meses a partir de `maxRangeMonths`), `frontend/src/components/imports/ImportForm.tsx` (sem login e senha; aviso e botão "Ir para a configuração" sem credenciais; aviso de `needsUpdate`) e `frontend/src/pages/ImportPage.tsx`, com os textos em `frontend/src/i18n/locales/`

**Checkpoint**: importação pela tela sem digitar senha; limite de 3 meses em tela e servidor.

---

## Phase 5: User Story 3 - "Atualizar base" (Priority: P2)

**Goal**: botão que importa do último pedido importado até hoje, recusando intervalos maiores que 3 meses.

**Independent Test**: com pedidos importados pelo Fake, defaults trazem `updateBase.available: true` com o período certo e `POST …/runs/update-base` inicia com `kind: updateBase`; sem pedidos importados ou com intervalo > 3 meses, `available: false` com o motivo e o `POST` é recusado sem criar execução.

### Tests for User Story 3

- [X] T026 [P] [US3] Testes em `backend/Ambrosio.ApiTests/Controllers/ImportUpdateBaseTests.cs`: sem pedido importado → defaults com `available: false` e motivo, `POST …/runs/update-base` → 400 sem execução criada; depois de uma importação Fake, defaults com `startDate` = data do último pedido importado e `endDate` = hoje (São Paulo), `POST` → 202 com `kind: updateBase` e o mesmo período; sem credenciais → 400; execução ativa → 409. O cenário "mais de 3 meses" é conferido por uma importação Fake de período antigo seguido do `POST` (400 com a mensagem de importar em partes)
- [X] T027 [P] [US3] Testes unitários em `frontend/src/components/imports/ImportForm.test.tsx` (ou `frontend/src/components/imports/UpdateBaseButton.test.tsx`): botão habilitado com o período nos defaults, modal de confirmação com as datas e a simulação, botão desabilitado com o motivo como dica

### Implementation for User Story 3

- [X] T028 [P] [US3] Criar `backend/Ambrosio.DTO/Imports/ImportUpdateBaseInfo.cs` (`dryRun`) e acrescentar `UpdateBase` (`available`, `startDate`, `endDate`, `reason`) a `backend/Ambrosio.DTO/Imports/ImportDefaultsInfo.cs`
- [X] T029 [US3] Em `backend/Ambrosio.Domain/Services/ImportRunService.cs`, calcular o período do "Atualizar base" num método único (data do último pedido importado do +Vendas — o mesmo dado de `GetSuggestedStartDateAsync` em `backend/Ambrosio.Infra/Repository/ImportRunRepository.cs` — até `SaoPauloCalendar.Today`), usado por `GetDefaultsAsync` e pelo novo `StartUpdateBaseAsync(userId, info, startedVia)`, que recusa sem pedidos importados, com intervalo maior que `MaxRangeMonths` ou sem credenciais (mensagens de `contracts/api.md`) e cria a execução com `Kind = UpdateBase` pelo mesmo caminho do `StartAsync`
- [X] T030 [US3] Acrescentar `POST /imports/mais-vendas/runs/update-base` a `backend/Ambrosio.API/Controllers/ImportsController.cs` (202, 400, 409, 423 como o início normal)
- [X] T031 [US3] Frontend: `startUpdateBase` em `frontend/src/services/importService.ts` e no contexto/hook de importação (`frontend/src/contexts/ImportContext.tsx`, `frontend/src/hooks/useImport.ts`), e o botão "Atualizar base" com modal de confirmação (skill `react-modal`) e dica de indisponibilidade em `frontend/src/components/imports/ImportForm.tsx`, com os textos em `frontend/src/i18n/locales/`

**Checkpoint**: "Atualizar base" pela tela, com o período calculado no servidor.

---

## Phase 6: User Story 4 - Importar pelo plugin do ChatGPT (Priority: P2)

**Goal**: `iniciar_importacao` passa a iniciar com as credenciais guardadas; nova tool `atualizar_base`; nada de credenciais pela conversa.

**Independent Test**: com credenciais cadastradas, `iniciar_importacao` com `confirmar: true` cria a execução (`startedVia: mcp`) visível em `GET /imports/mais-vendas/runs/current`; `atualizar_base` idem com `kind: updateBase`; sem credenciais, as duas devolvem `isError` com o link da configuração; `senha` como argumento é recusado e não aparece na auditoria.

### Tests for User Story 4

- [X] T032 [P] [US4] Reescrever `backend/Ambrosio.ApiTests/McpServer/ImportToolsTests.cs`: `sugestao_periodo_importacao` com `temCredenciais`, `maxMeses` e `atualizarBase`; `iniciar_importacao` sem `confirmar` não cria execução; com `confirmar: true` cria e devolve `importacao_id` visto pela API com `startedVia: mcp`; período de mais de 3 meses → mesma mensagem da API; sem credenciais → `isError` com `/configuracoes/mais-vendas`; `atualizar_base` (prévia e início, sem pedidos importados → recusa); `login`/`senha`/`password` recusados e ausentes da auditoria; auditoria com período e modo. Cadastrar credenciais Fake pelo `PUT` da API antes de cada cenário
- [X] T033 [P] [US4] Em `backend/Ambrosio.ApiTests/McpServer/ParityTests.cs`, acrescentar a lista explícita de operações sem tool (`GET`, `PUT` e `DELETE /imports/mais-vendas/credentials`, FR-019) e ajustar a contagem de tools para 29

### Implementation for User Story 4

- [X] T034 [US4] Em `backend/Ambrosio.McpServer/Mcp/Tools/ImportTools.cs`: `iniciar_importacao` passa a validar e iniciar pelo `IImportRunService.StartAsync(userId, info, ImportRunStartedVia.Mcp)` (prévia sem criar execução, com as mesmas validações e o aviso de `needsUpdate`); nova tool `atualizar_base` (`simular`, `confirmar`) com `[AmbrosioScopes("imports:write")]`, `[AmbrosioEndpoint("POST /imports/mais-vendas/runs/update-base")]` e `StartUpdateBaseAsync`; `sugestao_periodo_importacao` devolve `temCredenciais`, `situacaoCredenciais`, `maxMeses` e `atualizarBase`; sem credenciais, erro com o link `{Mcp:FrontendBaseUrl}/configuracoes/mais-vendas`; descrições com o aviso de nunca pedir a senha (contracts/mcp-tools.md)
- [X] T035 [US4] Garantir que as tools de importação passam pelo `WriteToolHelper` (auditoria com período, modo e desfecho, sem credenciais) e que a mensagem de falta de credenciais é tratada como erro de domínio em `backend/Ambrosio.McpServer/Mcp/Results/ToolResultFactory.cs`

**Checkpoint**: importação e "Atualizar base" pela conversa, sem credenciais na conversa.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T036 [P] Atualizar `plugin/skills/vendas-ambrosio/SKILL.md` e `plugin/README.md` (importação pela conversa, "Atualizar base", credenciais só na tela de configuração) e o roteiro de verificação
- [X] T037 [P] Atualizar `bruno/Imports/` (requisições `Credenciais - Consultar`, `Credenciais - Salvar`, `Credenciais - Remover`, `Atualizar Base`; `Iniciar Importacao.bru` sem login e senha; `maisVendasLogin`/`maisVendasPassword` usados só no `PUT`) e `bruno/Mcp/Tools/` (`Iniciar Importacao` iniciando de fato e `Atualizar Base`)
- [X] T038 [P] Atualizar `deploy/README.md` (chave nova, como gerar, efeito de trocá-la) e `backend/README.md` (variável nova), e marcar em `specs/006-mais-vendas-import/spec.md` e `specs/007-chatgpt-mcp-plugin/research.md` que FR-005/FR-006 da 006 e o R9 da 007 foram substituídos pela feature 008
- [ ] T039 Rodar a skill `security-review` sobre o diff da feature (cifra, dados associados, ausência da senha em respostas, logs e auditoria, isolamento entre vendedoras) e corrigir os achados
- [ ] T040 Rodar `dotnet test backend/Ambrosio.ApiTests` (importação, credenciais, MCP e paridade) contra a homologação reconstruída (`docker compose up -d --build` pela skill `wsl-docker`, com `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY` no `.env`) e, no frontend, `npx tsc -b --noEmit`, `npx eslint src` e `npx vitest run`
- [ ] T041 Executar a verificação manual de `specs/008-import-credentials/quickstart.md` §2 e §3 (tela e ChatGPT) e confirmar no banco que login e senha estão só cifrados

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências. T001 (emenda) é pré-requisito formal da implementação.
- **Foundational (Phase 2)**: depende do Setup. Bloqueia todas as histórias.
- **US1 (Phase 3)**: depende da Foundational. As outras histórias precisam de credenciais cadastradas, então **US1 vem primeiro na prática**.
- **US2 (Phase 4)**: depende de US1 (credenciais para iniciar) e da Foundational.
- **US3 (Phase 5)**: depende de US2 (mesmo caminho de início, limite de 3 meses).
- **US4 (Phase 6)**: depende de US2 (início sem credenciais no DTO) e de US3 (`atualizar_base`).
- **Polish (Phase 7)**: depende das histórias desejadas.

### Within Each User Story

- Testes escritos primeiro e falhando antes da implementação.
- DTOs → validators → serviços → controller/tools → frontend.
- Fechar a história no checkpoint antes da próxima.

### Parallel Opportunities

- Setup: T003, T004 e T005 em paralelo depois de T002.
- Foundational: T007 e T009 em paralelo com T008; T010 depois de T008/T009.
- US1: T012 e T013 em paralelo; T014, T015 e T018 em paralelo.
- US2: T020 e T021 em paralelo.
- US3: T026, T027 e T028 em paralelo.
- US4: T032 e T033 em paralelo.
- Polish: T036, T037 e T038 em paralelo.

---

## Parallel Example: User Story 1

```text
# Testes da US1 juntos:
T012 MaisVendasCredentialsTests.cs
T013 MaisVendasSettingsPage.test.tsx

# Peças independentes juntas:
T014 DTOs de credenciais
T015 MaisVendasCredentialsUpdateInfoValidator.cs
T018 types/service/context/hook do frontend
```

## Parallel Example: User Story 4

```text
T032 ImportToolsTests.cs | T033 ParityTests.cs (exceções de credenciais)
```

---

## Implementation Strategy

### MVP First (User Story 1 + User Story 2)

1. Phase 1 (com a emenda aprovada) e Phase 2.
2. US1: credenciais cadastradas e protegidas.
3. US2: importação pela tela sem senha e com o limite de 3 meses.
4. **Parar e validar**: testes de API e verificação da tela (quickstart §2).

### Incremental Delivery

1. US1 + US2 → importação sem redigitar a senha (o mínimo que entrega valor, já que remover os campos da tela exige as credenciais guardadas).
2. US3 → "Atualizar base".
3. US4 → importação pelo plugin.
4. Polish → documentação, Bruno, revisão de segurança, homologação e verificação manual.

### Notes

- Frontend e backend da US2 precisam ir para produção no mesmo deploy: o `POST …/runs` deixa de aceitar credenciais.
- O GitHub Secret `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY` precisa existir antes do primeiro deploy (o pipeline para sem ele).
- Commit a cada tarefa ou grupo lógico.
