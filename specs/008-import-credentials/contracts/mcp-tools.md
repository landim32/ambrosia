# Contrato: tools de importação do plugin (008)

Revisa as tools de importação da feature 007 ([contracts/mcp-tools.md](../../007-chatgpt-mcp-plugin/contracts/mcp-tools.md)). As demais regras comuns (token obrigatório, escopos, `confirmar`, auditoria, schema fechado) continuam.

| Tool | Endpoint | Escopo | RO | D | I | OW | Entrada | Saída |
|---|---|---|---|---|---|---|---|---|
| `sugestao_periodo_importacao` | `GET /imports/mais-vendas/defaults` | `imports:read` | ✓ | — | — | ✗ | — | Período sugerido, `maxMeses`, `temCredenciais`, `situacaoCredenciais`, `atualizarBase` (`disponivel`, `inicio`, `fim`, `motivo`) |
| `iniciar_importacao` | `POST /imports/mais-vendas/runs` | `imports:write` | ✗ | ✗ | ✗ | ✓ | `inicio`, `fim`, `simular?`, `confirmar` | Prévia (período, blocos, simulação) ou execução iniciada (`importacao_id`) |
| `atualizar_base` (nova) | `POST /imports/mais-vendas/runs/update-base` | `imports:write` | ✗ | ✗ | ✗ | ✓ | `simular?`, `confirmar` | Prévia do período calculado ou execução iniciada |

Regras:

- **Credenciais**: as tools usam as credenciais guardadas. Sem credenciais, devolvem `isError` com o texto "Cadastre as credenciais do +Vendas na configuração do Ambrosio: {link}", com `{link}` = `{Mcp:FrontendBaseUrl}/configuracoes/mais-vendas`. Com credenciais marcadas como "precisam ser atualizadas", a prévia avisa, mas não impede.
- **Senha nunca pela conversa**: nenhuma tool aceita `login`, `senha` ou `password` (schema fechado); a descrição de `iniciar_importacao` e `atualizar_base` diz "Nunca peça nem repasse a senha do +Vendas na conversa".
- **Prévia** (`confirmar: false`): valida com as mesmas regras do servidor (datas, 3 meses, credenciais, bloqueio, execução ativa) sem criar execução.
- **Execução**: `confirmar: true` chama o mesmo `IImportRunService` da API, com `startedVia = mcp`, e audita período, modo e resultado (sem credenciais).
- **Sem tool de credenciais** (FR-019): as rotas `GET/PUT/DELETE /imports/mais-vendas/credentials` ficam fora do plugin e entram numa lista explícita de exceções do teste de paridade.
