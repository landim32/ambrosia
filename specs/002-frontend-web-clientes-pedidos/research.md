# Research — Frontend web de consulta de clientes e pedidos

**Feature**: `002-frontend-web-clientes-pedidos` | **Date**: 2026-10-26

Phase 0 do plano. Cada item resolve um "NEEDS CLARIFICATION" do Technical Context ou fixa uma prática para uma dependência/integração. Decisões tomadas com o solicitante nesta sessão estão marcadas com **[decisão do solicitante]**.

---

## 1. Stack de testes do frontend **[decisão do solicitante]**

- **Decision** (revista em 2026-10-26 pelo solicitante): só Vitest + React Testing Library (jsdom) para formatadores, hooks, serviços e componentes. **Sem testes E2E no frontend.**
- **Rationale**: o frontend é só de exibição; a matriz Playwright (6 navegadores, ~500 MB de download, API de pé) custa mais do que protege. Os 3 fusos de SC-007 são provados nos testes unitários de `lib/dates` (mudando `TZ`); 360 px, acessibilidade (SC-006) e a matriz de navegadores (FR-031, SC-010) são verificados manualmente pelo quickstart.
- **Alternatives considered**: Vitest + RTL + Playwright (escolha original do plan, tomada pelo decisor automático do autopiloto e revertida); Vitest + Cypress (sem WebKit real, fusos exigem plugin).

## 2. Login com Google no navegador **[decisão do solicitante]**

- **Decision**: Google Identity Services (GIS) carregado diretamente (`https://accounts.google.com/gsi/client`), encapsulado em um hook `useGoogleIdentity` que chama `google.accounts.id.initialize({ client_id, callback })` e `renderButton`. O `credential` recebido no callback é o ID token enviado a `POST /auth/google`. Tipagem via `@types/google.accounts` (dev dependency).
- **Rationale**: nenhuma dependência de runtime extra; é o que a HU 002 (`docs/historias/002`) propõe; o backend já valida a audiência com `Auth:GoogleClientId`.
- **Alternatives considered**: `@react-oauth/google` (menos código, mas adiciona dependência de terceiros ao bundle).
- **Configuração necessária**: `VITE_GOOGLE_CLIENT_ID` com o mesmo valor de `Auth__GoogleClientId` da API. No cliente OAuth do Google Cloud, as origens JavaScript autorizadas devem incluir `http://localhost:5173` (dev), `http://localhost:8080` (homolog) e `https://ambrosiaai.site` (prod).

## 3. Filtro de canal por trecho **[decisão do solicitante]**

- **Decision**: ajustar o backend nesta feature. `OrderRepository.cs` (linha ~90) passa de `o.Channel.ToLower() == channel` para `o.Channel.ToLower().Contains(channel)`; o parâmetro `channel` em `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` passa a "Trecho do canal (case-insensitive)"; `OrderRepositoryTest` ganha caso de trecho parcial.
- **Rationale**: FR-014 exige busca por trecho sem distinguir maiúsculas; a mudança é de uma linha, isolada, e mantém compatibilidade com quem enviava o canal completo (continua casando). `ToLower().Contains` em vez de `EF.Functions.ILike` para continuar funcionando no provider InMemory usado em `Ambrosio.Tests`.
- **Alternatives considered**: manter igualdade e documentar a limitação (violaria a letra do FR-014); lista fixa de canais (descartada no clarify).

## 4. Layout do projeto e casing de diretórios

- **Decision**: projeto Vite em `frontend/` com `src/types/`, `src/Services/`, `src/Contexts/`, `src/hooks/`, `src/components/`, `src/pages/`, `src/lib/` e `src/i18n/`. Casing conforme o Princípio III da constituição (`Contexts/`, `Services/` em maiúscula; `hooks/`, `types/` em minúscula).
- **Rationale**: a constituição prevalece sobre a skill `react-architecture` (que usa `contexts/`/`services/`); o próprio Sync Impact Report registra a divergência como pendente e o Princípio III como inviolável. Imports usam o casing exato do disco (build em Linux no Docker).
- **Alternatives considered**: seguir o casing da skill (quebraria o build no container se um import divergisse e contraria o Princípio III).

## 5. Sessão: armazenamento, expiração e redirecionamento

