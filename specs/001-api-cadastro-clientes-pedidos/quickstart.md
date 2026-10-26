# Quickstart — validação ponta a ponta

**Feature**: `001-api-cadastro-clientes-pedidos`
Guia para provar que a API funciona de ponta a ponta. Contrato completo em [contracts/openapi.yaml](./contracts/openapi.yaml); tabelas e regras em [data-model.md](./data-model.md).

## Pré-requisitos

- .NET SDK 8.0 (`dotnet --version` → 8.x)
- PostgreSQL 16+ **instalado localmente** (sem Docker, conforme constituição) com um banco `ambrosia_dev`
- Ferramenta EF: `dotnet tool install --global dotnet-ef --version 9.*`
- `curl` (ou o Swagger UI)

## Configuração (PowerShell)

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__AmbrosiaContext = 'Host=localhost;Port=5432;Database=ambrosia_dev;Username=postgres;Password=postgres'
$env:Auth__JwtSecret = 'dev-secret-key-with-at-least-32-characters!!'
$env:Auth__GoogleClientId = 'SEU_CLIENT_ID.apps.googleusercontent.com'   # só para /auth/google
$env:CpfProtection__Pepper = 'dev-pepper-change-me'
$env:CpfProtection__EncryptionKey = [Convert]::ToBase64String((1..32 | ForEach-Object { [byte]$_ }))  # 32 bytes; em produção use chave aleatória
```

> `appsettings.Development.json` já traz valores de desenvolvimento equivalentes; as variáveis acima só são necessárias para sobrescrevê-los.

## Subir a API

```powershell
cd backend
dotnet build
dotnet ef database update --project Ambrosia.Infra --startup-project Ambrosia.API
dotnet run --project Ambrosia.API
```

Esperado: API em `http://localhost:5000`, Swagger em `http://localhost:5000/swagger` com o botão **Authorize** (Bearer). As 4 tabelas (`users`, `customers`, `orders`, `order_items`) existem no banco.

## Cenário 1 — Autenticação (US1, SC-004)

```powershell
# Sem token → 401
curl -i http://localhost:5000/customers

# Dev login (apenas Development) → 200 com token
$a = curl -s -X POST http://localhost:5000/auth/dev-login -H "Content-Type: application/json" `
  -d '{"email":"ana@exemplo.com","name":"Ana"}' | ConvertFrom-Json
$TA = $a.token

# Perfil → 200, sem campo de senha
curl -s http://localhost:5000/users/me -H "Authorization: Bearer $TA"
```

Esperado: 401 sem token; `users/me` devolve `id`, `email`, `name`, `provider: "dev"` e **nenhum** campo `password*`. Repetir o dev-login com o mesmo e-mail devolve o mesmo `user.id` (sem duplicar).

Para o fluxo real: obter um ID token do Google (ex.: OAuth 2.0 Playground com o `GoogleClientId` configurado) e chamar `POST /auth/google {"idToken":"..."}` — mesmo resultado.

## Cenário 2 — Clientes (US2)

```powershell
$H = @{ Authorization = "Bearer $TA"; "Content-Type" = "application/json" }

# CPF inválido → 400 com campo "cpf"
curl -s -X POST http://localhost:5000/customers -H "Authorization: Bearer $TA" -H "Content-Type: application/json" `
  -d '{"name":"Maria","cpf":"111.111.111-11"}'

# CPF válido → 201 (Location: /customers/{id})
$c = curl -s -X POST http://localhost:5000/customers -H "Authorization: Bearer $TA" -H "Content-Type: application/json" `
  -d '{"name":"Maria Silva","cpf":"529.982.247-25"}' | ConvertFrom-Json
$CID = $c.id

# Mesmo CPF de novo → 409
curl -i -X POST http://localhost:5000/customers -H "Authorization: Bearer $TA" -H "Content-Type: application/json" `
  -d '{"name":"Outra","cpf":"52998224725"}'

# Listagem com busca → CPF mascarado
curl -s "http://localhost:5000/customers?search=mar&page=1&pageSize=10" -H "Authorization: Bearer $TA"

# Detalhe → CPF completo + summary zerado
curl -s http://localhost:5000/customers/$CID -H "Authorization: Bearer $TA"

# Atualizar nome → 200
curl -s -X PUT http://localhost:5000/customers/$CID -H "Authorization: Bearer $TA" -H "Content-Type: application/json" -d '{"name":"Maria S. Souza"}'
```

Esperado: listagem mostra `cpfMasked: "***.982.247-**"` e nunca o CPF completo (SC-005); detalhe mostra `cpf: "529.982.247-25"` e `summary.purchases = 0`.

## Cenário 3 — Pedidos (US3, SC-006)

```powershell
# Venda com 2 itens → 201
$body = @"
{"customerId":$CID,"orderNumber":"00004751","orderDate":"2026-10-24","kind":"sale","channel":"Linx","origin":"cota","status":"Faturado","total":589.80,
 "items":[{"productName":"Calça Inez","skuRef":"42CA500UIE_12182","size":"38","price":399.90},
          {"productName":"Blusa Lia","skuRef":"41BL200AAA_00001","size":"P","price":189.90}]}
