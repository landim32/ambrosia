# Research: Importação do +Vendas pelo frontend (pedidos, clientes e fotos)

**Feature**: `006-mais-vendas-import` | **Date**: 2026-10-27 | **Spec**: [spec.md](./spec.md)

Cada seção fecha uma decisão técnica do plano. Nenhum "NEEDS CLARIFICATION" fica em aberto: as perguntas da HU já foram respondidas na spec (Clarifications + Assumptions) e o que depende da origem real (formato da API interna `client-data`, seletores exatos) é tratado como **ajuste na primeira execução em modo simulação**, com o desenho preparado para as duas fontes (resposta interceptada ou DOM).

## §1. Onde o coletor roda

- **Decisão**: dentro da própria API (.NET 8 + `Microsoft.Playwright`), num `BackgroundService` (`ImportRunWorker`) alimentado por um `System.Threading.Channels.Channel<ImportJob>` em memória. A requisição `POST /imports/mais-vendas/runs` valida, grava a execução (`import_runs`, status `queued`) **sem credenciais**, enfileira o job com login e senha só no objeto em memória e responde `202`.
- **Rationale**: as credenciais ficam no mesmo processo que as recebeu (nunca trafegam de novo), o coletor reaproveita `ICpfProtectionAppService`, `IImageService.StoreFromServerAsync` e as regras de pedido/cliente do Domain direto, e a stack continua uma só (constituição §II). A HU (§6) já recomendava este desenho.
- **Alternativas consideradas**: serviço Node separado com Playwright (doc 03) — repassaria a senha por rede interna e duplicaria a stack; coleta pelo navegador da vendedora — impossível (CORS do +Vendas e automação).

## §2. Playwright .NET e Chromium na imagem da API

- **Decisão**: pacote `Microsoft.Playwright` (versão fixa no `Ambrosia.Infra.csproj`), Chromium headless. No `backend/Dockerfile`:
  - estágio `build` (SDK): `dotnet tool install --global PowerShell` e `pwsh <bin>/playwright.ps1 install chromium` com `PLAYWRIGHT_BROWSERS_PATH=/ms-playwright` (só os binários do navegador, sem `--with-deps`);
  - estágio `final` (`aspnet:8.0`, Debian): `apt-get install` da lista fixa de dependências do Chromium (libnss3, libatk-bridge2.0-0, libdrm2, libxkbcommon0, libgbm1, libasound2, fonts-liberation etc.), `COPY --from=build /ms-playwright /ms-playwright` e `ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright`; continua rodando como usuário `app`.
  - compose (homolog e prod): `shm_size: 512m` no serviço `api` (Chromium precisa de `/dev/shm`) e `mem_limit` documentado em `deploy/README.md`. O estágio `migrator` não muda.
- **Rationale**: imagem final cresce ~300 MB (só Chromium), sem SDK nem outros navegadores; o download dos binários acontece em camada de build cacheável.
- **Alternativas consideradas**: `mcr.microsoft.com/playwright/dotnet:v1.x` como base final — baseada no SDK, ~2 GB, três navegadores, usuário root; `playwright.ps1 install --with-deps` no estágio final — exigiria pwsh na imagem de runtime.

## §3. Origem simulada para os testes de API (constituição §VIII)