- **Decision**: `localStorage` na chave `"login-with-metamask:auth"` (constituição §VI) com `{ token, expiresAt, user }` (o `LoginResult` da API). Um `AuthContext` expõe `session`, `isAuthenticated`, `loginWithGoogle(idToken)`, `loginDev(email, name)`, `logout(reason?)`. A expiração é verificada (a) ao montar o guard de rota, (b) antes de cada requisição no `apiClient` e (c) por um timer agendado para `expiresAt`. Ao expirar localmente ou receber 401, `logout('expirada')` limpa o storage e navega para `/login?motivo=expirada`, guardando a rota original em `state.returnTo`; após o login, `navigate(returnTo ?? '/clientes', { replace: true })`.
- **Rationale**: atende FR-003/004/005/007 e os cenários 3–6 da US1 sem cookies (constituição). O aviso "Sua sessão expirou" é exibido pela `LoginPage` a partir do parâmetro `motivo`, não por toast, para sobreviver ao redirecionamento.
- **Alternatives considered**: `sessionStorage` (não mantém sessão entre aberturas, viola FR-003); renomear a chave para `ambrosio:auth` (emenda futura, fora do escopo — spec Assumptions).

## 6. Cliente HTTP e tratamento de erros

- **Decision**: `src/Services/apiClient.ts` com Fetch API: injeta `Authorization: Bearer`, serializa query strings omitindo valores vazios, e normaliza erros em classes: `UnauthorizedError` (401 → `onUnauthorized`), `NotFoundError` (404), `ApiError` (demais: `mensagem` do `ErrorResult` JSON ou o texto do 500 `text/plain`) e `NetworkError` (falha de rede → "Não foi possível conectar"). Serviços por entidade (`authService`, `userService`, `customerService`, `orderService`) seguem o padrão de classe da skill `react-architecture` (singleton + classe, `handleResponse` privado).
- **Rationale**: a API da HU 001 devolve DTOs "puros" em 200 e `ErrorResult` (`sucesso`, `mensagem`, `erros`) apenas em erro — diferente do wrapper `sucesso` em toda resposta que a skill assume. Em vez de checar `result.sucesso`, os contextos checam o tipo do erro lançado. Essa adaptação é registrada no plano (Complexity Tracking).
- **Alternatives considered**: Axios (constituição restringe a legado); envolver cada resposta num `{ sucesso, dados }` artificial (ruído sem ganho).

## 7. URL base da API e mesma origem

- **Decision**: `VITE_API_URL` padrão `/api` (relativo à origem) em todos os ambientes. Em `vite dev`, `server.proxy` repassa `/api` → `http://localhost:5000` com `rewrite` removendo o prefixo; em Homolog e Produção o nginx do gateway já faz isso (`deploy/nginx/routes.inc`).
- **Rationale**: elimina CORS em todos os ambientes (mesma origem), reproduz produção no dev e dispensa variável diferente por ambiente. A API já lê `X-Forwarded-Prefix` (`ReverseProxy:Enabled`).
- **Alternatives considered**: `VITE_API_URL=http://localhost:5000` em dev (funciona com o CORS de Development, mas diverge de produção e expõe a diferença de origem no código).

## 8. Formulário de dev-login ausente do pacote de produção

- **Decision**: flag de build `VITE_DEV_LOGIN` (`"true"` em `.env.development` e `.env.homolog`, ausente em `.env.production`). O componente `DevLoginForm` é importado via `React.lazy(() => import(...))` dentro de `if (import.meta.env.VITE_DEV_LOGIN === 'true')`. O Vite substitui a expressão em tempo de build e o Rollup elimina o ramo morto e o chunk. Verificação: `grep -ri "dev-login" frontend/dist` vazio no build de produção (SC-008).
- **Rationale**: FR-002/SC-008 exigem ausência no pacote, não apenas ocultação. Homolog precisa do formulário porque a API o aceita lá (constituição §VI).
- **Alternatives considered**: `import.meta.env.DEV` (excluiria o formulário também em Homolog); variável de runtime (ficaria no bundle).
- **Build Docker**: `frontend/Dockerfile` recebe `ARG VITE_DEV_LOGIN` e `ARG VITE_GOOGLE_CLIENT_ID`; `docker-compose.yml` (Homolog) passa `VITE_DEV_LOGIN=true` e `VITE_GOOGLE_CLIENT_ID=${AUTH_GOOGLE_CLIENT_ID:-}`; `docker-compose-prod.yml` passa só `VITE_GOOGLE_CLIENT_ID=${AUTH_GOOGLE_CLIENT_ID}`.

