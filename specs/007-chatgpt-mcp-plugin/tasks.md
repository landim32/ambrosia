---
description: "Lista de tarefas da feature 007-chatgpt-mcp-plugin"
---

# Tasks: Plugin do Ambrosio no ChatGPT

**Input**: Documentos de design em `specs/007-chatgpt-mcp-plugin/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md), [constitution-amendment.md](constitution-amendment.md)

**Tests**: incluídos. A constituição (Princípio VII) exige testes de API para endpoints novos no mesmo ciclo, criados com a skill `dotnet-test-api` em `backend/Ambrosio.ApiTests/` e rodados contra a homologação. O backend **não** recebe testes de unidade (Princípio VIII). O frontend recebe teste unitário (Vitest).

**Organization**: tarefas agrupadas por história de usuário da spec (US1 a US6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história de usuário da tarefa (US1…US6)
- Caminhos relativos à raiz do repositório (`C:\repos\ambrosia`)

## Convenções para todas as tarefas

- Backend: skill `dotnet-architecture` para entidades, serviços, repositórios e DI; namespaces file-scoped `Ambrosio.*`; DTOs com `[JsonPropertyName]` camelCase.
- Banco: snake_case, `bigint` `{entidade}_id`, sem cascata (`ClientSetNull`), `timestamp without time zone`.
- Docker: sempre pela skill `wsl-docker` (`wsl -e bash -lc "cd /mnt/c/repos/ambrosia && docker compose …"`).
- Testes de API: skill `dotnet-test-api`; cada teste cria os próprios usuários por `POST /auth/register` + `POST /auth/login` (fixture `ApiTestFixture.RegisterAsync`/`LoginAsync`).
- Endereços canônicos vêm de configuração (`Auth:Issuer`, `Mcp:ResourceUrl`), nunca fixos no código, e são recusados no startup se terminarem em barra.
- Nenhuma tool aceita `userId`: o usuário é sempre o `sub` do token do AS.

---

## Phase 1: Setup (infraestrutura compartilhada)

**Purpose**: emenda da constituição, projeto novo e encaixe nos ambientes.

- [X] T001 Aplicar a emenda 3.3.0 → 3.4.0 descrita em `specs/007-chatgpt-mcp-plugin/constitution-amendment.md` ao arquivo `.specify/memory/constitution.md`: nova linha "Esquema do MCP", regras de cookie e de rotas anônimas substituídas, regra da chave de assinatura, quality gate novo, Sync Impact Report e `Last Amended: 2026-10-27`
- [X] T002 Criar o projeto `backend/Ambrosio.McpServer/Ambrosio.McpServer.csproj` (`Microsoft.NET.Sdk.Web`, `net8.0`, `Nullable` e `ImplicitUsings` habilitados) referenciando `Ambrosio.Application`, `Ambrosio.Domain`, `Ambrosio.DTO`, `Ambrosio.Infra` e `Ambrosio.Infra.Interfaces`, e adicioná-lo a `backend/Ambrosio.sln`
- [X] T003 Adicionar ao `backend/Ambrosio.McpServer/Ambrosio.McpServer.csproj` os pacotes com versão fixa: `ModelContextProtocol.AspNetCore` (última 2.x estável que compila em `net8.0`), `OpenIddict.AspNetCore` e `OpenIddict.EntityFrameworkCore` (7.7.x, ≥ 7.7.0) e `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.x; adicionar `OpenIddict.EntityFrameworkCore.Models` (7.7.x) a `backend/Ambrosio.Infra/Ambrosio.Infra.csproj`
- [X] T004 [P] Adicionar o target `mcp` ao `backend/Dockerfile`: estágio `aspnet:8.0` que publica `Ambrosio.McpServer` (mesmo padrão do target `final`, porta 8080, usuário `app`, `ENTRYPOINT ["dotnet", "Ambrosio.McpServer.dll"]`)
- [X] T005 [P] Adicionar o serviço `mcp` ao `docker-compose.yml` (imagem `ambrosio-mcp:homolog`, `container_name: ambrosio-homolog-mcp`, `target: mcp`, mesmo `x-api-environment`, mais `Auth__Issuer: ${AUTH_ISSUER}`, `Mcp__ResourceUrl: ${MCP_RESOURCE_URL}`, `Auth__SigningKeyPem: ${AUTH_SIGNING_KEY_PEM}`, `Auth__AllowedRedirectUris__2` com a URI de teste da suíte, `depends_on` do `migrator` concluído) e as variáveis novas comentadas em `.env.example`
- [X] T006 [P] Adicionar o serviço `mcp` ao `docker-compose-prod.yml` (mantendo os nomes de projeto, container e rede `ambrosia-*` da produção), as variáveis `AUTH_ISSUER`, `MCP_RESOURCE_URL` e `AUTH_SIGNING_KEY_PEM` a `.env.prod.example` e aos secrets gravados por `.github/workflows/deploy-prod.yml`
- [X] T007 [P] Trocar em `deploy/nginx/routes.inc` os blocos 503 de `/auth/` e `/mcp` por `proxy_pass` para `http://mcp:8080` (sem reescrever o caminho; em `/mcp`: `proxy_buffering off`, `proxy_read_timeout 300s`), e rotear `/.well-known/oauth-protected-resource` e `/.well-known/oauth-authorization-server/` para o mesmo upstream, mantendo `return 404` para os demais `/.well-known/`
- [X] T008 [P] Criar `backend/Ambrosio.McpServer/appsettings.json`, `appsettings.Development.json`, `appsettings.Homolog.json` e `appsettings.Production.json` com as seções `Auth` e `Mcp` do contrato `contracts/oauth-endpoints.md` §7 (sem segredos), seguindo a skill `dotnet-env`
- [X] T009 [P] Acrescentar a `backend/Ambrosio.ApiTests/appsettings.Test.json` as chaves `McpBaseUrl` (`http://localhost:8080`), `Auth:Issuer`, `Mcp:ResourceUrl` e `Auth:TestRedirectUri`, e expô-las em `backend/Ambrosio.ApiTests/Fixtures/ApiTestFixture.cs`

