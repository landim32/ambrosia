---

description: "Lista de tarefas da feature 002 — Frontend web de consulta de clientes e pedidos"
---

# Tasks: Frontend web de consulta de clientes e pedidos

**Input**: Documentos de design em `specs/002-frontend-web-clientes-pedidos/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-routes.md, contracts/api-consumption.md, quickstart.md

**Tests**: INCLUÍDOS. O plano (Technical Context → Testing) e o research §1 fixam, por decisão do solicitante, Vitest + RTL para `lib/`, `Services/`, `Contexts/` e componentes, Playwright (matriz FR-031, 360 px, 3 fusos, axe-core) para E2E e xUnit para o novo caso de `OrderRepositoryTest`. Em cada história, os testes são escritos ANTES da implementação e devem FALHAR primeiro.

**Organization**: tarefas agrupadas por história de usuário (US1–US5 da spec) para implementação e teste independentes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história a que a tarefa pertence (US1…US5)
- Todos os caminhos são relativos à raiz do repositório (`C:\repos\ambrosia`)

## Regras transversais (valem para TODAS as tarefas)

- **Casing (constituição §III, inviolável)**: `frontend/src/Contexts/`, `frontend/src/Services/` (maiúsculas), `frontend/src/hooks/`, `frontend/src/types/` (minúsculas). Imports com casing idêntico ao disco.
- **Convenções (§IV)**: componentes PascalCase; funções/variáveis camelCase em arrow functions; constantes UPPER_CASE; `interface` sempre (nenhum `type`; as unions de string `kind`/`origin` ficam como propriedades de `OrderInfo` e são referenciadas por `OrderInfo['kind']`/`OrderInfo['origin']`); nenhum `any`; propriedades camelCase acessadas diretamente do JSON.
- **Stack (§II)**: React 18, TS 5, React Router 6, Vite 6, Tailwind 4 + shadcn/ui, i18next 25, Fetch API. PROIBIDO: Axios, Redux/Zustand, Bootstrap/MUI, TanStack Query, `REACT_APP_`.
- **Skill `react-architecture`**: types → Service (classe singleton com `handleResponse` privado) → Context (`loading`, `error`, `clearError`, métodos `useCallback`) → hook com null-check → provider em `main.tsx`. Exceção formal à regra 7 da skill (§I), registrada no Complexity Tracking do plano e a ser justificada no PR: serviços NÃO checam `result.sucesso` em 200; erros chegam como classes do `apiClient`.
- **Somente leitura (FR-030)**: nenhuma tela, botão ou serviço de criar/editar/excluir.
- **Textos**: toda string visível vem de `frontend/src/i18n/locales/pt-BR.ts` (FR-024).
- **Docker**: qualquer comando `docker`/`docker compose` via skill `wsl-docker` (`wsl --cd /mnt/c/repos/ambrosia -e docker ...`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: criar o projeto Vite em `frontend/` com toolchain, estilo, testes e variáveis de ambiente.

- [ ] T001 Criar o projeto Vite React + TypeScript em `frontend/` (`npm create vite@latest frontend -- --template react-ts`), remover os arquivos de exemplo (`App.css`, `assets/react.svg`) e criar a árvore de pastas do plano: `frontend/src/{types,Services,Contexts,hooks,lib,i18n/locales,components/{ui,layout,common,customers,orders},pages,test}` e `frontend/e2e/`
- [ ] T002 Fixar dependências em `frontend/package.json`: runtime `react@^18`, `react-dom@^18`, `react-router-dom@^6`, `i18next@^25`, `react-i18next`, `sonner`, `clsx`, `tailwind-merge`, `class-variance-authority`, `lucide-react`; dev `vite@^6`, `@vitejs/plugin-react`, `typescript@^5`, `tailwindcss@^4`, `@tailwindcss/vite`, `@types/google.accounts`, `vitest`, `@testing-library/react`, `@testing-library/jest-dom`, `@testing-library/user-event`, `jsdom`, `@playwright/test`, `@axe-core/playwright`, `eslint` + `typescript-eslint` + `eslint-plugin-react-hooks`; scripts `dev`, `build` (`tsc -b && vite build`), `preview`, `lint`, `type-check` (`tsc -b --noEmit`), `test` (`vitest run`), `test:e2e` (`playwright test`); `"engines": { "node": ">=22" }`
- [ ] T003 [P] Configurar `frontend/tsconfig.json`, `frontend/tsconfig.app.json` e `frontend/tsconfig.node.json` com `strict: true`, `noUncheckedIndexedAccess: true`, `forceConsistentCasingInFileNames: true`, alias `@/*` → `src/*` e `types: ["vite/client", "google.accounts"]`
- [ ] T004 [P] Configurar `frontend/vite.config.ts` com `@vitejs/plugin-react`, `@tailwindcss/vite`, alias `@` → `src`, `server.port: 5173` e `server.proxy['/api'] = { target: 'http://localhost:5000', changeOrigin: true, rewrite: (p) => p.replace(/^\/api/, '') }` (research §7)
- [ ] T005 [P] Configurar `frontend/vitest.config.ts` (mesclando `vite.config.ts`, `environment: 'jsdom'`, `setupFiles: ['src/test/setup.ts']`, `include: ['src/**/*.test.{ts,tsx}']`) e criar `frontend/src/test/setup.ts` importando `@testing-library/jest-dom/vitest` e `@/i18n`, e limpando `localStorage` em `afterEach`
- [ ] T006 [P] Configurar `frontend/playwright.config.ts` com `testDir: 'e2e'`, `baseURL: 'http://localhost:5173'`, `webServer: { command: 'npm run dev', url: 'http://localhost:5173', reuseExistingServer: true }`, `globalSetup: './e2e/seed.ts'` e os projetos `chromium`, `firefox`, `webkit`, `msedge` (`channel: 'msedge'`), `Mobile Chrome` (`devices['Pixel 7']`) e `Mobile Safari` (`devices['iPhone 14']`) (FR-031)
- [ ] T007 [P] Criar `frontend/src/index.css` com `@import "tailwindcss";` e os tokens de tema do shadcn/ui (cores `--background`, `--foreground`, `--primary`, `--destructive` etc. com contraste AA), e `frontend/components.json` via `npx shadcn@latest init` (estilo padrão, `aliases.components: '@/components'`, `aliases.utils: '@/lib/utils'`), gerando `frontend/src/lib/utils.ts` com `cn()`
- [ ] T008 Adicionar componentes shadcn/ui em `frontend/src/components/ui/`: `npx shadcn@latest add button card input label select badge avatar skeleton separator sonner` (depende de T007)
- [ ] T009 [P] Criar arquivos de ambiente: `frontend/.env.example` (documentando `VITE_API_URL=/api`, `VITE_SITE_BASENAME=`, `VITE_GOOGLE_CLIENT_ID=`, `VITE_DEV_LOGIN=`), `frontend/.env.development` (`VITE_API_URL=/api`, `VITE_DEV_LOGIN=true`), `frontend/.env.homolog` (`VITE_API_URL=/api`, `VITE_DEV_LOGIN=true`) e `frontend/.env.production` (`VITE_API_URL=/api`, SEM `VITE_DEV_LOGIN`); declarar as variáveis em `frontend/src/vite-env.d.ts` (`interface ImportMetaEnv { readonly VITE_API_URL: string; readonly VITE_SITE_BASENAME?: string; readonly VITE_GOOGLE_CLIENT_ID?: string; readonly VITE_DEV_LOGIN?: string }`) e adicionar `.env.local`, `dist/`, `test-results/`, `playwright-report/` a `frontend/.gitignore`
- [ ] T010 [P] Configurar ESLint em `frontend/eslint.config.js` (typescript-eslint strict, `react-hooks`, regra `@typescript-eslint/no-explicit-any: error`, `@typescript-eslint/consistent-type-definitions: ['error', 'interface']`, `prefer-arrow-callback`, `func-style: ['error', 'expression']`), ignorando `src/components/ui/**` (gerado)
- [ ] T011 [P] Criar `frontend/index.html` com `<html lang="pt-BR">`, `<meta name="viewport" content="width=device-width, initial-scale=1">`, `<title>Ambrosia</title>` e `frontend/public/favicon.svg`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: tipos, cliente HTTP, i18n, formatadores compartilhados, sessão mínima e esqueleto de rotas/estados que TODAS as histórias usam.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes fundacionais (escrever primeiro)

