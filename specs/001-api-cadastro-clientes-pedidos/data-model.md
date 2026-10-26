# Data Model — API de cadastro e consulta de clientes e pedidos

**Feature**: `001-api-cadastro-clientes-pedidos` | **Date**: 2026-10-25
**Convenções**: constituição §V (snake_case, PK `{entidade}_id` bigint identity, `timestamp without time zone`, `varchar(n)`, enums `integer`, `ClientSetNull`, sem cascade). Divergências em relação ao doc 02 estão listadas no fim.

## Diagrama

```text
users 1 ──── * customers 1 ──── * orders 1 ──── * order_items
  │                                 ▲
  └─────────────────────────────────┘  (orders.user_id — redundante por projeto, para
                                         isolamento e índices sem join)
```

## Entidades

### User (`users`)

Pessoa que acessa a API. Criado automaticamente no primeiro login OAuth (ou dev-login).

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| `user_id` | bigint identity | não | PK `users_pkey` |
| `email` | varchar(260) | não | Único (`ux_users_email`), armazenado em minúsculas, normalizado (`Trim().ToLowerInvariant()`) |
| `name` | varchar(200) | não | Vem do provedor; pode ser vazio → usa a parte local do e-mail |
| `password_hash` | varchar(260) | sim | Só existe se houver cadastro local (fora do escopo); **nunca** mapeado para DTO |
| `provider` | integer | não | `AuthProvider`: `Google=1`, `Dev=2` |
| `provider_subject` | varchar(260) | sim | `sub` do provedor (Google) |
| `created_at` | timestamp without time zone | não | UTC |
| `updated_at` | timestamp without time zone | não | UTC; atualizado a cada login (nome/subject podem mudar) |

Modelo `User` (Domain): `Id, Email, Name, PasswordHash, Provider, ProviderSubject, CreatedAt, UpdatedAt`. Método `UpdateFromProvider(name, subject)`.

### Customer (`customers`)

Comprador atendido por **um** usuário. O mesmo CPF pode existir para usuários diferentes.

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| `customer_id` | bigint identity | não | PK `customers_pkey` |
| `user_id` | bigint | não | FK `fk_user_customer` → `users`, `ClientSetNull` |
| `name` | varchar(200) | não | Obrigatório, 2–200 caracteres |
| `cpf_hash` | varchar(64) | não | HMAC-SHA256 hex do CPF normalizado (research §4) |
| `cpf_encrypted` | varchar(120) | não | AES-256-GCM base64 |
| `cpf_masked` | varchar(14) | não | `***.456.789-**` |
| `created_at` | timestamp without time zone | não | UTC |
| `updated_at` | timestamp without time zone | não | UTC |

Índices: `ux_customers_user_cpf (user_id, cpf_hash)` único; `ix_customers_user_name (user_id, name)`.

Modelo `Customer` (Domain): `Id, UserId, Name, CpfHash, CpfEncrypted, CpfMasked, CreatedAt, UpdatedAt`, navegação `Orders`. Método `UpdateName(string)` (valida não vazio).

Validações (FR-006, FR-007, FR-011): nome obrigatório; CPF obrigatório e válido (`Cpf.TryParse`); duplicidade por `(user_id, cpf_hash)` → 409.

### Order (`orders`)

Venda, devolução ou cancelamento de um cliente, registrado por um usuário.

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| `order_id` | bigint identity | não | PK `orders_pkey` |
| `user_id` | bigint | não | FK `fk_user_order` → `users`, `ClientSetNull`; **deve** ser igual ao `user_id` do cliente |
| `customer_id` | bigint | não | FK `fk_customer_order` → `customers`, `ClientSetNull` (exclusão de cliente com pedidos falha no banco — edge case da spec) |
| `order_number` | varchar(60) | não | Ex.: `00004751`, `v1161148crb-01`; não é único sozinho |
| `order_date` | date | não | Não pode ser futura |
| `kind` | integer | não | `OrderKind`: `Sale=1`, `Return=2`, `Cancellation=3` |
| `channel` | varchar(60) | sim | Texto livre (Linx, Online, Vitrine, Box, Vintage, …) |
| `origin` | integer | sim | `OrderOrigin`: `Cota=1`, `Omni=2` |
| `status` | varchar(60) | sim | Texto livre (Faturado, Cancelado, …) |
| `total` | numeric(12,2) | não | `> 0` se `kind = Sale`; `< 0` se `Return`/`Cancellation`; nunca 0 |
| `original_sale_on` | date | sim | Cancelamento: data da venda original |
| `cancelled_on` | date | sim | Cancelamento: data do cancelamento |
| `external_id` | varchar(60) | sim | 4º segmento da URL do +Vendas (a confirmar, doc 01) |
| `created_at` | timestamp without time zone | não | UTC |
| `updated_at` | timestamp without time zone | não | UTC |

Índices: `ux_orders_user_number_date_kind (user_id, order_number, order_date, kind)` único (FR-014); `ix_orders_user_date (user_id, order_date DESC)`; `ix_orders_user_customer (user_id, customer_id)`.

Modelo `Order` (Domain): todos os campos acima + navegações `Customer`, `Items`. Métodos: `AddItem(OrderItem)` (atribui `LineNo` sequencial a partir de 1), `UpdateStatusAndTotal(status, total)` (revalida o sinal do total contra `Kind`), `static bool IsTotalSignValid(kind, total)`.