## 9. Filtros refletidos na URL e validação

- **Decision**: `useSearchParams` (React Router 6) como única fonte de verdade dos filtros de `/pedidos` (`de`, `ate`, `tipo`, `canal`, `busca`, `pagina`) e de `/clientes` (`busca`, `pagina`) e `/clientes/:id` (`pagina`). Um parser `parseOrderFilters(searchParams)` descarta valores inválidos (tipo fora de `sale|return|cancellation`, data que não é `AAAA-MM-DD` válida, página não inteira ≥ 1) e devolve o objeto `OrderFilters` que o serviço converte nos nomes da API (`from`, `to`, `kind`, `channel`, `search`, `page`). Página além do fim: a resposta vem com `items: []` e `totalCount` > 0 → estado vazio com ação "Voltar à primeira página".
- **Rationale**: FR-015 e edge cases "Filtro inválido no endereço" e "Página além do fim". Nomes em português na URL mantêm o endereço legível para a vendedora (FR-024).
- **Alternatives considered**: estado em Context sincronizado manualmente com a URL (duas fontes de verdade, risco de divergência).

## 10. Datas sem hora e período no fuso de Brasília

- **Decision**: `src/lib/dates.ts` com `parseDateOnly('AAAA-MM-DD')` → `new Date(y, m - 1, d)` (componentes locais, nunca `new Date(iso)`) e `formatDateOnly` via `Intl.DateTimeFormat('pt-BR')`. Os campos de período (`<input type="date">`) produzem `AAAA-MM-DD` que é enviado **sem conversão** à API: o dia-calendário escolhido pela vendedora é o próprio dia em Brasília. Sem período escolhido, `from`/`to` não são enviados (FR-014).
- **Rationale**: evita o deslocamento de um dia em fusos negativos/positivos (FR-020, SC-007) e o risco do "hoje" em UTC da API.
- **Alternatives considered**: `date-fns-tz`/`luxon` (dependência extra sem necessidade; `Intl` nativo basta).

## 11. Moeda e destaque de negativos

- **Decision**: `formatCurrency` com `Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })`, que produz `R$ 1.234,56` e `-R$ 1.234,56`. Componente `OrderTotal` aplica classe de destaque (`text-destructive` + `Badge` do tipo) quando `kind !== 'sale'`.
- **Rationale**: FR-018/FR-019 sem biblioteca extra; o sinal vem do próprio `total` negativo da API.

## 12. CPF: máscara, revelar e nunca persistir

- **Decision**: listas e detalhe do pedido usam exclusivamente `cpfMasked`. No perfil, `CustomerDetailInfo.cpf` fica apenas no estado do `CustomerContext` em memória; o componente `CpfReveal` controla `revealed` em `useState` local (reseta ao desmontar). Nada do CPF completo vai ao `localStorage` nem a logs.
- **Rationale**: FR-012/FR-022/SC-003 e edge case "CPF revelado".

## 13. Imagens dos itens

- **Decision**: `<img loading="lazy" referrerPolicy="no-referrer" decoding="async" onError={→ placeholder}>` com `aspect-square` fixo e `alt` descritivo; placeholder é um ícone + fundo neutro no mesmo tamanho.
- **Rationale**: FR-023 e edge case do domínio do +Vendas (hotlink/expiração) sem vazar a origem.

## 14. Busca com debounce e paginação

- **Decision**: hook `useDebouncedValue(value, 400)`; ao mudar o valor "estável", a página volta a 1 e a URL é atualizada com `replace`. Paginação fixa em `pageSize=20` (padrão da API), componente `Pagination` com "Anterior"/"Próxima", "página X de Y" e total de registros, reaproveitado nas três listas.
- **Rationale**: FR-008/009/011/013; 400 ms equilibra resposta e número de chamadas em rede móvel.

## 15. Estado: Context API por entidade