"@
$o = curl -s -X POST http://localhost:5000/orders -H "Authorization: Bearer $TA" -H "Content-Type: application/json" -d $body | ConvertFrom-Json
$OID = $o.id

# Venda com total negativo → 400 (campo "total")
# Pedido sem itens → 400 (campo "items")
# Data futura → 400 (campo "orderDate")

# Cancelamento com total negativo e datas → 201
$cancel = @"
{"customerId":$CID,"orderNumber":"00004751","orderDate":"2026-10-25","kind":"cancellation","channel":"Linx","status":"Cancelado","total":-589.80,
 "originalSaleOn":"2026-10-24","cancelledOn":"2026-10-25",
 "items":[{"productName":"Calça Inez","skuRef":"42CA500UIE_12182","size":"38","price":-399.90}]}
"@
curl -s -X POST http://localhost:5000/orders -H "Authorization: Bearer $TA" -H "Content-Type: application/json" -d $cancel

# Mesmo número + data + tipo de novo → 409
curl -i -X POST http://localhost:5000/orders -H "Authorization: Bearer $TA" -H "Content-Type: application/json" -d $body

# Listagem com filtros, ordenada por data desc
curl -s "http://localhost:5000/orders?from=2026-10-01&to=2026-10-31&kind=sale&channel=linx&customerId=$CID&page=1&pageSize=20" -H "Authorization: Bearer $TA"

# Detalhe → cliente + itens numerados (lineNo 1, 2)
curl -s http://localhost:5000/orders/$OID -H "Authorization: Bearer $TA"

# Atualizar status/total → 200
curl -s -X PUT http://localhost:5000/orders/$OID -H "Authorization: Bearer $TA" -H "Content-Type: application/json" -d '{"status":"Faturado","total":579.80}'

# Resumo do cliente agora: purchases=1, netTotal=579.80-589.80=-10.00, lastPurchase=2026-10-24
curl -s http://localhost:5000/customers/$CID -H "Authorization: Bearer $TA"
```

Esperado: os exemplos do doc 01 (venda, cancelamento com total negativo e datas) são aceitos sem perda de informação; `summary` reflete os totais líquidos.

## Cenário 4 — Isolamento entre usuários (US4, SC-002)

```powershell
$b = curl -s -X POST http://localhost:5000/auth/dev-login -H "Content-Type: application/json" -d '{"email":"bia@exemplo.com","name":"Bia"}' | ConvertFrom-Json
$TB = $b.token

curl -s http://localhost:5000/customers -H "Authorization: Bearer $TB"          # totalCount = 0
curl -i http://localhost:5000/customers/$CID -H "Authorization: Bearer $TB"      # 404
curl -i http://localhost:5000/orders/$OID -H "Authorization: Bearer $TB"         # 404
curl -i -X PUT http://localhost:5000/customers/$CID -H "Authorization: Bearer $TB" -H "Content-Type: application/json" -d '{"name":"Hack"}'  # 404
curl -i -X POST http://localhost:5000/orders -H "Authorization: Bearer $TB" -H "Content-Type: application/json" -d $body                      # 404 (cliente de A)

# Mesmo CPF para B → 201 (cliente independente)
curl -s -X POST http://localhost:5000/customers -H "Authorization: Bearer $TB" -H "Content-Type: application/json" -d '{"name":"Maria (de Bia)","cpf":"529.982.247-25"}'
```

Esperado: 100% das tentativas de B sobre dados de A respondem 404 ou lista vazia; o mesmo CPF gera clientes distintos por usuário.

## Testes automatizados

```powershell
cd backend
dotnet test Ambrosia.Tests
```

Esperado: todos os testes verdes (value object `Cpf`, validators, serviços com Moq, `CpfProtectionAppService` round-trip, `JwtTokenAppService`, `OrderRepository` com InMemory cobrindo filtros e isolamento).

Opcional (`Ambrosia.ApiTests`, com a API no ar): `$env:ApiBaseUrl='http://localhost:5000'; dotnet test Ambrosia.ApiTests`.

## Desempenho (SC-003)

Com ~10.000 pedidos inseridos para um usuário (script de carga nas tasks), `GET /orders?page=1&pageSize=20` e `GET /customers?search=a` devem responder em < 1 s; verificar com `EXPLAIN ANALYZE` que as consultas usam `ix_orders_user_date` / `ix_customers_user_name`.

## Checklist de aceite

- [X] 401 em todas as rotas de clientes/pedidos sem token
- [X] Mesmo e-mail → mesmo usuário; perfil sem senha
- [X] CPF inválido 400; duplicado 409; listagem só mascarado; detalhe completo
- [X] Sinal do total por tipo; ≥ 1 item; data futura recusada; duplicidade (número, data, tipo) 409
- [X] Filtros e ordenação por data desc; página além do fim → `items: []`
- [X] Usuário B não vê/altera nada de A (404); mesmo CPF em A e B → clientes distintos
- [X] Swagger acessível com Bearer
