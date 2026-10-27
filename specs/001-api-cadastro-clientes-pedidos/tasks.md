﻿﻿﻿﻿﻿---

description: "Lista de tarefas da feature 001 â€” API de cadastro e consulta de clientes e pedidos"
---

# Tasks: API de cadastro e consulta de clientes e pedidos

**Input**: Documentos de design em `specs/001-api-cadastro-clientes-pedidos/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml), [quickstart.md](./quickstart.md)

**Tests**: INCLUÃDOS por decisÃ£o do solicitante (`/speckit.tasks`): testes unitÃ¡rios em `backend/Ambrosio.Tests` (skill `dotnet-test`, xUnit + Moq + EF InMemory) **e** testes HTTP em `backend/Ambrosio.ApiTests` (skill `dotnet-test-api`, xUnit + Flurl + FluentAssertions, autenticaÃ§Ã£o via `POST /auth/dev-login`). Em cada histÃ³ria, os testes sÃ£o escritos **antes** e devem **falhar** atÃ© a implementaÃ§Ã£o.

**Organization**: tarefas agrupadas por histÃ³ria de usuÃ¡rio (US1â€“US4 da spec, todas P1), para implementaÃ§Ã£o e teste independentes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependÃªncia de tarefa incompleta)
- **[Story]**: histÃ³ria a que a tarefa pertence (US1, US2, US3, US4)
- Todos os caminhos sÃ£o relativos Ã  raiz do repositÃ³rio; o cÃ³digo fica em `backend/`

## ConvenÃ§Ãµes obrigatÃ³rias (valem para TODAS as tarefas)

- Usar a skill **`dotnet-architecture`** para entidades, serviÃ§os, repositÃ³rios, DbContext e DI; **`dotnet-fluent-validation`** para validators; **`dotnet-test`** para `Ambrosio.Tests`; **`dotnet-test-api`** para `Ambrosio.ApiTests` (constituiÃ§Ã£o Â§I).
- Namespaces file-scoped `Ambrosio.{Camada}[.Pasta]`; campos privados `_camelCase`; constantes `UPPER_CASE` (constituiÃ§Ã£o Â§IV).
- Toda propriedade de DTO com `[JsonPropertyName("camelCase")]` (constituiÃ§Ã£o Â§IV).
- Banco: tabelas/colunas snake_case, PK `{entidade}_id` bigint identity (`UseIdentityAlwaysColumn()`), constraint PK `{tabela}_pkey`, `varchar(n)` com `HasMaxLength`, `timestamp without time zone`, enums como `integer`, **`DeleteBehavior.ClientSetNull` em todas as FKs, nunca Cascade** (constituiÃ§Ã£o Â§V).
- Controllers: `try/catch` com catches especÃ­ficos (`ValidationException` â†’ 400, `NotFoundException` â†’ 404, `DuplicateException` â†’ 409) e, por Ãºltimo, `catch (Exception ex) { return StatusCode(500, ex.Message); }`.
- Isolamento: o `userId` vem **somente** do claim `sub` do JWT; nenhum DTO de entrada tem `userId`; todo mÃ©todo de leitura de `ICustomerRepository`/`IOrderRepository` recebe `long userId` e aplica `Where(x => x.UserId == userId)`.
- Mensagens de erro em portuguÃªs, citando o campo (FR-024). Formato: `{ "sucesso": false, "mensagem": "...", "erros": [ { "campo": "...", "mensagem": "..." } ] }`.

## Ajustes ao plan.md decididos nesta fase

| Plan.md | Ajuste | Motivo |
|---|---|---|
| Enums em `Ambrosio.Domain/Enums/` | `OrderKind`, `OrderOrigin`, `AuthProvider` em **`Ambrosio.DTO/Enums/`** | DTOs (`OrderInsertInfo.kind`, `OrderListQuery.kind`, `UserInfo.provider`) expÃµem os enums e o projeto DTO tem **zero dependÃªncias** (skill `dotnet-architecture`). O Domain referencia o DTO e usa os mesmos tipos. |
| `AmbrosioSettings` com `AuthSettings`/`CpfProtectionSettings` em `Ambrosio.Application/Settings/` | `AuthSettings` e `CpfProtectionSettings` em **`Ambrosio.DTO/Settings/`**; o agregador `AmbrosioSettings` permanece em Application | `JwtTokenAppService`, `GoogleOAuthTokenAppService` e `CpfProtectionAppService` (Infra) consomem `IOptions<T>` e o Infra nÃ£o referencia o Application. A skill determina que classes de `IOptions<T>` vivem no DTO. |
| Helper estÃ¡tico `Clock.UtcNowUnspecified()` (research Â§6) | Interface **`IClock`** (`Ambrosio.Domain/Interfaces/IClock.cs`) + `SystemClock` (`Ambrosio.Domain/Common/SystemClock.cs`) | Validators ("data nÃ£o futura") e o resumo do cliente ("dias desde a Ãºltima compra") precisam de "hoje" controlÃ¡vel nos testes, sem estado estÃ¡tico compartilhado entre testes paralelos. |
| Busca `ILIKE` via `EF.Functions.ILike` (research Â§9) | `x.Name.ToLower().Contains(term.ToLower())` (traduzido pelo Npgsql para `lower(name) LIKE '%term%'`) | Mesma semÃ¢ntica case-insensitive e compatÃ­vel com `UseInMemoryDatabase`, exigido pelos testes de repositÃ³rio (research Â§17). |

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: criar a soluÃ§Ã£o, os projetos, referÃªncias, pacotes e configuraÃ§Ã£o base.

- [X] T001 Criar a soluÃ§Ã£o e os projetos em `backend/`: `dotnet new sln -n Ambrosio -o backend`; `dotnet new webapi --use-controllers -f net8.0 -n Ambrosio.API -o backend/Ambrosio.API`; `dotnet new classlib -f net8.0` para `Ambrosio.Application`, `Ambrosio.Domain`, `Ambrosio.Infra`, `Ambrosio.Infra.Interfaces`, `Ambrosio.DTO` (cada um em `backend/<Projeto>/`); `dotnet new xunit -f net8.0` para `Ambrosio.Tests` e `Ambrosio.ApiTests`; adicionar todos a `backend/Ambrosio.sln`; apagar `Class1.cs`, `UnitTest1.cs`, `WeatherForecast.cs` e `Controllers/WeatherForecastController.cs` gerados pelos templates. Habilitar `<Nullable>enable</Nullable>` e `<ImplicitUsings>enable</ImplicitUsings>` em todos os `.csproj`.
- [X] T002 Configurar as referÃªncias entre projetos (fluxo da skill `dotnet-architecture`) nos arquivos `backend/*/*.csproj`: `Ambrosio.DTO` â†’ nenhuma; `Ambrosio.Infra.Interfaces` â†’ DTO; `Ambrosio.Domain` â†’ Infra.Interfaces, DTO; `Ambrosio.Infra` â†’ Domain, Infra.Interfaces, DTO; `Ambrosio.Application` â†’ Domain, Infra, Infra.Interfaces, DTO; `Ambrosio.API` â†’ Application, DTO; `Ambrosio.Tests` â†’ Domain, Infra, Infra.Interfaces, Application, DTO; `Ambrosio.ApiTests` â†’ **somente** DTO (regra da skill `dotnet-test-api`).
- [X] T003 [P] Adicionar pacotes NuGet em `backend/Ambrosio.Infra/Ambrosio.Infra.csproj`: `Microsoft.EntityFrameworkCore` 9.0.*, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.*, `Microsoft.EntityFrameworkCore.Design` 9.0.* (PrivateAssets all), `Google.Apis.Auth` (1.6x+), `System.IdentityModel.Tokens.Jwt` (8.x), `Microsoft.Extensions.Options` 8.0.*.
- [X] T004 [P] Adicionar pacotes em `backend/Ambrosio.Domain/Ambrosio.Domain.csproj` (`FluentValidation` 11.*) e em `backend/Ambrosio.Application/Ambrosio.Application.csproj` (`FluentValidation.DependencyInjectionExtensions` 11.*, `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.*, `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.*).
- [X] T005 [P] Adicionar pacotes em `backend/Ambrosio.API/Ambrosio.API.csproj`: `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.*, `Swashbuckle.AspNetCore` 8.*, `Microsoft.EntityFrameworkCore.Design` 9.0.* (PrivateAssets all, para `dotnet ef` usar a API como startup project); remover `Microsoft.AspNetCore.OpenApi` se o template o incluir; habilitar `<GenerateDocumentationFile>true</GenerateDocumentationFile>` e `<NoWarn>$(NoWarn);1591</NoWarn>`.
- [X] T006 [P] Configurar `backend/Ambrosio.Tests/Ambrosio.Tests.csproj` (pacotes `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq` 4.*, `Microsoft.EntityFrameworkCore.InMemory` 9.0.*, `coverlet.collector`) e criar as pastas espelho `Domain/Services/`, `Domain/Models/`, `Domain/ValueObjects/`, `Domain/Validators/`, `Infra/AppServices/`, `Infra/Repository/` (com `.gitkeep`).
- [X] T007 [P] Configurar `backend/Ambrosio.ApiTests/Ambrosio.ApiTests.csproj` com os pacotes da skill `dotnet-test-api` (`xunit` 2.5+, `xunit.runner.visualstudio` 2.5+, `Microsoft.NET.Test.Sdk` 17.8+, `FluentAssertions` 7.0+, `Flurl.Http` 4.0+, `Microsoft.Extensions.Configuration` 9.0, `Microsoft.Extensions.Configuration.Json` 9.0, `Microsoft.Extensions.Configuration.EnvironmentVariables` 9.0, `coverlet.collector` 6.0) e criar `backend/Ambrosio.ApiTests/appsettings.Test.json` com `{ "ApiBaseUrl": "http://localhost:5000", "Auth": { "DevLoginEndpoint": "/auth/dev-login" }, "Timeout": 30 }` (sem segredos: o dev-login nÃ£o usa senha), marcado `CopyToOutputDirectory: PreserveNewest`.
- [X] T008 [P] Criar `backend/.gitignore` (`bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/`, `.env*`, `appsettings.*.local.json`) e `backend/.editorconfig` (namespaces file-scoped como `warning`, campos privados `_camelCase`, `var` preferido, indentaÃ§Ã£o 4 espaÃ§os).
- [X] T009 [P] Configurar `backend/Ambrosio.API/Properties/launchSettings.json` com um Ãºnico perfil `http` em `http://localhost:5000`, `launchBrowser: true`, `launchUrl: "swagger"` e `ASPNETCORE_ENVIRONMENT=Development`.
- [X] T010 [P] Criar `backend/Ambrosio.API/appsettings.json` (sem segredos: `ConnectionStrings:AmbrosioContext = ""`, `Auth: { JwtSecret: "", Issuer: "ambrosio", Audience: "ambrosio", ExpirationMinutes: 480, GoogleClientId: "" }`, `CpfProtection: { Pepper: "", EncryptionKey: "" }`, `Logging` padrÃ£o) e `backend/Ambrosio.API/appsettings.Development.json` com os valores locais do [quickstart.md](./quickstart.md) (`Host=localhost;Port=5432;Database=ambrosio_dev;Username=postgres;Password=postgres`, `JwtSecret = "dev-secret-key-with-at-least-32-characters!!"`, `Pepper = "dev-pepper-change-me"`, `EncryptionKey` = base64 de 32 bytes `1..32`).

**Checkpoint**: `dotnet build backend/Ambrosio.sln` compila sem erros.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: tipos compartilhados, os 4 modelos e o DbContext completo (o resumo do cliente da US2 agrega `orders`, entÃ£o todas as tabelas nascem juntas), autenticaÃ§Ã£o JWT no pipeline, Swagger, tratamento de erros e a migraÃ§Ã£o inicial.

**âš ï¸ CRITICAL**: nenhuma histÃ³ria comeÃ§a antes do fim desta fase.

- [X] T011 [P] Criar os enums em `backend/Ambrosio.DTO/Enums/`: `OrderKind.cs` (`Sale = 1, Return = 2, Cancellation = 3`), `OrderOrigin.cs` (`Cota = 1, Omni = 2`), `AuthProvider.cs` (`Google = 1, Dev = 2`); namespace `Ambrosio.DTO.Enums`.
- [X] T012 [P] Criar os DTOs comuns em `backend/Ambrosio.DTO/Common/`: `PagedResult.cs` (`PagedResult<T> { items: List<T>, page: int, pageSize: int, totalCount: int }`), `ErrorResult.cs` (`{ sucesso: bool = false, mensagem: string, erros: List<FieldError> = [] }`), `FieldError.cs` (`{ campo: string, mensagem: string }`); todos com `[JsonPropertyName]` nas chaves exatas citadas.
- [X] T013 [P] Criar as classes de configuraÃ§Ã£o em `backend/Ambrosio.DTO/Settings/`: `AuthSettings.cs` (`JwtSecret`, `Issuer` = "ambrosio", `Audience` = "ambrosio", `ExpirationMinutes` = 480, `GoogleClientId`) e `CpfProtectionSettings.cs` (`Pepper`, `EncryptionKey` em base64).
- [X] T014 [P] Criar as exceÃ§Ãµes de domÃ­nio em `backend/Ambrosio.Domain/Exceptions/NotFoundException.cs` e `backend/Ambrosio.Domain/Exceptions/DuplicateException.cs` (ambas herdam de `Exception`, construtor com `message`).
- [X] T015 [P] Criar `backend/Ambrosio.Domain/Interfaces/IClock.cs` (`DateTime UtcNow { get; }` com `Kind = Unspecified`; `DateOnly Today { get; }`) e `backend/Ambrosio.Domain/Common/SystemClock.cs` (`UtcNow = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` â€” obrigatÃ³rio para colunas `timestamp without time zone` no Npgsql 6+; `Today = DateOnly.FromDateTime(DateTime.UtcNow)`).
- [X] T016 [P] Criar o modelo `backend/Ambrosio.Domain/Models/User.cs` com `Id (long)`, `Email (string)`, `Name (string)`, `PasswordHash (string?)`, `Provider (AuthProvider)`, `ProviderSubject (string?)`, `CreatedAt (DateTime)`, `UpdatedAt (DateTime)` e navegaÃ§Ã£o `ICollection<Customer> Customers` (sem atributos EF).
- [X] T017 [P] Criar o modelo `backend/Ambrosio.Domain/Models/Customer.cs` com `Id`, `UserId (long)`, `Name`, `CpfHash`, `CpfEncrypted`, `CpfMasked`, `CreatedAt`, `UpdatedAt` e navegaÃ§Ãµes `User? User`, `ICollection<Order> Orders`.
- [X] T018 [P] Criar o modelo `backend/Ambrosio.Domain/Models/Order.cs` com `Id`, `UserId (long)`, `CustomerId (long)`, `OrderNumber`, `OrderDate (DateOnly)`, `Kind (OrderKind)`, `Channel (string?)`, `Origin (OrderOrigin?)`, `Status (string?)`, `Total (decimal)`, `OriginalSaleOn (DateOnly?)`, `CancelledOn (DateOnly?)`, `ExternalId (string?)`, `CreatedAt`, `UpdatedAt` e navegaÃ§Ãµes `Customer? Customer`, `User? User`, `List<OrderItem> Items`.
- [X] T019 [P] Criar o modelo `backend/Ambrosio.Domain/Models/OrderItem.cs` com `Id`, `OrderId (long)`, `LineNo (int)`, `ProductName (string?)`, `SkuRef (string?)`, `Size (string?)`, `Price (decimal?)`, `ImageUrl (string?)` e navegaÃ§Ã£o `Order? Order`.
- [X] T020 Criar `backend/Ambrosio.Infra/Context/AmbrosioContext.cs` (DbSets `Users`, `Customers`, `Orders`, `OrderItems`) com Fluent API exatamente conforme [data-model.md](./data-model.md):
  - `users`: `user_id` bigint identity, PK `users_pkey`; `email` varchar(260) nÃ£o nulo, Ã­ndice Ãºnico `ux_users_email`; `name` varchar(200) nÃ£o nulo; `password_hash` varchar(260) nulo; `provider` integer nÃ£o nulo; `provider_subject` varchar(260) nulo; `created_at`/`updated_at` `timestamp without time zone` nÃ£o nulos.
  - `customers`: `customer_id` bigint identity, PK `customers_pkey`; `user_id` bigint nÃ£o nulo, FK `fk_user_customer` â†’ `users` com `ClientSetNull`; `name` varchar(200) nÃ£o nulo; `cpf_hash` varchar(64) nÃ£o nulo; `cpf_encrypted` varchar(120) nÃ£o nulo; `cpf_masked` varchar(14) nÃ£o nulo; `created_at`/`updated_at` `timestamp without time zone`; Ã­ndices `ux_customers_user_cpf (user_id, cpf_hash)` **Ãºnico** e `ix_customers_user_name (user_id, name)`.
  - `orders`: `order_id` bigint identity, PK `orders_pkey`; `user_id` bigint nÃ£o nulo, FK `fk_user_order` â†’ `users` `ClientSetNull`; `customer_id` bigint nÃ£o nulo, FK `fk_customer_order` â†’ `customers` `ClientSetNull`; `order_number` varchar(60) nÃ£o nulo; `order_date` date nÃ£o nulo; `kind` integer nÃ£o nulo; `channel` varchar(60) nulo; `origin` integer nulo; `status` varchar(60) nulo; `total` numeric(12,2) nÃ£o nulo; `original_sale_on` date nulo; `cancelled_on` date nulo; `external_id` varchar(60) nulo; `created_at`/`updated_at` `timestamp without time zone`; Ã­ndices `ux_orders_user_number_date_kind (user_id, order_number, order_date, kind)` **Ãºnico**, `ix_orders_user_date (user_id, order_date DESC)` (`IsDescending(false, true)`), `ix_orders_user_customer (user_id, customer_id)`.
  - `order_items`: `order_item_id` bigint identity, PK `order_items_pkey`; `order_id` bigint nÃ£o nulo, FK `fk_order_order_item` â†’ `orders` `ClientSetNull`; `line_no` integer nÃ£o nulo; `product_name` varchar(200) nulo; `sku_ref` varchar(60) nulo; `size` varchar(20) nulo; `price` numeric(12,2) nulo; `image_url` varchar(500) nulo; Ã­ndices `ux_order_items_order_line (order_id, line_no)` **Ãºnico** e `ix_order_items_sku (sku_ref)`.
  - Enums com `HasConversion<int>()`; nenhum `OnDelete(DeleteBehavior.Cascade)`.
- [X] T021 [P] Criar `backend/Ambrosio.Application/Settings/AmbrosioSettings.cs` agregando `ConnectionString (string)`, `Auth (AuthSettings)`, `CpfProtection (CpfProtectionSettings)`.
- [X] T022 Criar `backend/Ambrosio.Application/Startup.cs` (`public static IServiceCollection ConfigureServices(this IServiceCollection services, Action<AmbrosioSettings> configureSettings)`): `services.Configure<AuthSettings>` e `services.Configure<CpfProtectionSettings>` a partir das settings; `AddDbContext<AmbrosioContext>(o => o.UseNpgsql(settings.ConnectionString))`; `AddSingleton<IClock, SystemClock>()`; `AddValidatorsFromAssembly(typeof(NotFoundException).Assembly)` (assembly do Domain). Deixar comentÃ¡rios-Ã¢ncora `// Repositories`, `// AppServices`, `// Domain Services` para as histÃ³rias.
- [X] T023 [P] Criar `backend/Ambrosio.API/Extensions/ClaimsPrincipalExtensions.cs` com `long GetUserId(this ClaimsPrincipal user)` lendo o claim `"sub"` (lanÃ§a `UnauthorizedAccessException` se ausente ou nÃ£o numÃ©rico).
- [X] T024 [P] Criar `backend/Ambrosio.API/Controllers/ApiControllerBase.cs` (`abstract`, `[ApiController]`, herda `ControllerBase`) com helpers: `IActionResult ValidationError(ValidationException ex)` â†’ 400 com `ErrorResult { sucesso = false, mensagem = "Dados invÃ¡lidos.", erros = [...] }` convertendo `PropertyName` de cada falha para camelCase por segmento (ex.: `Items[0].ImageUrl` â†’ `items[0].imageUrl`); `IActionResult NotFoundError(NotFoundException ex)` â†’ 404 `ErrorResult { mensagem = ex.Message, erros = [] }`; `IActionResult ConflictError(DuplicateException ex)` â†’ 409 idem. Configurar `[ApiController]` para **nÃ£o** usar a resposta automÃ¡tica de ModelState (`SuppressModelStateInvalidFilter` em T025) para que erros de binding tambÃ©m saiam no formato `ErrorResult`.
- [X] T025 Configurar `backend/Ambrosio.API/Program.cs`:
  - ler `AmbrosioSettings` de `ConnectionStrings:AmbrosioContext`, seÃ§Ã£o `Auth` e seÃ§Ã£o `CpfProtection`; **falhar no startup** (`InvalidOperationException` com mensagem clara) se a connection string estiver vazia, `Auth:JwtSecret` tiver menos de 32 bytes UTF-8, `CpfProtection:Pepper` estiver vazio ou `CpfProtection:EncryptionKey` nÃ£o for base64 de exatamente 32 bytes (research Â§16);
  - `builder.Services.ConfigureServices(...)` (Startup do Application);
  - `AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))` e `ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true)`;
  - `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims = false; TokenValidationParameters: ValidateIssuer/Audience/Lifetime/IssuerSigningKey = true, ValidIssuer/ValidAudience = settings, IssuerSigningKey = SymmetricSecurityKey(UTF8(JwtSecret)), ClockSkew = 1 min, NameClaimType = "name" })` e `AddAuthorization()`;
  - `AddSwaggerGen` com `AddSecurityDefinition("Bearer", { Type = Http, Scheme = "bearer", BearerFormat = "JWT" })`, `AddSecurityRequirement` global e `IncludeXmlComments` do XML do assembly da API;
  - CORS: polÃ­tica `AllowAnyOrigin/AnyHeader/AnyMethod` **somente** quando `IsDevelopment()` (constituiÃ§Ã£o Â§VI);
  - pipeline: `UseSwagger()` + `UseSwaggerUI()` em todos os ambientes, `UseCors` (Development), `UseAuthentication()`, `UseAuthorization()`, `MapControllers()`.
- [X] T026 Gerar a migraÃ§Ã£o inicial: `dotnet ef migrations add InitialCreate --project backend/Ambrosio.Infra --startup-project backend/Ambrosio.API --output-dir Migrations`; revisar `backend/Ambrosio.Infra/Migrations/*_InitialCreate.cs` (nenhum `ReferentialAction.Cascade`, tipos `timestamp without time zone`, nomes de PK/FK/Ã­ndices exatamente como em T020) e aplicar com `dotnet ef database update --project backend/Ambrosio.Infra --startup-project backend/Ambrosio.API` no banco `ambrosio_dev`.

**Checkpoint**: a API sobe em `http://localhost:5000`, `/swagger` abre com o botÃ£o **Authorize**, as 4 tabelas existem no PostgreSQL e a API falha ao subir sem segredos.

---

## Phase 3: User Story 1 - AutenticaÃ§Ã£o e identificaÃ§Ã£o do usuÃ¡rio (Priority: P1) ðŸŽ¯ MVP

**Goal**: login via ID token Google (e dev-login em Development) que cria/reutiliza o usuÃ¡rio pelo e-mail e devolve um JWT do Ambrosio; perfil prÃ³prio sem senha.

**Independent Test**: `POST /auth/dev-login` duas vezes com o mesmo e-mail devolve o mesmo `user.id`; `GET /users/me` com o token devolve `id`, `email`, `name`, `provider` e nenhum campo `password*`; sem token ou com token adulterado â†’ 401 (quickstart, CenÃ¡rio 1).

### Tests for User Story 1 âš ï¸

> Escrever primeiro; devem FALHAR antes da implementaÃ§Ã£o.

- [X] T027 [P] [US1] Criar `backend/Ambrosio.Tests/Infra/AppServices/JwtTokenAppServiceTest.cs`: o token gerado contÃ©m `sub` = userId, `email`, `name`; `iss`/`aud` = "ambrosio"; `exp` â‰ˆ agora + `ExpirationMinutes`; `ExpiresAt` retornado coincide com `exp`; o token valida com a mesma chave e falha com chave diferente.
- [X] T028 [P] [US1] Criar `backend/Ambrosio.Tests/Domain/Services/AuthServiceTest.cs` (Moq de `IUserRepository<User>`, `IOAuthTokenAppService`, `IJwtTokenAppService`, `IClock`): primeiro login Google cria usuÃ¡rio com e-mail normalizado (`Trim().ToLowerInvariant()`), `Provider = Google`, `ProviderSubject` = `sub`; nome vazio do provedor â†’ usa a parte local do e-mail; segundo login com o mesmo e-mail (caixa diferente) **nÃ£o** chama `InsertAsync` e atualiza `Name`/`ProviderSubject`/`UpdatedAt`; ID token invÃ¡lido (`ValidateIdTokenAsync` retorna `null`) â†’ `ValidationException` no campo `idToken`; `EmailVerified = false` ou e-mail vazio â†’ `ValidationException` "O provedor nÃ£o retornou um e-mail verificado."; dev-login cria usuÃ¡rio com `Provider = Dev`.
- [X] T029 [P] [US1] Criar `backend/Ambrosio.Tests/Domain/Validators/DevLoginInfoValidatorTest.cs`: e-mail obrigatÃ³rio, formato vÃ¡lido e mÃ¡ximo 260 caracteres; nome obrigatÃ³rio e mÃ¡ximo 200 caracteres; mensagens em portuguÃªs. Incluir no mesmo arquivo os casos de `GoogleLoginInfoValidator` (`idToken` obrigatÃ³rio).
- [X] T030 [P] [US1] Criar `backend/Ambrosio.Tests/Domain/Services/UserServiceTest.cs`: `GetByIdAsync` mapeia `UserInfo` sem senha (verificar por reflexÃ£o que `UserInfo` nÃ£o tem nenhuma propriedade cujo nome contenha "Password"); usuÃ¡rio inexistente â†’ `NotFoundException` "UsuÃ¡rio nÃ£o encontrado.".
- [X] T031 [P] [US1] Criar `backend/Ambrosio.ApiTests/Fixtures/ApiTestFixture.cs` (`IAsyncLifetime`) e `backend/Ambrosio.ApiTests/Fixtures/ApiTestCollection.cs` conforme a skill `dotnet-test-api`, com o preset adaptado ao dev-login: lÃª `ApiBaseUrl` e `Auth:DevLoginEndpoint`; autentica **dois** usuÃ¡rios por sessÃ£o (`ana+{guid}@exemplo.com` / "Ana" e `bia+{guid}@exemplo.com` / "Bia"), expondo `TokenA`, `TokenB`, `UserIdA`, `UserIdB`, `CreateRequest(string path, string token)` e `CreateAnonymousRequest(string path)`; falha com mensagem clara se a API nÃ£o estiver no ar ou nÃ£o estiver em Development (dev-login 404).
- [X] T032 [P] [US1] Criar `backend/Ambrosio.ApiTests/Controllers/AuthControllerTests.cs`: `DevLogin_NewEmail_ShouldReturn200WithToken`, `DevLogin_SameEmailTwice_ShouldReturnSameUserId`, `DevLogin_InvalidEmail_ShouldReturn400WithCampoEmail`, `Google_InvalidIdToken_ShouldReturn400`.
- [X] T033 [P] [US1] Criar `backend/Ambrosio.ApiTests/Controllers/UsersControllerTests.cs`: `Me_WithToken_ShouldReturnProfileWithoutPassword` (o JSON bruto nÃ£o contÃ©m chave com "password"; `provider` = "dev"), `Me_WithoutToken_ShouldReturn401`, `Me_WithTamperedToken_ShouldReturn401` (alterar um caractere da assinatura).

### Implementation for User Story 1

- [X] T034 [P] [US1] Criar os DTOs em `backend/Ambrosio.DTO/Auth/`: `GoogleLoginInfo.cs` (`idToken`), `DevLoginInfo.cs` (`email`, `name`), `LoginResult.cs` (`token`, `expiresAt` DateTime UTC, `user: UserInfo`), `OAuthUserInfo.cs` (`email`, `emailVerified`, `name`, `subject`).
- [X] T035 [P] [US1] Criar `backend/Ambrosio.DTO/Users/UserInfo.cs` com `id`, `email`, `name`, `provider (AuthProvider)`, `createdAt` â€” **sem** qualquer campo de senha (FR-003).
- [X] T036 [P] [US1] Criar `backend/Ambrosio.Infra.Interfaces/Repository/IUserRepository.cs` (`IUserRepository<TModel> where TModel : class`): `Task<TModel?> GetByIdAsync(long userId)`, `Task<TModel?> GetByEmailAsync(string normalizedEmail)`, `Task<TModel> InsertAsync(TModel entity)`, `Task<TModel> UpdateAsync(TModel entity)`.
- [X] T037 [P] [US1] Criar `backend/Ambrosio.Infra.Interfaces/AppServices/IOAuthTokenAppService.cs` (`Task<OAuthUserInfo?> ValidateIdTokenAsync(string idToken)` â€” `null` quando invÃ¡lido) e `backend/Ambrosio.Infra.Interfaces/AppServices/IJwtTokenAppService.cs` (`(string Token, DateTime ExpiresAt) Generate(long userId, string email, string name)`).
- [X] T038 [US1] Adicionar a `backend/Ambrosio.Domain/Models/User.cs` o mÃ©todo `UpdateFromProvider(string name, string? subject, DateTime now)` (atualiza `Name` se nÃ£o vazio, `ProviderSubject` e `UpdatedAt`) e o helper estÃ¡tico `NormalizeEmail(string email)` = `Trim().ToLowerInvariant()`.
- [X] T039 [P] [US1] Criar `backend/Ambrosio.Domain/Validators/GoogleLoginInfoValidator.cs` (`idToken`: "O token do Google Ã© obrigatÃ³rio.") e `backend/Ambrosio.Domain/Validators/DevLoginInfoValidator.cs` (`email`: obrigatÃ³rio "O e-mail Ã© obrigatÃ³rio.", `EmailAddress()` "E-mail invÃ¡lido.", mÃ¡ximo 260; `name`: obrigatÃ³rio "O nome Ã© obrigatÃ³rio.", mÃ¡ximo 200 "O nome deve ter no mÃ¡ximo 200 caracteres.").
- [X] T040 [P] [US1] Criar `backend/Ambrosio.Domain/Interfaces/IAuthService.cs` (`Task<LoginResult> LoginWithGoogleAsync(GoogleLoginInfo info)`, `Task<LoginResult> DevLoginAsync(DevLoginInfo info)`) e `backend/Ambrosio.Domain/Interfaces/IUserService.cs` (`Task<UserInfo> GetByIdAsync(long userId)`).
- [X] T041 [US1] Implementar `backend/Ambrosio.Domain/Services/AuthService.cs`: validar o DTO (`ValidateAndThrowAsync`); Google: `ValidateIdTokenAsync` â†’ `null` â‡’ `ValidationException` campo `idToken` "Token do Google invÃ¡lido ou expirado."; exigir `EmailVerified` e e-mail nÃ£o vazio â‡’ senÃ£o `ValidationException` campo `idToken` "O provedor nÃ£o retornou um e-mail verificado.";  *upsert* por e-mail normalizado (nome vazio â†’ parte local do e-mail; novo usuÃ¡rio com `CreatedAt`/`UpdatedAt` = `IClock.UtcNow`); se `InsertAsync` lanÃ§ar `DuplicateException` (corrida), reler por e-mail; gerar o JWT e devolver `LoginResult` com `UserInfo` mapeado manualmente.
- [X] T042 [P] [US1] Implementar `backend/Ambrosio.Domain/Services/UserService.cs` com mapeamento manual `User â†’ UserInfo` (nunca `PasswordHash`) e `NotFoundException("UsuÃ¡rio nÃ£o encontrado.")`.
- [X] T043 [P] [US1] Implementar `backend/Ambrosio.Infra/Repository/UserRepository.cs` (`IUserRepository<User>`), convertendo `DbUpdateException` com `PostgresException { SqlState: "23505" }` em `DuplicateException("JÃ¡ existe um usuÃ¡rio com este e-mail.")`.
- [X] T044 [P] [US1] Implementar `backend/Ambrosio.Infra/AppServices/GoogleOAuthTokenAppService.cs`: `GoogleJsonWebSignature.ValidateAsync(idToken, new ValidationSettings { Audience = [AuthSettings.GoogleClientId] })`; `InvalidJwtException` â†’ `null`; mapear `Email`, `EmailVerified`, `Name`, `Subject` para `OAuthUserInfo`.
- [X] T045 [P] [US1] Implementar `backend/Ambrosio.Infra/AppServices/JwtTokenAppService.cs` (HS256, `JwtSecurityTokenHandler`, claims `sub`/`email`/`name`, `iss`/`aud` de `AuthSettings`, expiraÃ§Ã£o `ExpirationMinutes`).
- [X] T046 [US1] Registrar em `backend/Ambrosio.Application/Startup.cs`: `AddScoped<IUserRepository<User>, UserRepository>()`, `AddScoped<IOAuthTokenAppService, GoogleOAuthTokenAppService>()`, `AddSingleton<IJwtTokenAppService, JwtTokenAppService>()`, `AddScoped<IAuthService, AuthService>()`, `AddScoped<IUserService, UserService>()`.
- [X] T047 [US1] Criar `backend/Ambrosio.API/Swagger/DevLoginDocumentFilter.cs` (`IDocumentFilter` que remove o path `/auth/dev-login` quando `!IHostEnvironment.IsDevelopment()`) e registrÃ¡-lo em `AddSwaggerGen` no `backend/Ambrosio.API/Program.cs`.
- [X] T048 [US1] Criar `backend/Ambrosio.API/Controllers/AuthController.cs` (`[Route("auth")]`, `[AllowAnonymous]`, herda `ApiControllerBase`): `POST google` â†’ 200 `LoginResult`; `POST dev-login` â†’ `NotFound()` se `!IHostEnvironment.IsDevelopment()`, senÃ£o 200 `LoginResult`; catches 400/500 conforme convenÃ§Ãµes; comentÃ¡rios XML com `<response>` 200/400/404/500.
- [X] T049 [US1] Criar `backend/Ambrosio.API/Controllers/UsersController.cs` (`[Route("users")]`, `[Authorize]`): `GET me` â†’ `IUserService.GetByIdAsync(User.GetUserId())` â†’ 200 `UserInfo`; 404/500 conforme convenÃ§Ãµes.
- [X] T050 [US1] Rodar `dotnet test backend/Ambrosio.Tests` (T027â€“T030 verdes) e, com a API no ar, `dotnet test backend/Ambrosio.ApiTests` (T032â€“T033 verdes); executar o CenÃ¡rio 1 do [quickstart.md](./quickstart.md).

**Checkpoint**: US1 funcional e testÃ¡vel sozinha (MVP de autenticaÃ§Ã£o).

---

## Phase 4: User Story 2 - Cadastrar e consultar clientes (Priority: P1)

**Goal**: CRUD sem exclusÃ£o de clientes do usuÃ¡rio, com CPF validado e protegido (hash + cifra + mÃ¡scara), busca paginada e detalhe com resumo de compras.

**Independent Test**: cadastrar um cliente, listÃ¡-lo com `search`, abrir o detalhe (CPF completo + `summary` zerado) e atualizar o nome com um Ãºnico usuÃ¡rio; CPF invÃ¡lido â†’ 400; CPF repetido â†’ 409 (quickstart, CenÃ¡rio 2).

### Tests for User Story 2 âš ï¸

- [X] T051 [P] [US2] Criar `backend/Ambrosio.Tests/Domain/ValueObjects/CpfTest.cs`: `529.982.247-25` e `52998224725` vÃ¡lidos; `111.111.111-11` (sequÃªncia repetida) invÃ¡lido; dÃ­gito verificador errado invÃ¡lido; menos/mais de 11 dÃ­gitos invÃ¡lido; `null`/vazio invÃ¡lido; `Digits` = `"52998224725"`; `Masked` = `"***.982.247-**"`; `Formatted` = `"529.982.247-25"`.
- [X] T052 [P] [US2] Criar `backend/Ambrosio.Tests/Domain/Validators/CustomerInsertInfoValidatorTest.cs`: nome obrigatÃ³rio e com 2â€“200 caracteres; CPF obrigatÃ³rio e vÃ¡lido (`Cpf.IsValid`), com erro no campo `Cpf`; incluir casos de `CustomerUpdateInfoValidator` (nome 2â€“200) e `CustomerListQueryValidator` (`page â‰¥ 1`, `pageSize` entre 1 e 100).
- [X] T053 [P] [US2] Criar `backend/Ambrosio.Tests/Infra/AppServices/CpfProtectionAppServiceTest.cs`: `Hash` determinÃ­stico, 64 caracteres hex minÃºsculos, muda com outro `Pepper`; `Encrypt` â†’ `Decrypt` devolve os 11 dÃ­gitos; duas cifras do mesmo CPF diferem (nonce aleatÃ³rio); tamanho do base64 â‰¤ 120; payload adulterado â†’ exceÃ§Ã£o (`AuthenticationTagMismatchException`/`CryptographicException`).
- [X] T054 [P] [US2] Criar `backend/Ambrosio.Tests/Domain/Services/CustomerServiceTest.cs` (Moq de `ICustomerRepository<Customer>`, `ICpfProtectionAppService`, `IClock`): criaÃ§Ã£o normaliza o CPF, grava `CpfHash`/`CpfEncrypted`/`CpfMasked` e o `UserId` recebido; `ExistsByCpfHashAsync = true` â†’ `DuplicateException` "JÃ¡ existe um cliente com este CPF."; `GetByIdAsync` com repositÃ³rio retornando `null` â†’ `NotFoundException` "Cliente nÃ£o encontrado."; listagem devolve `CustomerInfo` (sÃ³ `cpfMasked`); detalhe decifra e formata o CPF e inclui `summary`; atualizaÃ§Ã£o de nome altera `UpdatedAt`; CPF invÃ¡lido â†’ `ValidationException`.
- [X] T055 [P] [US2] Criar `backend/Ambrosio.Tests/Infra/Repository/CustomerRepositoryTest.cs` (`UseInMemoryDatabase` com nome Ãºnico por teste): `ListAsync` retorna sÃ³ clientes do `userId`, filtra por trecho do nome sem diferenciar maiÃºsculas, ordena por nome, pagina e devolve lista vazia alÃ©m do fim com `totalCount` correto; `GetByIdAsync` com outro `userId` â†’ `null`; `GetSummaryAsync` sem pedidos â†’ `purchases = 0`, `netTotal = 0`, `lastPurchase = null`, `daysSinceLastPurchase = null`; com pedidos semeados (sale 100,00 em 2026-10-01, sale 50,00 em 2026-10-20, return âˆ’30,00 em 2026-10-22 e um pedido de outro usuÃ¡rio) â†’ `purchases = 2`, `netTotal = 120,00`, `lastPurchase = 2026-10-20`, `daysSinceLastPurchase` = `today âˆ’ 2026-10-20`.
- [X] T056 [P] [US2] Criar `backend/Ambrosio.ApiTests/Helpers/TestDataHelper.cs` com `GenerateValidCpf()` (9 dÃ­gitos aleatÃ³rios + 2 dÃ­gitos verificadores, formatado `000.000.000-00`) e `NewCustomer(string? name = null)` â†’ `CustomerInsertInfo`.
- [X] T057 [P] [US2] Criar `backend/Ambrosio.ApiTests/Controllers/CustomersControllerTests.cs`: `Create_ValidCpf_ShouldReturn201WithLocation`, `Create_InvalidCpf_ShouldReturn400WithCampoCpf`, `Create_DuplicateCpfSameUser_ShouldReturn409` (mesmo CPF sem pontuaÃ§Ã£o), `List_WithSearch_ShouldReturnOnlyMatchesWithMaskedCpf` (o JSON da listagem nÃ£o contÃ©m o CPF completo â€” SC-005), `List_PageBeyondEnd_ShouldReturnEmptyItems`, `GetById_ShouldReturnFullCpfAndZeroSummary`, `Update_Name_ShouldReturn200`.

### Implementation for User Story 2

- [X] T058 [P] [US2] Criar os DTOs em `backend/Ambrosio.DTO/Customers/`: `CustomerInsertInfo.cs` (`name`, `cpf`), `CustomerUpdateInfo.cs` (`name`), `CustomerInfo.cs` (`id`, `name`, `cpfMasked`, `createdAt`, `updatedAt`), `CustomerDetailInfo.cs` (herda `CustomerInfo` + `cpf`, `summary`), `CustomerSummaryInfo.cs` (`purchases` int, `netTotal` decimal, `lastPurchase` DateOnly?, `daysSinceLastPurchase` int?), `CustomerListQuery.cs` (`search` string?, `page` = 1, `pageSize` = 20).
- [X] T059 [P] [US2] Criar `backend/Ambrosio.Infra.Interfaces/Repository/ICustomerRepository.cs` (`ICustomerRepository<TModel> where TModel : class`): `Task<TModel?> GetByIdAsync(long customerId, long userId)`, `Task<bool> ExistsByCpfHashAsync(long userId, string cpfHash)`, `Task<(List<TModel> Items, int TotalCount)> ListAsync(long userId, string? search, int page, int pageSize)`, `Task<TModel> InsertAsync(TModel entity)`, `Task<TModel> UpdateAsync(TModel entity)`, `Task<CustomerSummaryInfo> GetSummaryAsync(long customerId, long userId, DateOnly today)`.
- [X] T060 [P] [US2] Criar `backend/Ambrosio.Infra.Interfaces/AppServices/ICpfProtectionAppService.cs`: `string Hash(string cpfDigits)`, `string Encrypt(string cpfDigits)`, `string Decrypt(string payload)`.
- [X] T061 [P] [US2] Criar o value object `backend/Ambrosio.Domain/ValueObjects/Cpf.cs` (research Â§5): `static bool TryParse(string? value, out Cpf cpf)`, `static bool IsValid(string? value)`; remove tudo que nÃ£o Ã© dÃ­gito, exige 11 dÃ­gitos, rejeita sequÃªncias repetidas, valida os 2 dÃ­gitos verificadores (mÃ³dulo 11); expÃµe `Digits`, `Masked` (`***.` + dÃ­gitos 4â€“6 + `.` + dÃ­gitos 7â€“9 + `-**`) e `Formatted`.
- [X] T062 [P] [US2] Adicionar a `backend/Ambrosio.Domain/Models/Customer.cs` o mÃ©todo `UpdateName(string name, DateTime now)` (lanÃ§a `ArgumentException` se vazio; faz `Trim()`; atualiza `UpdatedAt`).
- [X] T063 [US2] Criar os validators em `backend/Ambrosio.Domain/Validators/`: `CustomerInsertInfoValidator.cs` (`Name`: `NotEmpty` "O nome Ã© obrigatÃ³rio.", `Length(2, 200)` "O nome deve ter entre 2 e 200 caracteres."; `Cpf`: `NotEmpty` "O CPF Ã© obrigatÃ³rio.", `Must(Cpf.IsValid)` "CPF invÃ¡lido."), `CustomerUpdateInfoValidator.cs` (mesmas regras de `Name`), `CustomerListQueryValidator.cs` (`Page â‰¥ 1` "A pÃ¡gina deve ser maior ou igual a 1."; `PageSize` entre 1 e 100 "O tamanho da pÃ¡gina deve estar entre 1 e 100.").
- [X] T064 [P] [US2] Criar `backend/Ambrosio.Domain/Interfaces/ICustomerService.cs`: `Task<PagedResult<CustomerInfo>> ListAsync(long userId, CustomerListQuery query)`, `Task<CustomerDetailInfo> GetByIdAsync(long userId, long customerId)`, `Task<CustomerDetailInfo> CreateAsync(long userId, CustomerInsertInfo info)`, `Task<CustomerDetailInfo> UpdateAsync(long userId, long customerId, CustomerUpdateInfo info)`.
- [X] T065 [US2] Implementar `backend/Ambrosio.Domain/Services/CustomerService.cs`: validar DTOs; criar com `Cpf.TryParse` â†’ `Hash`, `Encrypt`, `Masked`; checar `ExistsByCpfHashAsync(userId, hash)` â‡’ `DuplicateException("JÃ¡ existe um cliente com este CPF.")`; `GetByIdAsync(customerId, userId) == null` â‡’ `NotFoundException("Cliente nÃ£o encontrado.")` (mesma mensagem para inexistente e de outro usuÃ¡rio); detalhe = `CustomerInfo` + `cpf` decifrado e formatado + `GetSummaryAsync(customerId, userId, IClock.Today)`; mapeamento manual (`CustomerInfo` nunca leva o CPF completo).
- [X] T066 [P] [US2] Implementar `backend/Ambrosio.Infra/AppServices/CpfProtectionAppService.cs` (research Â§4): `Hash` = HMAC-SHA256(dÃ­gitos, UTF8(`Pepper`)) em hex minÃºsculo (64 chars); `Encrypt` = `AesGcm` com chave de 32 bytes (`Convert.FromBase64String(EncryptionKey)`), nonce de 12 bytes aleatÃ³rio, tag de 16 bytes, saÃ­da base64 de `nonce || tag || ciphertext`; `Decrypt` inverte.
- [X] T067 [P] [US2] Implementar `backend/Ambrosio.Infra/Repository/CustomerRepository.cs` (`ICustomerRepository<Customer>`): todas as consultas comeÃ§am por `Where(c => c.UserId == userId)`; `ListAsync` com busca `c.Name.ToLower().Contains(term.ToLower())`, `OrderBy(c => c.Name).ThenBy(c => c.Id)`, `Skip((page âˆ’ 1) Ã— pageSize).Take(pageSize)`, `AsNoTracking`; `GetSummaryAsync` agrega `Orders` com `UserId == userId && CustomerId == customerId` (`purchases = Count(kind == Sale)`, `netTotal = Sum(total)` ou 0, `lastPurchase = Max(order_date where kind == Sale)`, `daysSinceLastPurchase = today.DayNumber âˆ’ lastPurchase.DayNumber` em memÃ³ria); `InsertAsync` converte `23505` em `DuplicateException("JÃ¡ existe um cliente com este CPF.")`.
- [X] T068 [US2] Registrar em `backend/Ambrosio.Application/Startup.cs`: `AddScoped<ICustomerRepository<Customer>, CustomerRepository>()`, `AddSingleton<ICpfProtectionAppService, CpfProtectionAppService>()`, `AddScoped<ICustomerService, CustomerService>()`.
- [X] T069 [US2] Criar `backend/Ambrosio.API/Controllers/CustomersController.cs` (`[Route("customers")]`, `[Authorize]`, herda `ApiControllerBase`): `GET` (`[FromQuery] CustomerListQuery`) â†’ 200 `PagedResult<CustomerInfo>`; `GET {id:long}` â†’ 200 `CustomerDetailInfo`; `POST` â†’ 201 `CreatedAtAction(nameof(GetById), new { id }, detail)`; `PUT {id:long}` â†’ 200 `CustomerDetailInfo`; `userId` sempre de `User.GetUserId()`; catches 400/404/409/500; comentÃ¡rios XML e `[ProducesResponseType]` conforme [contracts/openapi.yaml](./contracts/openapi.yaml).
- [X] T070 [US2] Rodar `dotnet test backend/Ambrosio.Tests` (T051â€“T055 verdes) e `dotnet test backend/Ambrosio.ApiTests` (T057 verde); executar o CenÃ¡rio 2 do [quickstart.md](./quickstart.md) e conferir no banco que `customers.cpf_hash`/`cpf_encrypted` nÃ£o contÃªm o CPF em claro.

**Checkpoint**: US1 e US2 funcionam de forma independente.

---

## Phase 5: User Story 3 - Cadastrar e consultar pedidos com itens (Priority: P1)

**Goal**: cadastro de pedidos (venda, devoluÃ§Ã£o, cancelamento) com itens numerados, regras de sinal do total, unicidade `(nÃºmero, data, tipo)` por usuÃ¡rio, listagem filtrada/paginada e atualizaÃ§Ã£o de status/total.

**Independent Test**: cadastrar uma venda com 2 itens e um cancelamento com total negativo e datas para um cliente; consultar detalhe (itens `lineNo` 1, 2) e listagem filtrada; venda negativa, pedido sem itens e data futura â†’ 400; duplicidade â†’ 409 (quickstart, CenÃ¡rio 3).

### Tests for User Story 3 âš ï¸

- [X] T071 [P] [US3] Criar `backend/Ambrosio.Tests/Domain/Models/OrderTest.cs`: `AddItem` atribui `LineNo` 1, 2, 3â€¦; `IsTotalSignValid` â†’ `Sale` exige `> 0`, `Return`/`Cancellation` exigem `< 0`, `0` sempre invÃ¡lido; `UpdateStatusAndTotal` com sinal incompatÃ­vel com `Kind` lanÃ§a exceÃ§Ã£o e nÃ£o altera o pedido; com valores vÃ¡lidos atualiza `Status`, `Total` e `UpdatedAt`.
- [X] T072 [P] [US3] Criar `backend/Ambrosio.Tests/Domain/Validators/OrderInsertInfoValidatorTest.cs` (com `IClock` fixo em 2026-10-25): `customerId > 0`; `orderNumber` obrigatÃ³rio e mÃ¡ximo 60; `orderDate` obrigatÃ³ria e â‰¤ hoje (2026-10-26 recusada); `kind` dentro do enum; venda com total â‰¤ 0 â†’ erro em `Total` "O total de uma venda deve ser positivo."; devoluÃ§Ã£o/cancelamento com total â‰¥ 0 â†’ "O total de uma devoluÃ§Ã£o ou cancelamento deve ser negativo."; `items` vazio â†’ "O pedido deve ter ao menos um item."; `channel`, `status`, `externalId` mÃ¡ximo 60; `cancelledOn < originalSaleOn` â†’ erro; itens: ao menos um de `productName`/`skuRef`, `productName` mÃ¡ximo 200, `skuRef` mÃ¡ximo 60, `size` mÃ¡ximo 20, `imageUrl` mÃ¡ximo 500 e URL absoluta http/https. Incluir casos de `OrderUpdateInfoValidator` (`status` mÃ¡ximo 60; `total` â‰  0) e `OrderListQueryValidator` (`page â‰¥ 1`, `pageSize` 1â€“100, `from â‰¤ to`).
- [X] T073 [P] [US3] Criar `backend/Ambrosio.Tests/Domain/Services/OrderServiceTest.cs` (Moq de `IOrderRepository<Order>`, `ICustomerRepository<Customer>`, `IClock`): cliente nÃ£o encontrado para o `userId` â†’ `NotFoundException("Cliente nÃ£o encontrado.")` e nenhum `InsertAsync`; `ExistsAsync = true` â†’ `DuplicateException("JÃ¡ existe um pedido com este nÃºmero, data e tipo.")`; criaÃ§Ã£o grava `UserId` do token, `CustomerId`, itens com `LineNo` sequencial e timestamps; `OrderDetailInfo` traz `customer.cpfMasked` e itens; atualizaÃ§Ã£o com total de sinal incompatÃ­vel com o `Kind` gravado â†’ `ValidationException` no campo `total`; atualizaÃ§Ã£o de pedido inexistente/de outro usuÃ¡rio â†’ `NotFoundException("Pedido nÃ£o encontrado.")`.
- [X] T074 [P] [US3] Criar `backend/Ambrosio.Tests/Infra/Repository/OrderRepositoryTest.cs` (InMemory, research Â§17): isolamento por `userId`; `from`/`to` inclusivos; filtro `kind`; `channel` por igualdade sem diferenciar maiÃºsculas (`"linx"` encontra `"Linx"`); `customerId`; `search` em `order_number` **ou** nome do cliente; ordenaÃ§Ã£o `order_date DESC, order_id DESC`; paginaÃ§Ã£o e pÃ¡gina alÃ©m do fim â†’ vazio com `totalCount` correto; `GetByIdAsync` inclui `Customer` e `Items` ordenados por `LineNo` e devolve `null` para outro usuÃ¡rio; `ExistsAsync` considera `(userId, orderNumber, orderDate, kind)` â€” mesmo nÃºmero/data com outro tipo **nÃ£o** Ã© duplicado.
- [X] T075 [P] [US3] Estender `backend/Ambrosio.ApiTests/Helpers/TestDataHelper.cs` com `NewSaleOrder(long customerId)` (venda 589,80 com os 2 itens do quickstart: "CalÃ§a Inez" `42CA500UIE_12182` tam. 38 R$ 399,90 e "Blusa Lia" `41BL200AAA_00001` tam. P R$ 189,90, canal "Linx", origem `cota`, status "Faturado", nÃºmero Ãºnico por execuÃ§Ã£o) e `NewCancellationOrder(long customerId, string orderNumber)` (total âˆ’589,80, `originalSaleOn`, `cancelledOn`, item com preÃ§o âˆ’399,90).
- [X] T076 [P] [US3] Criar `backend/Ambrosio.ApiTests/Controllers/OrdersControllerTests.cs`: `Create_Sale_ShouldReturn201WithNumberedItems`, `Create_Cancellation_NegativeTotal_ShouldReturn201KeepingDates` (SC-006), `Create_SaleNegativeTotal_ShouldReturn400WithCampoTotal`, `Create_NoItems_ShouldReturn400WithCampoItems`, `Create_FutureDate_ShouldReturn400WithCampoOrderDate`, `Create_Duplicate_ShouldReturn409`, `Create_UnknownChannel_ShouldReturn201` (canal "Outlet"), `List_WithFilters_ShouldReturnMatchesOrderedByDateDesc`, `GetById_ShouldReturnCustomerAndItems`, `Update_StatusAndTotal_ShouldReturn200`, `CustomerSummary_AfterOrders_ShouldReflectNetTotal` (`GET /customers/{id}`).

### Implementation for User Story 3

- [X] T077 [P] [US3] Criar os DTOs em `backend/Ambrosio.DTO/Orders/`: `OrderItemInsertInfo.cs` (`productName`, `skuRef`, `size`, `price` decimal?, `imageUrl`), `OrderInsertInfo.cs` (`customerId`, `orderNumber`, `orderDate` DateOnly, `kind` OrderKind, `channel`, `origin` OrderOrigin?, `status`, `total` decimal, `originalSaleOn` DateOnly?, `cancelledOn` DateOnly?, `externalId`, `items` List<OrderItemInsertInfo>), `OrderUpdateInfo.cs` (`status`, `total`), `OrderItemInfo.cs` (`id`, `lineNo` + campos do item), `OrderInfo.cs` (`id`, `customerId`, `customerName`, `orderNumber`, `orderDate`, `kind`, `channel`, `origin`, `status`, `total`, `itemsCount`, `createdAt`, `updatedAt`), `OrderDetailInfo.cs` (herda `OrderInfo` + `originalSaleOn`, `cancelledOn`, `externalId`, `customer: CustomerInfo`, `items: List<OrderItemInfo>`), `OrderListQuery.cs` (`from`, `to` DateOnly?, `kind` OrderKind?, `channel`, `customerId` long?, `search`, `page` = 1, `pageSize` = 20).
- [X] T078 [P] [US3] Criar `backend/Ambrosio.Infra.Interfaces/Repository/IOrderRepository.cs` (`IOrderRepository<TModel> where TModel : class`): `Task<TModel?> GetByIdAsync(long orderId, long userId)` (com cliente e itens), `Task<bool> ExistsAsync(long userId, string orderNumber, DateOnly orderDate, OrderKind kind)`, `Task<(List<TModel> Items, int TotalCount)> ListAsync(long userId, OrderListQuery query)`, `Task<TModel> InsertAsync(TModel entity)`, `Task<TModel> UpdateAsync(TModel entity)`.
- [X] T079 [US3] Adicionar a `backend/Ambrosio.Domain/Models/Order.cs`: `static bool IsTotalSignValid(OrderKind kind, decimal total)` (`Sale` â†’ `total > 0`; `Return`/`Cancellation` â†’ `total < 0`); `AddItem(OrderItem item)` (atribui `LineNo = Items.Count + 1`); `UpdateStatusAndTotal(string? status, decimal total, DateTime now)` (revalida o sinal contra `Kind` e lanÃ§a `ArgumentException` se invÃ¡lido). `Kind` nÃ£o tem mÃ©todo de alteraÃ§Ã£o (imutÃ¡vel apÃ³s criaÃ§Ã£o).
- [X] T080 [US3] Criar os validators em `backend/Ambrosio.Domain/Validators/` (todos recebem `IClock` quando precisam de "hoje"): `OrderItemInsertInfoValidator.cs`, `OrderInsertInfoValidator.cs` (aplica `RuleForEach(x => x.Items).SetValidator(new OrderItemInsertInfoValidator())`), `OrderUpdateInfoValidator.cs`, `OrderListQueryValidator.cs`, com exatamente as regras e mensagens de T072; regras com banco (cliente do usuÃ¡rio, duplicidade) ficam no serviÃ§o.
- [X] T081 [P] [US3] Criar `backend/Ambrosio.Domain/Interfaces/IOrderService.cs`: `Task<PagedResult<OrderInfo>> ListAsync(long userId, OrderListQuery query)`, `Task<OrderDetailInfo> GetByIdAsync(long userId, long orderId)`, `Task<OrderDetailInfo> CreateAsync(long userId, OrderInsertInfo info)`, `Task<OrderDetailInfo> UpdateAsync(long userId, long orderId, OrderUpdateInfo info)`.
- [X] T082 [US3] Implementar `backend/Ambrosio.Domain/Services/OrderService.cs`: validar DTOs; buscar cliente por `(customerId, userId)` â‡’ `null` â†’ `NotFoundException("Cliente nÃ£o encontrado.")` (FR-023); `ExistsAsync` â‡’ `DuplicateException("JÃ¡ existe um pedido com este nÃºmero, data e tipo.")`; montar `Order` (`UserId` do token, `Trim()` em textos, timestamps de `IClock.UtcNow`) e adicionar itens via `AddItem`; atualizaÃ§Ã£o: buscar por `(orderId, userId)` â‡’ `NotFoundException("Pedido nÃ£o encontrado.")`, checar `IsTotalSignValid(order.Kind, total)` â‡’ `ValidationException` campo `total`, chamar `UpdateStatusAndTotal`; mapeamento manual para `OrderInfo`/`OrderDetailInfo` (cliente com CPF mascarado; `itemsCount = Items.Count`).
- [X] T083 [P] [US3] Implementar `backend/Ambrosio.Infra/Repository/OrderRepository.cs` (`IOrderRepository<Order>`): todas as consultas comeÃ§am por `Where(o => o.UserId == userId)`; `ListAsync` aplica `from`/`to` inclusivos, `kind`, `channel` (`o.Channel != null && o.Channel.ToLower() == channel.ToLower()`), `customerId`, `search` (`o.OrderNumber.ToLower().Contains(t) || o.Customer!.Name.ToLower().Contains(t)`), `Include(o => o.Customer)`, `Include(o => o.Items)`, `OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id)`, paginaÃ§Ã£o e `AsNoTracking`; `GetByIdAsync` com `Include` de cliente e itens ordenados por `LineNo`; `InsertAsync` grava pedido e itens numa Ãºnica `SaveChangesAsync` e converte `23505` em `DuplicateException("JÃ¡ existe um pedido com este nÃºmero, data e tipo.")`.
- [X] T084 [US3] Registrar em `backend/Ambrosio.Application/Startup.cs`: `AddScoped<IOrderRepository<Order>, OrderRepository>()` e `AddScoped<IOrderService, OrderService>()`.
- [X] T085 [US3] Criar `backend/Ambrosio.API/Controllers/OrdersController.cs` (`[Route("orders")]`, `[Authorize]`, herda `ApiControllerBase`): `GET` (`[FromQuery] OrderListQuery`) â†’ 200 `PagedResult<OrderInfo>`; `GET {id:long}` â†’ 200 `OrderDetailInfo`; `POST` â†’ 201 `CreatedAtAction` com `OrderDetailInfo`; `PUT {id:long}` â†’ 200 `OrderDetailInfo`; catches 400/404/409/500; comentÃ¡rios XML e `[ProducesResponseType]` conforme [contracts/openapi.yaml](./contracts/openapi.yaml) (o 404 do `POST` documenta "Cliente nÃ£o encontrado (inclui cliente de outro usuÃ¡rio)").
- [X] T086 [US3] Rodar `dotnet test backend/Ambrosio.Tests` (T071â€“T074 verdes) e `dotnet test backend/Ambrosio.ApiTests` (T076 verde); executar o CenÃ¡rio 3 do [quickstart.md](./quickstart.md), incluindo o resumo do cliente (`purchases = 1`, `netTotal = âˆ’10,00`, `lastPurchase = 2026-10-24`).

**Checkpoint**: US1, US2 e US3 funcionam de forma independente.

---

## Phase 6: User Story 4 - Isolamento de dados por usuÃ¡rio (Priority: P1)

**Goal**: garantir e provar que nenhum usuÃ¡rio lÃª, altera ou referencia dados de outro (FR-021 a FR-023, SC-002, SC-004).

**Independent Test**: com os usuÃ¡rios A e B do fixture, B nÃ£o lista, nÃ£o lÃª, nÃ£o altera e nÃ£o vincula nada de A (sempre 404 ou lista vazia); o mesmo CPF em A e B gera clientes distintos; nenhuma rota protegida responde sem token vÃ¡lido (quickstart, CenÃ¡rio 4).

### Tests for User Story 4 âš ï¸

- [X] T087 [P] [US4] Criar `backend/Ambrosio.Tests/Infra/Repository/RepositoryIsolationContractTest.cs`: por reflexÃ£o sobre `ICustomerRepository<>` e `IOrderRepository<>`, todo mÃ©todo exceto `InsertAsync`/`UpdateAsync` deve ter um parÃ¢metro `long userId`; e por reflexÃ£o sobre todos os tipos de `Ambrosio.DTO` nos namespaces `Customers` e `Orders`, nenhum DTO de entrada (`*InsertInfo`, `*UpdateInfo`, `*ListQuery`) tem propriedade `UserId`.
- [X] T088 [P] [US4] Criar `backend/Ambrosio.ApiTests/Controllers/IsolationTests.cs` (A cria cliente e pedido no setup): `ListCustomers_AsUserB_ShouldNotContainUserACustomers`, `ListOrders_AsUserB_ShouldNotContainUserAOrders`, `GetCustomer_OfUserA_AsUserB_ShouldReturn404`, `UpdateCustomer_OfUserA_AsUserB_ShouldReturn404AndKeepName`, `GetOrder_OfUserA_AsUserB_ShouldReturn404`, `UpdateOrder_OfUserA_AsUserB_ShouldReturn404AndKeepTotal`, `CreateOrder_WithUserACustomer_AsUserB_ShouldReturn404`, `SameCpf_ForUsersAAndB_ShouldCreateTwoIndependentCustomers`, `NotFoundBody_ForOtherUserResource_ShouldEqualBodyForNonexistentId` (nÃ£o revela existÃªncia).
- [X] T089 [P] [US4] Criar `backend/Ambrosio.ApiTests/Controllers/UnauthorizedAccessTests.cs` (SC-004): as 8 operaÃ§Ãµes protegidas (`GET /users/me`, `GET`/`POST /customers`, `GET`/`PUT /customers/{id}`, `GET`/`POST /orders`, `GET`/`PUT /orders/{id}`) respondem 401 sem `Authorization`, com token aleatÃ³rio e com token assinado por outra chave (gerado no prÃ³prio teste).

### Implementation for User Story 4

- [X] T090 [US4] Auditar `backend/Ambrosio.Infra/Repository/CustomerRepository.cs` e `backend/Ambrosio.Infra/Repository/OrderRepository.cs`: toda consulta de leitura aplica `Where(x => x.UserId == userId)` como **primeiro** filtro (inclusive o `Include` de `Customer` em pedidos e a agregaÃ§Ã£o de `GetSummaryAsync`); `UpdateAsync` sÃ³ recebe entidades obtidas por `GetByIdAsync(id, userId)`. Corrigir qualquer desvio encontrado.
- [X] T091 [US4] Auditar `backend/Ambrosio.API/Controllers/CustomersController.cs`, `OrdersController.cs` e `UsersController.cs`: `[Authorize]` no nÃ­vel da classe; `userId` obtido apenas por `User.GetUserId()`; nenhuma aÃ§Ã£o com `[AllowAnonymous]`; respostas 404 idÃªnticas para recurso inexistente e de outro usuÃ¡rio. Corrigir qualquer desvio.
- [X] T092 [US4] Rodar `dotnet test backend/Ambrosio.Tests` e `dotnet test backend/Ambrosio.ApiTests` (T087â€“T089 verdes) e executar o CenÃ¡rio 4 do [quickstart.md](./quickstart.md).

**Checkpoint**: todas as histÃ³rias funcionam e o isolamento estÃ¡ provado por testes (SC-002 = 100%).

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: documentaÃ§Ã£o, desempenho, seguranÃ§a e validaÃ§Ã£o final.

- [X] T093 [P] Revisar os comentÃ¡rios XML (`<summary>`, `<param>`, `<response code="...">`) e os `[ProducesResponseType]` de `backend/Ambrosio.API/Controllers/AuthController.cs`, `UsersController.cs`, `CustomersController.cs` e `OrdersController.cs` para que o Swagger seja autoexplicativo (SC-001).
- [X] T094 Comparar o `swagger.json` gerado em `http://localhost:5000/swagger/v1/swagger.json` com `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` (paths, mÃ©todos, cÃ³digos de status, nomes de propriedades camelCase, enums como string minÃºscula `sale`/`return`/`cancellation`/`cota`/`omni`/`google`/`dev`) e corrigir divergÃªncias no cÃ³digo ou, se a mudanÃ§a for intencional, no contrato.
- [X] T095 [P] Criar o script de carga `backend/scripts/seed-load-test.sql` (SC-003): para um `user_id` informado via `\set`, inserir ~2.000 clientes (hash/cifra/mÃ¡scara fictÃ­cios) e ~10.000 pedidos com 1â€“3 itens usando `generate_series`, com datas distribuÃ­das em 12 meses e tipos/canais variados; incluir no fim os `EXPLAIN ANALYZE` das consultas de `GET /orders?page=1&pageSize=20` e `GET /customers?search=a`.
- [X] T096 Executar `backend/scripts/seed-load-test.sql` no `ambrosio_dev`, medir `GET /orders?page=1&pageSize=20`, `GET /orders` com filtros e `GET /customers?search=a` (meta < 1 s; interna p95 < 300 ms) e confirmar uso de `ix_orders_user_date`/`ix_customers_user_name`; se nÃ£o for atingida, ajustar as consultas de `backend/Ambrosio.Infra/Repository/OrderRepository.cs` (ex.: `Count` separado sem `Include`, projeÃ§Ã£o para `itemsCount`).
- [X] T097 [P] RevisÃ£o de seguranÃ§a (SC-005, constituiÃ§Ã£o Â§VI): buscar em `backend/` por `Password` em DTOs e por logs que contenham CPF ou tokens; confirmar que `backend/Ambrosio.API/appsettings.json` nÃ£o tem segredos, que CORS `AllowAnyOrigin` sÃ³ existe em Development e que `/auth/dev-login` responde 404 com `ASPNETCORE_ENVIRONMENT=Production` (subir a API com segredos de teste e chamar a rota).
- [X] T098 [P] Criar `backend/README.md` com prÃ©-requisitos, variÃ¡veis de ambiente (`ConnectionStrings__AmbrosioContext`, `ASPNETCORE_ENVIRONMENT`, `Auth__JwtSecret`, `Auth__GoogleClientId`, `Auth__ExpirationMinutes`, `CpfProtection__Pepper`, `CpfProtection__EncryptionKey`), comandos de migraÃ§Ã£o/execuÃ§Ã£o/testes e link para `specs/001-api-cadastro-clientes-pedidos/quickstart.md`.
- [X] T099 Executar o [quickstart.md](./quickstart.md) completo do zero (banco recriado com `dotnet ef database drop` + `update`) e marcar todos os itens do "Checklist de aceite".
- [X] T100 [P] Redigir a proposta de emenda da constituiÃ§Ã£o em `specs/001-api-cadastro-clientes-pedidos/constitution-amendment.md` (2.0.0 â†’ 2.1.0, MINOR): resolver `TODO(AUTH_SCHEME)` do PrincÃ­pio VI com "JWT Bearer emitido pela API apÃ³s login OAuth/OIDC (Google)" e proteÃ§Ã£o `[Authorize]` (research Â§15). **NÃ£o** editar `.specify/memory/constitution.md` aqui: a emenda segue em PR separado, conforme Governance.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependÃªncias.
- **Foundational (Phase 2)**: depende do Setup; **bloqueia** todas as histÃ³rias. T020 depende de T016â€“T019; T022 depende de T020â€“T021; T025 depende de T022â€“T024; T026 depende de T025.
- **US1 (Phase 3)**: depende da Phase 2.
- **US2 (Phase 4)**: depende da Phase 2; os testes HTTP (T057) usam o fixture de T031 (US1), e a API exige token, portanto **na prÃ¡tica roda apÃ³s a US1**. Os testes unitÃ¡rios (T051â€“T055) e a implementaÃ§Ã£o nÃ£o dependem da US1.
- **US3 (Phase 5)**: depende da Phase 2 e da US2 (o pedido exige cliente: `ICustomerRepository`, `CustomerInfo` e `POST /customers` sÃ£o usados por T073, T076, T082).
- **US4 (Phase 6)**: depende de US1, US2 e US3 (verifica o isolamento delas).
- **Polish (Phase 7)**: depende das histÃ³rias desejadas concluÃ­das.

### User Story Dependencies

```text
Setup â”€â–º Foundational â”€â–º US1 (Auth) â”€â–º US2 (Clientes) â”€â–º US3 (Pedidos) â”€â–º US4 (Isolamento) â”€â–º Polish
                           â””â”€â”€â”€â”€â”€â”€â”€â”€ (testes unitÃ¡rios da US2 podem comeÃ§ar junto com a US1) â”˜
```

### Within Each User Story

- Testes primeiro (devem falhar) â†’ DTOs/interfaces â†’ modelos/value objects â†’ validators â†’ serviÃ§os â†’ repositÃ³rios/AppServices â†’ registro no `Startup.cs` â†’ controller â†’ validaÃ§Ã£o (testes + quickstart).
- Tarefas no mesmo arquivo sÃ£o sequenciais: `Startup.cs` (T022 â†’ T046 â†’ T068 â†’ T084), `Program.cs` (T025 â†’ T047), `TestDataHelper.cs` (T056 â†’ T075), modelos (T016 â†’ T038; T017 â†’ T062; T018 â†’ T079).

### Parallel Opportunities

- Setup: T003â€“T010 em paralelo apÃ³s T001â€“T002.
- Foundational: T011â€“T019, T021, T023, T024 em paralelo; depois T020 â†’ T022 â†’ T025 â†’ T026.
- US1: testes T027â€“T033 em paralelo; T034â€“T037, T039, T040 em paralelo; T042â€“T045 em paralelo apÃ³s T041 (ou junto, se as interfaces existirem).
- US2: testes T051â€“T057 em paralelo; T058â€“T062 e T064 em paralelo; T066 e T067 em paralelo.
- US3: testes T071â€“T076 em paralelo; T077, T078, T081 em paralelo; T083 em paralelo com T082.
- US4: T087â€“T089 em paralelo.
- Polish: T093, T095, T097, T098, T100 em paralelo.

---

## Parallel Example: User Story 1

```bash
# Testes da US1 juntos:
Task: "JwtTokenAppServiceTest em backend/Ambrosio.Tests/Infra/AppServices/JwtTokenAppServiceTest.cs"
Task: "AuthServiceTest em backend/Ambrosio.Tests/Domain/Services/AuthServiceTest.cs"
Task: "ApiTestFixture em backend/Ambrosio.ApiTests/Fixtures/ApiTestFixture.cs"
Task: "AuthControllerTests em backend/Ambrosio.ApiTests/Controllers/AuthControllerTests.cs"

# Contratos da US1 juntos:
Task: "DTOs em backend/Ambrosio.DTO/Auth/"
Task: "UserInfo em backend/Ambrosio.DTO/Users/UserInfo.cs"
Task: "IUserRepository em backend/Ambrosio.Infra.Interfaces/Repository/IUserRepository.cs"
Task: "IOAuthTokenAppService e IJwtTokenAppService em backend/Ambrosio.Infra.Interfaces/AppServices/"
```

## Parallel Example: User Story 2

```bash
Task: "CpfTest em backend/Ambrosio.Tests/Domain/ValueObjects/CpfTest.cs"
Task: "CpfProtectionAppServiceTest em backend/Ambrosio.Tests/Infra/AppServices/CpfProtectionAppServiceTest.cs"
Task: "CustomerRepositoryTest em backend/Ambrosio.Tests/Infra/Repository/CustomerRepositoryTest.cs"

Task: "Cpf value object em backend/Ambrosio.Domain/ValueObjects/Cpf.cs"
Task: "CpfProtectionAppService em backend/Ambrosio.Infra/AppServices/CpfProtectionAppService.cs"
Task: "CustomerRepository em backend/Ambrosio.Infra/Repository/CustomerRepository.cs"
```

## Parallel Example: User Story 3

```bash
Task: "OrderTest em backend/Ambrosio.Tests/Domain/Models/OrderTest.cs"
Task: "OrderInsertInfoValidatorTest em backend/Ambrosio.Tests/Domain/Validators/OrderInsertInfoValidatorTest.cs"
Task: "OrderRepositoryTest em backend/Ambrosio.Tests/Infra/Repository/OrderRepositoryTest.cs"

Task: "DTOs em backend/Ambrosio.DTO/Orders/"
Task: "IOrderRepository em backend/Ambrosio.Infra.Interfaces/Repository/IOrderRepository.cs"
Task: "IOrderService em backend/Ambrosio.Domain/Interfaces/IOrderService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup.
2. Phase 2: Foundational (bloqueante).
3. Phase 3: US1 (autenticaÃ§Ã£o).
4. **PARAR e VALIDAR**: CenÃ¡rio 1 do quickstart + testes da US1.

### Incremental Delivery

1. Setup + Foundational â†’ base pronta (API sobe, banco migrado, Swagger com Bearer).
2. + US1 â†’ login e perfil (MVP tÃ©cnico).
3. + US2 â†’ clientes com CPF protegido (primeiro valor de negÃ³cio).
4. + US3 â†’ pedidos com itens (paridade com o +Vendas, SC-006).
5. + US4 â†’ isolamento provado (SC-002, SC-004) â€” prÃ©-requisito para uso com mais de uma vendedora.
6. Polish â†’ desempenho (SC-003), seguranÃ§a (SC-005), documentaÃ§Ã£o (SC-001) e emenda da constituiÃ§Ã£o.

### Parallel Team Strategy

1. Equipe conclui Setup + Foundational junto.
2. Depois: Dev A na US1; Dev B nos testes unitÃ¡rios e na implementaÃ§Ã£o de domÃ­nio/infra da US2 (Cpf, CpfProtection, CustomerRepository); Dev C nos validators e no modelo `Order` da US3 (T071, T072, T079, T080).
3. Controllers e testes HTTP seguem a ordem US1 â†’ US2 â†’ US3 â†’ US4.

---

## Notes

- [P] = arquivos diferentes, sem dependÃªncias pendentes.
- [Story] liga cada tarefa Ã  histÃ³ria da spec para rastreabilidade.
- Verificar que os testes falham antes de implementar.
- Commitar apÃ³s cada tarefa ou grupo lÃ³gico (o workspace ainda nÃ£o Ã© um repositÃ³rio git).
- `Ambrosio.ApiTests` exige a API no ar em `Development` (`$env:ApiBaseUrl='http://localhost:5000'; dotnet test backend/Ambrosio.ApiTests`).
- Nunca executar `docker`/`docker compose` localmente (constituiÃ§Ã£o Â§II).
