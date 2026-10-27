# Quickstart — validação ponta a ponta

**Feature**: `002-frontend-web-clientes-pedidos`
Guia para provar que o frontend funciona de ponta a ponta contra a API da HU 001. Rotas e parâmetros em [contracts/ui-routes.md](./contracts/ui-routes.md); chamadas e erros em [contracts/api-consumption.md](./contracts/api-consumption.md); tipos e estados em [data-model.md](./data-model.md).

## Pré-requisitos

- Node.js 22 LTS (`node --version` → 22.x) e npm 10+
- API da HU 001 rodando em Development: `http://localhost:5000` (ver [quickstart da 001](../001-api-cadastro-clientes-pedidos/quickstart.md)); o `dev-login` precisa estar ativo
- (Opcional, login Google) um Client ID OAuth do Google com `http://localhost:5173` nas origens JavaScript autorizadas e o mesmo valor em `Auth__GoogleClientId` da API

## Configuração

```powershell
cd frontend
npm ci
Copy-Item .env.example .env.local      # opcional; os padrões já funcionam em dev
```

Variáveis (prefixo `VITE_` obrigatório):

| Variável | Dev (`.env.development`) | Homolog (`.env.homolog`) | Produção (`.env.production`) |
|---|---|---|---|
| `VITE_API_URL` | `/api` (proxy do Vite → `localhost:5000`) | `/api` | `/api` |
| `VITE_GOOGLE_CLIENT_ID` | vazio ou seu Client ID | build arg do compose | build arg do compose (`AUTH_GOOGLE_CLIENT_ID`) |
| `VITE_DEV_LOGIN` | `true` | `true` | **ausente** |
| `VITE_SITE_BASENAME` | vazio | vazio | vazio |

## Subir em desenvolvimento

```powershell
npm run dev      # http://localhost:5173
```

Esperado: abrir `http://localhost:5173/clientes` sem sessão redireciona para `/login`; a tela mostra o botão "Entrar com Google" e o formulário de entrada de desenvolvimento.

## Dados de teste

Semear pela API (PowerShell, com o token do dev-login `$TA` obtido como no quickstart da 001): ao menos 25 clientes (para paginar), um cliente sem compras, e pedidos `sale`, `return` e `cancellation` com itens (um com `imageUrl` inválida). Criar uma segunda vendedora (`bia@exemplo.com`) com um cliente e um pedido próprios para o teste de isolamento.

## Cenário 1 — Sessão (US1; SC-002, SC-008)

1. Em aba anônima, abrir `/pedidos?tipo=sale` → redireciona para `/login`.
2. Entrar pelo formulário de desenvolvimento (`ana@exemplo.com`, `Ana`) → volta para `/pedidos?tipo=sale`; cabeçalho mostra "Ana" e o e-mail.
3. DevTools → Application → localStorage: chave `login-with-metamask:auth` com `token`, `expiresAt` e `user`; **nenhum** CPF ou pedido guardado.
4. Fechar e reabrir o navegador → continua conectada.
5. Editar o `token` no localStorage (corromper) e navegar → volta a `/login` com "Sua sessão expirou".
6. Editar `expiresAt` para o passado e recarregar → mesmo aviso, sem chamada à API (Network vazio).
7. "Sair" → `/login` sem aviso; localStorage sem a chave.

## Cenário 2 — Clientes e perfil (US2; SC-001, SC-003)

1. `/clientes` → 20 cards com nome e CPF mascarado, "Página 1 de 2", total de clientes; "Próxima"/"Anterior" funcionam e atualizam `?pagina=`.
2. Digitar `mar` na busca → sem apertar botão, após a pausa a lista filtra e a URL fica `?busca=mar`; apagar → lista completa.
3. Buscar `zzzz` → "Nenhum cliente encontrado" + botão limpar busca.
4. Tocar num card → `/clientes/:id` com nome, avatar com iniciais, CPF `***.456.789-**`, botão "mostrar"; resumo: compras, total em `R$`, última compra `dd/mm/aaaa` e dias desde a última compra. Comparar com `GET /customers/{id}`.
5. "mostrar" → CPF completo formatado e botão vira "ocultar"; sair e voltar ao perfil → mascarado novamente.
6. Lista de pedidos do cliente: mais recentes primeiro, 20 por página, cada card abre `/pedidos/:id`.
7. Cliente sem compras → "0 compras", `R$ 0,00`, "sem compras" e lista vazia.
8. `/clientes/999999` e `/clientes/{id da Bia}` (logada como Ana) → ambas mostram "Cliente não encontrado", idênticas.

## Cenário 3 — Pedidos com filtros (US3; SC-004)

1. `/pedidos` sem filtros → todos os pedidos, data decrescente, cards com cliente, número, data, canal, tipo, etiqueta de origem e total.
2. Filtrar período, tipo, canal (`lin` casa `Linx`) e texto → lista e total mudam; URL reflete `de`, `ate`, `tipo`, `canal`, `busca`.
3. Recarregar a página → filtros mantidos; colar a URL em outra aba (logada) → mesma lista.
4. Devolução/cancelamento → total `-R$ …` com destaque visual distinto da venda.
5. `/pedidos?tipo=invalido&de=2026-13-99` → carrega sem filtros (parâmetros ignorados).
6. `/pedidos?pagina=99` → estado vazio com "Voltar à primeira página".
7. Filtros sem resultado → "Nenhum pedido encontrado" + limpar filtros.

## Cenário 4 — Detalhe do pedido (US4)

