# Implementation Plan: API de cadastro e consulta de clientes e pedidos

**Branch**: `001-api-cadastro-clientes-pedidos` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-api-cadastro-clientes-pedidos/spec.md`

## Summary

API REST em .NET 8 + PostgreSQL que permite a uma vendedora autenticada (Google OIDC → JWT próprio do Ambrosio) cadastrar e consultar **clientes** (nome + CPF protegido) e **pedidos** com itens (venda, devolução, cancelamento), com isolamento total de dados por usuário. A arquitetura segue a skill `dotnet-architecture` (Clean Architecture em 6 projetos + testes), validação com FluentValidation em português, enums inteiros no banco e expostos como string no JSON, e Swagger com suporte a Bearer.

Decisões tomadas com o solicitante nesta fase (ver [research.md](./research.md)):
- **OAuth**: o cliente obtém um ID token do Google; `POST /auth/google` valida o token, cria/localiza o usuário pelo e-mail e devolve um JWT do Ambrosio usado nas demais chamadas.
- **Login de desenvolvimento**: `POST /auth/dev-login` emite JWT sem Google, disponível **apenas** em `ASPNETCORE_ENVIRONMENT=Development`.
- **Layout**: solução em `backend/`, deixando `frontend/` para fases futuras.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (LTS)

**Primary Dependencies**:
- ASP.NET Core 8 Web API (controllers)
- Entity Framework Core 9.x + `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x
- Swashbuckle.AspNetCore 8.x (Swagger/OpenAPI, com security scheme Bearer)
- `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x + `System.IdentityModel.Tokens.Jwt` (emissão/validação do JWT próprio)
- `Google.Apis.Auth` (validação do ID token Google)
- FluentValidation 11.x + `FluentValidation.DependencyInjectionExtensions`
- Testes: xUnit, Moq, `Microsoft.EntityFrameworkCore.InMemory` (apenas para repositórios com lógica própria)

**Storage**: PostgreSQL 16+ (4 tabelas: `users`, `customers`, `orders`, `order_items`); migrações via `dotnet ef`

**Testing**: xUnit + Moq em `backend/Ambrosio.Tests` (skill `dotnet-test`); testes HTTP ponta a ponta opcionais em `Ambrosio.ApiTests` (skill `dotnet-test-api`) usando o `dev-login` como fixture de autenticação

**Target Platform**: Linux server (container, porta 8080) em produção; Windows/macOS/Linux para desenvolvimento local (sem Docker local, conforme constituição)

**Project Type**: web-service (REST API, sem front-end nesta entrega)

**Performance Goals**: listagens de clientes e pedidos ≤ 1 s com até 10.000 pedidos por usuário (SC-003); meta interna p95 < 300 ms para leituras paginadas

**Constraints**:
- Sem delete em cascata; sem endpoints de exclusão nesta entrega
- Convenções de banco da constituição (snake_case, PK `{entidade}_id` bigint identity, `timestamp without time zone`, `ClientSetNull`)
- CPF nunca em claro no banco; nunca completo em listagens; senha nunca em respostas
- Todo acesso a clientes/pedidos filtrado por `user_id` do token (nenhuma consulta sem esse filtro)
- Mensagens de erro em português com o campo inválido

**Scale/Scope**: dezenas de usuários (vendedoras), ~10 mil pedidos e ~2 mil clientes por usuário; 11 endpoints; 4 entidades

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Princípio | Como o plano atende | Status |
|---|---|---|---|
| I | Skills de arquitetura obrigatórias | Backend gerado com `dotnet-architecture` (DTO → Infra.Interfaces → Domain → Infra → Application); validação com `dotnet-fluent-validation`; testes com `dotnet-test`. `react-architecture` não se aplica (sem front-end). | ✅ |
| II | Stack fixa | .NET 8.0, EF Core 9.x, PostgreSQL, Swashbuckle 8.x. Nenhum ORM alternativo. Nenhum comando `docker` local (quickstart usa PostgreSQL instalado localmente). | ✅ |
| III | Case sensitivity | Pastas do backend em PascalCase conforme a skill (`Controllers/`, `Services/`, `Repository/`, `Context/`). Sem front-end. | ✅ |
| IV | Convenções de código | Namespaces file-scoped `Ambrosio.{Camada}`, PascalCase, `_camelCase` em campos privados, `[JsonPropertyName("camelCase")]` em todos os DTOs, DTOs nomeados `Info`/`InsertInfo`/`UpdateInfo`, wrapper de erro com chaves `sucesso`, `mensagem`, `erros`. | ✅ |
| V | Banco de dados | Tabelas snake_case plural (`users`, `customers`, `orders`, `order_items`); PK `user_id`, `customer_id`, `order_id`, `order_item_id` bigint identity; constraints `{tabela}_pkey`; FKs `fk_user_customer`, `fk_user_order`, `fk_customer_order`, `fk_order_order_item`; `DeleteBehavior.ClientSetNull`; `timestamp without time zone`; `varchar(n)`; enums `kind`/`origin`/`provider` como `integer`. **Exceção justificada**: `orders.status` e `orders.channel` são texto livre (ver Complexity Tracking). | ✅ (1 exceção justificada) |
| VI | Autenticação e segurança | `TODO(AUTH_SCHEME)` resolvido nesta feature: **JWT Bearer emitido pelo Ambrosio após validação de ID token Google (OIDC)**. `[Authorize]` em `UsersController`, `CustomersController`, `OrdersController`; `[AllowAnonymous]` apenas nas ações de login. CORS `AllowAnyOrigin` só em Development. Tokens não vão em cookies (API só devolve o token no corpo). Segredos só por configuração/variáveis de ambiente. **Follow-up**: emenda da constituição registrando o esquema (ver research.md §15). | ✅ (emenda pendente, fora do escopo do código) |
| — | Variáveis de ambiente | `ConnectionStrings__AmbrosioContext`, `ASPNETCORE_ENVIRONMENT` + variáveis da feature (`Auth__*`, `CpfProtection__*`). | ✅ |
| — | Tratamento de erros | Controllers com `try/catch` devolvendo `StatusCode(500, ex.Message)` como último catch; catches específicos antes para `ValidationException` → 400, `NotFoundException` → 404, `DuplicateException` → 409. | ✅ |

**Resultado do gate (pré-Phase 0)**: APROVADO com 1 exceção justificada (status/canal como texto).

**Re-avaliação pós-Phase 1**: o data-model e os contratos mantêm todas as convenções acima; nenhuma nova violação introduzida. APROVADO.

## Project Structure

### Documentation (this feature)

```text
specs/001-api-cadastro-clientes-pedidos/
├── plan.md              # Este arquivo
├── spec.md              # Especificação funcional
├── research.md          # Phase 0: decisões técnicas
├── data-model.md        # Phase 1: entidades, tabelas, regras
├── quickstart.md        # Phase 1: guia de validação ponta a ponta
├── contracts/
│   └── openapi.yaml     # Phase 1: contrato REST (OpenAPI 3.0)
└── tasks.md             # Phase 2 (gerado por /speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosio.sln
├── Ambrosio.API/                         # Presentation (ASP.NET Core Web API)
│   ├── Controllers/
│   │   ├── AuthController.cs             # POST /auth/google, POST /auth/dev-login (Development)
│   │   ├── UsersController.cs            # GET /users/me
│   │   ├── CustomersController.cs        # /customers
│   │   └── OrdersController.cs           # /orders
│   ├── Extensions/
│   │   └── ClaimsPrincipalExtensions.cs  # GetUserId() a partir do claim "sub"
│   ├── Program.cs                        # JwtBearer, Swagger (Bearer), CORS, JsonStringEnumConverter, Startup.ConfigureServices
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── Properties/launchSettings.json
├── Ambrosio.Application/
│   ├── Startup.cs                        # DI centralizado (DbContext, repositórios, AppServices, serviços, validators)
│   └── Settings/
│       └── AmbrosioSettings.cs           # ConnectionString, AuthSettings, CpfProtectionSettings
├── Ambrosio.Domain/
│   ├── Enums/
│   │   ├── OrderKind.cs                  # Sale=1, Return=2, Cancellation=3
│   │   ├── OrderOrigin.cs                # Cota=1, Omni=2
│   │   └── AuthProvider.cs              # Google=1, Dev=2
│   ├── Models/
│   │   ├── User.cs
│   │   ├── Customer.cs
│   │   ├── Order.cs
│   │   └── OrderItem.cs
│   ├── ValueObjects/
│   │   └── Cpf.cs                        # Normalização, validação de dígitos, máscara
│   ├── Exceptions/
│   │   ├── NotFoundException.cs
│   │   └── DuplicateException.cs
│   ├── Interfaces/
│   │   ├── IAuthService.cs
│   │   ├── IUserService.cs
│   │   ├── ICustomerService.cs
│   │   └── IOrderService.cs
│   ├── Services/
│   │   ├── AuthService.cs
│   │   ├── UserService.cs
│   │   ├── CustomerService.cs
│   │   └── OrderService.cs
│   └── Validators/
│       ├── CustomerInsertInfoValidator.cs
│       ├── CustomerUpdateInfoValidator.cs
│       ├── OrderInsertInfoValidator.cs
│       ├── OrderItemInsertInfoValidator.cs
│       └── OrderUpdateInfoValidator.cs
├── Ambrosio.Infra.Interfaces/
│   ├── Repository/
│   │   ├── IUserRepository.cs            # IUserRepository<TModel>
│   │   ├── ICustomerRepository.cs        # ICustomerRepository<TModel> (+ ICustomerSummary via DTO)
│   │   └── IOrderRepository.cs           # IOrderRepository<TModel>
│   └── AppServices/
│       ├── IOAuthTokenAppService.cs      # Valida ID token externo → OAuthUserInfo (DTO)
│       ├── IJwtTokenAppService.cs        # Emite JWT do Ambrosio
│       └── ICpfProtectionAppService.cs   # Hash, cifra, decifra
├── Ambrosio.Infra/
│   ├── Context/
│   │   └── AmbrosioContext.cs            # DbSets + Fluent API (snake_case, FKs, índices únicos)
│   ├── Migrations/
│   ├── Repository/
│   │   ├── UserRepository.cs
│   │   ├── CustomerRepository.cs         # Sempre filtra por user_id; GetSummaryAsync
│   │   └── OrderRepository.cs            # Sempre filtra por user_id; filtros + paginação
│   └── AppServices/
│       ├── GoogleOAuthTokenAppService.cs
│       ├── JwtTokenAppService.cs
│       └── CpfProtectionAppService.cs    # HMAC-SHA256 + AES-256-GCM
├── Ambrosio.DTO/
│   ├── Auth/  GoogleLoginInfo, DevLoginInfo, LoginResult, OAuthUserInfo
│   ├── Users/ UserInfo
│   ├── Customers/ CustomerInfo, CustomerInsertInfo, CustomerUpdateInfo, CustomerDetailInfo, CustomerSummaryInfo, CustomerListQuery
│   ├── Orders/ OrderInfo, OrderInsertInfo, OrderUpdateInfo, OrderDetailInfo, OrderItemInfo, OrderItemInsertInfo, OrderListQuery
│   └── Common/ PagedResult<T>, ErrorResult, FieldError
└── Ambrosio.Tests/                       # xUnit + Moq, espelha as camadas
    ├── Domain/Services/   AuthServiceTest, CustomerServiceTest, OrderServiceTest
    ├── Domain/ValueObjects/ CpfTest
    ├── Domain/Validators/  OrderInsertInfoValidatorTest, CustomerInsertInfoValidatorTest
    ├── Infra/AppServices/  CpfProtectionAppServiceTest, JwtTokenAppServiceTest
    └── Infra/Repository/   OrderRepositoryTest (InMemory: filtros e isolamento)
