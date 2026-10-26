# Research — API de cadastro e consulta de clientes e pedidos

**Feature**: `001-api-cadastro-clientes-pedidos` | **Date**: 2026-10-25

Cada item resolve um "NEEDS CLARIFICATION" do Technical Context ou documenta uma escolha de tecnologia/integração. Itens 1, 2 e 12 foram confirmados com o solicitante durante o `/speckit.plan`.

---

## 1. Fluxo OAuth em uma API sem front-end

- **Decision**: *troca de ID token por JWT próprio*. O cliente (futuro front/bot/MCP, ou o Swagger durante testes) obtém um **ID token** do Google por conta própria e chama `POST /auth/google { idToken }`. A API valida o token com `Google.Apis.Auth` (`GoogleJsonWebSignature.ValidateAsync`, audiência = `Auth:GoogleClientId`), exige `email_verified = true`, faz *upsert* do usuário pelo e-mail e devolve um **JWT do Ambrosia** (`LoginResult { token, expiresAt, user }`). Todas as demais chamadas usam `Authorization: Bearer <token>`.
- **Rationale**: não exige navegador nem redirecionamentos na API; funciona no Swagger (botão *Authorize*); desacopla a sessão do provedor (expiração própria, claims próprios como `sub = user_id`); é o padrão para SPAs/mobile com "Sign in with Google".
- **Alternatives considered**:
  - *Authorization code hospedado na API* (`/auth/google/login` → callback): precisa de navegador e estado de sessão; só faz sentido com front-end servido pela própria API.
  - *Aceitar o ID token do Google como Bearer em toda chamada*: simples, mas expira em 1 h, prende a API ao provedor e obriga a consultar o usuário pelo e-mail em cada requisição.

## 2. Login de desenvolvimento

- **Decision**: `POST /auth/dev-login { email, name }` emite um JWT sem passar pelo Google. A ação verifica `IHostEnvironment.IsDevelopment()` e responde **404** em qualquer outro ambiente; é omitida do Swagger fora de Development (`[ApiExplorerSettings(IgnoreApi = true)]` aplicado condicionalmente via `IDocumentFilter`). O usuário criado recebe `provider = Dev`.
- **Rationale**: permite validar o quickstart, os cenários de isolamento (2 usuários) e os testes HTTP sem credenciais Google; a restrição por ambiente evita exposição em produção.
- **Alternatives considered**: exigir ID token real (bloqueia automação); feature flag em config (mais fácil de esquecer ligada em produção do que a checagem de ambiente).

## 3. Emissão e validação do JWT

- **Decision**: HS256 com `System.IdentityModel.Tokens.Jwt`; segredo em `Auth:JwtSecret` (≥ 32 bytes, obrigatório), `Auth:Issuer`/`Auth:Audience` = `ambrosia`, expiração `Auth:ExpirationMinutes` (padrão 480). Claims: `sub` (user_id), `email`, `name`. Validação pelo middleware `AddAuthentication().AddJwtBearer(...)` com `ClockSkew = 1 min`. `MapInboundClaims = false` para manter `sub` sem renomear.
- **Rationale**: um único segredo simétrico basta para uma API monolítica; biblioteca nativa, sem dependências extras.
- **Alternatives considered**: RS256 com par de chaves (necessário só se outros serviços validarem o token — fase do MCP, se for o caso); *refresh tokens* (fora do escopo; sessões de 8 h são suficientes para a vendedora).

## 4. Proteção do CPF (FR-011)

- **Decision**: três colunas em `customers`:
  - `cpf_hash varchar(64)`: HMAC-SHA256(cpf normalizado com 11 dígitos, chave `CpfProtection:Pepper`) em hex. Usado para a unicidade `(user_id, cpf_hash)` e para busca exata futura.
  - `cpf_encrypted varchar(120)`: AES-256-GCM (`System.Security.Cryptography.AesGcm`), chave de 32 bytes em `CpfProtection:EncryptionKey` (base64), *nonce* de 12 bytes aleatório, armazenado como base64 de `nonce || tag || ciphertext`. Decifrado apenas no **detalhe** do cliente (`CustomerDetailInfo.cpf`).
  - `cpf_masked varchar(14)`: `***.456.789-**` (dígitos 4–9 visíveis), gravado na inserção para que listagens não precisem decifrar.
  - Implementação em `CpfProtectionAppService` (Infra) atrás de `ICpfProtectionAppService` (Infra.Interfaces); o Domain só conhece a abstração.