- [ ] T012 [P] Teste do `apiClient` em `frontend/src/Services/apiClient.test.ts` (mock de `fetch`): 200 retorna JSON tipado; injeta `Authorization: Bearer <token>` exceto em `/auth/*`; `Accept: application/json` sempre e `Content-Type: application/json` em POST; omite parâmetros `undefined`/vazios da query; 400/409 com `ErrorResult` → `ApiError(status, mensagem, erros)`; 401 → `UnauthorizedError` e chama `onUnauthorized`; 404 (com `ErrorResult` ou corpo vazio) → `NotFoundError`; 500 `text/plain` → `ApiError(500, texto)`; `fetch` rejeitado → `NetworkError`; `expiresAt` vencido → lança `UnauthorizedError` SEM chamar `fetch`
- [ ] T013 [P] Teste de datas em `frontend/src/lib/dates.test.ts`: `parseDateOnly('2026-03-01')` devolve dia 1 local; `formatDateOnly('2026-03-01')` → `01/03/2026`; `isValidDateOnly('2026-13-99')` → false; repetir com `process.env.TZ` = `America/Sao_Paulo`, `UTC` e `Asia/Tokyo` (FR-020, SC-007)
- [ ] T014 [P] Teste de moeda em `frontend/src/lib/currency.test.ts`: `formatCurrency(1234.56)` → `R$ 1.234,56`; `formatCurrency(-1234.56)` → `-R$ 1.234,56`; `formatCurrency(0)` → `R$ 0,00` (comparar normalizando o NBSP do `Intl`) (FR-019)
- [ ] T015 [P] Teste do armazenamento de sessão em `frontend/src/Services/authStorage.test.ts`: grava/lê `AuthSession` na chave `login-with-metamask:auth`; descarta (e remove) JSON inválido, `token` vazio, `expiresAt` não parseável ou no passado; `clear()` remove a chave

### Implementação fundacional

