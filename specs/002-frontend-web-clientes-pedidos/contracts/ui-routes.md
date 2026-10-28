# Contrato de rotas do SPA

**Feature**: `002-frontend-web-clientes-pedidos` — interface que o frontend expõe à vendedora (endereços compartilháveis) e ao gateway.

Base: `VITE_SITE_BASENAME` (vazio em produção; o app fica na raiz de `https://ambrosioai.site`). Todas as rotas, exceto `/login`, exigem sessão válida (FR-001); sem sessão, redirecionam para `/login` guardando a rota original (FR-007).

| Rota | Tela | Query string aceita | Chamadas à API | Estados |
|---|---|---|---|---|
| `/` | — | — | — | Redireciona para `/clientes` |
| `/login` | Entrada | `motivo=expirada` → mostra "Sua sessão expirou" | `POST /auth/google`; `POST /auth/dev-login` (só builds com `VITE_DEV_LOGIN=true`) | Se já autenticada, redireciona para `returnTo` ou `/clientes` |
| `/clientes` | Lista de clientes | `busca` (texto), `pagina` (≥ 1) | `GET /customers?search&page&pageSize=20` | carregando · lista · vazio ("Nenhum cliente encontrado" + limpar busca) · erro |
| `/clientes/:id` | Perfil do cliente | `pagina` (lista de pedidos do cliente) | `GET /customers/{id}`; `GET /orders?customerId={id}&page&pageSize=20` | carregando · perfil · "Cliente não encontrado" (404) · erro |
| `/pedidos` | Lista de pedidos | `de`, `ate` (`AAAA-MM-DD`), `tipo` (`sale`/`return`/`cancellation`), `canal` (texto), `busca` (texto), `pagina` | `GET /orders?from&to&kind&channel&search&page&pageSize=20` | carregando · lista · vazio ("Nenhum pedido encontrado" + limpar filtros) · erro |
| `/pedidos/:id` | Detalhe do pedido | — | `GET /orders/{id}` | carregando · detalhe · "Pedido não encontrado" (404) · erro |
| `*` | Não encontrado | — | — | Link para `/clientes` |

## Regras transversais

- **Cabeçalho** (todas as telas internas): nome e e-mail de `session.user`, navegação Clientes / Pedidos e botão "Sair" (FR-005/006). O cabeçalho usa a sessão guardada; `GET /users/me` não é chamado a cada tela.
- **Parâmetros inválidos** são ignorados e a lista carrega sem eles (edge case). `pagina` além do fim mostra o estado vazio com "Voltar à primeira página".
- **Mudança de filtro/busca** reposiciona `pagina=1` e usa `history.replace`; mudança de página usa `push` (voltar do navegador percorre páginas).
- **Sessão expirada ou 401** em qualquer rota → `/login?motivo=expirada` com `state.returnTo` = rota + query atual.
- **Nenhuma rota** oferece criar, editar ou excluir (FR-030).

## Gateway (nginx)

- O container do frontend serve `dist/` na porta 80 com fallback `try_files $uri /index.html` para todas as rotas acima.
- O gateway (`deploy/nginx/routes.inc`) já reserva `/api/`, `/auth/`, `/mcp` e `/.well-known/` antes de `location /` → `frontend:80`; o frontend **não** deve registrar rotas com esses prefixos.
