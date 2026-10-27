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
| `ASPNETCORE_ENVIRONMENT` | `Development` abre o CORS; `Homolog` e `Production` não abrem. A autenticação é a mesma nos três: cadastro, login com senha ou Google — não há atalho sem credencial em ambiente nenhum (constituição §VI) |
| `ConnectionStrings__AmbrosiaContext` | Connection string do PostgreSQL |
| `Auth__JwtSecret` | Segredo HS256 do JWT (mínimo 32 bytes UTF-8) |
| `Auth__GoogleClientId` | Client ID usado como audiência do ID token Google |
| `Auth__ExpirationMinutes` | Duração do token (padrão 480) |
| `CpfProtection__Pepper` | Pepper do HMAC do CPF (obrigatório) |
| `CpfProtection__EncryptionKey` | Chave AES: base64 de exatamente 32 bytes |
| `ObjectStorage__AccessKey` / `ObjectStorage__SecretKey` | Par de chaves do storage de imagens (obrigatório em **todos** os ambientes, inclusive Development — é credencial de conta, nunca valor de appsettings) |
| `ObjectStorage__ServiceUrl`, `ObjectStorage__Region`, `ObjectStorage__Bucket`, `ObjectStorage__ForcePathStyle` | Onde ficam as imagens; os valores não-secretos estão no `appsettings.<Ambiente>.json` e estas variáveis só servem para apontar para outro storage |
| `ObjectStorage__PresignedReadMinutes` (1-60), `ObjectStorage__PresignedUploadMinutes` (1-15) | Validade das URLs pré-assinadas |
| `ObjectStorage__MaxUploadBytes` (>0), `ObjectStorage__AllowedMimeTypes` | Limite de tamanho e tipos aceitos |
| `ObjectStorage__PendingTtlMinutes` (1-1440), `ObjectStorage__CleanupIntervalMinutes` (1-60) | Prazo do job que remove envio abandonado (FR-018) |

A API **recusa subir** com configuração incompleta — inclusive sem as chaves do storage (FR-020): a
mensagem diz qual variável falta e nunca mostra valor nenhum. Nunca versione segredos reais; em
produção use chaves aleatórias.

### Storage de imagens (feature 004)

As imagens vão para um storage compatível com S3, privado, e o binário **não passa pela API** no
caminho do cliente: `POST /images/uploads` devolve uma URL pré-assinada de `PUT`, o navegador sobe
direto no bucket, e `POST /images/uploads/{id}/confirm` confere tamanho, tipo pelo conteúdo e SHA-256
antes de aprovar. Os três ambientes usam DigitalOcean Spaces (não há container de storage no compose —
ver [`deploy/README.md`](../deploy/README.md)).

Em Development, exporte as chaves antes de rodar:

```powershell
$env:ObjectStorage__AccessKey = '<spaces access key>'
$env:ObjectStorage__SecretKey = '<spaces secret key>'
```

O `AWSSDK.S3` e o `SixLabors.ImageSharp` ficam restritos ao `Ambrosia.Infra`, atrás de
`IObjectStorageAppService` e `IImageProcessingAppService`, e o primeiro `BackgroundService` do projeto
(`PendingImageCleanupService`, na `Ambrosia.Application`) roda a limpeza. O ImageSharp 3.2.0 exige
licença da Six Labors no build **Release** (em `Debug` é só aviso): veja a seção de licença em
[`deploy/README.md`](../deploy/README.md).

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

1. **Cadastro** — `POST /auth/register { name, email, password }` cria a conta (a senha fica
   apenas como hash PBKDF2 em `users.password_hash`) e já devolve a sessão: `201` com o JWT.
2. **Entrada** — `POST /auth/login { email, password }` devolve o mesmo JWT. Credencial recusada
   responde `401` com a mensagem única "E-mail ou senha inválidos", sem revelar se o e-mail existe.
3. **Google** — o cliente obtém um ID token e chama `POST /auth/google`; reutilizando uma conta
   que tinha senha, a senha é removida neste login (FR-009).
4. A API devolve um JWT do Ambrosia (`sub` = `user_id`) usado como `Authorization: Bearer <token>`
   em todas as demais rotas.

Não existe emissão de token sem credencial: a antiga entrada de desenvolvimento foi removida de
todos os ambientes (003, FR-012).

Recursos de outro usuário respondem **404**, igual a um id inexistente.