- [ ] T016 [P] Criar `frontend/src/types/common.ts` com `interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number }`, `interface FieldError { campo: string; mensagem: string }`, `interface ErrorResult { sucesso: false; mensagem: string; erros: FieldError[] }` e a constante `PAGE_SIZE = 20`
- [ ] T017 [P] Criar `frontend/src/types/auth.ts` com `UserInfo { id: number; email: string; name: string; provider: 'google' | 'dev'; createdAt: string }`, `LoginResult { token: string; expiresAt: string; user: UserInfo }` (`expiresAt` ISO date-time), `GoogleLoginInfo { idToken: string }`, `DevLoginInfo { email: string; name: string }`, `AuthSession { token: string; expiresAt: string; user: UserInfo }` (data-model §1)
- [ ] T018 [P] Criar `frontend/src/types/customer.ts` com `CustomerInfo { id: number; name: string; cpfMasked: string; createdAt: string; updatedAt: string; photoUrl?: string | null }`, `CustomerSummaryInfo { purchases: number; netTotal: number; lastPurchase: string | null /* AAAA-MM-DD */; daysSinceLastPurchase: number | null }`, `CustomerDetailInfo extends CustomerInfo { cpf: string; summary: CustomerSummaryInfo }`, `CustomerListParams { search?: string; page: number; pageSize: 20 }`
- [ ] T019 [P] Criar `frontend/src/types/order.ts` com `OrderInfo { id: number; customerId: number; customerName: string; orderNumber: string; orderDate: string /* AAAA-MM-DD */; kind: 'sale' | 'return' | 'cancellation'; channel: string | null; origin: 'cota' | 'omni' | null; status: string | null; total: number; itemsCount: number; createdAt: string; updatedAt: string }`, `OrderItemInfo { id: number; lineNo: number; productName: string | null; skuRef: string | null; size: string | null; price: number | null; imageUrl: string | null }`, a constante `ORDER_KINDS: readonly OrderInfo['kind'][]`, `OrderDetailInfo extends OrderInfo { originalSaleOn: string | null; cancelledOn: string | null; externalId: string | null; customer: CustomerInfo; items: OrderItemInfo[] }`, `OrderFilters { from?: string; to?: string; kind?: OrderInfo['kind']; channel?: string; customerId?: number; search?: string; page: number; pageSize: 20 }` (conferir os tipos de `orderNumber` e `id` contra `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml`)
- [ ] T020 Implementar `frontend/src/Services/apiClient.ts`: constante `API_URL = import.meta.env.VITE_API_URL ?? '/api'`; classes `ApiError(status, mensagem, erros)`, `UnauthorizedError`, `NotFoundError(mensagem?)`, `NetworkError`; função `apiRequest<T>(method, path, { query?, body?, auth? })` que verifica `expiresAt` antes do envio, monta cabeçalhos, chama `fetch`, interpreta respostas conforme a tabela "Interpretação de respostas" de `contracts/api-consumption.md` e chama o callback registrado por `setUnauthorizedHandler(fn)` em 401/sessão vencida (faz passar T012)
- [ ] T021 [P] Implementar `frontend/src/Services/apiHelpers.ts` com `getHeaders(session: AuthSession | null, withBody: boolean)` e `buildQuery(params: Record<string, string | number | undefined>)` (omite vazios; `pageSize` sempre 20)
- [ ] T022 [P] Implementar `frontend/src/Services/authStorage.ts` com `AUTH_STORAGE_KEY = 'login-with-metamask:auth'`, `readSession(): AuthSession | null` (validações de data-model §3), `writeSession(s)`, `clearSession()`; NUNCA guardar CPF, clientes, pedidos ou filtros (faz passar T015)
- [ ] T023 [P] Implementar `frontend/src/lib/dates.ts` com `parseDateOnly(s)` (`new Date(y, m - 1, d)`, nunca `new Date(iso)`), `isValidDateOnly(s)` (regex `^\d{4}-\d{2}-\d{2}$` + data real), `formatDateOnly(s)` via `Intl.DateTimeFormat('pt-BR')` → `dd/mm/aaaa`, `formatDateTime(iso)` → `dd/mm/aaaa HH:mm` (faz passar T013)
- [ ] T024 [P] Implementar `frontend/src/lib/currency.ts` com `formatCurrency(n)` via `Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })` (faz passar T014)
- [ ] T025 [P] Criar `frontend/src/i18n/locales/pt-BR.ts` com recurso organizado por `common` (Sair, Clientes, Pedidos, Anterior, Próxima, "Página {{page}} de {{pages}}", "{{count}} registros", Tentar de novo, Carregando, Voltar à primeira página), `login` (Entrar com Google, Entrada de desenvolvimento, E-mail, Nome, Entrar, "Sua sessão expirou"), `customers` (Buscar cliente, "Nenhum cliente encontrado", Limpar busca, Mostrar, Ocultar, Compras, Total líquido, Última compra, "Dias desde a última compra", "Sem compras", "Cliente não encontrado"), `orders` (Venda, Devolução, Cancelamento, Data inicial, Data final, Tipo, Todos, Canal, "Número do pedido ou cliente", Limpar filtros, "Nenhum pedido encontrado", "Pedido não encontrado", Status, Origem, Venda original, Cancelado em, Itens, Referência, Tamanho, Preço) e `errors` ("Não foi possível conectar", "Entrada de desenvolvimento indisponível", "Página não encontrada"); e `frontend/src/i18n/index.ts` inicializando i18next + `initReactI18next` com `lng: 'pt-BR'`, `fallbackLng: 'pt-BR'`, `initImmediate: false`, `interpolation.escapeValue: false`
- [ ] T026 [P] Criar componentes de estado em `frontend/src/components/common/`: `LoadingList.tsx` (N `Skeleton` em cards, `aria-busy="true"`, `role="status"`), `EmptyState.tsx` (props `title`, `actionLabel?`, `onAction?`), `ErrorState.tsx` (props `message`, `onRetry`, botão "Tentar de novo"), `NotFound.tsx` (props `title`, link para `/clientes`) (FR-025/027)
- [ ] T027 [P] Criar `frontend/src/components/common/Pagination.tsx`: props `page`, `pageSize`, `totalCount`, `onPageChange`; botões "Anterior"/"Próxima" (desabilitados nos limites, alvo ≥ 44 px, `aria-label`), texto "Página X de Y" e "N registros"; `nav aria-label="Paginação"` (FR-008/011/013)
- [ ] T028 [P] Criar `frontend/src/lib/errors.ts` com `toUserMessage(err: unknown, t)` → `NetworkError` = "Não foi possível conectar"; `ApiError` = `mensagem`; demais = mensagem genérica; e `notifyError(err, t, onRetry)` que dispara `toast.error` do `sonner` com ação "Tentar de novo" (FR-026, skill `react-alert`)
- [ ] T029 Criar `frontend/src/App.tsx` com `BrowserRouter basename={import.meta.env.VITE_SITE_BASENAME || undefined}`, rotas lazy (`React.lazy`) para `LoginPage`, `CustomersPage`, `CustomerProfilePage`, `OrdersPage`, `OrderDetailPage`, `NotFoundPage` conforme `contracts/ui-routes.md` (`/` → `Navigate` para `/clientes`; `*` → `NotFoundPage`), `Suspense` com `LoadingList`; inicialmente com páginas placeholder que serão preenchidas pelas histórias; nenhuma rota sob `/api`, `/auth`, `/mcp`, `/.well-known`
- [ ] T030 Criar `frontend/src/main.tsx` importando `./index.css` e `./i18n`, renderizando `<StrictMode>` → `App` e `<Toaster richColors position="top-center" />` do `components/ui/sonner`; deixar o ponto de encaixe da cadeia de providers (`AuthProvider` → `CustomerProvider` → `OrderProvider`) documentado com comentário, a ser preenchida em US1/US2/US3
- [ ] T031 [P] Criar `frontend/src/pages/NotFoundPage.tsx` usando `NotFound` com "Página não encontrada"

**Checkpoint**: `npm run lint`, `npm run type-check` e `npm test` verdes; `npm run dev` abre o esqueleto em `http://localhost:5173`.

---

## Phase 3: User Story 1 - Entrar e manter a sessão (Priority: P1) 🎯 MVP

**Goal**: login com Google (e dev-login fora de produção), sessão persistida em `localStorage`, guarda de rotas com retorno à rota original, expiração local/401 com aviso "Sua sessão expirou", "Sair" e cabeçalho com nome e e-mail.

**Independent Test**: abrir `/pedidos?tipo=sale` sem sessão → `/login`; entrar (dev-login `ana@exemplo.com`/`Ana`) → volta a `/pedidos?tipo=sale` com "Ana" e o e-mail no cabeçalho; recarregar mantém a sessão; `expiresAt` no passado → `/login?motivo=expirada` com o aviso e sem chamada à API; "Sair" → `/login` sem a chave no `localStorage` (quickstart Cenário 1).

### Tests for User Story 1 ⚠️

- [ ] T032 [P] [US1] Teste do `AuthContext` em `frontend/src/Contexts/AuthContext.test.tsx`: sessão válida no storage → `isAuthenticated` true; `loginDev` grava sessão; `logout()` limpa storage sem motivo; `logout('expirada')` limpa e navega para `/login?motivo=expirada` com `state.returnTo`; timer agendado para `expiresAt` dispara `logout('expirada')` (usar `vi.useFakeTimers`); 401 vindo do `apiClient` dispara `logout('expirada')`
- [ ] T033 [P] [US1] Teste de componentes em `frontend/src/components/layout/RequireAuth.test.tsx` e `frontend/src/pages/LoginPage.test.tsx`: sem sessão, `RequireAuth` redireciona para `/login` guardando `returnTo` = path + query; `LoginPage` com `?motivo=expirada` mostra "Sua sessão expirou"; após login navega para `returnTo` ou `/clientes`; `DevLoginForm` aparece só com `VITE_DEV_LOGIN === 'true'` (`vi.stubEnv`)
- [ ] T034 [P] [US1] Criar `frontend/e2e/seed.ts` (globalSetup Playwright): via `POST http://localhost:5000/auth/dev-login` cria `ana@exemplo.com` e `bia@exemplo.com`; para Ana semeia ≥ 25 clientes (nomes incluindo "Mar…"), 1 cliente sem compras, pedidos `sale`, `return` e `cancellation` (com `originalSaleOn`/`cancelledOn`), canais incluindo `Linx`, itens com e sem `imageUrl` (um com URL inválida); para Bia 1 cliente e 1 pedido; grava os IDs em `frontend/e2e/.seed.json` (ignorado no git); e `frontend/e2e/fixtures.ts` com fixture `loggedIn` (login programático gravando `login-with-metamask:auth` no `localStorage`) e helper de `timezoneId`
- [ ] T035 [P] [US1] E2E em `frontend/e2e/auth.spec.ts`: cenários 1–7 da US1 / quickstart Cenário 1 (redireciono com retorno, cabeçalho, persistência após novo contexto com `storageState`, token corrompido → aviso, `expiresAt` vencido → aviso sem requisição a `/api`, "Sair" remove a chave, formulário de dev visível em dev)

