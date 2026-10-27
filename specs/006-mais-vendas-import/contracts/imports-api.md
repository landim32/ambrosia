# Contrato: API de importação do +Vendas

**Feature**: `006-mais-vendas-import` | Base: `{API}` (produção `https://ambrosiaai.site/api`, homolog `http://localhost:8080/api`). Todas as rotas exigem `Authorization: Bearer <JWT>`; `userId` vem só do claim `sub`. Recurso de outra vendedora responde `404` igual a inexistente (FR-053). Erros seguem `ErrorResult` (`sucesso`, `mensagem`, `erros[]{campo, mensagem}`). Enums em camelCase (`JsonStringEnumConverter`). Datas `AAAA-MM-DD`; instantes ISO-8601 UTC.

**Garantia transversal (FR-006)**: nenhuma resposta — inclusive `400`, `409`, `429` e `500` — contém `login`, `password` ou `accountHash`. Os DTOs de saída não têm esses campos.

## Rotas

| Método | Rota | Sucesso | Erros |
|---|---|---|---|
| GET | `/imports/mais-vendas/defaults` | 200 `ImportDefaultsInfo` | 401 |
| POST | `/imports/mais-vendas/runs` | 202 `ImportRunInfo` (`status: queued`) | 400 validação · 401 · 409 execução ativa · 429 bloqueio |
| GET | `/imports/mais-vendas/runs/current` | 200 `ImportRunDetailInfo` · 204 sem execução ativa | 401 |
| GET | `/imports/mais-vendas/runs/{runId}` | 200 `ImportRunDetailInfo` | 401 · 404 |
| GET | `/imports/mais-vendas/runs?page=1&pageSize=10` | 200 `PagedResult<ImportRunInfo>` | 400 · 401 |
| POST | `/imports/mais-vendas/runs/{runId}/cancel` | 202 `ImportRunDetailInfo` (`cancelRequested: true`) | 401 · 404 · 409 já finalizada |

## GET `/imports/mais-vendas/defaults`

```json
{
  "suggestedStartDate": "2026-10-20",
  "suggestedEndDate": "2026-10-27",
  "blockDays": 7,
  "maxRangeDays": 366,
  "lockedUntil": null
}
```

- `suggestedStartDate` = data do último pedido da vendedora com `source = maisVendas`; `null` quando não há (FR-002).
- `suggestedEndDate` = hoje em America/Sao_Paulo.
- `lockedUntil` ≠ `null` quando há bloqueio por credenciais recusadas (FR-008a): a tela desabilita o envio e mostra o horário.

## POST `/imports/mais-vendas/runs`

Corpo (`ImportRunStartInfo`; `password` documentado no Swagger como `format: password`):

```json
{ "login": "s482", "password": "…", "startDate": "2026-09-26", "endDate": "2026-10-25", "dryRun": false }
```

Respostas:

- `202` → `ImportRunInfo` com `status: "queued"`, `blocksTotal` já calculado (30 dias → 5 blocos), `counts` zerados.
- `400` → `erros[]` por campo: `login` ("Informe o login do +Vendas."), `password` ("Informe a senha do +Vendas."), `startDate` ("A data inicial deve ser anterior ou igual à data final."), `endDate` ("A data final não pode estar no futuro."), `endDate` ("O período não pode ultrapassar 366 dias."). As mensagens nunca repetem o valor enviado.
- `409` → `"Já existe importação em andamento."` (FR-016). A execução em curso não é afetada.
- `429` → `"Muitas tentativas com credenciais recusadas. Tente novamente às 14:45."` + cabeçalho `Retry-After: <segundos>` (FR-008a).

## `ImportRunInfo`

```json
{
  "id": 17, "mode": "apply", "status": "running",
  "startDate": "2026-09-26", "endDate": "2026-10-25", "blockDays": 7,
  "blocksTotal": 5, "blocksFinished": 2, "currentBlockNo": 3,
  "requestedAt": "2026-10-27T14:30:02Z", "startedAt": "2026-10-27T14:30:03Z", "finishedAt": null,
  "durationSeconds": null,
  "counts": { "inserted": 21, "updated": 0, "unchanged": 0, "missing": 0, "failed": 0, "conflicts": 0,
              "customersCreated": 15, "imagesStored": 30, "imagesFailed": 1 },
  "errorMessage": null
}
```

`status`: `queued` (aguardando vaga, FR-017) · `running` · `succeeded` · `failed` · `partial` · `cancelled` (regra FR-047a). `mode`: `dryRun` · `apply`.

## `ImportRunDetailInfo` (= `ImportRunInfo` +)