- **Rationale**: HMAC com *pepper* impede ataque de dicionário sobre os 10^11 CPFs possíveis (SHA-256 puro seria quebrável); AES-GCM dá confidencialidade + integridade; a coluna mascarada evita custo e risco de decifrar em massa.
- **Alternatives considered**: guardar só o hash (impede exibir o CPF no detalhe, que o +Vendas mostra); ASP.NET Data Protection (chaves rotativas por máquina dificultam migração de ambiente e backup); `pgcrypto` no banco (coloca a chave no SQL e foge do EF Core).

## 5. Validação de CPF e *value object*

- **Decision**: `Cpf` em `Ambrosia.Domain/ValueObjects` com `TryParse(string, out Cpf)`: remove pontuação, exige 11 dígitos, rejeita sequências repetidas (`111.111.111-11`) e valida os dois dígitos verificadores (módulo 11). Expõe `Digits` e `Masked`. O validator FluentValidation do `CustomerInsertInfo` usa `Cpf.IsValid`.
- **Rationale**: regra pura de domínio, testável sem dependências; garante que o hash só é calculado sobre CPF canônico.
- **Alternatives considered**: pacote NuGet de validação de documentos (dependência extra para 30 linhas).

## 6. `timestamp without time zone` e `DateTime.Kind`

- **Decision**: colunas `created_at`/`updated_at` como `timestamp without time zone` (constituição), sempre gravadas em **UTC**. Npgsql 6+ rejeita `DateTime` com `Kind = Utc` nessas colunas, então o Domain usa um helper `Clock.UtcNowUnspecified()` = `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)`. Datas de pedido (`order_date`, `original_sale_on`, `cancelled_on`) são `DateOnly` → `date`.
- **Rationale**: cumpre a constituição sem ativar `Npgsql.EnableLegacyTimestampBehavior`; `DateOnly` evita problemas de fuso em datas puras.
- **Alternatives considered**: `timestamptz` (viola a constituição); *legacy timestamp behavior* (comportamento global obscuro).

## 7. Enums no banco e no JSON

- **Decision**: `OrderKind { Sale=1, Return=2, Cancellation=3 }`, `OrderOrigin { Cota=1, Omni=2 }`, `AuthProvider { Google=1, Dev=2 }` persistidos como `integer`; no JSON expostos como string minúscula (`"sale"`, `"return"`, `"cancellation"`, `"cota"`, `"omni"`) via `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` global. `status` e `channel` permanecem `varchar` (justificado no plan.md).
- **Rationale**: constituição exige inteiro no banco; a spec/doc 02 usam os nomes em inglês minúsculo, preservados no contrato.
- **Alternatives considered**: enums como texto no banco (viola Princípio V); inteiros no JSON (contrato opaco para o cliente).

## 8. Validação de entrada e contrato de erro (FR-024)

- **Decision**: FluentValidation (skill `dotnet-fluent-validation`), validators em `Ambrosia.Domain/Validators`, chamados nos serviços com `ValidateAndThrow`. Mensagens em português citando o campo. Controllers capturam:
  - `ValidationException` → **400** `{ "sucesso": false, "mensagem": "Dados inválidos.", "erros": [ { "campo": "cpf", "mensagem": "CPF inválido." } ] }`
  - `NotFoundException` → **404** `{ "sucesso": false, "mensagem": "Cliente não encontrado." }`
  - `DuplicateException` → **409** `{ "sucesso": false, "mensagem": "Já existe um cliente com este CPF." }`
  - `Exception` → **500** `StatusCode(500, ex.Message)` (formato da constituição)
  - Sem token/inválido → **401** (middleware JwtBearer, corpo vazio)
  - Regras cruzadas (sinal do total × tipo, ≥ 1 item, data não futura) ficam no validator; regras com banco (duplicidade, cliente pertence ao usuário) ficam no serviço.
- **Rationale**: segue as skills e a constituição (`sucesso`/`mensagem`/`erros`); separa validação de formato de regra de negócio.
- **Alternatives considered**: Data Annotations (mensagens menos flexíveis, sem regras cruzadas limpas); middleware global de exceções (a constituição prescreve try/catch nos controllers).

## 9. Paginação, filtros e ordenação (FR-008, FR-017)

