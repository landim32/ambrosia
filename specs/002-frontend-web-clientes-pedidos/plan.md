# Implementation Plan: Frontend web de consulta de clientes e pedidos

**Branch**: `002-frontend-web-clientes-pedidos` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/002-frontend-web-clientes-pedidos/spec.md`

## Summary

Primeiro frontend do Ambrosio: SPA React 18 + TypeScript 5 + Vite 6, **somente leitura**, mobile-first, que permite à vendedora entrar com a conta Google (ou pelo dev-login fora de produção), encontrar clientes, ver o perfil com resumo de compras e CPF mascarado/revelável, consultar pedidos com filtros refletidos na URL e abrir o detalhe de cada pedido com seus itens. Consome exclusivamente a API REST da HU 001 na mesma origem (`/api`), guarda a sessão em `localStorage` conforme a constituição, segue a skill `react-architecture` (types → Services → Contexts → hooks → providers) com Tailwind CSS 4 + shadcn/ui, e é publicado como container nginx no profile `frontend` já previsto nos dois `docker-compose`.

Decisões tomadas com o solicitante nesta fase (ver [research.md](./research.md)): testes com **Vitest + RTL**, sem E2E no frontend (revisto em 2026-10-26: o Playwright foi retirado); login Google com **Google Identity Services direto**; e **ajuste do backend** para o filtro de canal passar de igualdade a trecho (FR-014).

## Technical Context

**Language/Version**: TypeScript 5.x (frontend); C# 12 / .NET 8.0 para o ajuste pontual do backend

**Primary Dependencies**:
- React 18.x, React Router 6.x, Vite 6.x (`@vitejs/plugin-react`), Tailwind CSS 4.x (`@tailwindcss/vite`), shadcn/ui (Radix UI), i18next 25.x + react-i18next, sonner (toasts)
- Fetch API nativa (nenhum Axios); Google Identity Services (script oficial) + `@types/google.accounts`
- Testes: Vitest + @testing-library/react + jsdom
- Backend (ajuste): EF Core 9.x já presente

**Storage**: nenhum no frontend além de `localStorage` (chave `login-with-metamask:auth` com `{ token, expiresAt, user }`); dados vêm da API (PostgreSQL via HU 001)

**Testing**: Vitest + RTL para `lib/`, `Services/`, `Contexts/` e componentes (os 3 fusos de SC-007 nos testes de `lib/dates`); sem E2E no frontend, com 360 px, acessibilidade e a matriz FR-031 verificados manualmente pelo quickstart; xUnit para o caso novo de `OrderRepositoryTest`

**Target Platform**: navegadores Chrome (Android), Safari (iOS), Chrome/Edge/Firefox/Safari desktop nas 2 últimas versões estáveis; publicado como imagem `nginx:1.27-alpine` (porta 80) atrás do gateway nginx existente

**Project Type**: web application (frontend SPA consumindo a API existente em `backend/`)

**Performance Goals**: listas e detalhes visíveis em ≤ 2 s em 4G para até 10.000 pedidos por vendedora (SC-005); bundle inicial ≤ 250 kB gzip; perfil de cliente alcançado em < 30 s a partir do login (SC-001)

**Constraints**:
- Somente leitura: nenhuma ação de criar/editar/excluir (FR-030)
- Sessão em `localStorage`, nunca cookies; CPF completo só no perfil sob demanda e nunca persistido (FR-012/022)
- Formulário de dev-login ausente do pacote de produção (FR-002/SC-008)
- Mesma origem em todos os ambientes (`/api`): sem CORS; dev usa proxy do Vite
- Layout ≥ 360 px sem rolagem horizontal; acessibilidade básica (FR-028/029); interface em pt-BR (FR-024)
- Datas sem hora tratadas como dia-calendário (FR-020)

**Scale/Scope**: 5 telas (login, lista de clientes, perfil, lista de pedidos, detalhe) + cabeçalho e estados; 3 entidades de frontend (Auth/User, Customer, Order); 7 endpoints consumidos; ~2 mil clientes e ~10 mil pedidos por vendedora

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Princípio | Como o plano atende | Status |
|---|---|---|---|
| I | Skills de arquitetura obrigatórias | Frontend gerado com `react-architecture` para `User`/`Auth`, `Customer` e `Order` (types → Service → Context → hook → provider em `main.tsx`); toasts com `react-alert` (sonner). O ajuste do `OrderRepository` segue `dotnet-architecture` (repositório continua filtrando por `userId` como primeira cláusula). Comandos Docker locais via `wsl-docker`. `dotnet-graphql` não se aplica (REST). **Exceção formal** à regra 7 da skill (não checar `result.sucesso` em respostas 200), registrada no Complexity Tracking e a ser justificada explicitamente no PR (Governance → Compliance). | ⚠️ Exceção justificada |
| II | Stack fixa | React 18, TS 5, React Router 6, Vite 6, Tailwind 4 + shadcn/ui, i18next 25, Fetch API. Sem Bootstrap/MUI, sem Redux/Zustand, sem Axios, sem bundler alternativo. Variáveis com prefixo `VITE_`. | ✅ |
| III | Case sensitivity | `src/Contexts/`, `src/Services/`, `src/hooks/`, `src/types/` exatamente como a tabela do princípio; imports com casing idêntico (build roda em Linux no Docker). Prevalece sobre o casing minúsculo da skill (divergência já registrada na constituição). | ✅ |
| IV | Convenções de código | Componentes PascalCase, funções/variáveis camelCase em arrow functions, constantes UPPER_CASE (`AUTH_STORAGE_KEY`), `interface` em vez de `type` (as unions de string `kind`/`origin` são declaradas como propriedades de `OrderInfo` e referenciadas por acesso indexado, ex.: `OrderInfo['kind']`; nenhum `type` é criado), acesso direto às propriedades camelCase do JSON. Backend: namespaces file-scoped e PascalCase mantidos. | ✅ |
| V | Banco de dados | Nenhuma tabela, coluna ou migração nova. | ✅ (N/A) |
| VI | Autenticação e segurança | JWT Bearer da API obtido via `POST /auth/google` (ID token do GIS) ou `POST /auth/dev-login` (só Development/Homolog, excluído do bundle de produção); token em `localStorage` na chave da constituição, nunca em cookie; nenhum segredo no frontend (`VITE_GOOGLE_CLIENT_ID` é público por natureza); CSP restritiva no nginx do frontend; CPF exibido mascarado em listagens. Nenhum `userId` enviado em DTO de entrada. | ✅ |
| — | Variáveis de ambiente | `VITE_API_URL` (obrigatória, padrão `/api`), `VITE_SITE_BASENAME` (opcional), mais `VITE_GOOGLE_CLIENT_ID` e `VITE_DEV_LOGIN` (build-time), todas via `import.meta.env.VITE_*`. | ✅ |
| — | Tratamento de erros | Frontend: `handleError`/`clearError`/`loading` em cada contexto (skill); erros normalizados no `apiClient`. Backend: inalterado (`try/catch` → 500). | ✅ |

**Resultado do gate (pré-Phase 0)**: APROVADO COM EXCEÇÃO. Nenhuma violação da §IV; uma exceção formal à §I (regra 7 da skill `react-architecture`), justificada abaixo e que deve constar na descrição do PR conforme a Governance.

**Re-avaliação pós-Phase 1**: data-model, contratos e quickstart mantêm todas as regras acima; o ajuste de backend não toca banco nem autenticação. APROVADO.

## Project Structure

### Documentation (this feature)

```text
specs/002-frontend-web-clientes-pedidos/
├── plan.md              # Este arquivo
├── spec.md              # Especificação funcional
├── research.md          # Phase 0: decisões técnicas
├── data-model.md        # Phase 1: tipos, sessão, estados de UI
├── quickstart.md        # Phase 1: guia de validação ponta a ponta
├── contracts/
│   ├── ui-routes.md         # Rotas do SPA, query strings, estados
│   └── api-consumption.md   # Endpoints consumidos, mapeamento de erros, delta do backend
├── checklists/requirements.md
├── decisions.md
└── tasks.md             # Phase 2 (gerado por /speckit.tasks)
```

### Source Code (repository root)

```text
frontend/                                  # NOVO — projeto Vite
├── index.html
├── package.json
├── vite.config.ts                         # @vitejs/plugin-react, @tailwindcss/vite, server.proxy /api → localhost:5000
├── vitest.config.ts
├── tsconfig.json / tsconfig.app.json / tsconfig.node.json
├── components.json                        # shadcn/ui
├── .env.example / .env.development / .env.homolog / .env.production
├── Dockerfile                             # node:22-alpine build → nginx:1.27-alpine (ARG VITE_DEV_LOGIN, VITE_GOOGLE_CLIENT_ID)
├── nginx.conf                             # try_files → index.html, cache de /assets, CSP
├── public/
│   └── favicon.svg
└── src/
    ├── main.tsx                           # AuthProvider → CustomerProvider → OrderProvider → App; Toaster
    ├── App.tsx                            # BrowserRouter(basename) + rotas lazy + RequireAuth
    ├── index.css                          # @import "tailwindcss"; tokens shadcn
    ├── i18n/
    │   ├── index.ts                       # i18next (pt-BR, initImmediate:false)
    │   └── locales/pt-BR.ts
    ├── types/
    │   ├── auth.ts   customer.ts   order.ts   common.ts
    ├── Services/
    │   ├── apiClient.ts                   # fetch + Bearer + erros normalizados
    │   ├── apiHelpers.ts                  # getHeaders(auth), buildQuery
    │   ├── authService.ts   userService.ts   customerService.ts   orderService.ts
    ├── Contexts/
    │   ├── AuthContext.tsx   CustomerContext.tsx   OrderContext.tsx
    ├── hooks/
    │   ├── useAuth.ts   useCustomer.ts   useOrder.ts
    │   ├── useDebouncedValue.ts   useGoogleIdentity.ts   useOrderFilters.ts
    ├── lib/
    │   ├── currency.ts   dates.ts   cpf.ts   initials.ts   orderKind.ts   filters.ts   utils.ts (cn)
    ├── components/
    │   ├── ui/                            # gerados pelo shadcn (button, card, input, label, select, badge, avatar, skeleton, separator, sonner)
    │   ├── layout/   AppHeader.tsx   AppShell.tsx   RequireAuth.tsx
    │   ├── common/   Pagination.tsx   EmptyState.tsx   ErrorState.tsx   LoadingList.tsx   NotFound.tsx   LazyImage.tsx
    │   ├── customers/ CustomerCard.tsx   CustomerAvatar.tsx   CpfReveal.tsx   CustomerSummary.tsx
    │   └── orders/    OrderCard.tsx   OrderTotal.tsx   OrderKindBadge.tsx   OrderFiltersBar.tsx   OrderItemRow.tsx
    ├── pages/
    │   ├── LoginPage.tsx   DevLoginForm.tsx (lazy, só com VITE_DEV_LOGIN)
    │   ├── CustomersPage.tsx   CustomerProfilePage.tsx
    │   ├── OrdersPage.tsx   OrderDetailPage.tsx   NotFoundPage.tsx
    └── test/
        ├── setup.ts                       # jest-dom, i18n
        └── **/*.test.ts(x)                # Vitest + RTL

backend/                                   # AJUSTE PONTUAL
├── Ambrosio.Infra/Repository/OrderRepository.cs      # channel: igualdade → Contains (case-insensitive)
└── Ambrosio.Tests/Infra/Repository/OrderRepositoryTest.cs  # caso de trecho

docker-compose.yml / docker-compose-prod.yml          # build.args do serviço frontend (VITE_DEV_LOGIN, VITE_GOOGLE_CLIENT_ID)
specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml   # descrição do parâmetro channel
```

**Structure Decision**: aplicação web com `frontend/` novo ao lado do `backend/` existente (layout já anunciado em `MAESTRO.md` e nos compose). O frontend segue a skill `react-architecture` (tipos, serviços de classe, contextos com `loading`/`error`, hooks com null-check e providers em `main.tsx`) com o casing da constituição; páginas são finas e delegam aos contextos; toda formatação (moeda, datas, CPF, tipos) fica em funções puras em `lib/` para ser testada em isolamento. Os filtros vivem na URL (`useSearchParams`) e não em estado global. O único código de backend tocado é a cláusula de canal do `OrderRepository`, que continua recebendo `userId` como primeira cláusula.

## Complexity Tracking

> Preenchido apenas para desvios justificados. A exceção à regra 7 da skill `react-architecture` (§I) é formal: deve ser justificada explicitamente no PR (Governance → Compliance).

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| **Exceção formal (§I)**: serviços não checam `result.sucesso` em respostas 200 (skill `react-architecture`, regra 7) | A API da HU 001 devolve DTOs puros em 200 e `ErrorResult` (`sucesso`/`mensagem`/`erros`) apenas em erro; o `apiClient` converte erros em classes (`UnauthorizedError`, `NotFoundError`, `ApiError`, `NetworkError`) e os contextos tratam por tipo | Envolver cada resposta num wrapper `{ sucesso, dados }` artificial duplicaria o contrato e esconderia o status HTTP, que é o que distingue 404 ("não encontrado") de 401 (sessão) |
| Ajuste de backend dentro de uma feature de frontend | FR-014 (trecho de canal) não é atendível só no cliente; a mudança é de uma linha + teste + descrição no contrato, sem migração | Documentar a limitação violaria FR-014; criar uma feature separada atrasaria a entrega por uma linha de código |