```json
{
  "blocks": [
    { "blockNo": 1, "startDate": "2026-09-26", "endDate": "2026-10-02", "status": "succeeded",
      "counts": { "inserted": 12, "updated": 0, "unchanged": 0, "missing": 0, "failed": 0, "conflicts": 0,
                  "customersCreated": 9, "imagesStored": 17, "imagesFailed": 0 },
      "errorMessage": null, "startedAt": "…", "finishedAt": "…" },
    { "blockNo": 3, "status": "failed", "errorMessage": "Falha ao gravar o pedido 41900017 de 2026-10-14: …",
      "counts": { "inserted": 14, "…": 0 }, "…": "…" },
    { "blockNo": 4, "status": "notExecuted", "…": "…" }
  ],
  "changes": [
    { "orderId": 903, "orderNumber": "41900001", "orderDate": "2026-09-28", "kind": "sale",
      "channel": "Linx", "origin": "cota", "action": "inserted", "changedFields": null, "errorMessage": null },
    { "orderId": 877, "orderNumber": "00004751", "orderDate": "2026-10-03", "kind": "sale",
      "channel": "Linx", "origin": "cota", "action": "updated", "changedFields": ["total", "status"], "errorMessage": null },
    { "orderId": null, "orderNumber": "70000003", "orderDate": "2026-10-05", "kind": "return",
      "channel": "Online", "origin": "omni", "action": "error", "changedFields": null,
      "errorMessage": "CPF ausente ou inválido na origem." }
  ],
  "breakdown": [
    { "channel": "Linx", "origin": "cota", "kind": "sale", "action": "inserted", "count": 30 },
    { "channel": "Online", "origin": "omni", "kind": "cancellation", "action": "unchanged", "count": 2 }
  ],
  "previousRunOrdersCount": 49,
  "countDropWarning": false
}
```

- `changes[].action`: `inserted` · `updated` · `unchanged` · `missing` (pedido importado antes e ausente da origem neste bloco — não apagado, FR-027) · `conflict` (pedido manual com a mesma chave, não sobrescrito, FR-028) · `error` (pedido com erro; os demais seguem, FR-036).
- `changes[].orderId` permite o link para `/pedidos/{orderId}` na tela (FR-049); é `null` em simulação e em `error`.
- `breakdown` agrupa `changes` por canal × origem × tipo × ação (FR-049).
- `previousRunOrdersCount`/`countDropWarning`: comparação com a última execução `succeeded`/`partial` em modo gravação do **mesmo período** (FR-051); `null`/`false` quando não há.
- Em `failed` por credenciais, conta diferente ou etapa não suportada: `blocks[*].status = notExecuted`, `counts` zerados, `errorMessage` ∈ {`"Credenciais recusadas pelo +Vendas."`, `"Conta do +Vendas diferente da vinculada."`, `"O login do +Vendas exige uma etapa não suportada (código ou captcha)."`}. Interrupção: `"Execução interrompida pela reinicialização do servidor."`.

## GET `/imports/mais-vendas/runs`

`PagedResult<ImportRunInfo>` (`items`, `page`, `pageSize`, `totalCount`) ordenado por `requestedAt` desc. `pageSize` 1–50 (padrão 10).

## POST `/imports/mais-vendas/runs/{runId}/cancel`

- `202` → detalhe com `cancelRequested` refletido em `status` ainda `running`; o worker encerra ao fim do bloco atual e o status vira `cancelled` (FR-019). Em `queued`, cancela de imediato.
- `409` → `"A execução já foi finalizada."` quando o status é final.

## Alterações em contratos existentes

- `GET /orders`, `GET /orders/{id}`: `OrderInfo`/`OrderDetailInfo` ganham `source` (`manual` | `maisVendas`); `OrderItemInfo` ganha `imageId` (`number | null`). Sem quebra: campos adicionais.
- `PUT /orders/{id}` continua permitido em pedidos importados; a reimportação sobrescreve os campos vindos da origem (clarificação 2 da spec).

## Contrato de UI (frontend)

| Rota SPA | Conteúdo |
|---|---|
| `/importacao` | formulário (login, senha `type=password autoComplete=off`, data inicial, data final, "Simular (não gravar)"), execução em andamento (progresso por bloco, polling 3 s) **ou** último relatório; histórico paginado |
| `/importacao?inicio=AAAA-MM-DD&fim=AAAA-MM-DD` | formulário pré-preenchido com as datas do bloco ("Repetir este bloco", FR-026); login e senha vazios |
| `/pedidos/{id}` | destino dos links do relatório |

Regras de tela: envio bloqueado com mensagem no campo (FR-003); senha limpa ao iniciar (FR-007); `failed`/`partial` em destaque (FR-050); aviso de queda de contagem (FR-051); "aguardando" visível quando `queued` (FR-017); redirecionamento ao login sem sessão (FR-001).