**Checkpoint**: solução compila com o projeto vazio; `docker compose up` sobe o serviço `mcp`.

---

## Phase 2: Foundational (pré-requisitos de todas as histórias)

**Purpose**: banco, configuração, OAuth básico e o esqueleto do MCP com escopo, desafio, prévia e auditoria.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

- [X] T010 Criar `backend/Ambrosio.DTO/Settings/AuthServerSettings.cs` e `McpSettings.cs` (Issuer, ResourceUrl, SigningKeyPem, AllowedRedirectUris, Cimd.AllowedHosts, AccessTokenMinutes = 30, RefreshTokenDays = 14, AllowedOrigins, GoogleClientId) e a validação de startup em `backend/Ambrosio.McpServer/Program.cs` que recusa configuração incompleta, URLs com barra no final, `http` fora de Development e falta de `SigningKeyPem` em Homolog/Production (padrão do `ValidateSettings` da API, pulando em `EF.IsDesignTime`)
- [X] T011 Criar as entidades OpenIddict com chave `long` em `backend/Ambrosio.Infra/Context/OAuth/` (`OAuthApplication`, `OAuthAuthorization`, `OAuthScope`, `OAuthToken`, herdando dos modelos `OpenIddictEntityFramework*<long, …>`), com `registration_source` e `metadata_fetched_at` em `OAuthApplication`, e registrá-las no `backend/Ambrosio.Infra/Context/AmbrosioContext.cs` com Fluent API conforme `data-model.md` §1.1–1.4 (tabelas `oauth_*`, colunas snake_case, PKs `{entidade}_id`, FKs `ClientSetNull`, sobrescrevendo a cascata padrão)
- [X] T012 [P] Criar a entidade `McpAuditEntry` em `backend/Ambrosio.Domain/Models/McpAuditEntry.cs`, o enum `McpAuditOutcome` em `backend/Ambrosio.DTO/Enums/McpAuditOutcome.cs` e o mapeamento da tabela `mcp_audit_entries` (com índice `ix_mcp_audit_entries_user_created`) em `backend/Ambrosio.Infra/Context/AmbrosioContext.cs`, conforme `data-model.md` §1.5
- [X] T013 Gerar a migração `AddOAuthServer` em `backend/Ambrosio.Infra/Migrations/` (`dotnet ef migrations add AddOAuthServer --project Ambrosio.Infra --startup-project Ambrosio.API`) e conferir no SQL gerado: nomes snake_case, `bigint identity`, nenhum `ON DELETE CASCADE`
- [X] T014 Criar `backend/Ambrosio.McpServer/Auth/SigningKeyProvider.cs`: lê a chave RSA de `Auth:SigningKeyPem` (PKCS#8), e em Development sem a chave gera uma RSA 2048 e grava em `%APPDATA%/Ambrosio/mcp-signing.pem` para reutilizar (R13)
- [X] T015 Configurar o servidor OpenIddict em `backend/Ambrosio.McpServer/Auth/OpenIddictSetup.cs` e chamar em `Program.cs`: `UseEntityFrameworkCore().UseDbContext<AmbrosioContext>().ReplaceDefaultEntities<…, long>()`, `SetIssuer` explícito, endpoints `connect/authorize`, `connect/token`, `connect/userinfo`, `connect/revoke`, `.well-known/jwks` sob o caminho `/auth`, `AllowAuthorizationCodeFlow`, `AllowRefreshTokenFlow`, `RequireProofKeyForCodeExchange`, só `S256`, `DisableAccessTokenEncryption`, chave do `SigningKeyProvider`, validades de `AuthServerSettings`, rolling refresh tokens, `UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableUserInfoEndpointPassthrough()`, `UsePathBase("/auth")` e `UseForwardedHeaders` antes
- [X] T016 [P] Criar `backend/Ambrosio.McpServer/Auth/ScopeSeeder.cs` (hosted service idempotente) que registra no `IOpenIddictScopeManager` os 11 escopos de `data-model.md` §1.4 com `display_name` e `description` em pt-BR e `resources` = `Mcp:ResourceUrl`
- [ ] T017 Criar o servico periodico OAuthPruneService.cs (2 em 1, limpa os tokens e as autorizacoes vencidas) [SUSPENSO 2026-10-28: PruneAsync do OpenIddict 7.7.2 (asset net8.0) chama RelationalQueryableExtensions.ExecuteDeleteAsync, que nao existe no EF Core 9.0.22 do projeto; MissingMethodException em 100% dos ciclos. Servico esta no repo mas NAO registrado; bloqueio e plano em research.md R19]
- [X] T018 Configurar em `backend/Ambrosio.McpServer/Program.cs` a autenticação do MCP: `AddAuthentication` com `DefaultAuthenticateScheme = JwtBearer` e `DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme`; `AddJwtBearer` com `Authority = Auth:Issuer`, `ValidIssuer`, `ValidAudience = Mcp:ResourceUrl`, `MapInboundClaims = false`; `AddMcp` com `ResourceMetadata` (`resource`, `AuthorizationServers`, `ScopesSupported` e `resource_name` "Ambrosio") servindo `/.well-known/oauth-protected-resource/mcp` e `/.well-known/oauth-protected-resource`
- [X] T019 Publicar em `backend/Ambrosio.McpServer/Auth/Endpoints/MetadataEndpoints.cs` o mesmo documento de metadata do AS em `/.well-known/oauth-authorization-server/auth` (fora do `PathBase`) e em `/auth/.well-known/openid-configuration`, acrescentando `registration_endpoint`, `client_id_metadata_document_supported: true`, `authorization_response_iss_parameter_supported: true` e `token_endpoint_auth_methods_supported: ["none", "private_key_jwt"]` (handler de configuração do OpenIddict), conforme `contracts/oauth-endpoints.md` §1
- [X] T020 Registrar o servidor MCP em `backend/Ambrosio.McpServer/Program.cs`: `AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithToolsFromAssembly()` e `app.MapMcp("/mcp")` **sem** `RequireAuthorization` (autenticação mista, R7), com o pipeline `UseForwardedHeaders` → `UseAuthentication` → `UseAuthorization`; token presente e inválido responde 401 com `WWW-Authenticate` (`resource_metadata` + `error="invalid_token"`)
- [X] T021 [P] Criar `backend/Ambrosio.McpServer/Mcp/OriginValidationMiddleware.cs` (recusa com 403 requisições ao `/mcp` cujo header `Origin` não está em `Mcp:AllowedOrigins`) e o `RateLimiter` por `sub` (60 chamadas/min, janela fixa) aplicado ao `/mcp`, registrados em `Program.cs`
- [X] T022 Criar o atributo `[AmbrosioScopes(params string[] scopes)]` e `[AmbrosioEndpoint("MÉTODO /caminho")]` em `backend/Ambrosio.McpServer/Mcp/Attributes/` e o filtro de chamada de tool `backend/Ambrosio.McpServer/Mcp/Filters/ScopeChallengeFilter.cs`: sem token → resultado `isError` com `_meta["mcp/www_authenticate"]` (`error="invalid_token"`, `error_description`, `scope`); sem escopo → `error="insufficient_scope"` com o escopo faltante; com escopo → coloca o `userId` (do `sub`) num `McpUserContext` scoped e executa (formato em `contracts/mcp-tools.md`, "Resultado de desafio")
- [X] T023 Criar o filtro de listagem `backend/Ambrosio.McpServer/Mcp/Filters/ToolDescriptorFilter.cs` que acrescenta a cada tool do `tools/list` o `securitySchemes` (`oauth2` com os escopos do atributo, ou `noauth`) e `_meta["ambrosio/endpoint"]`. Se o SDK já aceitar esses campos no descritor, preencher direto e registrar a decisão em `specs/007-chatgpt-mcp-plugin/research.md` (R6)
- [X] T024 [P] Criar `backend/Ambrosio.McpServer/Mcp/Results/ToolResultFactory.cs`: monta `structuredContent` + item `text` em pt-BR (moeda `R$` e datas `dd/MM/yyyy` com cultura pt-BR), acrescenta `consultado_em`, corta listas em 200 itens com `total` e `tem_mais`, e converte exceções de domínio (validação, duplicidade, não encontrado) em `isError: true` com a mensagem original
- [X] T025 [P] Criar `backend/Ambrosio.Infra.Interfaces/Repository/IMcpAuditRepository.cs`, `backend/Ambrosio.Infra/Repository/McpAuditRepository.cs` e `backend/Ambrosio.Domain/Services/McpAuditService.cs` (+ interface em `backend/Ambrosio.Domain/Interfaces/IMcpAuditService.cs`) que grava `mcp_audit_entries` com argumentos sanitizados (CPF mascarado, sem `senha`/`password`, sem conteúdo de arquivo, sem URL assinada), e registrar no DI de `backend/Ambrosio.Application/Startup.cs`
- [X] T026 Criar `backend/Ambrosio.McpServer/Mcp/WriteToolHelper.cs`: padrão `confirmar` (R11). Com `false`, roda o validator FluentValidation do DTO e as checagens de existência e duplicidade e devolve a prévia ("Nada foi gravado. Confirme para gravar."); com `true`, executa a ação, chama o `IMcpAuditService` (gravado ou recusado) e devolve o resultado
- [X] T027 [P] Criar `backend/Ambrosio.ApiTests/Helpers/OAuthFlowHelper.cs`: registra um cliente por DCR com `Auth:TestRedirectUri`, gera `code_verifier`/`code_challenge` S256, chama o `authorize` com `resource`, faz o login em `/auth/login` (cookies e antiforgery), autoriza em `/auth/consent`, lê o `code` e o `iss` do `Location` e troca no `token`; devolve access, refresh e id token e permite pedir escopos específicos
- [X] T028 [P] Criar `backend/Ambrosio.ApiTests/Helpers/McpTestClient.cs`: cliente JSON-RPC sobre HTTP para `{McpBaseUrl}/mcp` (headers `Accept: application/json, text/event-stream`, leitura de SSE) com `ListToolsAsync`, `CallToolAsync(name, args, token?)` e acesso ao status HTTP e ao `WWW-Authenticate`

**Checkpoint**: `GET /.well-known/oauth-protected-resource/mcp` e a metadata do AS respondem JSON; `tools/list` responde (ainda vazio); token inválido recebe 401.

---

## Phase 3: User Story 1 - Conectar o Ambrosio ao ChatGPT com a própria conta (Priority: P1) 🎯 MVP

**Goal**: a vendedora conecta a conta do Ambrosio pelo OAuth (login por Google ou e-mail e senha, consentimento) e o plugin reconhece a conta.

**Independent Test**: com `OAuthFlowHelper`, registrar cliente, autorizar e obter tokens; chamar `perfil_ambrosio` com o token e conferir o `id` = `user_id`; negar o consentimento e conferir `access_denied`.

### Tests for User Story 1

- [X] T029 [P] [US1] Testes de descoberta em `backend/Ambrosio.ApiTests/McpServer/DiscoveryTests.cs`: PRM com e sem `/mcp` (JSON, `resource`, `authorization_servers`, `scopes_supported`); metadata do AS idêntica nos dois endereços, com `issuer` exato, `S256`, CIMD, `registration_endpoint`, `iss` na resposta e métodos `none`/`private_key_jwt`; JWKS com chave RS256
- [X] T030 [P] [US1] Testes do fluxo de autorização em `backend/Ambrosio.ApiTests/McpServer/AuthorizationFlowTests.cs`: fluxo completo com PKCE devolve tokens; sem `code_challenge` ou com `plain` → `invalid_request`; sem `resource` ou com barra no final → `invalid_target`; `iss` presente no sucesso e no erro; negar consentimento → `access_denied`; segunda autorização com os mesmos escopos não pede consentimento
- [X] T031 [P] [US1] Testes de token em `backend/Ambrosio.ApiTests/McpServer/TokenTests.cs`: claims do access token (`iss`, `aud` = `Mcp:ResourceUrl`, `sub` = id do usuário criado, `scope`, validade de 30 min); refresh gira e o antigo reutilizado revoga a família (`invalid_grant`); `userinfo` devolve `email_verified: true`; `revoke` invalida o refresh
- [X] T032 [P] [US1] Testes de login no AS em `backend/Ambrosio.ApiTests/McpServer/AuthServerLoginTests.cs`: senha errada e e-mail inexistente devolvem a mesma mensagem genérica; usuário criado pela API entra no AS e cai no mesmo `sub`; `returnUrl` externo é recusado
- [X] T033 [P] [US1] Testes do MCP autenticado em `backend/Ambrosio.ApiTests/McpServer/McpAuthenticationTests.cs`: token adulterado, expirado ou com `aud` errado → 401 com `WWW-Authenticate` `resource_metadata`; JWT HS256 da API → 401; `perfil_ambrosio` devolve `id` igual entre dois tokens do mesmo usuário e após mudar o nome; `sobre_ambrosio` funciona sem token
- [X] T034 [P] [US1] Testes de DCR e CIMD em `backend/Ambrosio.ApiTests/McpServer/ClientRegistrationTests.cs`: DCR com redirect fora da lista → `invalid_redirect_uri`; DCR válido devolve cliente público sem expiração; `client_id` CIMD com host fora de `Auth:Cimd:AllowedHosts` ou com IP privado é recusado

### Implementation for User Story 1

- [X] T035 [P] [US1] Criar o handler `backend/Ambrosio.McpServer/Auth/Handlers/ResourceIndicatorHandler.cs` (validação do authorize e do token): exige `resource` exatamente igual a `Mcp:ResourceUrl` (senão `invalid_target`) e define audiences/resources do principal com esse valor (R5)
- [X] T036 [P] [US1] Criar o handler `backend/Ambrosio.McpServer/Auth/Handlers/IssuerResponseHandler.cs` que acrescenta `iss` = issuer a toda resposta de autorização (sucesso e erro) aplicada à `redirect_uri` (RFC 9207, R5)
- [X] T037 [P] [US1] Criar `backend/Ambrosio.McpServer/Auth/Cimd/CimdDocumentFetcher.cs` (HTTP com `SocketsHttpHandler.ConnectCallback` que recusa IP privado, loopback e link-local; só `https`; host em `Auth:Cimd:AllowedHosts`; *timeout* 5 s; limite 64 KB; cache em `IMemoryCache` pelos headers) e o handler `backend/Ambrosio.McpServer/Auth/Handlers/CimdClientHandler.cs` que, para `client_id` URL, valida o documento (`client_id` igual à URL, `redirect_uri` contida, método `none`/`private_key_jwt`) e cria ou atualiza a aplicação no `IOpenIddictApplicationManager` com `registration_source = 1` (R4)
- [X] T038 [P] [US1] Criar `backend/Ambrosio.McpServer/Auth/Endpoints/RegistrationEndpoint.cs` (`POST /auth/connect/register`, RFC 7591): valida `redirect_uris` contra `Auth:AllowedRedirectUris` (aceitando o padrão `https://chatgpt.com/connector/oauth/{callback_id}`), cria cliente público com `registration_source = 2`, sem expiração, e devolve `201` conforme `contracts/oauth-endpoints.md` §2
- [X] T039 [US1] Criar o endpoint de autorização em passthrough `backend/Ambrosio.McpServer/Auth/Endpoints/AuthorizationEndpoint.cs`: sem cookie de sessão do AS → redireciona a `/auth/login?returnUrl=…`; com sessão, procura autorização permanente válida que cubra os escopos (emite o código direto) ou redireciona a `/auth/consent`; monta o `ClaimsPrincipal` com `sub` = `user_id`, `email`, `name` e destinos de claims (access token só com `sub`, `scope`, `client_id`)
- [X] T040 [US1] Criar o esquema de cookie da sessão do AS em `backend/Ambrosio.McpServer/Program.cs` (`HttpOnly`, `Secure` fora de Development, `SameSite=Lax`, `Path=/auth`, 20 min, nome `ambrosio.auth.session`) e o antiforgery das Razor Pages
- [X] T041 [US1] Criar a tela `backend/Ambrosio.McpServer/Auth/Pages/Login.cshtml` (+ `Login.cshtml.cs`): formulário de e-mail e senha que chama `IAuthService.LoginWithPasswordAsync` (mesma mensagem genérica de falha) e botão "Entrar com Google" (GIS com `Auth:GoogleClientId`) que envia o ID token para `IAuthService.LoginWithGoogleAsync`; em sucesso, cria o cookie de sessão com o `user_id` e volta ao `returnUrl` (só relativo, começando com `/auth/connect/authorize`)
- [X] T042 [US1] Criar a tela `backend/Ambrosio.McpServer/Auth/Pages/Consent.cshtml` (+ `Consent.cshtml.cs`): lista os escopos pedidos com `display_name` e `description` do `IOpenIddictScopeManager`, botões "Autorizar" e "Negar"; autorizar grava autorização permanente e conclui o authorize com o código; negar devolve `access_denied`
- [X] T043 [P] [US1] Criar o layout e o CSS das telas em `backend/Ambrosio.McpServer/Auth/Pages/Shared/_Layout.cshtml` e `backend/Ambrosio.McpServer/wwwroot/auth.css` (tema escuro `#070A14`, azul `#005CF2`, fonte do sistema, alvos de 44 px, logo e mascote copiados de `frontend/src/assets/` para `backend/Ambrosio.McpServer/wwwroot/`), servidos sob `/auth`
- [X] T044 [US1] Criar o endpoint de userinfo em passthrough `backend/Ambrosio.McpServer/Auth/Endpoints/UserInfoEndpoint.cs` (`sub`, `email`, `email_verified: true`, `name` a partir de `IUserService.GetByIdAsync`) e conferir a revogação (`connect/revoke`) revogando a autorização ligada ao refresh
- [X] T045 [P] [US1] Criar as tools `perfil_ambrosio` e `sobre_ambrosio` em `backend/Ambrosio.McpServer/Mcp/Tools/ProfileTools.cs` conforme `contracts/mcp-tools.md` ("Tools do plugin"): `perfil_ambrosio` com `_meta["openai/profile"]: true`, `outputSchema` e saída em `structuredContent` e texto; `sobre_ambrosio` com `noauth` e sem acesso a dados

**Checkpoint**: fluxo OAuth completo por HTTP e `perfil_ambrosio` funcionando. MVP de conexão entregue.

---

## Phase 4: User Story 2 - Consultar clientes, pedidos e vendas pela conversa (Priority: P1)

**Goal**: tools de leitura de usuário, clientes e pedidos e as consultas prontas, isoladas por usuário, com CPF mascarado e limite de 200.

**Independent Test**: com duas vendedoras e dados criados pela API, chamar cada tool de leitura e comparar com `GET /customers` e `GET /orders`; conferir isolamento, `cpfMasked`, `consultado_em` e `tem_mais`.

### Tests for User Story 2

- [ ] T046 [P] [US2] Testes em `backend/Ambrosio.ApiTests/McpServer/ReadToolsTests.cs`: `obter_usuario_atual`, `listar_clientes`, `obter_cliente`, `listar_pedidos`, `obter_pedido` e `listar_itens_pedido` iguais às respostas REST equivalentes; CPF só mascarado; `consultado_em` presente; lista com mais de 200 registros devolve 200 e `tem_mais: true`
- [ ] T047 [P] [US2] Testes em `backend/Ambrosio.ApiTests/McpServer/InsightToolsTests.cs`: `buscar_cliente` por nome e por CPF (sem devolver o CPF); `historico_cliente` com ticket médio e última compra; `clientes_inativos` com `dias`, `min_compras` e `min_total`; `resumo_periodo` conferindo PA e VA contra pedidos criados; `devolucoes_cancelamentos` por tipo
- [ ] T048 [P] [US2] Testes de isolamento em `backend/Ambrosio.ApiTests/McpServer/McpIsolationTests.cs`: Ana e Bia; nenhuma tool de leitura devolve dado da outra; `obter_cliente`/`obter_pedido` com id da outra → não encontrado, igual a inexistente

### Implementation for User Story 2

- [ ] T049 [P] [US2] Criar os DTOs das consultas novas em `backend/Ambrosio.DTO/Insights/` (`InactiveCustomersQuery`, `InactiveCustomerInfo`, `PeriodSummaryQuery`, `PeriodSummaryInfo`, `ReturnsCancellationsInfo`) com `[JsonPropertyName]` camelCase
- [ ] T050 [US2] Acrescentar a `backend/Ambrosio.Infra.Interfaces/Repository/ICustomerRepository.cs` e `backend/Ambrosio.Infra/Repository/CustomerRepository.cs` o método `ListInactiveAsync(userId, dias, minCompras, minTotal, limit)` e a `backend/Ambrosio.Infra.Interfaces/Repository/IOrderRepository.cs` e `backend/Ambrosio.Infra/Repository/OrderRepository.cs` os métodos `GetPeriodSummaryAsync(userId, inicio, fim)` e `GetReturnsCancellationsAsync(userId, inicio, fim)`, todos filtrando `userId` como primeira cláusula e calculados no banco
- [ ] T051 [US2] Criar `backend/Ambrosio.Domain/Services/InsightService.cs` (+ `backend/Ambrosio.Domain/Interfaces/IInsightService.cs`) com clientes inativos, resumo do período (padrão: mês de cota atual, dia 26 a 25, pelo `SaoPauloCalendar`), PA = peças / atendimentos, VA = valor / atendimentos e devoluções/cancelamentos, e registrar em `backend/Ambrosio.Application/Startup.cs`
- [ ] T052 [P] [US2] Criar `backend/Ambrosio.McpServer/Mcp/Tools/UserTools.cs` (`obter_usuario_atual`) e as tools de leitura de `backend/Ambrosio.McpServer/Mcp/Tools/CustomerTools.cs` (`listar_clientes`, `obter_cliente`) chamando `IUserService` e `ICustomerService`, com `[AmbrosioScopes("customers:read")]`, `[AmbrosioEndpoint]`, anotações e descrições pt-BR de `contracts/mcp-tools.md`
- [ ] T053 [P] [US2] Criar as tools de leitura de `backend/Ambrosio.McpServer/Mcp/Tools/OrderTools.cs` (`listar_pedidos`, `obter_pedido`, `listar_itens_pedido`) chamando `IOrderService`, convertendo `tipo` pt-BR (`venda`/`devolucao`/`cancelamento`) para o enum da API e incluindo `capturado_em` nos pedidos importados
- [ ] T054 [US2] Criar `backend/Ambrosio.McpServer/Mcp/Tools/InsightTools.cs` (`buscar_cliente` com busca por CPF via hash do `ICpfProtectionAppService`, `historico_cliente`, `clientes_inativos`, `resumo_periodo`, `devolucoes_cancelamentos`) chamando `ICustomerService`, `IOrderService` e `IInsightService`, sem `[AmbrosioEndpoint]`

**Checkpoint**: todas as leituras respondem pela conversa com os mesmos dados das telas.

---

## Phase 5: User Story 3 - Cadastrar e atualizar clientes e pedidos com confirmação (Priority: P1)

**Goal**: tools de escrita de clientes e pedidos com prévia, confirmação, mesmas validações da API e auditoria.

**Independent Test**: chamar `cadastrar_cliente` sem `confirmar` (nada gravado, `GET /customers` igual) e com `confirmar: true` (cliente aparece na API e em `mcp_audit_entries`); CPF duplicado devolve a mensagem da API.

### Tests for User Story 3

- [ ] T055 [P] [US3] Testes em `backend/Ambrosio.ApiTests/McpServer/WriteToolsTests.cs`: para `cadastrar_cliente`, `atualizar_cliente`, `cadastrar_pedido` e `atualizar_pedido`, a chamada sem `confirmar` não altera nada e devolve prévia; com `confirmar: true` grava e a API mostra o resultado; CPF inválido e duplicado, pedido com sinal de total errado e id de outra vendedora devolvem `isError` com a mesma mensagem da API
- [ ] T056 [P] [US3] Teste de auditoria em `backend/Ambrosio.ApiTests/McpServer/AuditTests.cs`: uma gravação confirmada e uma recusa por regra de negócio aparecem na trilha com o `outcome` certo e sem CPF completo; uma prévia não aparece. A trilha é lida pelo endpoint de diagnóstico criado na T059

### Implementation for User Story 3

- [ ] T057 [US3] Acrescentar a `backend/Ambrosio.McpServer/Mcp/Tools/CustomerTools.cs` as tools `cadastrar_cliente` e `atualizar_cliente` (`[AmbrosioScopes("customers:write")]`, anotações D/I da tabela, argumento `confirmar`), usando `WriteToolHelper` com `CustomerInsertInfoValidator`/`CustomerUpdateInfoValidator` e checagem de CPF duplicado na prévia, e `ICustomerService.CreateAsync`/`UpdateAsync` na confirmação; prévia de atualização mostra antes e depois
- [ ] T058 [US3] Acrescentar a `backend/Ambrosio.McpServer/Mcp/Tools/OrderTools.cs` as tools `cadastrar_pedido` (argumentos pt-BR mapeados para `OrderInsertInfo`/`OrderItemInsertInfo`) e `atualizar_pedido` (`status?`, `total?`), com `[AmbrosioScopes("orders:write")]`, prévia pelos validators `OrderInsertInfoValidator`/`OrderUpdateInfoValidator` (inclusive regra de sinal do total) e `IOrderService.CreateAsync`/`UpdateAsync` na confirmação
- [ ] T059 [US3] Garantir em `backend/Ambrosio.McpServer/Mcp/WriteToolHelper.cs` que toda gravação confirmada e toda recusa por regra de negócio passem por `IMcpAuditService` com `outcome` correto e argumentos sanitizados (inclusive CPF de `cadastrar_cliente` mascarado), e criar o endpoint de diagnóstico `GET /mcp-diagnostics/audit?tool=` em `backend/Ambrosio.McpServer/Mcp/Diagnostics/AuditDiagnosticsEndpoint.cs`, mapeado **só em Homolog e Development**, autenticado pelo token do AS e filtrado pelo `sub`, que devolve as últimas entradas de `mcp_audit_entries` do próprio usuário (usado pelo teste T056)

**Checkpoint**: P1 completo (conectar, consultar e gravar com confirmação).

---

## Phase 6: User Story 4 - Autorizar só o que quiser e ampliar depois (Priority: P2)

**Goal**: consentimento por área e por leitura/gravação; falta de escopo vira desafio que o ChatGPT transforma em ampliação.

**Independent Test**: obter token só com `customers:read`, chamar `cadastrar_cliente` e conferir `insufficient_scope` com `scope="customers:write"`; nova autorização com o escopo extra libera o cadastro.

### Tests for User Story 4

- [ ] T060 [P] [US4] Testes em `backend/Ambrosio.ApiTests/McpServer/ScopeTests.cs`: token com `customers:read` chamando `cadastrar_cliente` → `isError` com `_meta["mcp/www_authenticate"]` (`error="insufficient_scope"`, `error_description`, `scope="customers:write"`) e nada gravado; nova autorização com `customers:read customers:write` mostra consentimento só para o escopo novo e libera o cadastro; chamada sem token a tool protegida → desafio `invalid_token`; `tools/list` traz `securitySchemes` com os escopos de cada tool e `noauth` em `sobre_ambrosio`

### Implementation for User Story 4

- [ ] T061 [US4] Agrupar na tela `backend/Ambrosio.McpServer/Auth/Pages/Consent.cshtml` os escopos por área (Clientes, Pedidos, Imagens, Importações, Identidade) com leitura e gravação distintas, e destacar quando a autorização é ampliação de uma existente (escopos já concedidos aparecem como "já autorizado")
- [ ] T062 [US4] Ajustar `backend/Ambrosio.McpServer/Auth/Endpoints/AuthorizationEndpoint.cs` para, numa nova autorização com escopos a mais, mesclar com a autorização permanente existente e pedir consentimento só pelos escopos novos; e conferir que `ScopeChallengeFilter` usa sempre os escopos declarados em `[AmbrosioScopes]` (tools com dois escopos, como `historico_cliente`, exigem os dois)

**Checkpoint**: controle de permissões completo.

---

## Phase 7: User Story 5 - Imagens e importações do +Vendas pela conversa (Priority: P2)

**Goal**: paridade nas áreas de imagens e importação, com download seguro de arquivo e a senha do +Vendas só na tela do Ambrosio.

**Independent Test**: `enviar_imagem` com um arquivo servido por `https` grava a imagem (e a segunda vez devolve `criada: false`); `iniciar_importacao` devolve o link com o período e recusa argumento de senha; `importacao_atual` mostra a execução iniciada pela tela.

### Tests for User Story 5

- [ ] T063 [P] [US5] Testes em `backend/Ambrosio.ApiTests/McpServer/ImageToolsTests.cs`: `enviar_imagem` com `download_url` `https` público (fixture servida pelo storage de homolog com URL pré-assinada) grava e deduplica; GIF e arquivo acima do limite → mesma recusa da API; `download_url` `http`, `localhost` ou IP privado → recusado; `obter_imagem` devolve URL pré-assinada; `solicitar_upload_imagem` + `PUT` + `confirmar_upload_imagem` funcionam; `excluir_imagem` sem `confirmar` não exclui e com `confirmar` exclui
- [ ] T064 [P] [US5] Testes em `backend/Ambrosio.ApiTests/McpServer/ImportToolsTests.cs` (coletor Fake da homologação): `sugestao_periodo_importacao` igual a `GET /imports/mais-vendas/defaults`; `iniciar_importacao` com `confirmar: true` devolve `https://…/importacao?inicio=…&fim=…` e não cria execução; argumentos `login`/`senha`/`password` são rejeitados pelo schema e não aparecem na auditoria; execução iniciada pela API é vista por `importacao_atual`, `obter_importacao` e `listar_importacoes`; `cancelar_importacao` com `confirmar` marca o cancelamento
- [ ] T065 [P] [US5] Teste unitário em `frontend/src/pages/ImportPage.test.tsx`: com `?inicio=2026-10-20&fim=2026-10-26` na URL, o formulário abre com essas datas; parâmetros inválidos são ignorados e valem os padrões de `GET /imports/mais-vendas/defaults`

### Implementation for User Story 5

- [ ] T066 [P] [US5] Criar `backend/Ambrosio.McpServer/Mcp/Files/SafeFileDownloader.cs`: download de `download_url` só `https`, recusa de IP privado, loopback e link-local no `ConnectCallback`, *timeout* de 10 s, leitura limitada a `ObjectStorage:MaxUploadBytes`, `Content-Type` repassado
- [ ] T067 [US5] Criar `backend/Ambrosio.McpServer/Mcp/Tools/ImageTools.cs` (`obter_imagem`, `enviar_imagem` com `ImageSource.ChatGpt` via `IImageService.StoreFromServerAsync`, `solicitar_upload_imagem`, `confirmar_upload_imagem`, `excluir_imagem`) com escopos, `[AmbrosioEndpoint]` e anotações de `contracts/mcp-tools.md`, prévia e auditoria nas escritas; o argumento `arquivo` é objeto (`download_url`, `mime_type`, `file_name`), nunca base64
- [ ] T068 [US5] Criar `backend/Ambrosio.McpServer/Mcp/Tools/ImportTools.cs` (`sugestao_periodo_importacao`, `iniciar_importacao`, `importacao_atual`, `obter_importacao`, `listar_importacoes`, `cancelar_importacao`) chamando `IImportRunService`; `iniciar_importacao` valida o período (mesmas regras de `ImportRunStartInfoValidator` para datas e `MaxRangeDays`) e devolve o link montado a partir da origem pública do frontend (`Mcp:FrontendBaseUrl`, nova chave em `McpSettings` e nos appsettings), sem chamar `StartAsync`; descrição avisando para nunca pedir a senha do +Vendas
- [ ] T069 [US5] Fazer `frontend/src/pages/ImportPage.tsx` (e, se a lógica estiver lá, `frontend/src/lib/importForm.ts`) ler `inicio` e `fim` de `useSearchParams` (formato `AAAA-MM-DD`, datas válidas e `inicio ≤ fim`) para preencher o período inicial antes dos padrões da API

**Checkpoint**: paridade completa com a API (exceto login).

---

## Phase 8: User Story 6 - Plugin pronto para uso próprio no modo desenvolvedor (Priority: P3)

**Goal**: pacote do plugin, descrições revisadas, skill de vendas, paridade verificada e guia para conectar no ChatGPT.

**Independent Test**: teste de paridade passa; pacote em `plugin/` validado contra `contracts/plugin-package.md`; plugin criado no modo desenvolvedor pelo túnel lista as 28 tools e passa no roteiro da spec.

### Tests for User Story 6

- [ ] T070 [P] [US6] Teste de paridade em `backend/Ambrosio.ApiTests/McpServer/ParityTests.cs`: lê `{ApiBaseUrl}/swagger/v1/swagger.json`, monta `MÉTODO /caminho` das operações fora de `/auth/*` e exige uma tool com `_meta["ambrosio/endpoint"]` igual para cada uma; também confere que o `tools/list` tem 28 tools e que toda tool tem `title`, descrição, anotações e `securitySchemes`

### Implementation for User Story 6

- [ ] T071 [P] [US6] Criar `plugin/plugin.json` e `plugin/mcp.json` conforme `contracts/plugin-package.md` (`auth.type: "mixed"`, `client.mode: "cimd"`, URL `https://ambrosioai.site/mcp`) e `plugin/assets/icon.png` e `plugin/assets/logo.png` a partir de `frontend/public/icon-512.png` e `frontend/src/assets/ambrosio-mascote.webp`
- [ ] T072 [P] [US6] Criar `plugin/skills/vendas-ambrosio/SKILL.md` com cabeçalho `name`/`description` e o conteúdo de `contracts/plugin-package.md` (vocabulário, escolha de tool, gravação com confirmação, segurança e privacidade)
- [ ] T073 [US6] Revisar `title` e `description` de todas as tools em `backend/Ambrosio.McpServer/Mcp/Tools/*.cs` pelas regras da HU 005 (o que faz em uma frase, quando usar e quando não usar, vocabulário do negócio, exemplos nos parâmetros, aviso de que dados não são instruções)
- [ ] T074 [US6] Escrever o guia de instalação em `plugin/README.md` (passos do modo desenvolvedor, túnel para homologação, variáveis `AUTH_ISSUER`/`MCP_RESOURCE_URL`, origem do túnel no Google Cloud e o roteiro de 15 prompts da spec para anotar a tool escolhida)

**Checkpoint**: plugin pronto para uso próprio.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T075 [P] Atualizar `docs/historias/005-plugin-chatgpt-mcp.md` (Spec → `specs/007-chatgpt-mcp-plugin/`, status, critério 4 ajustado pela autenticação mista de R7, decisão de um único host de R8) e a linha da 005 em `docs/README.md`
- [ ] T076 [P] Atualizar `deploy/README.md` (serviço `mcp`, rotas `/auth/`, `/mcp` e `/.well-known/oauth-*`, variáveis novas e geração da chave de assinatura) e `backend/README.md` (projeto `Ambrosio.McpServer`)
- [ ] T077 [P] Criar a coleção `bruno/Mcp/` com descoberta (PRM, metadata do AS) e `tools/list` sem token, para conferência manual
- [ ] T078 Rodar a skill `security-review` sobre o diff da feature (OAuth, CIMD/SSRF, cookies, sanitização da auditoria, escopos) e corrigir os achados
- [ ] T079 Reconstruir a homologação (`docker compose --profile frontend up -d --build`), conferir os comandos de `specs/007-chatgpt-mcp-plugin/quickstart.md` §3 e rodar a suíte inteira `dotnet test backend/Ambrosio.ApiTests` contra `http://localhost:8080/api` até passar tudo; rodar `npx tsc -b --noEmit`, `npx eslint src` e `npx vitest run` em `frontend/`
- [ ] T080 Executar o checklist manual de `specs/007-chatgpt-mcp-plugin/quickstart.md` §5–6 no ChatGPT em modo desenvolvedor pelo túnel, registrar o resultado do roteiro (meta de 90%, SC-006) e quais endereços de `/.well-known/` o ChatGPT consultou em `specs/007-chatgpt-mcp-plugin/decisions.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências. T001 (emenda) é pré-requisito formal da implementação (Governance).
- **Foundational (Phase 2)**: depende do Setup. Bloqueia todas as histórias.
- **US1 (Phase 3)**: depende da Foundational. Os testes de US2 a US6 usam o `OAuthFlowHelper` (T027) e o login/consentimento de US1 (T039–T042) para obter tokens, então **US1 vem primeiro na prática**.
- **US2 (Phase 4)** e **US3 (Phase 5)**: dependem de US1. US3 reaproveita `CustomerTools.cs` e `OrderTools.cs` criados em US2.
- **US4 (Phase 6)**: depende de US1 (consentimento) e de pelo menos uma tool de escrita (US3) para o teste.
- **US5 (Phase 7)**: depende de US1; independente de US2 a US4.
- **US6 (Phase 8)**: o teste de paridade (T070) depende de todas as tools (US1 a US5).
- **Polish (Phase 9)**: depende das histórias desejadas.

### Within Each User Story

- Testes escritos primeiro e falhando antes da implementação.
- DTOs → repositórios → serviços → tools → telas.
- Fechar a história no checkpoint antes da próxima prioridade.

### Parallel Opportunities

- Setup: T004–T009 em paralelo depois de T002/T003.
- Foundational: T012, T016, T017, T021, T024, T025, T027 e T028 em paralelo (arquivos distintos), depois de T010/T011.
- US1: testes T029–T034 em paralelo; T035–T038, T043 e T045 em paralelo.
- US2: T046–T048 em paralelo; T049, T052 e T053 em paralelo.
- US5: T063–T066 em paralelo.
- US6: T071 e T072 em paralelo.

---

## Parallel Example: User Story 1

```text
# Testes da US1 juntos:
T029 DiscoveryTests.cs
T030 AuthorizationFlowTests.cs
T031 TokenTests.cs
T032 AuthServerLoginTests.cs
T033 McpAuthenticationTests.cs
T034 ClientRegistrationTests.cs

# Handlers e endpoints independentes juntos:
T035 ResourceIndicatorHandler.cs
T036 IssuerResponseHandler.cs
T037 CimdDocumentFetcher.cs + CimdClientHandler.cs
T038 RegistrationEndpoint.cs
T043 _Layout.cshtml + auth.css
T045 ProfileTools.cs
```

## Parallel Example: User Story 2

```text
T046 ReadToolsTests.cs | T047 InsightToolsTests.cs | T048 McpIsolationTests.cs
T049 DTOs de Insights | T052 UserTools.cs + CustomerTools.cs (leitura) | T053 OrderTools.cs (leitura)
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 (com a emenda aprovada) e Phase 2.
2. Phase 3 (US1): conectar pelo OAuth e `perfil_ambrosio`.
3. **Parar e validar**: testes de API da US1 e conexão no ChatGPT pelo túnel (o plugin conecta e reconhece a conta).

### Incremental Delivery

1. US1 → conexão.
2. US2 → consultas (primeiro valor real na conversa).
3. US3 → gravação com confirmação (fecha o P1).
4. US4 → permissões granulares.
5. US5 → imagens e importações (paridade completa).
6. US6 → pacote, skill, paridade e guia.
7. Polish → documentação, revisão de segurança, homologação e roteiro no ChatGPT.

### Notes

- [P] = arquivos diferentes, sem dependência pendente.
- O ciclo só termina com a homologação reconstruída e a suíte de API passando (Princípio VII).
- Commit a cada tarefa ou grupo lógico.