```

**Structure Decision**: solução única em `backend/` com os 6 projetos da skill `dotnet-architecture` mais `Ambrosio.Tests`. A camada de apresentação (`Ambrosio.API`) só contém controllers, `Program.cs` e configuração; toda regra de negócio fica em `Ambrosio.Domain` e todo acesso a dados em `Ambrosio.Infra`. O identificador do usuário autenticado é extraído do claim `sub` no controller e passado explicitamente aos serviços (`long userId`), que o repassam aos repositórios; **nenhum método de repositório de clientes/pedidos existe sem o parâmetro `userId`**, o que torna o isolamento estrutural.

## Complexity Tracking

> Preenchido apenas para violações justificadas do Constitution Check.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `orders.status` e `orders.channel` como `varchar` em vez de `integer` (Princípio V: "Status/Enums → integer") | Ambos vêm do +Vendas como texto livre e não formam um conjunto fechado: a spec (Edge Cases) determina que canais fora da lista conhecida devem ser aceitos, e os status observados ("Faturado", "Cancelado") são apenas exemplos de uma lista que o sistema de origem pode ampliar. `kind` e `origin`, que **são** conjuntos fechados, ficam como `integer`. | Modelar como enum inteiro obrigaria a rejeitar (ou mapear para "Outro") valores novos do +Vendas, perdendo informação — o que contraria SC-006 ("cadastrados sem perda de informação"). Criar tabelas de domínio (`channels`, `statuses`) adicionaria 2 entidades e cadastro prévio sem ganho funcional nesta entrega. |