- **Decisão**: o Domain só conhece a abstração `IMaisVendasCollectorAppService` (Infra.Interfaces). A Infra tem **duas** implementações, escolhidas por `MaisVendas:Collector`:
  - `PlaywrightMaisVendasCollectorAppService` (`Playwright`) — produção e uso real em Development;
  - `FakeMaisVendasCollectorAppService` (`Fake`) — **Homolog**, determinística, sem rede, guiada pelo login e pela senha:
    - login no formato `conta+cenario` (ex.: `s482+ok`). A parte antes do `+` é o **identificador da conta** (vira o hash do vínculo, FR-008b); o sufixo escolhe o cenário: `ok`, `vazio`, `falha-bloco3` (falha no 15º pedido do 3º bloco), `contador-divergente`, `total-divergente`, `foto-erro`, `foto-gif`, `cpf-invalido`, `sem-ano`, `2fa` (login exige etapa não suportada), `instavel` (erro transitório que o retry supera);
    - senha `recusada` → credenciais recusadas; qualquer outra senha é a **versão do conteúdo** (`v1`, `v2`…): com `v2` o fake altera valor/status de alguns pedidos e o nome de um cliente, para provar "atualizado", "campos alterados" e "nome atualizado" (US3/US5);
    - fotos vêm de bytes embutidos (JPEG/PNG/WebP pequenos, os mesmos fixtures de `Ambrosia.ApiTests/Fixtures/Images`), com uma URL por SKU para provar a deduplicação; `foto-erro` falha a URL de um item; `foto-gif` devolve `image/gif`;
    - o atraso entre pedidos é `MaisVendas:Fake:DelayMs` (padrão 50 ms) para a suíte terminar em minutos.
  - `Program.ValidateSettings` **recusa** `Collector=Fake` com `ASPNETCORE_ENVIRONMENT=Production`.
- **Rationale**: o backend só pode ser testado por testes de API contra a homologação, e o +Vendas real não pode ser manipulado (assumption da spec). O fake deixa cada critério (falha no 15º pedido, divergência, foto com erro, nome alterado, conta diferente, bloqueio de tentativas) reproduzível e idempotente.
- **Alternativas consideradas**: um site estático simulando o +Vendas servido no compose e raspado pelo Playwright de verdade — manteria seletores duplicados em dois lugares e testaria HTML inventado, não a regra; mock por HTTP do `client-data` — formato ainda não inspecionado.

## §4. Fila, limites e credenciais em memória

- **Decisão**:
  - `ImportRunWorker : BackgroundService` lê o `Channel<ImportJob>` (ilimitado) e usa um `SemaphoreSlim(MaxConcurrentRuns)` (padrão 2): enquanto espera o semáforo a execução fica `queued` ("aguardando"); ao entrar vira `running`.
  - **Uma execução ativa por vendedora** é garantida no banco pelo índice único parcial `ux_import_runs_user_active (user_id) WHERE status IN (1, 2)`; o `INSERT` concorrente perde com `23505` → `409 "Já existe importação em andamento."` (FR-016, SC-008).
  - `ImportJob` guarda `login`/`password` como `string` apenas no objeto do job; não entra em DI, log, exceção, DTO de saída nem no banco. No `finally` da execução o job zera as referências e o `BrowserContext` é fechado (FR-005, FR-044). Limpeza determinística da memória gerenciada não é garantida pelo .NET (strings imutáveis); registra-se o limite e aceita-se, porque o processo é o mesmo que recebeu a requisição HTTPS.
  - Cancelamento: `ConcurrentDictionary<long, CancellationTokenSource>` por execução + coluna `cancel_requested`; o worker checa entre blocos (FR-019). Duas réplicas da API não são suportadas nesta entrega (fila em memória) — anotado em Complexity Tracking.
  - Reinicialização: no `StartAsync` o worker chama `IImportRunRepository.MarkInterruptedAsync(now)` → toda execução `queued`/`running` vira `failed` com "Execução interrompida pela reinicialização do servidor." (FR-020).
- **Alternativas consideradas**: Hangfire/Quartz (dependência nova e persistiria argumentos do job — a senha — em banco); fila externa (RabbitMQ) — a senha sairia do processo.

## §5. Progresso para a tela

- **Decisão**: **polling** de `GET /imports/mais-vendas/runs/current` a cada 3 s enquanto o status for `queued`/`running` (constante `IMPORT_POLL_MS` no frontend). Cumpre o atraso máximo de 5 s (FR-015) e a confirmação de início em ≤ 3 s (SC-006, o `202` é imediato).
- **Rationale**: simples, sem estado de conexão no gateway nginx nem no `apiClient`; a HU já propunha 3 s.
- **Alternativas consideradas**: SSE/SignalR — buffering do proxy, reconexão e um segundo canal de autenticação para pouco ganho.