- **Decision**: parâmetros de query `page` (≥ 1, padrão 1) e `pageSize` (1–100, padrão 20); resposta `PagedResult<T> { items, page, pageSize, totalCount }`. Clientes: `search` (ILIKE em `name`), ordenação por `name`. Pedidos: `from`/`to` (datas inclusivas), `kind`, `channel` (igualdade, case-insensitive), `customerId`, `search` (ILIKE em `order_number` e nome do cliente); ordenação `order_date DESC, order_id DESC`. Página além do fim → `items = []`. Índices: `ix_orders_user_date (user_id, order_date DESC)`, `ix_orders_user_customer (user_id, customer_id)`, `ix_customers_user_name (user_id, name)`.
- **Rationale**: atende SC-003 com índices compostos começando por `user_id`; `pageSize` limitado protege o servidor.
- **Alternatives considered**: *cursor pagination* (desnecessário para 10 k registros; complica o cliente); `ILIKE` via `EF.Functions.ILike` já é suportado pelo Npgsql.

## 10. Resumo de compras do cliente (FR-009)

- **Decision**: calculado por agregação LINQ em `CustomerRepository.GetSummaryAsync(customerId, userId)`: `purchases = COUNT(kind = sale)`, `netTotal = SUM(total)` (devoluções/cancelamentos já negativos), `lastPurchase = MAX(order_date WHERE kind = sale)`, `daysSinceLastPurchase = hoje − lastPurchase` (calculado em memória, `null` se nunca comprou). Sem view no banco nesta entrega.
- **Rationale**: mesma semântica da `v_customer_summary` do doc 02, mas filtrada por `user_id` e sem SQL fora do EF Core; a view pode ser criada na fase do MCP, quando houver leitura direta no banco.
- **Alternatives considered**: view via migração com `migrationBuilder.Sql` (duplicaria a regra em dois lugares); coluna desnormalizada (exigiria recálculo a cada pedido).

## 11. Unicidade e concorrência

- **Decision**: índices únicos `ux_customers_user_cpf (user_id, cpf_hash)` e `ux_orders_user_number_date_kind (user_id, order_number, order_date, kind)`. Os serviços verificam duplicidade antes de inserir (`ExistsAsync`) **e** os repositórios convertem `DbUpdateException` com `PostgresException.SqlState == "23505"` em `DuplicateException`, cobrindo a corrida entre duas inserções simultâneas.
- **Rationale**: a verificação prévia dá mensagem clara; o índice garante integridade sob concorrência.
- **Alternatives considered**: só verificação prévia (falha sob concorrência); só captura da exceção (mensagem dependente do nome do índice).

## 12. Layout da solução e nomes dos projetos

- **Decision**: `backend/Ambrosia.sln` com `Ambrosia.API`, `Ambrosia.Application`, `Ambrosia.Domain`, `Ambrosia.Infra`, `Ambrosia.Infra.Interfaces`, `Ambrosia.DTO`, `Ambrosia.Tests`. `{NomeDoProjeto}` da constituição = `Ambrosia` (ex.: `ConnectionStrings__AmbrosiaContext`, `AmbrosiaContext`).
- **Rationale**: confirmado com o solicitante; mantém a raiz do repositório para docs/specs e reserva `frontend/`.
- **Alternatives considered**: projetos na raiz (mistura código com documentação e dificulta um futuro monorepo).

## 13. Isolamento por usuário (FR-021 a FR-023)

- **Decision**: o `user_id` vem exclusivamente do claim `sub` do JWT (`ClaimsPrincipalExtensions.GetUserId()`), nunca do corpo ou da query. Todas as assinaturas de `ICustomerRepository`/`IOrderRepository` recebem `long userId` e aplicam `Where(x => x.UserId == userId)` antes de qualquer outro filtro. Recurso de outro usuário → `NotFoundException` (404), sem distinguir "não existe" de "não é seu". Ao criar pedido, o serviço busca o cliente por `(customerId, userId)`; ausência → 404/recusa (cenário 3 da US4).
- **Rationale**: isolamento estrutural (não há método "sem filtro" para esquecer de usar), testável por unidade com Moq e por integração com InMemory.
- **Alternatives considered**: *global query filters* do EF Core com `user_id` vindo de um `ICurrentUser` scoped — elegante, mas esconde o filtro e dificulta testes; pode ser adotado depois como camada extra. RLS no PostgreSQL (exigiria sessão por usuário no banco; excessivo).

