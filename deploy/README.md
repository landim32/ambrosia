# Ambientes do Ambrosio

| | Dev | Homolog | Produção |
|---|---|---|---|
| Onde roda | Windows, direto (`dotnet run`) | Docker no WSL2 | Servidor remoto com Docker |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Homolog` | `Production` |
| appsettings | `appsettings.Development.json` (valores preenchidos) | `appsettings.Homolog.json` + `.env` | `appsettings.Production.json` + `.env.prod` |
| Banco | PostgreSQL instalado no Windows (`localhost:5432`) | Container `postgres:17-alpine` (`localhost:5433`) | DigitalOcean Managed PostgreSQL |
| Endereço | `http://localhost:5000/swagger` | `http://localhost:8080/api/swagger` | `https://ambrosiaai.site/api/swagger` |
| HTTPS | Não | Não | Cloudflare (Full strict) + Origin Certificate no nginx |
| Migrações | `dotnet ef database update` | Serviço `migrator` (bundle EF) no `up` | Job `migrate` do pipeline |
| Storage de imagens | Spaces real (`emagine`, pasta `ambrosio/`), chaves no ambiente | Spaces real (`emagine`, pasta `ambrosio/`), chaves do `.env` | Spaces (`ambrosia-prod`), chaves dos GitHub Secrets |
| `/auth/register` e `/auth/login` | Sim | Sim | Sim |
| Coletor do +Vendas (`MaisVendas__Collector`) | `Playwright` (Chromium local) | `Playwright` (`Fake` só para a suíte de API) | `Playwright` (obrigatório) |

Autenticação: `POST /auth/register` (cadastro com nome, e-mail e senha) e `POST /auth/login` (e-mail + senha) são rotas anônimas da API, disponíveis nos três ambientes, como o `POST /auth/google` — atrás do gateway elas ficam em `/api/auth/*`, e o `/auth/` sem prefixo continua reservado à HU 008. Não existe emissão de token sem credencial em ambiente nenhum: a antiga entrada de desenvolvimento foi removida pela spec [`003-email-password-jwt-auth`](../specs/003-email-password-jwt-auth/) e responde 404 em Development, Homolog e Production (nem aparece no Swagger).

Rotas do gateway (homolog e produção, `deploy/nginx/routes.inc`): `/` → frontend (HU 002), `/api/` → API (o prefixo é removido), `/auth/` → HU 008, `/mcp` → HU 007. Rotas de serviços que ainda não existem respondem 503.

## Dev (sem Docker)

1. Instale o PostgreSQL 17 no Windows, com usuário `postgres`/`postgres` na porta 5432, e crie o banco `ambrosio_dev`. O container `ambrosio-postgres` do WSL também usa a 5432: pare-o antes (`wsl -e docker stop ambrosio-postgres`).
2. Dê às credenciais do Spaces (o storage de Dev é o mesmo bucket da homologação — **não** versione essas chaves):

```powershell
$env:ObjectStorage__AccessKey = '<spaces access key>'
$env:ObjectStorage__SecretKey = '<spaces secret key>'
```

3. No diretório `backend/`:

```powershell
dotnet tool restore
dotnet ef database update --project Ambrosio.Infra --startup-project Ambrosio.API
dotnet run --project Ambrosio.API
```

Sem as duas chaves a API **não sobe**: a validação de startup recusa configuração incompleta do
storage (FR-020) e a mensagem diz qual variável falta, sem mostrar valor nenhum. Para rodar a suíte de
testes de API sem Docker, acrescente os prazos curtos de homolog (os testes de expiração e de limpeza
dormem o tempo configurado):

```powershell
$env:ObjectStorage__PresignedReadMinutes = '1'
$env:ObjectStorage__PresignedUploadMinutes = '1'
$env:ObjectStorage__PendingTtlMinutes = '2'
$env:ObjectStorage__CleanupIntervalMinutes = '1'
```

## Storage de imagens (feature 004)

Não há container de storage no projeto: os três ambientes usam **DigitalOcean Spaces**. As imagens
`minio/minio` e `minio/mc` saíram do Docker Hub em 2025 (o research da feature previa a checagem), e a
decisão registrada em `specs/004-s3-image-storage/decisions.md` foi usar o Spaces também em
Development e homologação. O que muda na prática:

| | Bucket | CORS (origens) | Chaves |
|---|---|---|---|
| Dev e Homolog | `ambrosio` (nyc3) | `http://localhost:8080`, `http://localhost:5173` | `.env` / ambiente do shell |
| Produção | `ambrosia-prod` (nyc3) | `https://ambrosiaai.site` | GitHub Secrets |

- Endpoint, região, bucket e `ForcePathStyle` ficam no `appsettings.<Ambiente>.json`; **só**
  `ObjectStorage__AccessKey`/`ObjectStorage__SecretKey` vêm de variável de ambiente. O nome do bucket no
  Spaces é global entre contas, então se `ambrosio`/`ambrosia-prod` já estiverem tomados, escolha outro e
  ajuste o appsettings junto com `OBJECT_STORAGE_PUBLIC_ORIGIN`.
