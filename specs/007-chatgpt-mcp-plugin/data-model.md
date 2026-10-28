# Data Model: Plugin do Ambrosio no ChatGPT

**Feature**: `007-chatgpt-mcp-plugin` | **Data**: 2026-10-27 | **Base**: [spec.md](spec.md), [research.md](research.md)

Convenções do Princípio V: tabelas em snake_case no plural, chave `{entidade}_id` `bigint identity`, constraint `{tabela}_pkey`, FK `fk_{pai}_{filho}`, `ClientSetNull` (sem cascata), `timestamp without time zone` em UTC, `varchar` com tamanho máximo, enums em `integer`.

Migração: **`AddOAuthServer`** no `AmbrosioContext`, aplicada pelo serviço `migrator` como as anteriores.

---

## 1. Entidades novas

### 1.1 `oauth_applications`: clientes do login (ChatGPT)

Entidade do OpenIddict com chave `long`, mapeada por Fluent API. Uma linha por cliente: uma para cada `client_id` CIMD visto (por exemplo, `https://chatgpt.com/oauth/client.json`) e uma por registro DCR.

| Coluna | Tipo | Regra |
|---|---|---|
| `oauth_application_id` | bigint identity | PK `oauth_applications_pkey` |
| `client_id` | varchar(512) | Único. URL `https` no CIMD; identificador gerado no DCR |
| `client_type` | varchar(50) | `public` (autenticação `none`) ou `confidential` (`private_key_jwt`) |
| `display_name` | varchar(200) | Nome mostrado no consentimento ("ChatGPT") |
| `redirect_uris` | text | JSON. Só URIs de `Auth:AllowedRedirectUris` ou do documento CIMD |
| `permissions` | text | JSON (endpoints, grant types, escopos permitidos) |
| `json_web_key_set` | text, nulo | JWKS do documento CIMD, quando `private_key_jwt` |
| `registration_source` | integer | 1 = CIMD, 2 = DCR |
| `metadata_fetched_at` | timestamp, nulo | Última leitura do documento CIMD (cache) |
| `concurrency_token` | varchar(50) | Do OpenIddict |
| `created_at` / `updated_at` | timestamp | — |

Regras: cliente DCR nunca expira (FR-008, critério de reconexão sem `invalid_client`). Cliente CIMD é recriado ou atualizado quando o documento muda.

### 1.2 `oauth_authorizations`: consentimentos da vendedora

| Coluna | Tipo | Regra |
|---|---|---|
| `oauth_authorization_id` | bigint identity | PK |
| `oauth_application_id` | bigint, nulo | FK `fk_oauth_application_oauth_authorization`, `ClientSetNull` |
| `subject` | varchar(400) | `user_id` da vendedora em texto (`sub`) |
| `scopes` | text | JSON com os escopos concedidos |
| `status` | varchar(50) | `valid` ou `revoked` |
| `type` | varchar(50) | `permanent` (consentimento lembrado) ou `ad-hoc` |
| `creation_date` | timestamp | — |
| `concurrency_token` | varchar(50) | — |

Estados: `valid` → `revoked` (revogação pela vendedora ou reuso de refresh token). Uma autorização revogada invalida todos os tokens ligados a ela (FR-006).

### 1.3 `oauth_tokens`: códigos, access tokens e refresh tokens

| Coluna | Tipo | Regra |
|---|---|---|
| `oauth_token_id` | bigint identity | PK |
| `oauth_application_id` | bigint, nulo | FK `fk_oauth_application_oauth_token`, `ClientSetNull` |
| `oauth_authorization_id` | bigint, nulo | FK `fk_oauth_authorization_oauth_token`, `ClientSetNull` |
| `subject` | varchar(400) | `sub` |
| `type` | varchar(50) | `authorization_code`, `access_token`, `refresh_token` |
| `status` | varchar(50) | `valid`, `redeemed`, `revoked`, `inactive` |
| `reference_id` | varchar(100), nulo | Hash do token por referência (códigos e refresh tokens) |
| `payload` | text, nulo | Do OpenIddict |
| `creation_date` / `expiration_date` / `redemption_date` | timestamp, nulo | Access token 30 min; refresh 14 dias, deslizante |
| `concurrency_token` | varchar(50) | — |

Estados: `valid` → `redeemed` (código e refresh usados uma vez); `redeemed` reutilizado → revoga a família (R14). Linhas vencidas são removidas pela limpeza periódica (`PruneAsync`).