### Implementation for User Story 1

- [ ] T036 [P] [US1] Implementar `frontend/src/Services/authService.ts` (classe singleton, skill): `loginWithGoogle(idToken: string): Promise<LoginResult>` → `POST /auth/google` com `{ idToken }`; `loginDev(email, name): Promise<LoginResult>` → `POST /auth/dev-login` com `{ email, name }`; 404 do dev-login mapeado para `ApiError` com "Entrada de desenvolvimento indisponível"
- [ ] T037 [P] [US1] Implementar `frontend/src/Services/userService.ts` com `getMe(): Promise<UserInfo>` → `GET /users/me` (disponível para revalidação; não chamado a cada tela)
- [ ] T038 [US1] Implementar `frontend/src/Contexts/AuthContext.tsx`: `interface AuthContextType { session: AuthSession | null; isAuthenticated: boolean; loading: boolean; error: string | null; loginWithGoogle(idToken): Promise<boolean>; loginDev(email, name): Promise<boolean>; logout(reason?: 'expirada'): void; clearError(): void }`; estado inicial de `readSession()`; `setUnauthorizedHandler(() => logout('expirada'))`; timer `setTimeout` até `expiresAt`; `logout` chama `clearSession()` e navega para `/login` (com `?motivo=expirada` e `state.returnTo` = `location.pathname + location.search` quando houver motivo) (depende de T020, T022, T036)
- [ ] T039 [US1] Implementar `frontend/src/hooks/useAuth.ts` com null-check (`throw new Error('useAuth deve ser usado dentro de AuthProvider')`) e registrar `AuthProvider` em `frontend/src/main.tsx` dentro do `BrowserRouter` (mover `BrowserRouter` para `main.tsx` se necessário para `useNavigate` no provider)
- [ ] T040 [P] [US1] Implementar `frontend/src/hooks/useGoogleIdentity.ts`: injeta uma única vez `<script src="https://accounts.google.com/gsi/client" async>`, chama `google.accounts.id.initialize({ client_id: import.meta.env.VITE_GOOGLE_CLIENT_ID, callback: (r) => onCredential(r.credential) })` e `renderButton(ref, { theme: 'outline', size: 'large', locale: 'pt-BR', width })`; não renderiza nada se `VITE_GOOGLE_CLIENT_ID` vazio (research §2)
- [ ] T041 [US1] Implementar `frontend/src/components/layout/RequireAuth.tsx`: sem sessão (ou `expiresAt` vencido → `logout('expirada')`) → `<Navigate to="/login" state={{ returnTo: pathname + search }} replace />`; caso contrário `<Outlet />` (FR-001/007)
- [ ] T042 [P] [US1] Implementar `frontend/src/components/layout/AppHeader.tsx` (nome e e-mail de `session.user` truncados com `truncate`, links "Clientes"/"Pedidos" com `NavLink` e estado ativo, botão "Sair" com `aria-label`) e `frontend/src/components/layout/AppShell.tsx` (cabeçalho + `<main className="mx-auto max-w-screen-md px-4">` + `<Outlet />`) (FR-005/006)
- [ ] T043 [US1] Implementar `frontend/src/pages/DevLoginForm.tsx`: formulário com `Label`/`Input` para e-mail (`type="email"`, obrigatório) e nome (obrigatório), botão "Entrar", chama `loginDev`; erro via `notifyError`
- [ ] T044 [US1] Implementar `frontend/src/pages/LoginPage.tsx`: se autenticada → `Navigate` para `state.returnTo ?? '/clientes'`; mostra aviso "Sua sessão expirou" quando `motivo=expirada` (`role="alert"`); botão Google via `useGoogleIdentity` → `loginWithGoogle` → `navigate(returnTo ?? '/clientes', { replace: true })`; `const DevLoginForm = import.meta.env.VITE_DEV_LOGIN === 'true' ? lazy(() => import('./DevLoginForm')) : null` no nível do módulo, para o Rollup eliminar o chunk em produção (research §8, FR-002, SC-008)
- [ ] T045 [US1] Ligar em `frontend/src/App.tsx`: `/login` pública; demais rotas aninhadas em `RequireAuth` → `AppShell` (depende de T041, T042, T044)

**Checkpoint**: US1 funcional e testável sozinha (T032–T035 verdes).

---

## Phase 4: User Story 2 - Encontrar um cliente e ver seu perfil (Priority: P1)

**Goal**: lista de clientes paginada com busca debounced refletida na URL; perfil com avatar, CPF mascarado/revelável, resumo de compras e pedidos do cliente paginados; "Cliente não encontrado" indistinto.

**Independent Test**: com > 20 clientes, buscar `mar`, abrir um perfil, conferir resumo e pedidos contra `GET /customers/{id}` e `GET /orders?customerId=`; "mostrar"/"ocultar" CPF; `/clientes/999999` e o cliente da Bia → mesma tela "Cliente não encontrado" (quickstart Cenário 2).

### Tests for User Story 2 ⚠️

- [ ] T046 [P] [US2] Testes de funções puras em `frontend/src/lib/cpf.test.ts` e `frontend/src/lib/initials.test.ts`: `initials('Maria da Silva')` → `MD`; `initials('ana')` → `A`; `initials('  ')` → `?`; helpers de CPF nunca produzem CPF completo a partir de `cpfMasked`
- [ ] T047 [P] [US2] Testes de componentes em `frontend/src/components/customers/CpfReveal.test.tsx` e `frontend/src/components/customers/CustomerSummary.test.tsx`: `CpfReveal` mostra `***.456.789-**`, "mostrar" revela o completo e vira "ocultar", remontar volta a mascarado; `CustomerSummary` com `purchases: 0, netTotal: 0, lastPurchase: null` mostra "0", `R$ 0,00` e "Sem compras"
- [ ] T048 [P] [US2] Teste de `frontend/src/hooks/useDebouncedValue.test.ts` (fake timers, 400 ms) e de `frontend/src/Services/customerService.test.ts` (query `search`, `page`, `pageSize=20`; `getById` propaga `NotFoundError`)
- [ ] T049 [P] [US2] E2E em `frontend/e2e/customers.spec.ts`: quickstart Cenário 2 passos 1–8 (paginação com `?pagina=`, busca automática com `?busca=mar`, vazio com "Limpar busca", perfil, CPF mostrar/ocultar e reset ao voltar, cliente sem compras, 404 idêntico para inexistente e da Bia, CPF completo ausente da lista)

### Implementation for User Story 2

