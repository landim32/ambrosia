# Ambrosia — Backend

API REST (.NET 8 + PostgreSQL) para cadastro e consulta de **clientes** e **pedidos** de uma vendedora autenticada. Cada usuário enxerga apenas os próprios dados. O CPF nunca é gravado em claro (hash HMAC + cifra AES-256-GCM + máscara).

Guia de validação ponta a ponta: [quickstart.md](../specs/001-api-cadastro-clientes-pedidos/quickstart.md). Contrato: [openapi.yaml](../specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml).

## Estrutura

| Projeto | Papel |
|---|---|
| `Ambrosia.API` | Controllers, Program.cs, Swagger, autenticação JWT |
| `Ambrosia.Application` | Composição de DI (`Startup.cs`) |
| `Ambrosia.Domain` | Modelos, serviços, validators, value objects |
| `Ambrosia.DTO` | Contratos de entrada/saída, enums, settings |
| `Ambrosia.Infra` / `Ambrosia.Infra.Interfaces` | EF Core, repositórios, AppServices (JWT, Google, CPF) |
| `Ambrosia.Tests` | Testes unitários (xUnit + Moq + EF InMemory) |
| `Ambrosia.ApiTests` | Testes HTTP (xUnit + Flurl + FluentAssertions) |

## Pré-requisitos

- .NET SDK 8.0
- PostgreSQL 16+ instalado no Windows, com um banco `ambrosia_dev` em `localhost:5432` (dev roda sem Docker; homolog e produção estão em [`deploy/README.md`](../deploy/README.md))
- Ferramenta EF: `dotnet tool install --global dotnet-ef --version 9.*`

## Variáveis de ambiente

Sobrescrevem `appsettings*.json` (separador `__`). Em Development, `appsettings.Development.json` já traz valores de desenvolvimento.

| Variável | Descrição |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` habilita `/auth/dev-login` e CORS aberto; `Homolog` habilita só o `/auth/dev-login`; `Production` desliga os dois |
| `ConnectionStrings__AmbrosiaContext` | Connection string do PostgreSQL |
| `Auth__JwtSecret` | Segredo HS256 do JWT (mínimo 32 bytes UTF-8) |
| `Auth__GoogleClientId` | Client ID usado como audiência do ID token Google |
| `Auth__ExpirationMinutes` | Duração do token (padrão 480) |
| `CpfProtection__Pepper` | Pepper do HMAC do CPF (obrigatório) |
| `CpfProtection__EncryptionKey` | Chave AES: base64 de exatamente 32 bytes |

A API **recusa subir** com configuração incompleta. Nunca versione segredos reais; em produção use chaves aleatórias.

## Comandos

```powershell
cd backend

# Banco: aplicar migrações
dotnet ef database update --project Ambrosia.Infra --startup-project Ambrosia.API

# Executar (http://localhost:5000, Swagger em /swagger)
dotnet run --project Ambrosia.API
# ou em background, aguardando o Swagger responder:
powershell -File scripts/run-api.ps1
powershell -File scripts/stop-api.ps1

# Testes unitários
dotnet test Ambrosia.Tests

# Testes HTTP (exigem a API no ar em Development)
$env:ApiBaseUrl = 'http://localhost:5000'
dotnet test Ambrosia.ApiTests
```

Pare a API antes de `dotnet build` (`stop-api.ps1`): o host em execução trava as DLLs.

## Scripts

| Script | Uso |
|---|---|
| `scripts/run-api.ps1` / `stop-api.ps1` | Sobe/derruba a API (`-EnvironmentName`, `-BaseUrl`) |
| `scripts/ensure-postgres.ps1` | Garante o PostgreSQL do WSL/Docker acessível (`-Release` libera o keep-alive) |
| `scripts/mark-tasks.ps1` | Marca tarefas concluídas no `tasks.md` preservando BOM |
| `scripts/seed-load-test.sql` | Carga de ~2.000 clientes e ~10.000 pedidos (SC-003): `psql -v user_id=<id> -f ...` |

## Autenticação

1. O cliente obtém um ID token do Google e chama `POST /auth/google`.
2. A API devolve um JWT do Ambrosia (`sub` = `user_id`) usado como `Authorization: Bearer <token>`.
3. Em Development, `POST /auth/dev-login { email, name }` emite o token sem Google.

Recursos de outro usuário respondem **404**, igual a um id inexistente.