- A regra CORS é **por bucket** (como pede a FR-022): `AllowedOrigin` com as origens acima,
  `AllowedMethod` `GET, PUT, HEAD`, `AllowedHeader` `Content-Type`. Sem ela o navegador não consegue o
  preflight do `PUT` pré-assinado. No painel do Spaces (Bucket → CORS) o corpo é este XML, uma regra por
  origem:

  ```xml
  <CORSConfiguration>
    <CORSRule>
      <AllowedOrigin>http://localhost:8080</AllowedOrigin>
      <AllowedMethod>GET</AllowedMethod>
      <AllowedMethod>PUT</AllowedMethod>
      <AllowedMethod>HEAD</AllowedMethod>
      <AllowedHeader>Content-Type</AllowedHeader>
      <ExposeHeader>Content-Length</ExposeHeader>
      <MaxAgeSeconds>3000</MaxAgeSeconds>
    </CORSRule>
  </CORSConfiguration>
  ```

  Em produção, troque a origem por `https://ambrosiaai.site`. Com AWS CLI configurado para o Spaces
  (`aws configure` + `AWS_ENDPOINT_URL_S3=https://nyc3.digitaloceanspaces.com`) dá para aplicar o mesmo
  arquivo com `aws s3api put-bucket-cors --bucket <bucket> --cors-cors-file file://cors.xml`.

  Conferir de pé, sem credencial nenhuma:

  ```bash
  curl -s -o /dev/null -w '%{http_code}\n' \
    https://emagine.nyc3.digitaloceanspaces.com/ambrosio/users/0/qualquer-coisa.jpg   # 403 (bucket privado)

  curl -s -i -X OPTIONS \
    "https://emagine.nyc3.digitaloceanspaces.com/ambrosio/users/0/qualquer-coisa.jpg" \
    -H 'Origin: http://localhost:8080' -H 'Access-Control-Request-Method: PUT' |
    grep -i access-control-allow            # a origem autorizada e o método PUT
  ```
- O bucket tem de ficar **privado** (bloqueio de acesso público ativado). Não há ACL pública em nenhuma
  operação da API: o acesso do navegador acontece só por URL pré-assinada (FR-004).
- O container do frontend coloca a origem do storage na CSP a partir de `STORAGE_ORIGIN`
  (`frontend/nginx/default.conf.template` é um template com envsubst), que o compose preenche com
  `OBJECT_STORAGE_PUBLIC_ORIGIN`. Sem a variável, o default do Dockerfile é o endpoint do Spaces.


## Importação do +Vendas (feature 006)

A coleta roda **dentro da API**, num Chromium headless controlado pelo `Microsoft.Playwright` (versão
fixa no `Ambrosio.Infra.csproj`). O `backend/Dockerfile` baixa o navegador dessa mesma versão num
estágio próprio (`playwright`) e a imagem final traz as bibliotecas de sistema do Chromium e o
diretório `/ms-playwright`. Login e senha do +Vendas **não** são configuração: a vendedora os informa a
cada execução, e eles vivem só em memória durante a coleta (FR-005/FR-006).

| Configuração | Homolog | Produção |
|---|---|---|
| `MaisVendas__Collector` | `Playwright` (padrão do `docker-compose.yml`; `Fake` com `MAIS_VENDAS_COLLECTOR=Fake` para a suíte de API) | `Playwright` (padrão do `docker-compose-prod.yml`) |
| `shm_size` do serviço `api` | `512m` | `512m` — com os 64 MB padrão de `/dev/shm` o Chromium aborta no meio da navegação |
| `mem_limit` recomendado do `api` | — | `1536m` (API + um Chromium por execução, `MaxConcurrentRuns = 2`) |

- **`Fake` é recusado em Production**: a API não sobe com `MaisVendas__Collector=Fake` e
  `ASPNETCORE_ENVIRONMENT=Production` (`Program.ValidateSettings`). Um fake "importando com sucesso"
  gravaria pedidos inventados na base da vendedora; o deploy falha cedo e visível em vez disso.
- **Uma réplica da API**: a fila de importação é em memória (`Channel<ImportJob>`) porque uma fila
  externa persistiria os argumentos do job — e eles contêm a senha. Escalar a API horizontalmente
  exigiria rever esse desenho (plan.md, Complexity Tracking).
- **Reinício no meio de uma execução**: no arranque, execuções `queued`/`running` órfãs viram
  `failed` com "Execução interrompida pela reinicialização do servidor." (FR-020); o que foi gravado
  fica, e a vendedora repete o período sem duplicar nada.
- Em Development, sem Docker, instale o navegador uma vez depois do build:
  `pwsh backend/Ambrosio.API/bin/Debug/net8.0/playwright.ps1 install chromium`.

### Checklist de liberação em produção