- [ ] T050 [P] [US2] Implementar `frontend/src/lib/initials.ts` (`initials(name)` → até 2 letras maiúsculas das duas primeiras palavras, `?` se vazio) e `frontend/src/lib/cpf.ts` (`isMaskedCpf(s)`; nenhuma função de "desmascarar") (faz passar T046)
- [ ] T051 [P] [US2] Implementar `frontend/src/hooks/useDebouncedValue.ts` (`useDebouncedValue<T>(value: T, delay = 400): T`)
- [ ] T052 [P] [US2] Implementar `frontend/src/Services/customerService.ts` (classe singleton): `list(params: CustomerListParams): Promise<PagedResult<CustomerInfo>>` → `GET /customers?search&page&pageSize=20`; `getById(id: number): Promise<CustomerDetailInfo>` → `GET /customers/{id}`
- [ ] T053 [P] [US2] Implementar em `frontend/src/Services/orderService.ts` (classe singleton) o método `list(filters: OrderFilters): Promise<PagedResult<OrderInfo>>` → `GET /orders` mapeando `from`, `to`, `kind`, `channel`, `customerId`, `search`, `page`, `pageSize=20` (omitindo vazios); será reutilizado por US3/US4
- [ ] T054 [US2] Implementar `frontend/src/Contexts/CustomerContext.tsx`: `interface CustomerContextType { customers: CustomerInfo[]; totalCount: number; selectedCustomer: CustomerDetailInfo | null; loading: boolean; error: string | null; notFound: boolean; loadCustomers(params): Promise<void>; loadCustomer(id): Promise<void>; clearSelected(): void; clearError(): void }`; `NotFoundError` → `notFound = true`; `UnauthorizedError` ignorado (o `AuthContext` trata); outros → `error` + `notifyError`; o CPF completo fica só em memória (`selectedCustomer`) e é limpo em `clearSelected` (depende de T052)
- [ ] T055 [US2] Implementar `frontend/src/hooks/useCustomer.ts` com null-check e registrar `CustomerProvider` dentro de `AuthProvider` em `frontend/src/main.tsx`
- [ ] T056 [P] [US2] Implementar `frontend/src/components/customers/CustomerAvatar.tsx` (`Avatar` shadcn; `photoUrl` → `AvatarImage` com `referrerPolicy="no-referrer"`; senão `AvatarFallback` com `initials(name)`; `aria-hidden` no fallback) e `frontend/src/components/customers/CustomerCard.tsx` (card clicável como `Link` para `/clientes/:id`, avatar, nome com `truncate`, `cpfMasked`; alvo ≥ 44 px; foco visível) (FR-008/010)
- [ ] T057 [P] [US2] Implementar `frontend/src/components/customers/CpfReveal.tsx` (prop `cpf`, `cpfMasked`; `useState(false)` local; botão "Mostrar"/"Ocultar" com `aria-pressed` e `aria-label`; nada persistido) e `frontend/src/components/customers/CustomerSummary.tsx` (compras, `formatCurrency(netTotal)`, `formatDateOnly(lastPurchase)` ou "Sem compras", dias desde a última compra ou "—") (FR-010/012, faz passar T047)
- [ ] T058 [P] [US2] Implementar `frontend/src/components/orders/OrderKindBadge.tsx` (rótulo i18n de `orderKindLabel(kind)`; variante `destructive` para `return`/`cancellation`), `frontend/src/components/orders/OrderTotal.tsx` (`formatCurrency(total)`; classe `text-destructive font-semibold` quando `isNegativeKind(kind)`) e `frontend/src/lib/orderKind.ts` (`orderKindLabel(k)` → chave i18n Venda/Devolução/Cancelamento; `isNegativeKind(k)` → `k !== 'sale'`) (FR-018/021)
- [ ] T059 [US2] Implementar `frontend/src/components/orders/OrderCard.tsx`: `Link` para `/pedidos/:id` com `customerName` (truncate), `orderNumber`, `formatDateOnly(orderDate)`, `channel` como veio (ou "—"), `OrderKindBadge`, etiqueta `Badge variant="outline"` para `origin` quando houver, e `OrderTotal` (FR-013; depende de T058)
- [ ] T060 [US2] Implementar `frontend/src/pages/CustomersPage.tsx`: lê `busca` e `pagina` de `useSearchParams` (página inválida → 1); `Input` com `Label` "Buscar cliente" controlado localmente + `useDebouncedValue` → atualiza URL com `busca` e `pagina=1` via `setSearchParams(..., { replace: true })`; mudança de página com `push`; chama `loadCustomers`; estados `LoadingList` / `ErrorState` (Tentar de novo) / `EmptyState` ("Nenhum cliente encontrado" + "Limpar busca", ou "Voltar à primeira página" quando `items` vazio e `totalCount > 0`) / grade de `CustomerCard` (1 coluna < 640 px, 2 acima) + `Pagination` (FR-008/009, depende de T054, T056)
- [ ] T061 [US2] Implementar `frontend/src/pages/CustomerProfilePage.tsx`: `:id` (não inteiro → "Cliente não encontrado"); `loadCustomer(id)` e, em paralelo, `orderService.list({ customerId: id, page, pageSize: 20 })` com estado local de `loading`/`error` para a lista; `pagina` da URL controla a lista de pedidos; renderiza `CustomerAvatar`, nome, `CpfReveal`, `CustomerSummary`, lista de `OrderCard` + `Pagination` + `EmptyState` para cliente sem pedidos; `notFound` → `NotFound` "Cliente não encontrado" (mesma tela para inexistente e de outra vendedora); `clearSelected()` no unmount (FR-010/011/012/027; depende de T053, T054, T057, T059)
- [ ] T062 [US2] Registrar as rotas `/clientes` e `/clientes/:id` com as páginas reais em `frontend/src/App.tsx`

**Checkpoint**: US1 + US2 funcionais (MVP completo de consulta por cliente).

---

## Phase 5: User Story 3 - Consultar os pedidos com filtros (Priority: P2)

**Goal**: lista de todos os pedidos com filtros de período, tipo, canal (trecho, case-insensitive) e texto, refletidos na URL; destaque de negativos; inclui o ajuste de backend do filtro de canal.

**Independent Test**: com pedidos de venda, devolução e cancelamento, aplicar cada filtro (incluindo `canal=lin` casando `Linx`), recarregar e confirmar persistência; `/pedidos?tipo=invalido&de=2026-13-99` carrega sem filtros; `/pedidos?pagina=99` mostra "Voltar à primeira página" (quickstart Cenário 3).

### Tests for User Story 3 ⚠️

