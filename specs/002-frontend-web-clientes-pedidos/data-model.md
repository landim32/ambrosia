# Data Model — Frontend web de consulta de clientes e pedidos

**Feature**: `002-frontend-web-clientes-pedidos` | **Date**: 2026-10-26

Esta feature **não cria dados persistentes**: todo dado de negócio vem da API da HU 001 ([openapi.yaml](../001-api-cadastro-clientes-pedidos/contracts/openapi.yaml)). O "modelo" aqui é o conjunto de tipos TypeScript que espelham os DTOs da API, o estado de sessão guardado no navegador e os estados de interface. Tipos ficam em `frontend/src/types/` (um arquivo por entidade), sempre como `interface`, com campos em camelCase idênticos ao JSON.

## 1. Tipos espelhados da API

### `types/auth.ts`

| Interface | Campos | Origem |
|---|---|---|
| `UserInfo` | `id: number`, `email: string`, `name: string`, `provider: 'google' \| 'dev'`, `createdAt: string` | `UserInfo` |
| `LoginResult` | `token: string`, `expiresAt: string` (ISO date-time), `user: UserInfo` | `LoginResult` |
| `GoogleLoginInfo` | `idToken: string` | `GoogleLoginInfo` |
| `DevLoginInfo` | `email: string`, `name: string` | `DevLoginInfo` |
| `AuthSession` | `token: string`, `expiresAt: string`, `user: UserInfo` | derivado de `LoginResult`; é o que vai ao `localStorage` |

### `types/customer.ts`

| Interface | Campos | Origem |
|---|---|---|
| `CustomerInfo` | `id: number`, `name: string`, `cpfMasked: string`, `createdAt: string`, `updatedAt: string`, `photoUrl?: string \| null` (futuro, HU 010) | `CustomerInfo` |
| `CustomerSummaryInfo` | `purchases: number`, `netTotal: number`, `lastPurchase: string \| null` (`AAAA-MM-DD`), `daysSinceLastPurchase: number \| null` | `CustomerSummaryInfo` |
| `CustomerDetailInfo` | `CustomerInfo` + `cpf: string` (completo, formatado), `summary: CustomerSummaryInfo` | `CustomerDetailInfo` |
| `CustomerListParams` | `search?: string`, `page: number`, `pageSize: 20` | query de `GET /customers` |

### `types/order.ts`

| Interface / tipo | Campos | Origem |
|---|---|---|
| `OrderInfo` | `id`, `customerId`, `customerName`, `orderNumber`, `orderDate` (`AAAA-MM-DD`), `kind: 'sale' \| 'return' \| 'cancellation'`, `channel: string \| null`, `origin: 'cota' \| 'omni' \| null`, `status: string \| null`, `total: number`, `itemsCount: number`, `createdAt`, `updatedAt` | `OrderInfo` |
| `OrderItemInfo` | `id: number`, `lineNo: number`, `productName: string \| null`, `skuRef: string \| null`, `size: string \| null`, `price: number \| null`, `imageUrl: string \| null` | `OrderItemInfo` |
| `OrderDetailInfo` | `OrderInfo` + `originalSaleOn: string \| null`, `cancelledOn: string \| null`, `externalId: string \| null`, `customer: CustomerInfo`, `items: OrderItemInfo[]` | `OrderDetailInfo` |
| `OrderFilters` | `from?: string`, `to?: string`, `kind?: OrderInfo['kind']`, `channel?: string`, `customerId?: number`, `search?: string`, `page: number`, `pageSize: 20` | query de `GET /orders` |

### `types/common.ts`

| Interface | Campos | Origem |
|---|---|---|
| `PagedResult<T>` | `items: T[]`, `page: number`, `pageSize: number`, `totalCount: number` | `PagedCustomerInfo` / `PagedOrderInfo` |
| `FieldError` | `campo: string`, `mensagem: string` | `FieldError` |
| `ErrorResult` | `sucesso: false`, `mensagem: string`, `erros: FieldError[]` | `ErrorResult` |

**Regras de tipagem**: nenhum `any`; nulos como `\| null` (não opcional) exceto `photoUrl`, que ainda não existe na API; IDs chamam-se `id` porque é assim que a API os expõe (a convenção `{entity}Id` da skill vale para FKs como `customerId`).

## 2. Erros normalizados (`Services/apiClient.ts`)

| Classe | Quando | Dado carregado | Reação na UI |
|---|---|---|---|
| `UnauthorizedError` | HTTP 401 ou `expiresAt` vencido antes de chamar | — | `AuthContext.logout('expirada')` → `/login?motivo=expirada` |
| `NotFoundError` | HTTP 404 | `mensagem` do `ErrorResult` | Tela "não encontrado" (cliente/pedido) |
| `ApiError` | HTTP 400/409/500 e outros | `status`, `mensagem` (JSON) ou texto do 500 | Toast com `mensagem` + "Tentar de novo" |
| `NetworkError` | `fetch` rejeitou (offline, DNS, CORS) | — | Toast "Não foi possível conectar" + "Tentar de novo" |