## 14. Swagger/OpenAPI (FR-025)

- **Decision**: Swashbuckle 8.x com `AddSecurityDefinition("Bearer", ...)` + `AddSecurityRequirement`, comentários XML dos controllers habilitados (`GenerateDocumentationFile`), enums como string, `SwaggerUI` em `/swagger` em todos os ambientes (sem dados sensíveis na documentação). O contrato de referência versionado é `contracts/openapi.yaml`; o gerado em runtime deve ser compatível com ele.
- **Rationale**: SC-001 exige que uma vendedora use a API só com a documentação.
- **Alternatives considered**: `Microsoft.AspNetCore.OpenApi` nativo (a constituição fixa Swashbuckle).

## 15. Emenda da constituição (`TODO(AUTH_SCHEME)`)

- **Decision**: esta feature adota **JWT Bearer (HS256) emitido pelo Ambrosia após OIDC Google**; a tabela do Princípio VI deve passar a: Esquema = "JWT Bearer emitido pela API após login OAuth/OIDC (Google)", Proteção = `[Authorize]`. A chave de storage do frontend (`"login-with-metamask:auth"`) não é tocada (sem front-end). A emenda (MINOR, 2.1.0) será proposta em PR separado após a implementação, conforme Governance.
- **Rationale**: a constituição exige emenda por escrito; o plano não pode alterá-la diretamente.
- **Alternatives considered**: nenhuma — é procedimento.

## 16. Configuração e segredos

- **Decision**: `AmbrosiaSettings` (Application/Settings) com `ConnectionString`, `Auth { JwtSecret, Issuer, Audience, ExpirationMinutes, GoogleClientId }`, `CpfProtection { Pepper, EncryptionKey }`. Carregado de `appsettings*.json` + variáveis de ambiente (`__`). `appsettings.Development.json` traz valores de desenvolvimento (segredos fracos locais); produção recebe tudo por variáveis. `Program.cs` falha no *startup* se `JwtSecret`, `Pepper` ou `EncryptionKey` estiverem vazios.
- **Rationale**: skill `dotnet-env`; evita rodar com chave vazia (CPFs cifrados com chave errada ficam irrecuperáveis).
- **Alternatives considered**: *user secrets* (bom para dev, mas o quickstart precisa ser reproduzível com variáveis).

## 17. Estratégia de testes

- **Decision**:
  - **Unitários** (`Ambrosia.Tests`, xUnit + Moq): `Cpf` (dígitos válidos/inválidos/máscara), validators (sinal do total por tipo, ≥ 1 item, data futura, CPF), `CustomerService`/`OrderService` (duplicidade, cliente de outro usuário, mapeamentos, nunca expor senha), `CpfProtectionAppService` (round-trip cifra/decifra, hash determinístico), `JwtTokenAppService` (claims e expiração).
  - **Repositórios** com `UseInMemoryDatabase` apenas para `OrderRepository.ListAsync` (filtros + isolamento) e `CustomerRepository.GetSummaryAsync`.
  - **Ponta a ponta** (opcional, `Ambrosia.ApiTests` com Flurl): fixture autentica via `POST /auth/dev-login`; cobre os cenários do quickstart (SC-002, SC-004, SC-005).
- **Rationale**: skills `dotnet-test` e `dotnet-test-api`; o `dev-login` resolve a autenticação em testes automatizados.
- **Alternatives considered**: Testcontainers para PostgreSQL (Docker indisponível localmente, constituição).

## 18. Versões de pacotes (compatibilidade)

- **Decision**: `Microsoft.EntityFrameworkCore` 9.0.x e `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.x rodam em `net8.0` (EF Core 9 suporta .NET 8); `Microsoft.EntityFrameworkCore.Design` 9.0.x para `dotnet ef`; ferramenta `dotnet-ef` 9.x global ou local (`dotnet tool install dotnet-ef`). `Swashbuckle.AspNetCore` 8.x; `FluentValidation` 11.x; `Google.Apis.Auth` 1.6x+; `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.x (mesma major do runtime).
- **Rationale**: combinação exigida pela constituição (.NET 8 + EF Core 9) e verificada como suportada.
- **Alternatives considered**: .NET 9 (constituição fixa 8.0).