## §6. Idempotência e impressão digital (`source_hash`)

- **Decisão**: `SourceHashCalculator` (Domain/Common) serializa o pedido normalizado em JSON canônico — chaves em ordem fixa, decimais com 2 casas (`InvariantCulture`), datas ISO, strings com `Trim()` — e calcula SHA-256 hex minúsculo. Entram: `orderNumber`, `orderDate`, `kind`, `channel`, `origin`, `status`, `total`, `originalSaleOn`, `cancelledOn`, `externalId`, `customer.cpfHash`, `customer.name`, `items[]` (`lineNo`, `productName`, `skuRef`, `size`, `price`, `imageUrl`) ordenados por `lineNo`. **Não** entram `captured_at`, `image_id` nem ids internos (FR-022).
- Upsert por `(user_id, order_number, order_date, kind)`:
  - inexistente → insere com `source = mais_vendas`, `source_hash`, `captured_at = source_changed_at = now` → **inserido**;
  - existe com `source = manual` → não toca; registra **conflito** (FR-028);
  - existe com o mesmo hash → só `captured_at` → **inalterado**;
  - existe com hash diferente → aplica campos da origem (inclusive os itens, substituídos), `source_changed_at = now`, lista de campos alterados (comparação campo a campo + `items`) → **atualizado** (FR-023, clarificação 2). Os campos que só existem no Ambrosia (`created_at`, `customer_id` do mesmo CPF, ligações de imagem de itens que mantiveram `image_url`) são preservados.
- **Alternativas consideradas**: comparar campo a campo sem hash — mais consultas e sem rastro compacto; guardar o payload bruto — contém CPF e foi excluído do escopo.

## §7. Gravação atômica por pedido

- **Decisão**: cada pedido é gravado em **seu próprio escopo de DI** (`IServiceScopeFactory.CreateAsyncScope()` → `AmbrosiaContext` novo) por `IImportRunRepository.UpsertImportedOrderAsync(...)`, dentro de uma transação explícita (`BeginTransactionAsync`) que cobre: cliente sob demanda (busca por `cpf_hash`; insere ou atualiza o nome), upsert do pedido e substituição dos itens. Falha → rollback só daquele pedido; o pedido conta como "com erro" e o bloco segue (FR-012, FR-036). Corrida na criação do cliente (`23505` em `ux_customers_user_cpf`) → relê e repete uma vez.
- **Rationale**: pedido e itens nunca ficam pela metade; falha no 15º não desfaz os 14 anteriores; não há transação de bloco a manter aberta durante minutos de coleta.

## §8. Vínculo de conta e bloqueio de tentativas

- **Decisão**: tabela `mais_vendas_accounts` (uma linha por vendedora). `account_hash` = HMAC-SHA256 (hex minúsculo) do identificador da conta na origem com **prefixo de domínio** `mais-vendas-account:` e o pepper já existente (`CpfProtection:Pepper`), via novo `IAccountHashAppService` na Infra (o `ICpfProtectionAppService` continua exclusivo do CPF). O identificador é o código da vendedora exibido pelo +Vendas (ex.: `S482`) ou, na falta, o login normalizado (`Trim().ToLowerInvariant()`).
  - Primeira execução em modo **gravação** com login aceito e sem vínculo → grava o hash e `linked_at`. Qualquer execução (inclusive simulação) com vínculo existente e hash diferente → `failed` com "Conta do +Vendas diferente da vinculada." antes de qualquer gravação (FR-008b).
  - `failed_login_count` incrementa a cada execução terminada com "Credenciais recusadas pelo +Vendas."; na 3ª seguida define `locked_until = now + 15 min`; zera num login aceito. `POST /runs` com `locked_until > now` → `429` + cabeçalho `Retry-After` e mensagem com o horário (FR-008a). Os limites vêm de `MaisVendas:MaxFailedLogins` e `MaisVendas:LockoutMinutes`.
- **Alternativas consideradas**: guardar o login em claro para comparar — proibido pela FR-006; bloqueio em memória — perderia o estado numa reinicialização.

