# Contrato: tools do servidor MCP

**Endpoint**: `POST https://ambrosioai.site/mcp` (HTTP streamable, *stateless*). Em homologação, `http://localhost:8080/mcp` (pelo gateway) ou a URL do túnel.

## Regras comuns

- **Autenticação** (R23, revendo o R7): toda requisição ao `/mcp` exige token. Sem token → HTTP 401 com `WWW-Authenticate: Bearer resource_metadata="https://ambrosioai.site/.well-known/oauth-protected-resource/mcp", scope="…todos os escopos…"`, o desafio que faz o cliente abrir o login. Token inválido → HTTP 401 com `error="invalid_token"`. Token válido sem o escopo da tool → resultado de desafio `insufficient_scope` (abaixo).
- **Identidade**: o usuário é sempre o `sub` do token. Nenhuma tool aceita `userId` como argumento.
- **Descritor**: toda tool tem `name`, `title` e `description` em pt-BR (dizendo quando usar e quando não usar), `inputSchema` tipado, `annotations` (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`), `securitySchemes` e `_meta["ambrosio/endpoint"]` com a operação equivalente da API (`"GET /customers"`), usada pelo teste de paridade.
- **Saída**: `structuredContent` + um item `text` curto em pt-BR (valores em `R$ 1.234,56`, datas `dd/mm/aaaa`). Todo resultado traz `consultado_em` (UTC, ISO 8601). Listas trazem `itens` (máx. 200), `total` e `tem_mais`.
- **CPF**: só `cpfMasked`. Nunca o CPF completo, em nenhuma tool.
- **Escrita** (R11): argumento `confirmar` (boolean, padrão `false`). Com `false` → prévia, nada gravado, sem auditoria. Com `true` → grava, audita (`mcp_audit_entries`) e devolve o registro gravado.
- **Erros de domínio**: `isError: true` com a mensagem da API em pt-BR (validação, duplicidade, não encontrado). Recurso de outro usuário = não encontrado.

### Resultado de desafio (falta token ou escopo)

```json
{
  "isError": true,
  "content": [{ "type": "text", "text": "É preciso autorizar o Ambrosio a cadastrar clientes." }],
  "_meta": {
    "mcp/www_authenticate": [
      "Bearer resource_metadata=\"https://ambrosioai.site/.well-known/oauth-protected-resource/mcp\", error=\"insufficient_scope\", error_description=\"Falta o escopo customers:write\", scope=\"customers:write\""
    ]
  }
}
```

Sem token: `error="invalid_token"`, `error_description="Conecte sua conta do Ambrosio"` e `scope` com os escopos da tool.

## Tools

Legenda das anotações: **RO** `readOnlyHint`, **D** `destructiveHint`, **I** `idempotentHint`, **OW** `openWorldHint`. Todas as de leitura: RO `true`, D `false`, OW `false`.

### Usuário, clientes e pedidos

| Tool | Endpoint | Escopos | RO | D | I | Entrada | Saída (`structuredContent`) |
|---|---|---|---|---|---|---|---|
| `obter_usuario_atual` | `GET /users/me` | `customers:read` | ✓ | — | — | — | `id`, `nome`, `email`, `provedor` |
| `listar_clientes` | `GET /customers` | `customers:read` | ✓ | — | — | `busca?`, `pagina?` (≥ 1), `tamanho?` (1–100, padrão 20) | Lista de `id`, `nome`, `cpfMasked`, resumo |
| `obter_cliente` | `GET /customers/{id}` | `customers:read` | ✓ | — | — | `cliente_id` | `id`, `nome`, `cpfMasked`, `resumo` (`compras`, `totalLiquido`, `ultimaCompra`, `diasSemComprar`) |
| `cadastrar_cliente` | `POST /customers` | `customers:write` | ✗ | ✗ | ✗ | `nome`, `cpf`, `confirmar` | Prévia ou cliente criado (`cpfMasked`) |
| `atualizar_cliente` | `PUT /customers/{id}` | `customers:write` | ✗ | ✓ | ✓ | `cliente_id`, `nome`, `confirmar` | Prévia (antes/depois) ou cliente atualizado |
| `listar_pedidos` | `GET /orders` | `orders:read` | ✓ | — | — | `inicio?`, `fim?` (AAAA-MM-DD), `tipo?` (`venda`, `devolucao`, `cancelamento`), `canal?`, `cliente_id?`, `busca?`, `pagina?`, `tamanho?` | Lista de pedidos |
| `obter_pedido` | `GET /orders/{id}` | `orders:read` | ✓ | — | — | `pedido_id` | Pedido com cliente (`cpfMasked`), itens e `capturado_em` quando importado |
| `listar_itens_pedido` | `GET /orders/{id}` (itens) | `orders:read` | ✓ | — | — | `pedido_id` | Itens em ordem de linha |
| `cadastrar_pedido` | `POST /orders` | `orders:write` | ✗ | ✗ | ✗ | `cliente_id`, `numero`, `data`, `tipo`, `canal?`, `origem?`, `status?`, `total`, `venda_original_em?`, `cancelado_em?`, `itens[]` (`produto?`, `referencia?`, `tamanho?`, `preco?`), `confirmar` | Prévia ou pedido criado |
| `atualizar_pedido` | `PUT /orders/{id}` | `orders:write` | ✗ | ✓ | ✓ | `pedido_id`, `status?`, `total?`, `confirmar` | Prévia (antes/depois) ou pedido atualizado |

### Imagens

| Tool | Endpoint | Escopos | RO | D | I | OW | Entrada | Saída |
|---|---|---|---|---|---|---|---|---|
| `obter_imagem` | `GET /images/{imageId}` | `customers:read` | ✓ | — | — | ✗ | `imagem_id` | Metadados, `url` pré-assinada e `urlExpiraEm` |
| `enviar_imagem` | `POST /images` | `images:write` | ✗ | ✗ | ✓ | ✓ | `arquivo` (objeto: `download_url`, `mime_type?`, `file_name?`; nunca base64), `confirmar` | Prévia (tipo, tamanho) ou imagem gravada (`criada` indica se era nova) |
| `solicitar_upload_imagem` | `POST /images/uploads` | `images:write` | ✗ | ✗ | ✓ | ✗ | `tipo_mime`, `tamanho_bytes`, `sha256`, `confirmar` | URL pré-assinada de envio e `imagem_id` |
| `confirmar_upload_imagem` | `POST /images/uploads/{imageId}/confirm` | `images:write` | ✗ | ✗ | ✓ | ✗ | `imagem_id`, `confirmar` | Imagem disponível ou motivo da recusa |
| `excluir_imagem` | `DELETE /images/{imageId}` | `images:write` | ✗ | ✓ | ✓ | ✗ | `imagem_id`, `confirmar` | Prévia ou confirmação da exclusão |

`enviar_imagem`: download só de `https`, recusando IP privado, loopback e link-local; *timeout* de 10 s; tamanho máximo igual ao do storage; tipos aceitos iguais aos do storage.

### Importação do +Vendas

| Tool | Endpoint | Escopos | RO | D | I | OW | Entrada | Saída |
|---|---|---|---|---|---|---|---|---|
| `sugestao_periodo_importacao` | `GET /imports/mais-vendas/defaults` | `imports:read` | ✓ | — | — | ✗ | — | `inicioSugerido` (ou nulo), `fimSugerido` |
| `iniciar_importacao` | `POST /imports/mais-vendas/runs` | `imports:write` | ✗ | ✗ | ✓ | ✓ | `inicio`, `fim`, `simular?`, `confirmar` | Prévia (período e blocos) ou **link** `https://ambrosioai.site/importacao?inicio=…&fim=…` para a vendedora digitar as credenciais na tela (R9). Não aceita login nem senha |
| `importacao_atual` | `GET /imports/mais-vendas/runs/current` | `imports:read` | ✓ | — | — | ✗ | — | Execução em andamento com progresso por bloco, ou nenhuma |
| `obter_importacao` | `GET /imports/mais-vendas/runs/{runId}` | `imports:read` | ✓ | — | — | ✗ | `importacao_id` | Relatório: contagens, blocos, alterações e erros |
| `listar_importacoes` | `GET /imports/mais-vendas/runs` | `imports:read` | ✓ | — | — | ✗ | `pagina?`, `tamanho?` | Histórico paginado |
| `cancelar_importacao` | `POST /imports/mais-vendas/runs/{runId}/cancel` | `imports:write` | ✗ | ✗ | ✓ | ✗ | `importacao_id`, `confirmar` | Prévia ou execução marcada para cancelar após o bloco atual |

A descrição de `iniciar_importacao` avisa: "Nunca peça nem repasse a senha do +Vendas na conversa. A vendedora digita a senha na tela do Ambrosio, no link devolvido."

### Consultas prontas (sem endpoint equivalente)

`_meta["ambrosio/endpoint"]` ausente: ficam fora da conta da paridade.

| Tool | Escopos | Entrada | Saída |
|---|---|---|---|
| `buscar_cliente` | `customers:read` | `nome?` ou `cpf?` (um dos dois) | Clientes encontradas, com `cpfMasked`. O CPF informado é normalizado e buscado pelo hash, nunca devolvido |
| `historico_cliente` | `customers:read`, `orders:read` | `cliente_id`, `limite?` (padrão 50, máx. 200) | Pedidos com itens, ticket médio e última compra |
| `clientes_inativos` | `customers:read` | `dias` (≥ 1), `min_compras?`, `min_total?` | Clientes e dias sem compra |
| `resumo_periodo` | `orders:read` | `inicio?`, `fim?` (padrão: mês de cota atual, 26 a 25) | `totalLiquido`, `atendimentos`, `pecas`, `pa`, `va` |
| `devolucoes_cancelamentos` | `orders:read` | `inicio`, `fim` | Contagem e soma por tipo |

### Tools do plugin

| Tool | `securitySchemes` | Descrição |
|---|---|---|
| `perfil_ambrosio` | `oauth2` (`openid`) | Tool de perfil: `_meta["openai/profile"]: true`; `inputSchema` `{ "type": "object", "properties": {}, "additionalProperties": false }`; anotações RO `true`, D `false`, OW `false`; `outputSchema` com `id` obrigatório (`minLength: 1`, `pattern: "\\S"`), `name`, `email`, `additionalProperties: false`. Saída `{ "id": "<user_id>", "name": "...", "email": "..." }` em `structuredContent` e como JSON no item de texto |
| `sobre_ambrosio` | `oauth2` (`openid`) | Explica o que o Ambrosio faz. Não acessa dados. Era `noauth` até o R23 |

## Paridade (FR-011)

O teste de API lê `/api/swagger/v1/swagger.json`, monta a lista `MÉTODO /caminho` das operações (fora de `/auth/*`) e compara com os valores de `_meta["ambrosio/endpoint"]` do `tools/list`. Toda operação sem tool faz o teste falhar.