## 3. Estado de sessão (`localStorage`)

- **Chave**: `login-with-metamask:auth` (constituição §VI).
- **Valor**: `AuthSession` serializado em JSON.
- **Validação ao ler**: JSON válido, `token` não vazio, `expiresAt` parseável e futuro; qualquer falha → sessão descartada (tratada como anônima).
- **Nunca armazenado**: CPF completo, dados de clientes/pedidos, filtros (estes vivem na URL).

### Transições de sessão

```text
Anônima ──login Google/dev OK──▶ Autenticada
Autenticada ──"Sair"──▶ Anônima (storage limpo, sem aviso)
Autenticada ──expiresAt vencido (timer/guard/pré-request)──▶ Expirada ──▶ Anônima + /login?motivo=expirada
Autenticada ──API responde 401──▶ Expirada ──▶ Anônima + /login?motivo=expirada
Anônima ──acesso a rota interna──▶ /login (state.returnTo = rota original) ──login OK──▶ returnTo
```

## 4. Estado de interface

### Consulta (`QueryState`, por lista/detalhe)

```text
idle ──load()──▶ loading ──200 com itens──▶ success
                          ──200 sem itens (ou página além do fim)──▶ empty
                          ──NotFoundError──▶ notFound (só detalhes)
                          ──ApiError/NetworkError──▶ error ──"Tentar de novo"──▶ loading
```

Representado nos contextos por `loading: boolean`, `error: string | null`, dados correntes e `totalCount`; `empty` e `notFound` são derivados na página.

### Filtros (URL ↔ `OrderFilters`)

| Parâmetro na URL | Campo | Validação | Nome na API |
|---|---|---|---|
| `de` | `from` | `AAAA-MM-DD` válido | `from` |
| `ate` | `to` | `AAAA-MM-DD` válido; se `de` > `ate`, ambos ignorados | `to` |
| `tipo` | `kind` | ∈ `sale\|return\|cancellation` | `kind` |
| `canal` | `channel` | texto ≤ 60, `trim` | `channel` (trecho, case-insensitive — ajuste backend) |
| `busca` | `search` | texto ≤ 200, `trim` | `search` |
| `pagina` | `page` | inteiro ≥ 1; padrão 1 | `page` |
| (perfil) `:id` da rota | `customerId` | inteiro | `customerId` |

Valores inválidos são descartados silenciosamente e removidos da URL na próxima navegação (edge case "Filtro inválido no endereço"). Para `/clientes` valem só `busca` e `pagina`.

### CPF no perfil (`CpfReveal`)

```text
masked ──"mostrar"──▶ revealed ──"ocultar" | desmontar──▶ masked
```

Estado local do componente; não persiste.

## 5. Regras de exibição (funções puras em `lib/`)

| Função | Entrada | Saída | Requisito |
|---|---|---|---|
| `formatCurrency(n)` | `number` | `R$ 1.234,56` / `-R$ 1.234,56` | FR-019 |
| `parseDateOnly(s)` | `AAAA-MM-DD` | `Date` local (sem deslocamento) | FR-020 |
| `formatDateOnly(s)` | `AAAA-MM-DD` | `dd/mm/aaaa` | FR-020 |
| `formatDateTime(s)` | ISO date-time | `dd/mm/aaaa HH:mm` (uso eventual) | — |
| `orderKindLabel(k)` | `OrderInfo['kind']` | Venda / Devolução / Cancelamento | FR-021 |
| `initials(name)` | `string` | até 2 letras maiúsculas | FR-010 |
| `isNegativeKind(k)` | `OrderInfo['kind']` | `k !== 'sale'` | FR-018 |
| `parseOrderFilters(sp)` / `toSearchParams(f)` | `URLSearchParams` ⇄ `OrderFilters` | ver tabela acima | FR-015 |

## 6. Entidades da spec × tipos

| Entidade (spec) | Tipo(s) | Onde aparece |
|---|---|---|
| Vendedora | `UserInfo` | Cabeçalho (nome, e-mail) |
| Sessão | `AuthSession` | `localStorage`, `AuthContext` |
| Cliente | `CustomerInfo`, `CustomerDetailInfo`, `CustomerSummaryInfo` | `/clientes`, `/clientes/:id`, cliente no detalhe do pedido |
| Pedido | `OrderInfo`, `OrderDetailInfo` | `/pedidos`, `/clientes/:id` (lista), `/pedidos/:id` |
| Item do pedido | `OrderItemInfo` | `/pedidos/:id` |