## §9. Segurança da coleta (somente leitura, allowlist, ritmo)

- **Decisão** (só no coletor Playwright):
  - `BrowserContext` novo por execução, sem `storageState`, `AcceptDownloads = false`, fechado no `finally` (FR-044).
  - `context.RouteAsync("**/*")`: permite apenas hosts `maisvendas.somalabs.com.br` e `api.mais-vendas.somalabs.com.br` (mais a origem das fotos quando igual); aborta analytics (Google Analytics, Clarity) e qualquer outro host — inclusive `somabox.somalabs.com.br` (FR-042). Navegação só para `/login*`, `/cotaPiloto*`, `/storeOmni*`, `/detailOrder/*`; cliques só em elementos da lista: abas (Todos/Vendas/Devoluções/Cancelamentos), filtro de canal, "Ver mais", linhas de pedido, setas/seletor de período. "Novo carrinho", "Sair" e o menu BOX nunca são tocados. Toda navegação passa por `NavigationGuard.EnsureAllowed(url)` e é registrada no log com rota e tempo (FR-052, SC-012).
  - Ritmo: `RateLimiter` token-bucket (1 token/650 ms) + pausa aleatória 300–900 ms antes de cada ação; o download de fotos consome do mesmo limitador (FR-043).
  - Retentativas: até 3 tentativas com espera 2 s → 4 s → 8 s para timeout, "Erro ao carregar…" ou resposta não-2xx; 3 falhas consecutivas abortam o bloco como `failed` (FR-045).
  - Login que apresenta captcha/2FA (seletor de código ou iframe de captcha detectado) → `MaisVendasLoginOutcome.UnsupportedStep` → execução `failed` com "O login do +Vendas exige uma etapa não suportada (código ou captcha)." (FR-046).
  - `Microsoft.Playwright` em `headless` com `IgnoreHTTPSErrors = false`.

## §10. Fonte dos dados: resposta interceptada primeiro, DOM depois

- **Decisão**: o coletor escuta `page.Response` para `POST …/salesperson/orders/client-data` e, quando a resposta trouxer os campos necessários, monta `CollectedOrder` a partir dela; o DOM é lido só para o que faltar (contadores das abas, totais exibidos, dados do detalhe). A estrutura `CollectedBlock { Orders[], TabCounters{aba→n}, TabTotals{aba→R$} }` é **independente da fonte**, então trocar DOM↔API não toca o Domain. O payload bruto **não é persistido** (assumption da spec).
- **Validação do bloco** (FR-010/FR-011): após coletar, `BlockValidator` confere `TabCounters` × linhas lidas por aba e `TabTotals` × soma dos pedidos por aba; qualquer divergência → bloco `failed` **antes** de gravar.
- Normalização (`MaisVendasNormalizer`, Domain/Common): `R$ 1.234,56`/`-R$ 1.234,56` → `decimal` 2 casas; CPF só dígitos e validado por `Cpf` (ValueObject existente); data `24 Out` → ano deduzido do intervalo do bloco e **confirmado** pelo segmento `AAAA-MM-DD` da URL de detalhe (divergência → bloco falha); venda cancelada → dois `CollectedOrder` (`sale` e `cancellation` negativo); devolução → `return` negativo (FR-029, FR-031).

## §11. Datas, fuso e blocos

- **Decisão**: "hoje" e os limites de período usam `TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")` sobre `IClock.UtcNow` (funciona no Linux com `tzdata` da imagem `aspnet` e no Windows via ICU). `ImportBlockPlanner.Split(start, end, blockDays)` gera blocos consecutivos `[start, min(start+6, end)]`, sem lacuna nem sobreposição; o último pode ser menor; um único dia gera um bloco (FR-009, edge cases). Validação (FluentValidation, `ImportRunStartInfoValidator`): `startDate <= endDate`, `endDate <= hoje(SP)`, `(endDate - startDate + 1) <= MaxRangeDays`, login e senha não vazios — mensagens **sem ecoar valores** (FR-003, FR-006).
- `GET /defaults`: `suggestedStartDate = MAX(order_date) WHERE user_id = ? AND source = 2` (índice `ix_orders_user_source_date`) ou `null`; `suggestedEndDate = hoje(SP)` (FR-002).

