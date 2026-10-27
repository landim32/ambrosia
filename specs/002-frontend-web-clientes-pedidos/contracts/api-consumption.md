# Contrato de consumo da API (HU 001) e ajuste de backend

**Feature**: `002-frontend-web-clientes-pedidos`. Fonte normativa dos endpoints: [`specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml`](../../001-api-cadastro-clientes-pedidos/contracts/openapi.yaml). Este documento fixa **o que** o frontend consome, **como** monta cada chamada e **como** interpreta as respostas.

## Base e cabeçalhos

- Base: `import.meta.env.VITE_API_URL` (padrão `/api`, relativo à origem). Em dev o Vite faz proxy de `/api` → `http://localhost:5000`; em Homolog/Produção o gateway remove o prefixo.
- Cabeçalhos: `Accept: application/json`; `Content-Type: application/json` em POST; `Authorization: Bearer <token>` em tudo exceto `/auth/*`.
- Query string: parâmetros `undefined`/vazios são omitidos; `pageSize` é sempre `20`.

## Endpoints consumidos

| Uso | Método e rota | Parâmetros enviados | Resposta esperada | Erros tratados |
|---|---|---|---|---|
| Login Google | `POST /auth/google` | body `{ idToken }` | `LoginResult` → `AuthSession` | 400 → toast com `mensagem` |
| Login dev (não produção) | `POST /auth/dev-login` | body `{ email, name }` | `LoginResult` | 400 → toast; 404 → "Entrada de desenvolvimento indisponível" |
| Lista de clientes | `GET /customers` | `search?`, `page`, `pageSize=20` | `PagedResult<CustomerInfo>` | 401 → sessão encerrada |
| Perfil do cliente | `GET /customers/{id}` | — | `CustomerDetailInfo` (inclui `cpf` completo e `summary`) | 404 → "Cliente não encontrado" (mesma tela para inexistente e de outra vendedora) |
| Pedidos do cliente | `GET /orders` | `customerId`, `page`, `pageSize=20` | `PagedResult<OrderInfo>` ordenado por data desc | 401 |
| Lista de pedidos | `GET /orders` | `from?`, `to?`, `kind?`, `channel?`, `search?`, `page`, `pageSize=20` | `PagedResult<OrderInfo>` | 400 (filtro recusado) → toast com `mensagem` |
| Detalhe do pedido | `GET /orders/{id}` | — | `OrderDetailInfo` (cliente com `cpfMasked`, itens por `lineNo`) | 404 → "Pedido não encontrado" |

**Não consumidos nesta feature**: `POST/PUT /customers`, `POST/PUT /orders` (somente leitura — FR-030). `GET /users/me` fica disponível para revalidar a sessão, mas o cabeçalho usa `session.user` guardado no login.

## Interpretação de respostas

| HTTP | Corpo | Mapeamento no `apiClient` |
|---|---|---|
| 200 | DTO puro (sem wrapper `sucesso`) | retorna o JSON tipado |
| 400 / 409 | `ErrorResult` `{ sucesso:false, mensagem, erros[] }` | `ApiError(status, mensagem, erros)` |
| 401 | vazio | `UnauthorizedError` → `onUnauthorized()` (logout com motivo "expirada") |
| 404 | `ErrorResult` ou vazio (`/auth/dev-login` fora de Development/Homolog) | `NotFoundError(mensagem?)` |
| 500 | `text/plain` com a mensagem da exceção | `ApiError(500, texto)` |
| falha de rede | — | `NetworkError` → "Não foi possível conectar" |

Datas `format: date` (`orderDate`, `originalSaleOn`, `cancelledOn`, `lastPurchase`) são tratadas como **dia-calendário** (`parseDateOnly`), nunca como instante.

## Ajuste de backend incluído nesta feature

**Motivo**: FR-014 exige filtro de canal por **trecho** sem distinguir maiúsculas; a implementação atual compara por igualdade.

| Arquivo | Mudança |
|---|---|
| `backend/Ambrosia.Infra/Repository/OrderRepository.cs` (~linha 90) | `o.Channel.ToLower() == channel` → `o.Channel.ToLower().Contains(channel)` |
| `specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml` (`/orders` → parâmetro `channel`) | descrição: `Igualdade case-insensitive` → `Trecho do canal (case-insensitive)` |
| `backend/Ambrosia.Tests/.../OrderRepositoryTest.cs` | novo caso: `channel=lin` casa `Linx`; `channel=LINX` continua casando; `channel=xyz` não casa |
| Documentação Swagger (`<param>` do `OrdersController`, se houver) | mesma redação |

Compatibilidade: quem enviava o canal completo continua obtendo os mesmos resultados (igualdade é um caso particular de trecho). Nenhuma migração de banco.

### Trecho atualizado do contrato OpenAPI (delta)

```yaml
# paths./orders.get.parameters
- { name: channel, in: query, schema: { type: string }, description: Trecho do canal (case-insensitive) }
```
