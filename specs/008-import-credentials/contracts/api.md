# Contrato: API de credenciais e importação (008)

Todas as rotas exigem o JWT da API (`[Authorize]`); o usuário vem do `sub`. Erros no formato `ErrorResult` (`sucesso`, `mensagem`, `erros[]`).

## Credenciais do +Vendas

### `GET /imports/mais-vendas/credentials`

`200`:

```json
{ "login": "vendedora@exemplo.com", "hasPassword": true, "passwordChangedAt": "2026-10-28T20:15:00Z", "status": "valid" }
```

Sem credenciais: `{ "login": null, "hasPassword": false, "passwordChangedAt": null, "status": "none" }`. `status`: `none`, `valid`, `needsUpdate`. **Nunca** há campo de senha.

### `PUT /imports/mais-vendas/credentials`

```json
{ "login": "vendedora@exemplo.com", "password": "nova-senha" }
```

| Situação | Resposta |
|---|---|
| Sem credenciais, login e senha informados | `200` com o mesmo corpo do `GET` |
| Com credenciais, mesmo login, `password` ausente ou vazio | `200` (nada muda) |
| Com credenciais, senha nova | `200`, `status` volta a `valid`, `passwordChangedAt` atualizado |
| Login diferente do cadastrado sem `password` | `400` — "Informe a senha para trocar o login." |
| Sem credenciais e sem `password` | `400` — "Informe a senha do +Vendas." |
| `login` vazio ou com mais de 120 caracteres; `password` com mais de 200 | `400` |

O corpo da resposta e as mensagens de erro nunca repetem a senha.

### `DELETE /imports/mais-vendas/credentials`

`204`. Idempotente.

## Importação

### `GET /imports/mais-vendas/defaults` (alterado)

```json
{
  "suggestedStartDate": "2026-10-20",
  "suggestedEndDate": "2026-10-28",
  "blockDays": 7,
  "maxRangeMonths": 3,
  "lockedUntil": null,
  "hasCredentials": true,
  "credentialsStatus": "valid",
  "updateBase": { "available": true, "startDate": "2026-10-20", "endDate": "2026-10-28", "reason": null }
}
```

`updateBase.available = false` com `reason`: `"Ainda não há pedidos importados para atualizar."` ou `"A última importação foi há mais de 3 meses. Importe em partes de até 3 meses, escolhendo as datas."`. Sai `maxRangeDays`.

### `POST /imports/mais-vendas/runs` (alterado)

```json
{ "startDate": "2026-09-26", "endDate": "2026-10-25", "dryRun": false }
```

`202` com a execução, como hoje. Saem `login` e `password` (campo extra é ignorado e nunca gravado). Recusas novas:

| Situação | Resposta |
|---|---|
| Sem credenciais | `400` — "Cadastre as credenciais do +Vendas na configuração." |
| Período maior que 3 meses | `400` — "O período não pode passar de 3 meses." |

As demais recusas (datas, bloqueio `423`, execução ativa `409`) seguem a feature 006.

### `POST /imports/mais-vendas/runs/update-base` (novo)

```json
{ "dryRun": false }
```

`202` com a execução (`kind: "updateBase"`), período calculado no servidor: data do último pedido importado do +Vendas (inclusive) até hoje (São Paulo). Recusas: sem credenciais (`400`), sem pedido importado (`400`), intervalo maior que 3 meses (`400`), bloqueio (`423`), execução ativa (`409`).

### `ImportRunInfo` (alterado)

Ganha `startedVia` (`screen`, `mcp`) e `kind` (`period`, `updateBase`).