## §12. Fotos dos itens

- **Decisão**: no coletor Playwright, a foto é baixada por `context.APIRequest.GetAsync(url)` (mesmos cookies da sessão, FR-037/edge case), com `Content-Length` conferido antes e leitura limitada a `ObjectStorage:MaxUploadBytes`; o `Content-Type` da resposta é o `mimeType` passado a `IImageService.StoreFromServerAsync(userId, stream, mimeType, ImageSource.Bot)`, que já recusa tipo fora de `AllowedMimeTypes` (`ImageRejectedException`) e deduplica por SHA-256 por usuário (FR-037/FR-038). O item recebe `image_id`; `image_url` continua com a URL de origem.
  - Pular download quando o item já gravado tem `image_id` e a mesma `image_url` (FR-040). Falha de download, tipo recusado ou tamanho excedido → item sem imagem, contagem `images_failed`, pedido gravado (FR-039). A foto é gravada **antes** do pedido; se o pedido falhar, a dedup reaproveita na repetição (FR-041).
  - Em **simulação** nada é baixado nem gravado: `images_stored` conta URLs distintas ainda não ligadas a um item com a mesma URL (FR-032).
- **Alternativas consideradas**: baixar com `HttpClient` separado — perderia a sessão quando a URL exige cookies.

## §13. Configuração nova (`MaisVendas`)

| Chave | Padrão | Faixa validada no startup | Uso |
|---|---|---|---|
| `MaisVendas:Collector` | `Playwright` | `Playwright` \| `Fake`; `Fake` proibido em Production | §3 |
| `MaisVendas:BaseUrl` | `https://maisvendas.somalabs.com.br` | URL https absoluta | §9 |
| `MaisVendas:BlockDays` | `7` | 1–31 | §11 |
| `MaisVendas:MaxRangeDays` | `366` | 1–3660 | §11 |
| `MaisVendas:MaxConcurrentRuns` | `2` | 1–8 | §4 |
| `MaisVendas:MaxFailedLogins` | `3` | 1–10 | §8 |
| `MaisVendas:LockoutMinutes` | `15` | 1–1440 | §8 |
| `MaisVendas:MinDelayMs` / `MaxDelayMs` | `300` / `900` | 100–5000, min ≤ max | §9 |
| `MaisVendas:Fake:DelayMs` | `50` | 0–5000 | §3 |

Classe `MaisVendasSettings` em `Ambrosia.DTO/Settings`, copiada em `AmbrosiaSettings` e exposta por `IOptions<MaisVendasSettings>` (mesmo padrão de `ObjectStorageSettings`). Homolog: `MaisVendas__Collector=Fake` no `x-api-environment` do compose; Development: `appsettings.Development.json` com `Playwright` (uso real) e instrução no quickstart para alternar.

## §14. Credenciais nunca em log, resposta ou Swagger

- **Decisão**: `ImportRunStartInfo` (DTO de entrada) é o único lugar em que `login`/`password` existem fora do job. Regras: o controller não loga o corpo; `ValidationError` já devolve só `campo` + mensagem (as mensagens do validator não citam valores); o binding falho do `System.Text.Json` cita caminho, não valor; `[DataType(DataType.Password)]` em `Password` faz o Swashbuckle emitir `format: password`; nenhum DTO de saída tem esses campos nem `accountHash`; os logs do worker usam `BeginScope(new { RunId, BlockNo })` e nunca recebem o `ImportJob`. O CPF só aparece mascarado (`CpfMasked`) em `import_run_changes`? — **não aparece**: a tabela de alterações guarda só a chave natural do pedido (FR-052).
- Frontend: `<input type="password" autoComplete="off">`, senha em `useState` local da página (nunca no Context, `localStorage` ou URL), zerada no `submit` bem-sucedido ou com erro (FR-007).