1. Abrir um pedido de venda com itens → cliente (link para o perfil) com CPF mascarado, número, data, tipo, canal, origem, status, total e itens em ordem de linha com imagem, nome, referência, tamanho e preço.
2. Item com `imageUrl` inválida → placeholder no mesmo espaço; aba Network mostra a requisição da imagem **sem** cabeçalho `Referer`.
3. Pedido de cancelamento → data da venda original e do cancelamento, total negativo com destaque.
4. `/pedidos/999999` → "Pedido não encontrado".

## Cenário 5 — Celular, rede e acessibilidade (US5; SC-006)

1. DevTools em modo dispositivo, 360 × 740 → percorrer todas as telas: sem rolagem horizontal, alvos de toque confortáveis, nomes longos truncados com reticências.
2. Network "Offline" e recarregar `/pedidos` → "Não foi possível conectar" com "Tentar de novo"; voltar online e tocar → lista carrega.
3. Parar a API e navegar → toast com a mensagem de erro e "Tentar de novo".
4. Navegar só com Tab → foco visível em todos os controles; leitor de tela anuncia rótulos dos campos de filtro e botões.

## Testes automatizados

```powershell
npm run lint && npm run type-check
npm test                 # Vitest + RTL: formatadores (moeda, datas em 3 fusos), parser de filtros, apiClient, AuthContext, componentes
```

Esperado: todas as suítes verdes. Não há testes E2E no frontend: 360 px, acessibilidade e a matriz de navegadores (FR-031) são verificados manualmente pelos cenários acima.

## Build de produção (SC-008)

```powershell
npm run build -- --mode production
Select-String -Path dist\**\*.js -Pattern "dev-login" -SimpleMatch   # esperado: nenhuma ocorrência
```

Esperado: `dist/` sem o formulário de desenvolvimento e bundle inicial ≤ 250 kB gzip (relatório do `vite build`).

## Homologação (Docker no WSL2, skill `wsl-docker`)

```powershell
wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build
```

Esperado: `http://localhost:8080/` abre o frontend (com formulário de dev, pois `VITE_DEV_LOGIN=true` no build de homolog); `http://localhost:8080/api/swagger` continua respondendo; login dev funciona na mesma origem (sem CORS); filtro de canal por trecho funciona (ajuste de backend incluído na imagem).

## Backend (ajuste do filtro de canal)

```powershell
cd backend
dotnet test Ambrosio.Tests --filter OrderRepositoryTest
```

Esperado: casos `channel=lin` → casa `Linx`; `channel=LINX` → casa; `channel=xyz` → vazio.

## Resultado da validação (2026-10-26, implementação /speckit.implement)

Automatizado e à revelia do navegador (executado nesta máquina):

| Item | Comando | Resultado |
|---|---|---|
| Testes do frontend | `npm test` | **19 arquivos, 176 testes aprovados** — inclui o smoke test de integração `src/App.test.tsx` (cadeia completa de providers: `RequireAuth` → `AppShell` → lista montada a partir do `fetch` mockado) |
| Lint / tipos | `npm run lint`, `npm run type-check` | limpos (0 erros, 0 warnings) |
| Build de produção | `npm run build -- --mode production` | entry `index-DnwvLCKQ.js` = **98,75 kB gzip** (orçamento: 250 kB) |
| SC-008 | `npm run verify:prod` | **PASS**: nenhuma ocorrência de `dev-login`/`DevLoginForm` em `dist/` |
| Build de homolog | `npm run build -- --mode homolog` | gera o chunk `DevLoginForm-*.js` (o dev-login existe onde deve) |
| Backend (trecho de canal) | `dotnet test Ambrosio.Tests --filter OrderRepositoryTest` | **14 aprovados, 0 falhas** |
| Imagem Docker | `docker compose --profile frontend build frontend` | build ok; container `healthy` |
| Gateway + headers | `curl -D - http://localhost:8080/clientes` | 200, CSP/Referrer-Policy/nosniff presentes, `index.html` com `no-store` |
| Cache de asset | `curl -D - /assets/index-*.js` | `Cache-Control: public, immutable` + `max-age=31536000` |
| API pela mesma origem | `POST /api/auth/dev-login` via gateway | 200 com JWT, sem CORS |
| Swagger | `GET /api/swagger` | 301 → 200 |
| FR-027 | `GET /orders/999999`, `GET /customers/999999` | 404 |
| Filtros da API | `kind=cancellation`, `search=00004751`, `search=Maria`, `from/to`, `page=99`, data invertida | 200 com os conjuntos esperados; invertida → 400 |

**C3.2 no container (revalidado após rebuild da API):** na primeira rodada,
`channel=lin` devolveu 0 porque o container `ambrosio-homolog-api` ainda rodava a
imagem construída antes do ajuste da T067. Após `docker compose build api migrator`
e `up -d`, a mesma bateria passou: `channel=lin` → 2, `channel=LINX` → 2,
`channel=Linx` → 2, `channel=ins` → 1 (trecho de "Instagram Direct"),
`channel=xyz` → 0, sem filtro → 3. `POST /api/auth/dev-login` via gateway → 200
com JWT (mesma origem, sem CORS), `/api/swagger` → 200 e
`/orders/{id}` devolve itens com `lineNo`, `skuRef` e `cpfMasked` (FR-017/FR-022).
A semeadura deixou 2 clientes e 3 pedidos na base de homologação da vendedora
`ana@exemplo.com` (ids 42/43, pedido 26).

Pendente de navegador (não automatizável aqui, sem E2E por decisão do solicitante):
Cenários 1.1–1.7, 2.1–2.8, 3.1–3.7, 4.1–4.4 e 5.1–5.4 — sessão/expiração na UI,
avatar/iniciais, "mostrar/ocultar" CPF, placeholder de imagem sem `Referer`, 360 px,
foco visível por Tab e a matriz de navegadores (FR-031, SC-006, SC-010).

