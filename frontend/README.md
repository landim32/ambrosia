# Ambrosia — Frontend (HU 002)

SPA **somente leitura** de consulta de clientes e pedidos da vendedora. Consome a
API REST da HU 001 na mesma origem (`/api`), guarda a sessão em `localStorage` e é
publicado como container nginx no profile `frontend` dos dois `docker-compose`.

Spec e plano: [`specs/002-frontend-web-clientes-pedidos/`](../specs/002-frontend-web-clientes-pedidos/).

## Pré-requisitos

| Ferramenta | Versão | Verificação |
|---|---|---|
| Node.js | 22 LTS (`>=22` em `engines`) | `node --version` → `v22.x` |
| npm | 10+ | `npm --version` |
| API da HU 001 | rodando em Development | `http://localhost:5000` |

```powershell
cd frontend
npm ci
```

## Comandos

| Comando | O que faz |
|---|---|
| `npm run dev` | Vite em `http://localhost:5173` com proxy `/api` → `http://localhost:5000` |
| `npm run lint` | ESLint (flat config; nenhum `any`, sempre `interface`) |
| `npm run type-check` | `tsc -b --noEmit` |
| `npm test` | Vitest + RTL + jsdom (19 arquivos, 176 testes) |
| `npm run test:watch` | Vitest em modo watch |
| `npm run build` | `tsc -b && vite build` → `dist/` |
| `npm run build -- --mode homolog` | Build com dev-login (`.env.homolog`) |
| `npm run preview` | Serve `dist/` localmente |
| `npm run verify:prod` | **Prova da SC-008**: falha se `dev-login`/`DevLoginForm` existir em `dist/`. Rode depois do build de produção |

Fluxo completo de verificação:

```powershell
npm run lint; npm run type-check; npm test
npm run build -- --mode production; npm run verify:prod
```

## Variáveis de ambiente

Todas com prefixo `VITE_` (constituição §Variáveis de Ambiente) e lidas por
`import.meta.env`. O Vite escolhe o arquivo pelo `--mode` do build.

| Variável | `.env.development` | `.env.homolog` | `.env.production` | Para que serve |
|---|---|---|---|---|
| `VITE_API_URL` | `/api` | `/api` | `/api` | Base das chamadas; sempre relativo à origem, então não há CORS |
| `VITE_DEV_LOGIN` | `true` | `true` | **ausente** | Mostra o formulário de entrada de desenvolvimento. Em produção a flag some do bundle (FR-002/SC-008) |
| `VITE_GOOGLE_CLIENT_ID` | vazio | build arg | build arg | Client ID do Google Identity Services. Vazio = o botão "Entrar com Google" fica desabilitado |
| `VITE_SITE_BASENAME` | vazio | vazio | vazio | `basename` do React Router, se o app sair da raiz da origem |

`.env.local` (gitignored) sobrepõe qualquer arquivo acima para teste local.

## Login com Google

O `VITE_GOOGLE_CLIENT_ID` é **público por natureza** (não é segredo): quem valida o
ID token é a API (`Auth__GoogleClientId`). No Google Cloud Console →
*Credentials → OAuth 2.0 Client ID → Web application*, adicione as origens
JavaScript autorizadas exatamente onde o app roda:

```text
http://localhost:5173     # npm run dev
http://localhost:8080     # homologação local (gateway nginx)
https://ambrosiaai.site   # produção
```

O mesmo Client ID vai para a API em `Auth__GoogleClientId` — valores diferentes
fazem `POST /auth/google` devolver 400/401.

## Docker (Windows → WSL2)

Não há Docker Desktop nesta máquina; o Engine fica no WSL2 (skill `wsl-docker`).
O `frontend/Dockerfile` é multi-stage (`node:22-alpine` → `nginx:1.27-alpine`) e
recebe `BUILD_MODE`, `VITE_DEV_LOGIN` e `VITE_GOOGLE_CLIENT_ID` como `build.args`
dos compose.

```powershell
# Homologação local: frontend + API + Postgres atrás do gateway
wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build

# Só a imagem do frontend
wsl --cd /mnt/c/repos/ambrosia/frontend -e docker build --build-arg BUILD_MODE=homolog --build-arg VITE_DEV_LOGIN=true -t ambrosia-frontend:homolog .
```

Depois: `http://localhost:8080/` abre o app com dev-login e
`http://localhost:8080/api/swagger` continua respondendo — o gateway
(`deploy/nginx/routes.inc`) roteia `/` → `frontend:80` e `/api/` → `api:8080`.

`nginx.conf` traz a CSP do Google Identity Services, `Referrer-Policy no-referrer`
e cache de 1 ano para `/assets/` com `index.html` sempre revalidado.

## Estrutura

```text
src/
├── types/        CustomerInfo, OrderInfo, AuthSession, PagedResult
├── Services/     apiClient (fetch + erros tipados) + um serviço por entidade
├── Contexts/     AuthContext, CustomerContext, OrderContext (loading/error/clearError)
├── hooks/        useAuth, useCustomer, useOrder, useDebouncedValue, useOrderFilters
├── lib/          funções puras testadas à parte: dates, currency, cpf, filters…
├── i18n/         pt-BR — toda string visível vem de locales/pt-BR.ts
├── components/   ui/ (shadcn, gerado) · layout/ · common/ · customers/ · orders/
└── pages/        uma pasta por tela; as rotas ficam em App.tsx
```

Regras que o ESLint não consegue aplicar sozinhas (constituição §III/§IV):
`Contexts/` e `Services/` com **C** e **S** maiúsculos — o build roda em Linux no
Docker e caixa importa. Páginas são finas: formatação (moeda, data, CPF, tipo)
mora em `lib/`, teste em `lib/*.test.ts`.

## Segurança e limites conhecidos

- **Somente leitura** (FR-030): nenhum `POST`/`PUT`/`DELETE` fora de `/auth/*`.
- O **CPF completo** só existe em memória, no perfil, e é descartado ao sair da
  tela (FR-022). Nunca vai para `localStorage`, log ou listagem.
- Sessão na chave `login-with-metamask:auth` da constituição; expira por timer,
  por checagem antes de cada chamada e por 401 da API.
- `VITE_SITE_BASENAME` vazio não é ignorado: `main.tsx` aplica o `basename` do
  Router a partir dele (T029/T039).