1. Confirmar com a Somalabs/termos de uso do +Vendas que a leitura automatizada da própria conta da
   vendedora é permitida.
2. Primeira execução real em **simulação** ("Simular (não gravar)") com um período curto; conferir no
   relatório que as contagens batem com as abas do +Vendas e, nos logs da API, que só rotas da
   allowlist (`maisvendas.somalabs.com.br`, `api.mais-vendas.somalabs.com.br`) foram visitadas.
3. Ajustar seletores, se necessário, só no `PlaywrightMaisVendasCollectorAppService` — o Domain não
   muda (research §10).
4. Só então a primeira execução em gravação, de um período curto, conferida em `/pedidos`.

## Homolog (docker compose no WSL)

```powershell
Copy-Item .env.example .env    # preencha os segredos (CPF_ENCRYPTION_KEY: openssl rand -base64 32)
wsl --cd /mnt/c/repos/ambrosia -e docker compose up -d --build
wsl --cd /mnt/c/repos/ambrosia -e docker compose ps
wsl --cd /mnt/c/repos/ambrosia -e docker compose logs --tail=100 api
wsl --cd /mnt/c/repos/ambrosia -e docker compose down          # mantém o volume do banco
```

Com o frontend (quando `frontend/Dockerfile` existir): `docker compose --profile frontend up -d --build`.

Testes HTTP contra a homologação (o gateway publica a API em `/api`):

```powershell
$env:ApiBaseUrl = 'http://localhost:8080/api'
dotnet test backend/Ambrosio.ApiTests
```

Atrás do gateway, a API lê `X-Forwarded-Prefix`/`Proto`/`For` (`ReverseProxy:Enabled` em `appsettings.Homolog.json` e `appsettings.Production.json`) e o Swagger declara `servers: [/api]`. As chaves do Data Protection ficam no volume `dataprotection_keys`.

## Produção (GitHub Actions)

Workflow `.github/workflows/deploy-prod.yml`, disparado manualmente (Actions → Deploy Production → Run workflow). Conecta por SSH em `/opt/ambrosia`, grava `.env.prod` e o certificado, faz o build, aplica as migrações, sobe os serviços e testa `https://ambrosiaai.site/api/swagger/v1/swagger.json`.

### Preparação única

1. **Servidor** (ex.: Droplet DigitalOcean): Docker Engine + plugin compose, `git`, `curl`, `openssl`; portas 80 e 443 abertas. Liberar o IP do servidor em *Trusted sources* do banco gerenciado.
2. **Cloudflare**: registro `A ambrosiaai.site` → IP do servidor com proxy ativo; SSL/TLS em **Full (strict)**; gerar um **Origin Certificate** para `ambrosiaai.site`.
3. **Banco**: criar o banco `ambrosio` no cluster gerenciado e montar a connection string com `SSL Mode=Require` (porta 25060).
4. **GitHub Secrets** (Settings → Secrets and variables → Actions):

| Secret | Conteúdo |
|---|---|
| `PROD_SSH_HOST`, `PROD_SSH_USER`, `PROD_SSH_PASSWORD`, `PROD_SSH_PORT` | Acesso SSH ao servidor |
| `DB_CONNECTION_STRING` | Connection string do PostgreSQL gerenciado |
| `AUTH_JWT_SECRET` | Segredo do JWT (64+ caracteres aleatórios) |
| `AUTH_GOOGLE_CLIENT_ID` | Client ID do Google OAuth |
| `CPF_PEPPER` | Pepper do HMAC do CPF |
| `CPF_ENCRYPTION_KEY` | Base64 de 32 bytes. **Nunca troque** depois do primeiro cadastro: CPFs gravados ficariam ilegíveis |
| `OBJECT_STORAGE_ACCESS_KEY`, `OBJECT_STORAGE_SECRET_KEY` | Par de chaves do Spaces (feature 004). A API não sobe sem as duas (FR-020) |
| `OBJECT_STORAGE_PUBLIC_ORIGIN` | `https://<bucket>.nyc3.digitaloceanspaces.com` — vai para a CSP do frontend |
| `CLOUDFLARE_ORIGIN_CERT`, `CLOUDFLARE_ORIGIN_KEY` | PEM do Origin Certificate e da chave privada |

Guarde `CPF_ENCRYPTION_KEY` e `CPF_PEPPER` também fora do GitHub (cofre de senhas): o GitHub não permite ler um secret depois de gravado.

5. **Spaces**: crie o bucket de produção (nome global entre contas), ative o bloqueio de acesso público
   e grave a regra CORS com `AllowedOrigin = https://ambrosiaai.site`, `AllowedMethod = GET, PUT, HEAD` e
   `AllowedHeader = Content-Type`. O endpoint e o bucket ficam fixos em `appsettings.Production.json`
   (`https://nyc3.digitaloceanspaces.com`, `ambrosia-prod`, `ForcePathStyle: false`); a região da
   assinatura é a mesma do endpoint, senão o SigV4 da pré-assinatura não bate com o que o Spaces espera.