### 1.4 `oauth_scopes`: permissões anunciadas

Escopos registrados na inicialização (semente idempotente): `openid`, `email`, `profile`, `offline_access`, `customers:read`, `customers:write`, `orders:read`, `orders:write`, `images:write`, `imports:read`, `imports:write`. Cada um com `display_name` e `description` em português, usados na tela de consentimento (FR-003, FR-004).

| Coluna | Tipo | Regra |
|---|---|---|
| `oauth_scope_id` | bigint identity | PK |
| `name` | varchar(200) | Único |
| `display_name` / `description` | varchar(400) | pt-BR |
| `resources` | text | JSON: `["https://ambrosiaai.site/mcp"]` |

### 1.5 `mcp_audit_entries`: auditoria das gravações pelo plugin

| Coluna | Tipo | Regra |
|---|---|---|
| `mcp_audit_entry_id` | bigint identity | PK `mcp_audit_entries_pkey` |
| `user_id` | bigint | FK `fk_user_mcp_audit_entry`, `ClientSetNull`. Vem do `sub` |
| `tool_name` | varchar(100) | Nome da tool (`cadastrar_cliente`…) |
| `client_id` | varchar(512), nulo | Cliente OAuth que chamou |
| `arguments` | text | JSON **sanitizado**: CPF mascarado, sem senha, sem conteúdo de arquivo, sem URL assinada |
| `outcome` | integer | 1 = gravado, 2 = recusado por regra de negócio, 3 = recusado por escopo, 4 = erro inesperado |
| `result_summary` | varchar(500), nulo | Id criado/alterado ou mensagem de erro em português |
| `created_at` | timestamp | UTC |

Regras: grava em toda chamada de escrita **confirmada** (`confirmar: true`) e nas recusas; prévias (`confirmar: false`) não são auditadas. Índice `ix_mcp_audit_entries_user_created` em (`user_id`, `created_at` desc). Leitura filtrada por `user_id` (Princípio VI).

---

## 2. Entidades existentes reaproveitadas (sem mudança de esquema)

| Entidade | Uso no plugin |
|---|---|
| `users` | Login no AS (e-mail e senha, Google). `sub` do token = `user_id`. `perfil_ambrosio` devolve `user_id` como `id` estável |
| `customers` | Tools de clientes e consultas prontas. CPF só mascarado nas saídas; busca por CPF pelo hash com pepper |
| `orders` / `order_items` | Tools de pedidos, histórico, resumo do período, devoluções e cancelamentos. `captured_at` alimenta `capturado_em` |
| `stored_images` | Tools de imagens (`ImageSource.ChatGpt` para gravações pelo plugin) |
| `import_runs` / `import_run_blocks` / `import_run_changes` | Tools de importação (acompanhar, relatório, histórico, cancelar) |

## 3. Objetos sem persistência

| Objeto | Descrição |
|---|---|
| **Prévia de gravação** | Saída de toda tool de escrita com `confirmar: false`: os dados normalizados que seriam gravados, os avisos de validação e a frase "Nada foi gravado. Confirme para gravar." |
| **Resultado de tool** | `structuredContent` tipado + item de texto curto em pt-BR. Sempre com `consultado_em` (UTC); listas com `total`, `tem_mais` e no máximo 200 itens |
| **Desafio de autorização** | Resultado com `isError: true` e `_meta["mcp/www_authenticate"]` (`Bearer resource_metadata="…", error="…", error_description="…", scope="…"`) |
| **Documento CIMD** | Metadados do cliente baixados de `client_id` (cache pelos headers HTTP), validados antes de qualquer autorização |

## 4. Consultas novas no domínio

| Consulta | Regra |
|---|---|
| Clientes inativos | Clientes do usuário com última compra há pelo menos `dias`, com filtros opcionais de número mínimo de compras e de total líquido mínimo; ordenados por dias sem compra; máximo 200 |
| Resumo do período | Para `inicio`–`fim` (padrão: mês de cota atual, dia 26 a 25): total líquido, atendimentos (vendas), peças, PA = peças / atendimentos, VA = valor / atendimentos (regras do doc 02) |
| Devoluções e cancelamentos | Contagem e soma por tipo no período |

Todas recebem `userId` como primeira cláusula (Princípio VI).