- **Decision**: `AuthProvider` → `CustomerProvider` → `OrderProvider` → `App` em `main.tsx` (skill `react-architecture`). Cada contexto mantém `loading`, `error` e os dados correntes (`customers` + `totalCount`, `selectedCustomer`, `orders` + `totalCount`, `selectedOrder`) e expõe métodos `useCallback` (`loadCustomers(params)`, `loadCustomer(id)`, `loadOrders(filters)`, `loadOrder(id)`, `clearError`). Páginas disparam os `load*` a partir dos parâmetros da URL.
- **Rationale**: constituição (Context API, sem Redux/Zustand) e skill obrigatória.
- **Alternatives considered**: TanStack Query (cache e revalidação excelentes, mas fora da stack fixa).

## 16. Estilo, componentes e acessibilidade

- **Decision**: Tailwind CSS 4 via `@tailwindcss/vite`; shadcn/ui inicializado com `npx shadcn@latest init` e componentes `button`, `card`, `input`, `label`, `select`, `badge`, `avatar`, `skeleton`, `separator`, `sonner`. Layout mobile-first: container `max-w-screen-md`, cards em coluna única < 640 px e grade de 2 colunas acima; alvos de toque ≥ 44 px; `focus-visible` dos componentes shadcn; cores de destaque de negativos com contraste AA. Toasts com `sonner` (skill `react-alert`) para erros de API e falha de rede com ação "Tentar de novo".
- **Rationale**: constituição §II (Tailwind + shadcn únicos), FR-025/026/028/029, US5.

## 17. Internacionalização

- **Decision**: `i18next` + `react-i18next` com instância global, `lng: 'pt-BR'`, `initImmediate: false`, um único recurso `src/i18n/locales/pt-BR.ts` organizado por tela (`common`, `login`, `customers`, `orders`, `errors`). Rótulos de tipo (`sale` → "Venda" etc.) ficam no recurso.
- **Rationale**: constituição §II (i18next 25.x) e Assumptions ("base de internacionalização preparada, só pt-BR"). A skill `add-react-i18n` é para bibliotecas (instância isolada, namespace) e não se aplica a um app.

## 18. Imagem Docker e CSP

- **Decision**: `frontend/Dockerfile` multi-stage: `node:22-alpine` (`npm ci` + `npm run build -- --mode <homolog|production>`) → `nginx:1.27-alpine` servindo `dist/` na porta 80 com `frontend/nginx.conf` (`try_files $uri /index.html`, cache longo para `/assets/`, `Content-Security-Policy` com `default-src 'self'; script-src 'self' https://accounts.google.com/gsi/client; connect-src 'self' https://accounts.google.com/gsi/; frame-src https://accounts.google.com/gsi/; style-src 'self' 'unsafe-inline' https://accounts.google.com/gsi/style; img-src 'self' data: https:`). O gateway já roteia `/` → `frontend:80`.
- **Rationale**: encaixa no profile `frontend` já previsto nos dois compose e no pipeline (`if [ -f frontend/Dockerfile ]`); CSP mitiga o risco de XSS com token em `localStorage` (docs/historias/002 §8). `img-src https:` é necessário porque as imagens dos itens vêm de domínios do +Vendas.
- **Alternatives considered**: servir o `dist/` pelo próprio gateway (acoplaria o nginx de borda ao build do frontend e quebraria o profile).

## 19. Performance (SC-005)

- **Decision**: rotas carregadas com `React.lazy` (uma chunk por página), orçamento de bundle inicial ≤ 250 kB gzip verificado em `vite build`, `pageSize=20`, imagens lazy. Nenhuma pré-busca agressiva.
- **Rationale**: listas e detalhes em ≤ 2 s em 4G dependem mais do tamanho do bundle e de uma chamada por tela do que de cache.

## 20. Avatar e foto futura (HU 010)

- **Decision**: componente `CustomerAvatar` recebe `name` e `photoUrl?: string | null`; sem URL mostra as iniciais (duas primeiras palavras). `CustomerInfo`/`CustomerDetailInfo` no frontend já declaram `photoUrl?: string | null` como campo opcional, ignorado enquanto a API não o devolver.
- **Rationale**: Assumptions ("pronto para exibir a foto quando o campo surgir") sem exigir mudança na API da 001.