## §15. Frontend (padrão da 002 + skill `react-architecture`)

- **Decisão**: rota `/importacao` (dentro de `RequireAuth` + `AppShell`), link "Importar" no `AppHeader`. Arquivos: `types/import.ts`, `Services/importService.ts` (classe + singleton sobre `apiRequest`), `Contexts/ImportContext.tsx` (`defaults`, `currentRun`, `lastRun`, `history`, `loading`, `error`, `startRun`, `cancelRun`, `loadDefaults`, `loadHistory`, `loadRun`, polling interno com `setInterval` 3 s enquanto `queued`/`running`), `hooks/useImport.ts`, `pages/ImportPage.tsx`, componentes `components/imports/ImportForm.tsx`, `ImportProgress.tsx`, `ImportReport.tsx`, `ImportHistory.tsx`, `ImportStatusBadge.tsx`, validação em `lib/importForm.ts` (sem react-hook-form/zod — não estão nas dependências; o `LoginPage` usa o mesmo padrão com `lib/authForm.ts`). "Repetir este bloco" navega para `/importacao?inicio=AAAA-MM-DD&fim=AAAA-MM-DD`, que pré-preenche as datas (FR-026). Strings em `pt-BR.ts` sob `import.*`. Testes Vitest + RTL para formulário (bloqueios de envio, limpeza da senha, `autoComplete`), progresso (polling e parada), relatório (destaque `failed`/`partial`, aviso de queda, botão repetir) e `importForm` (regras de data).
- O `ImportProvider` entra em `main.tsx` depois de `OrderProvider` (não tem dependentes).

## §16. Testes de API (`Ambrosia.ApiTests`, skill `dotnet-test-api`)

- **Decisão**: `Controllers/ImportsControllerTests.cs` contra Homolog com `Collector=Fake`, mais rotas novas em `UnauthorizedAccessTests` e `IsolationTests` (execução do usuário A → 404 para B). Helper `ImportRunWaiter.WaitUntilFinishedAsync(runId, token)` faz polling até status final (timeout 3 min). Cobertura mínima: defaults (vazio e com histórico), 400 (datas/credenciais/limite), 202 + `queued/running` + relatório `succeeded` com 49 pedidos do cenário `ok` (SC-001 em homolog usa a contagem do fake), reimportação `v1`→`v1` 100% inalterados (SC-002), `v1`→`v2` atualizados com campos, `falha-bloco3` + repetição do bloco = resultado idêntico (SC-003), `contador-divergente` sem gravação (SC-009), `foto-erro`/`foto-gif` (SC-010), simulação sem alteração nas contagens (SC-011), 409 em execução concorrente, cancelamento, `recusada` ×3 → 429, conta diferente → `failed`, `2fa` → `failed`, e **busca das credenciais** em todos os corpos de resposta (SC-004 parcial; a busca em banco/logs é manual no quickstart).

## §17. "Não encontrados" e conflitos

- **Decisão**: ao fim de um bloco **sem falha**, `IImportRunRepository.ListImportedKeysInRangeAsync(userId, from, to)` devolve as chaves naturais dos pedidos `source = mais_vendas` do intervalo; as que não estiverem no `CollectedBlock` viram `import_run_changes.action = missing` (sem alterar o pedido). Blocos falhos não entram (FR-027). Conflito com manual (§6) vira `action = conflict`.

## §18. Resultado final e aviso de queda

- **Decisão**: `ImportRun.Finish(now)` aplica a FR-047a: `cancelled` se houve cancelamento; `failed` se algum bloco `failed` ou a execução falhou antes dos blocos; `partial` se todos os blocos terminaram mas `failed_orders + conflicts + images_failed > 0`; senão `succeeded`. O aviso de queda (FR-051) é **derivado** no relatório: `previousRunOrdersCount` = total de pedidos vistos (`inserted + updated + unchanged`) da última execução `succeeded`/`partial` em modo gravação com o **mesmo período**; `countDropWarning = atual < anterior`.