- [ ] T063 [P] [US3] Adicionar caso em `backend/Ambrosia.Tests/Infra/Repository/OrderRepositoryTest.cs`: com pedido de canal `Linx`, `channel = "lin"` retorna o pedido; `channel = "LINX"` retorna; `channel = "xyz"` retorna vazio; e pedido de outra vendedora com `Linx` nunca aparece (filtro `userId` primeiro). Deve FALHAR antes de T067
- [ ] T064 [P] [US3] Teste do parser em `frontend/src/lib/filters.test.ts`: `parseOrderFilters` aceita `de`/`ate` `AAAA-MM-DD` válidos; descarta data inválida; se `de > ate`, ignora ambos; `tipo` ∈ `sale|return|cancellation` senão descarta; `canal` com `trim` e "texto ≤ 60" (acima disso descarta); `busca` com `trim` e "texto ≤ 200"; `pagina` inteiro ≥ 1, padrão 1; `toSearchParams` faz o caminho inverso omitindo vazios e `pagina=1` (data-model §4, tabela de filtros)
- [ ] T065 [P] [US3] Teste de `frontend/src/components/orders/OrderFiltersBar.test.tsx`: todos os campos têm rótulo acessível; alterar tipo chama `onChange` com `page: 1`; "Limpar filtros" zera tudo; campos de texto usam debounce
- [ ] T066 [P] [US3] E2E em `frontend/e2e/orders.spec.ts`: quickstart Cenário 3 passos 1–7 (ordem decrescente, cada filtro e combinação com total atualizado, URL com `de`, `ate`, `tipo`, `canal`, `busca`, reload mantém filtros, totais negativos `-R$` com destaque, parâmetros inválidos ignorados, página além do fim, vazio com "Limpar filtros")

### Implementation for User Story 3

- [ ] T067 [US3] Ajustar `backend/Ambrosia.Infra/Repository/OrderRepository.cs` (linha ~90): `o.Channel.ToLower() == channel` → `o.Channel.ToLower().Contains(channel)`, mantendo `userId` como primeira cláusula e o `Trim().ToLower()` existente (skill `dotnet-architecture`; faz passar T063; rodar `dotnet test backend/Ambrosia.Tests --filter OrderRepositoryTest`)
- [ ] T068 [P] [US3] Atualizar a documentação do parâmetro `channel`: XML doc de `backend/Ambrosia.API/Controllers/OrdersController.cs` (linha ~30, "compara por igualdade" → "filtra por trecho, sem diferenciar maiúsculas/minúsculas") e a descrição em `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` (`paths./orders.get.parameters` → `description: Trecho do canal (case-insensitive)`); verificar se `backend/Ambrosia.ApiTests/Controllers/OrdersControllerTests.cs` depende de igualdade e ajustar se necessário
- [ ] T069 [P] [US3] Implementar `frontend/src/lib/filters.ts` com `parseOrderFilters(sp: URLSearchParams): OrderFilters` e `toSearchParams(f: OrderFilters): URLSearchParams` mapeando URL ↔ API (`de`→`from`, `ate`→`to`, `tipo`→`kind`, `canal`→`channel`, `busca`→`search`, `pagina`→`page`) com as validações de T064, e `parsePage(sp)` reutilizável (faz passar T064)
- [ ] T070 [US3] Implementar `frontend/src/hooks/useOrderFilters.ts`: lê `useSearchParams` → `parseOrderFilters`; `setFilters(partial)` reposiciona `page: 1` e usa `replace`; `setPage(n)` usa `push`; `clearFilters()`; remove da URL parâmetros inválidos na próxima navegação (depende de T069)
- [ ] T071 [US3] Implementar `frontend/src/Contexts/OrderContext.tsx`: `interface OrderContextType { orders: OrderInfo[]; totalCount: number; selectedOrder: OrderDetailInfo | null; loading: boolean; error: string | null; notFound: boolean; loadOrders(filters: OrderFilters): Promise<void>; loadOrder(id: number): Promise<void>; clearSelected(): void; clearError(): void }` (mesmo tratamento de erros do `CustomerContext`; `loadOrder` implementado em US4) e `frontend/src/hooks/useOrder.ts` com null-check; registrar `OrderProvider` dentro de `CustomerProvider` em `frontend/src/main.tsx` (depende de T053)
- [ ] T072 [US3] Implementar `frontend/src/components/orders/OrderFiltersBar.tsx`: `Label` + `Input type="date"` "Data inicial"/"Data final" (valor enviado sem conversão de fuso, research §10), `Select` "Tipo" (Todos/Venda/Devolução/Cancelamento), `Input` "Canal" (texto livre, `maxLength={60}`) e `Input` "Número do pedido ou cliente" (`maxLength={200}`) com `useDebouncedValue`, botão "Limpar filtros"; layout em coluna < 640 px, grade acima; nenhum seletor de "mês de cota" (FR-014/016; faz passar T065)
- [ ] T073 [US3] Implementar `frontend/src/pages/OrdersPage.tsx`: `useOrderFilters` → `loadOrders(filters)` a cada mudança; `OrderFiltersBar`; total de resultados; estados `LoadingList` / `ErrorState` / `EmptyState` ("Nenhum pedido encontrado" + "Limpar filtros", ou "Voltar à primeira página" se `items` vazio e `totalCount > 0`) / lista de `OrderCard` + `Pagination` (FR-013/014/015/018; depende de T059, T070, T071, T072)
- [ ] T074 [US3] Registrar a rota `/pedidos` com a página real em `frontend/src/App.tsx`

**Checkpoint**: US1–US3 funcionais; filtro de canal por trecho validado contra a API.

---

## Phase 6: User Story 4 - Ver o detalhe de um pedido (Priority: P2)

**Goal**: detalhe do pedido com cliente (link + CPF mascarado), dados do pedido, datas de venda original/cancelamento, total com destaque e itens em ordem de linha com imagem lazy/placeholder sem `Referer`.

**Independent Test**: abrir um pedido de venda com vários itens e um de cancelamento; conferir campos contra `GET /orders/{id}`; item com `imageUrl` inválida mostra placeholder; `/pedidos/999999` → "Pedido não encontrado" (quickstart Cenário 4).

### Tests for User Story 4 ⚠️

- [ ] T075 [P] [US4] Teste de `frontend/src/components/common/LazyImage.test.tsx` e `frontend/src/components/orders/OrderItemRow.test.tsx`: `img` com `loading="lazy"`, `referrerPolicy="no-referrer"`, `decoding="async"` e `alt`; `src` nulo ou evento `error` → placeholder no mesmo espaço; `OrderItemRow` mostra nome, referência, tamanho e `formatCurrency(price)` (ou "—" para nulos)
- [ ] T076 [P] [US4] E2E em `frontend/e2e/order-detail.spec.ts`: quickstart Cenário 4 passos 1–4 (campos do detalhe, link para o perfil, CPF apenas mascarado — nenhum texto casando CPF completo na página, itens ordenados por `lineNo`, requisição de imagem sem cabeçalho `Referer` via `page.on('request')`, cancelamento com datas e total negativo, 404 para inexistente e para pedido da Bia)

### Implementation for User Story 4

