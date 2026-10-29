# Data Model: Credenciais do +Vendas guardadas, importação pelo plugin e "Atualizar base" (008)

Migração única: **`AddMaisVendasCredentials`**. Convenções do Princípio V (snake_case, `bigint`, `timestamp without time zone`, sem cascata).

## 1. `mais_vendas_accounts` (existente, feature 006) — colunas novas

| Coluna | Tipo | Nulo | Regra |
|---|---|---|---|
| `login_encrypted` | `text` | sim | `v1:` + base64(nonce ‖ tag ‖ cifra), AES-256-GCM, dados associados `mais-vendas:{user_id}:login` |
| `password_encrypted` | `text` | sim | Idem, dados associados `mais-vendas:{user_id}:password` |
| `credentials_saved_at` | `timestamp without time zone` | sim | Última gravação de login ou senha (UTC) |
| `password_changed_at` | `timestamp without time zone` | sim | Última troca de senha (UTC) — é a data que a tela mostra |
| `credentials_status` | `integer` | não, padrão 0 | 0 = sem credenciais, 1 = válidas, 2 = precisam ser atualizadas |

Regras:

- `login_encrypted` e `password_encrypted` são ambos nulos ou ambos preenchidos.
- Remover as credenciais zera as cinco colunas (status 0); o vínculo da conta (`account_hash`) e o bloqueio por recusas não mudam.
- Leitura sempre por `user_id` (1:1 com a vendedora, Princípio VI).

### Transições de `credentials_status`

| De | Evento | Para |
|---|---|---|
| 0 | vendedora salva login e senha | 1 |
| 1 ou 2 | vendedora troca a senha (com ou sem novo login) | 1 |
| 1 | +Vendas recusa o login numa execução | 2 |
| 2 | +Vendas aceita o login numa execução | 1 |
| 1 ou 2 | vendedora remove as credenciais | 0 |

## 2. `import_runs` (existente, feature 006) — colunas novas

| Coluna | Tipo | Nulo | Regra |
|---|---|---|---|
| `started_via` | `integer` | não, padrão 1 | 1 = tela, 2 = plugin do ChatGPT |
| `kind` | `integer` | não, padrão 1 | 1 = período escolhido, 2 = atualizar base |

Execuções antigas ficam com os padrões (tela, período). Nenhuma credencial é gravada na execução.

## 3. Objetos sem persistência

| Objeto | Campos | Uso |
|---|---|---|
| `MaisVendasCredentialsInfo` (DTO de saída) | `login`, `hasPassword`, `passwordChangedAt`, `status` | `GET /imports/mais-vendas/credentials`. Sem campo de senha |
| `MaisVendasCredentialsUpdateInfo` (DTO de entrada) | `login`, `password?` | `PUT`. `password` obrigatório sem credenciais ou quando o login muda |
| `ImportRunStartInfo` (alterado) | `startDate`, `endDate`, `dryRun` | Saem `login` e `password` |
| `ImportUpdateBaseInfo` (novo) | `dryRun` | `POST …/runs/update-base` |
| `ImportDefaultsInfo` (alterado) | + `hasCredentials`, `credentialsStatus`, `maxRangeMonths`, `updateBase { available, startDate, endDate, reason }`; sai `maxRangeDays` | Tela de importação e plugin |
| `ImportJob` (alterado) | sai `Login`/`Password` | O worker decifra as credenciais ao iniciar a execução |

## 4. Configuração

| Chave | Exemplo | Regra |
|---|---|---|
| `MaisVendas__CredentialsKey` | base64 de 32 bytes | Obrigatória em todos os ambientes; API e host do MCP recusam o startup sem ela. Variável `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY` nos composes e no pipeline |
| `MaisVendas__MaxRangeMonths` | `3` | Substitui `MaisVendas__MaxRangeDays` |