Validações (FR-012 a FR-016): cliente obrigatório e do mesmo usuário (serviço); número obrigatório; data obrigatória e ≤ hoje; `kind` válido; sinal do total conforme tipo; ≥ 1 item; duplicidade `(user_id, order_number, order_date, kind)` → 409. `original_sale_on`/`cancelled_on` aceitos para qualquer tipo, mas o validator alerta apenas se `cancelled_on < original_sale_on`.

### OrderItem (`order_items`)

Linha de um pedido.

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| `order_item_id` | bigint identity | não | PK `order_items_pkey` |
| `order_id` | bigint | não | FK `fk_order_order_item` → `orders`, `ClientSetNull` |
| `line_no` | integer | não | Sequencial a partir de 1 dentro do pedido |
| `product_name` | varchar(200) | sim | Ex.: `Calça Inez` |
| `sku_ref` | varchar(60) | sim | Ex.: `42CA500UIE_12182` |
| `size` | varchar(20) | sim | Ex.: `38`, `P` |
| `price` | numeric(12,2) | sim | Negativo em devolução/cancelamento (espelha a origem); sem validação de sinal |
| `image_url` | varchar(500) | sim | URL de origem, apenas referência |

Índices: `ux_order_items_order_line (order_id, line_no)` único; `ix_order_items_sku (sku_ref)`.

Modelo `OrderItem` (Domain): `Id, OrderId, LineNo, ProductName, SkuRef, Size, Price, ImageUrl`.

Validações (FR-015): ao menos um de `productName`/`skuRef` informado; `imageUrl`, se presente, deve ser URL absoluta http/https.

## Projeções (não persistidas)

### CustomerSummary (FR-009)

Calculado em `CustomerRepository.GetSummaryAsync(customerId, userId)` sobre `orders` do cliente:

| Campo | Cálculo |
|---|---|
| `purchases` | `COUNT(*) WHERE kind = Sale` |
| `netTotal` | `SUM(total)` (0 se não houver pedidos) |
| `lastPurchase` | `MAX(order_date) WHERE kind = Sale` (null se nunca comprou) |
| `daysSinceLastPurchase` | `hoje − lastPurchase` (null se nunca comprou) |

## Estados e transições

Não há máquina de estados formal nesta entrega. `status` é texto livre atualizável (FR-019). `kind` é imutável após a criação (um cancelamento é um **novo** pedido com total negativo, como no +Vendas). Clientes e pedidos não são excluídos.

## Mapeamento DTO ↔ Modelo (manual, no serviço)

| DTO | Direção | Observações |
|---|---|---|
| `UserInfo { id, email, name, provider, createdAt }` | saída | Nunca inclui `passwordHash` |
| `CustomerInsertInfo { name, cpf }` | entrada | `cpf` em qualquer formatação; serviço normaliza e protege |
| `CustomerUpdateInfo { name }` | entrada | — |
| `CustomerInfo { id, name, cpfMasked, createdAt, updatedAt }` | saída (listagem) | CPF só mascarado |
| `CustomerDetailInfo : CustomerInfo + { cpf, summary }` | saída (detalhe) | `cpf` decifrado e formatado `123.456.789-09` |
| `CustomerSummaryInfo { purchases, netTotal, lastPurchase, daysSinceLastPurchase }` | saída | — |
| `OrderInsertInfo { customerId, orderNumber, orderDate, kind, channel, origin, status, total, originalSaleOn, cancelledOn, externalId, items[] }` | entrada | `items` ≥ 1 |
| `OrderItemInsertInfo { productName, skuRef, size, price, imageUrl }` | entrada | `lineNo` atribuído pelo modelo |
| `OrderUpdateInfo { status, total }` | entrada | — |
| `OrderInfo { id, customerId, customerName, orderNumber, orderDate, kind, channel, origin, status, total, itemsCount, createdAt, updatedAt }` | saída (listagem) | — |
| `OrderDetailInfo : OrderInfo + { originalSaleOn, cancelledOn, externalId, customer: CustomerInfo, items: OrderItemInfo[] }` | saída (detalhe) | Cliente com CPF mascarado |
| `OrderItemInfo { id, lineNo, productName, skuRef, size, price, imageUrl }` | saída | — |
| `PagedResult<T> { items, page, pageSize, totalCount }` | saída | — |
| `ErrorResult { sucesso, mensagem, erros: FieldError[] }` / `FieldError { campo, mensagem }` | saída (erros) | Chaves em português (constituição) |

Todos os DTOs têm `[JsonPropertyName("camelCase")]`.

## Divergências em relação ao doc 02 (registradas na spec, Assumptions)

| Doc 02 | Este modelo | Motivo |
|---|---|---|
| `salesperson` | `users` | O conceito de vendedora é o próprio usuário autenticado |
| `SERIAL id` | `bigint identity {entidade}_id` | Constituição §V |
| `ON DELETE CASCADE` em `order_item` | `ClientSetNull`, sem endpoints de exclusão | Constituição §V |
| `TIMESTAMPTZ` | `timestamp without time zone` (UTC) | Constituição §V |
| `kind TEXT CHECK (...)` | `kind integer` (enum) | Constituição §V |
| `customer.cpf_hash UNIQUE` global | único por `(user_id, cpf_hash)` | US4 cenário 4 |
| `source_hash`, `captured_at` em `sales_order` | ausentes | Pertencem ao bot (doc 03), fora do escopo |
| `sales_period`, `period_kpi`, `raw_snapshot`, `product_image`, fase 2 | ausentes | Fora do escopo desta entrega |
| `v_customer_summary` (view) | agregação LINQ no repositório | research §10 |
