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

### Importação do +Vendas (feature 006)

Rotas `/imports/mais-vendas/*` (defaults, iniciar, execução atual, relatório, histórico, cancelar). A
vendedora informa login e senha do +Vendas **a cada execução**; `POST /runs` grava a execução sem
credencial, enfileira o trabalho numa fila em memória e responde `202`, e o `ImportRunWorker`
(`BackgroundService`) coleta bloco a bloco, valida contadores e totais da origem **antes** de gravar e
grava pedido a pedido de forma idempotente (chave natural + `source_hash`).

Seção `MaisVendas` do appsettings (variáveis `MaisVendas__*`):

| Chave | Padrão | Faixa |
|---|---|---|
| `Collector` | `Playwright` | `Playwright` ou `Fake` (Fake recusado em Production) |
| `BaseUrl` | `https://maisvendas.somalabs.com.br` | URL https absoluta |
| `BlockDays` | `7` | 1–31 |
| `MaxRangeDays` | `366` | 1–3660 |
| `MaxConcurrentRuns` | `2` | 1–10 |
| `MaxFailedLogins` / `LockoutMinutes` | `3` / `15` | recusas seguidas antes do bloqueio (FR-008a) |
| `MinDelayMs` / `MaxDelayMs` | `300` / `900` | pausa aleatória entre ações na origem |
| `Fake:DelayMs` | `50` | atraso por pedido do coletor fake |

**Coletor Fake (homologação)** — origem de teste determinística, sem rede, usada pela suíte
`Ambrosia.ApiTests`. O login escolhe o cenário (`conta+cenario`, a conta vira o vínculo) e a senha é a
versão do conteúdo:

| Login | Senha | Resultado |
|---|---|---|
| `s482+ok` | `v1` | 49 pedidos entre 26/09 e 25/10/2026, líquido R$ 123.300,58 |
| `s482+ok` | `v2` / `v3` | valores/status/nome alterados / um pedido do bloco 2 omitido (`missing`) |
| qualquer | `recusada` | credencial recusada (3× → bloqueio de 15 min, `429`) |
| `s999+ok` | `v1` | conta diferente da vinculada → `failed` |
| `s482+falha-bloco3` | `v1` | bloco 3 falha no 15º pedido; blocos 4–5 não executados |
| `s482+contador-divergente` / `total-divergente` | `v1` | bloco falha antes de gravar |
| `s482+foto-erro` / `foto-gif` | `v1` | pedido gravado, foto com erro, execução `partial` |
| `s482+cpf-invalido` | `v1` | dois pedidos com erro de CPF, execução `partial` |
| `s482+sem-ano`, `instavel`, `vazio`, `2fa` | `v1` | data sem ano, falha transitória, período vazio, etapa não suportada |

Nenhum desses valores é credencial real; nunca versione login ou senha do +Vendas.

**Coletor Playwright (Development/produção)** — depois do primeiro build, instale o Chromium da
versão fixa do pacote:

```powershell
pwsh Ambrosia.API/bin/Debug/net8.0/playwright.ps1 install chromium
```

Na imagem Docker isso é feito pelo estágio `playwright` do `Dockerfile`. A navegação é restrita por
allowlist (`NavigationGuard`) e ritmo (`OriginRateLimiter`, ≤ 2 req/s). Detalhes de deploy em
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