- [ ] T077 [P] [US4] Adicionar `getById(id: number): Promise<OrderDetailInfo>` → `GET /orders/{id}` em `frontend/src/Services/orderService.ts` e implementar `loadOrder(id)` em `frontend/src/Contexts/OrderContext.tsx` (`NotFoundError` → `notFound = true`)
- [ ] T078 [P] [US4] Implementar `frontend/src/components/common/LazyImage.tsx`: props `src: string | null`, `alt`, `className`; `<img loading="lazy" decoding="async" referrerPolicy="no-referrer">` em contêiner `aspect-square` fixo; `onError` → placeholder (ícone `ImageOff` do `lucide-react` + fundo `bg-muted`, `role="img"` com `aria-label`) sem erro visível (FR-023)
- [ ] T079 [US4] Implementar `frontend/src/components/orders/OrderItemRow.tsx`: `LazyImage` (`imageUrl`, alt = `productName`), nome do produto (truncate), referência (`skuRef`), tamanho (`size`), preço (`formatCurrency(price)`), valores nulos como "—" (depende de T078; faz passar T075)
- [ ] T080 [US4] Implementar `frontend/src/pages/OrderDetailPage.tsx`: `:id` (não inteiro → "Pedido não encontrado"); `loadOrder(id)`; seção cliente com `Link` para `/clientes/{customer.id}` e `customer.cpfMasked` (NUNCA CPF completo, FR-022); número, `formatDateOnly(orderDate)`, `OrderKindBadge`, canal como veio, origem como etiqueta, status, `OrderTotal`; quando houver, "Venda original" `formatDateOnly(originalSaleOn)` e "Cancelado em" `formatDateOnly(cancelledOn)`; itens ordenados por `lineNo` com `OrderItemRow`; estados `LoadingList` / `ErrorState` / `NotFound` "Pedido não encontrado"; `clearSelected()` no unmount (FR-017/018/027; depende de T077, T079)
- [ ] T081 [US4] Registrar a rota `/pedidos/:id` com a página real em `frontend/src/App.tsx`

**Checkpoint**: todas as telas de consulta existem (SC-009: todos os campos da API de consulta visíveis).

---

## Phase 7: User Story 5 - Usar no celular com conforto (Priority: P2)

**Goal**: layout ≥ 360 px sem rolagem horizontal, alvos de toque confortáveis, carregamento/erro/retry consistentes, acessibilidade básica e equivalência na matriz de navegadores.

**Independent Test**: percorrer todas as telas em 360 × 740 e com a rede desligada; axe-core sem violações; navegação só com Tab com foco visível (quickstart Cenário 5).

### Tests for User Story 5 ⚠️

- [ ] T082 [P] [US5] E2E em `frontend/e2e/mobile-a11y.spec.ts`: viewport 360 × 740 em `/login`, `/clientes`, `/clientes/:id`, `/pedidos`, `/pedidos/:id` e `*` verificando `document.documentElement.scrollWidth <= 360`; `AxeBuilder` (`wcag2a`, `wcag2aa`) sem violações em cada tela; navegação por Tab com foco visível nos controles; `context.setOffline(true)` → "Não foi possível conectar" + "Tentar de novo" e, ao voltar online, a lista carrega; API respondendo 500 (via `page.route`) → toast com a mensagem
- [ ] T083 [P] [US5] E2E de fusos em `frontend/e2e/orders.spec.ts` (bloco `test.describe` parametrizado): com `timezoneId` `America/Sao_Paulo`, `UTC` e `Asia/Tokyo`, a data de um pedido semeado aparece no mesmo `dd/mm/aaaa` na lista e no detalhe (SC-007)

### Implementation for User Story 5

- [ ] T084 [US5] Revisar responsividade em `frontend/src/components/layout/AppShell.tsx`, `frontend/src/components/layout/AppHeader.tsx`, `frontend/src/components/orders/OrderFiltersBar.tsx` e cards: `min-w-0` + `truncate` em textos longos, `flex-wrap` no cabeçalho, botões e links com `min-h-11 min-w-11` (≥ 44 px), nenhuma largura fixa > 360 px, `overflow-x` nunca necessário (FR-028)
- [ ] T085 [US5] Revisar acessibilidade em `frontend/src/components/` e `frontend/src/pages/`: todo `Input`/`Select` com `Label` associado (`htmlFor`), botões só com ícone com `aria-label`, `focus-visible:ring` preservado, `aria-live="polite"` no contador de resultados, `aria-busy` durante carregamento, cada página com um `<h1>` único, contraste AA de `text-destructive` e badges em `frontend/src/index.css` (FR-029)
- [ ] T086 [US5] Garantir retry uniforme: `CustomersPage`, `CustomerProfilePage`, `OrdersPage` e `OrderDetailPage` passam o `load*` atual como `onRetry` para `ErrorState` e `notifyError` (`frontend/src/lib/errors.ts`), com mensagem da API ou "Não foi possível conectar" (FR-025/026)
- [ ] T087 [US5] Rodar `npm run test:e2e` na matriz completa (chromium, firefox, webkit, msedge, Mobile Chrome, Mobile Safari) e corrigir divergências de layout/comportamento encontradas (FR-031, SC-010)

**Checkpoint**: todas as histórias funcionais e verificadas no celular e na matriz de navegadores.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: publicação Docker, CSP, orçamento de bundle, verificação de produção e documentação.

