# Ambientes do Ambrosia

| | Dev | Homolog | Produção |
|---|---|---|---|
| Onde roda | Windows, direto (`dotnet run`) | Docker no WSL2 | Servidor remoto com Docker |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Homolog` | `Production` |
| appsettings | `appsettings.Development.json` (valores preenchidos) | `appsettings.Homolog.json` + `.env` | `appsettings.Production.json` + `.env.prod` |
| Banco | PostgreSQL instalado no Windows (`localhost:5432`) | Container `postgres:17-alpine` (`localhost:5433`) | DigitalOcean Managed PostgreSQL |
| Endereço | `http://localhost:5000/swagger` | `http://localhost:8080/api/swagger` | `https://ambrosiaai.site/api/swagger` |
| HTTPS | Não | Não | Cloudflare (Full strict) + Origin Certificate no nginx |
| Migrações | `dotnet ef database update` | Serviço `migrator` (bundle EF) no `up` | Job `migrate` do pipeline |
| `/auth/dev-login` | Sim | Sim | Não |

Rotas do gateway (homolog e produção, `deploy/nginx/routes.inc`): `/` → frontend (HU 002), `/api/` → API (o prefixo é removido), `/auth/` → HU 007, `/mcp` → HU 006. Rotas de serviços que ainda não existem respondem 503.

## Dev (sem Docker)

1. Instale o PostgreSQL 17 no Windows, com usuário `postgres`/`postgres` na porta 5432, e crie o banco `ambrosia_dev`. O container `ambrosia-postgres` do WSL também usa a 5432: pare-o antes (`wsl -e docker stop ambrosia-postgres`).
2. No diretório `backend/`:

```powershell
dotnet tool restore
dotnet ef database update --project Ambrosia.Infra --startup-project Ambrosia.API
dotnet run --project Ambrosia.API
```

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
dotnet test backend/Ambrosia.ApiTests
```

Atrás do gateway, a API lê `X-Forwarded-Prefix`/`Proto`/`For` (`ReverseProxy:Enabled` em `appsettings.Homolog.json` e `appsettings.Production.json`) e o Swagger declara `servers: [/api]`. As chaves do Data Protection ficam no volume `dataprotection_keys`.

## Produção (GitHub Actions)

Workflow `.github/workflows/deploy-prod.yml`, disparado manualmente (Actions → Deploy Production → Run workflow). Conecta por SSH em `/opt/ambrosia`, grava `.env.prod` e o certificado, faz o build, aplica as migrações, sobe os serviços e testa `https://ambrosiaai.site/api/swagger/v1/swagger.json`.

### Preparação única

1. **Servidor** (ex.: Droplet DigitalOcean): Docker Engine + plugin compose, `git`, `curl`, `openssl`; portas 80 e 443 abertas. Liberar o IP do servidor em *Trusted sources* do banco gerenciado.
2. **Cloudflare**: registro `A ambrosiaai.site` → IP do servidor com proxy ativo; SSL/TLS em **Full (strict)**; gerar um **Origin Certificate** para `ambrosiaai.site`.
3. **Banco**: criar o banco `ambrosia` no cluster gerenciado e montar a connection string com `SSL Mode=Require` (porta 25060).
4. **GitHub Secrets** (Settings → Secrets and variables → Actions):

| Secret | Conteúdo |
|---|---|
| `PROD_SSH_HOST`, `PROD_SSH_USER`, `PROD_SSH_PASSWORD`, `PROD_SSH_PORT` | Acesso SSH ao servidor |
| `DB_CONNECTION_STRING` | Connection string do PostgreSQL gerenciado |
| `AUTH_JWT_SECRET` | Segredo do JWT (64+ caracteres aleatórios) |
| `AUTH_GOOGLE_CLIENT_ID` | Client ID do Google OAuth |
| `CPF_PEPPER` | Pepper do HMAC do CPF |
| `CPF_ENCRYPTION_KEY` | Base64 de 32 bytes. **Nunca troque** depois do primeiro cadastro: CPFs gravados ficariam ilegíveis |
| `CLOUDFLARE_ORIGIN_CERT`, `CLOUDFLARE_ORIGIN_KEY` | PEM do Origin Certificate e da chave privada |

Guarde `CPF_ENCRYPTION_KEY` e `CPF_PEPPER` também fora do GitHub (cofre de senhas): o GitHub não permite ler um secret depois de gravado.