- [ ] T088 [P] Criar `frontend/nginx.conf`: `listen 80`; `root /usr/share/nginx/html`; `location / { try_files $uri /index.html; }`; `location /assets/ { expires 1y; add_header Cache-Control "public, immutable"; }`; `index.html` sem cache; cabeçalho `Content-Security-Policy "default-src 'self'; script-src 'self' https://accounts.google.com/gsi/client; connect-src 'self' https://accounts.google.com/gsi/; frame-src https://accounts.google.com/gsi/; style-src 'self' 'unsafe-inline' https://accounts.google.com/gsi/style; img-src 'self' data: https:"`, `Referrer-Policy no-referrer`, `X-Content-Type-Options nosniff` (research §18)
- [ ] T089 [P] Criar `frontend/Dockerfile` multi-stage: `node:22-alpine` com `ARG BUILD_MODE=production`, `ARG VITE_DEV_LOGIN`, `ARG VITE_GOOGLE_CLIENT_ID` (exportados como `ENV` só no estágio de build), `npm ci`, `npm run build -- --mode $BUILD_MODE` → `nginx:1.27-alpine` copiando `dist/` e `nginx.conf`, `EXPOSE 80`; e `frontend/.dockerignore` (`node_modules`, `dist`, `e2e`, `test-results`, `playwright-report`, `.env.local`)
- [ ] T090 Adicionar `build.args` ao serviço `frontend` em `docker-compose.yml` (`BUILD_MODE: homolog`, `VITE_DEV_LOGIN: "true"`, `VITE_GOOGLE_CLIENT_ID: ${AUTH_GOOGLE_CLIENT_ID:-}`) e em `docker-compose-prod.yml` (`BUILD_MODE: production`, `VITE_GOOGLE_CLIENT_ID: ${AUTH_GOOGLE_CLIENT_ID}`, SEM `VITE_DEV_LOGIN`); conferir que `deploy/nginx/routes.inc` continua roteando `/` → `frontend:80` (depende de T089)
- [ ] T091 Verificar SC-008 e orçamento de bundle: `npm run build -- --mode production` em `frontend/`; buscar `dev-login` e `DevLoginForm` em `frontend/dist/**/*.js` (esperado: nenhuma ocorrência); confirmar no relatório do `vite build` bundle inicial ≤ 250 kB gzip; adicionar o script `verify:prod` em `frontend/package.json` que automatiza a busca e falha se encontrar
- [ ] T092 [P] Criar `frontend/README.md` com pré-requisitos (Node 22), variáveis `VITE_*` por ambiente, comandos (`dev`, `lint`, `type-check`, `test`, `test:e2e`, `build`, `verify:prod`), origens JavaScript autorizadas no Google Cloud (`http://localhost:5173`, `http://localhost:8080`, `https://ambrosiaai.site`) e build Docker via `wsl-docker`
- [ ] T093 Revisar FR-030 e FR-022 em todo `frontend/src/`: nenhum `POST`/`PUT`/`DELETE` além de `/auth/*`; `cpf` completo referenciado apenas em `CpfReveal`/`CustomerProfilePage`; nada de CPF, clientes ou pedidos em `localStorage` ou `console.*`
- [ ] T094 Homologação via skill `wsl-docker`: `wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build`; validar `http://localhost:8080/` (frontend com dev-login), `http://localhost:8080/api/swagger`, login dev na mesma origem sem CORS e filtro de canal por trecho
- [ ] T095 Executar a validação completa de `specs/002-frontend-web-clientes-pedidos/quickstart.md` (Cenários 1–5, testes automatizados, build de produção, backend) e registrar o resultado
- [ ] T096 [P] Atualizar `MAESTRO.md` (Active Technologies: TypeScript 5 / React 18 / Vite 6 para `002-frontend-web-clientes-pedidos`; Commands: `cd frontend; npm run dev`, `npm test`, `npm run test:e2e`)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências.
- **Foundational (Phase 2)**: depende do Setup — BLOQUEIA todas as histórias.
- **US1 (Phase 3)**: depende da Phase 2. É pré-requisito prático de US2–US5, porque todas as rotas internas ficam atrás de `RequireAuth`/`AppShell` (T041–T045).
- **US2 (Phase 4)**: depende de US1 (rotas protegidas). Cria `orderService.list`, `OrderCard`, `OrderKindBadge`, `OrderTotal`, reutilizados por US3.
- **US3 (Phase 5)**: depende de US1 e de T053/T058/T059 (US2). O ajuste de backend (T063, T067, T068) não depende de nada do frontend e pode começar logo após o Setup.
- **US4 (Phase 6)**: depende de US1 e de T053/T058 (serviço e componentes de pedido) e de T071 (`OrderContext`, US3).
- **US5 (Phase 7)**: depende de todas as telas (US1–US4) existirem.
- **Polish (Phase 8)**: T088/T089/T092 podem começar após a Phase 2; T090–T095 dependem de todas as histórias.

### Grafo de conclusão das histórias

```text
Setup → Foundational → US1 ─┬─▶ US2 ─┬─▶ US3 ─▶ US4 ─▶ US5 ─▶ Polish
                            │        │
Backend (T063→T067→T068) ───┴────────┴─▶ (integra em US3)
```

### Within Each User Story

- Testes escritos primeiro e FALHANDO.
- types → Services → Contexts → hooks → providers → componentes → páginas → rotas.
- História completa e validada no checkpoint antes da próxima prioridade.

### Parallel Opportunities

- Phase 1: T003, T004, T005, T006, T007, T009, T010, T011 em paralelo após T001/T002.
- Phase 2: testes T012–T015 juntos; tipos T016–T019 juntos; T021–T028 em paralelo (arquivos distintos); T020 após T016/T017.
- US1: T032–T035 juntos; T036, T037, T040, T042 em paralelo.
- US2: T046–T049 juntos; T050–T053 e T056–T058 em paralelo.
- US3: T063 e T067/T068 (backend, C#) em paralelo com todo o trabalho de frontend; T064–T066 juntos.
- US4: T075/T076 juntos; T077 e T078 em paralelo.
- US5: T082/T083 juntos.
- Polish: T088, T089, T092, T096 em paralelo.

---

## Parallel Example: User Story 2

```bash
# Testes da US2 juntos:
Task: "Testes de lib/cpf e lib/initials em frontend/src/lib/cpf.test.ts e initials.test.ts"
Task: "Testes de CpfReveal e CustomerSummary em frontend/src/components/customers/*.test.tsx"
Task: "Testes de useDebouncedValue e customerService"
Task: "E2E de clientes em frontend/e2e/customers.spec.ts"

# Implementação em paralelo (arquivos distintos):
Task: "lib/initials.ts e lib/cpf.ts"
Task: "hooks/useDebouncedValue.ts"
Task: "Services/customerService.ts"
Task: "Services/orderService.ts (list)"
Task: "CustomerAvatar + CustomerCard"
Task: "CpfReveal + CustomerSummary"
Task: "OrderKindBadge + OrderTotal + lib/orderKind.ts"
```

## Parallel Example: User Story 3

```bash
# Backend e frontend em paralelo:
Task: "Caso de trecho em backend/Ambrosia.Tests/Infra/Repository/OrderRepositoryTest.cs"
Task: "Teste de parseOrderFilters em frontend/src/lib/filters.test.ts"
Task: "Teste de OrderFiltersBar em frontend/src/components/orders/OrderFiltersBar.test.tsx"
Task: "E2E de pedidos em frontend/e2e/orders.spec.ts"
```

---

## Implementation Strategy

### MVP First (US1 + US2, ambas P1)

1. Phase 1: Setup.
2. Phase 2: Foundational (CRÍTICO).
3. Phase 3: US1 → validar login, persistência, expiração e "Sair".
4. Phase 4: US2 → validar busca e perfil do cliente.
5. **PARAR e VALIDAR** (quickstart Cenários 1 e 2) — já entrega o caso de uso principal na loja ("quem comprou o quê, quanto e quando").

US1 sozinha é testável, mas só entrega valor ao usuário junto com US2; por isso o MVP recomendado é US1 + US2.

### Incremental Delivery

1. Setup + Foundational → base pronta.
2. US1 → sessão (demo interna).
3. US2 → MVP de consulta por cliente.
4. US3 (+ ajuste de backend) → visão cronológica com filtros.
5. US4 → detalhe com itens.
6. US5 → endurecimento mobile/a11y/matriz de navegadores.
7. Polish → Docker, CSP, verificação de produção, homologação.

### Parallel Team Strategy

1. Time conclui Setup + Foundational + US1 juntos.
2. Depois:
   - Dev A: US2 (clientes/perfil).
   - Dev B: ajuste de backend (T063, T067, T068) + `lib/filters` e `OrderFiltersBar` de US3.
   - Dev C: Docker/nginx/CSP (T088, T089) e fixtures E2E.
3. US3/US4 integram os componentes de pedido criados em US2; US5 fecha a rodada.

---

## Notes

- [P] = arquivos diferentes, sem dependências pendentes.
- [Story] mapeia cada tarefa à história da spec para rastreabilidade.
- Verificar que os testes falham antes de implementar.
- Commit após cada tarefa ou grupo lógico.
- Evitar: tarefas vagas, conflitos no mesmo arquivo (`App.tsx` e `main.tsx` são tocados em sequência, nunca em paralelo), dependências entre histórias que quebrem a independência.
